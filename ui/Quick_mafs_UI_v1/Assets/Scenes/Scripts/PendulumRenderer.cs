using UnityEngine;
using UnityEngine.UI;

public class PendulumRenderer : MonoBehaviour
{
    public static int LastFetchedVersion { get; private set; }
    private const int BandHeight = 60;

    public static void ResetFetchedVersion() => LastFetchedVersion = 0;

    [SerializeField] private RawImage categoryImage;
    [SerializeField] private RawImage valueImage;

    Color32[] palette = {
                new Color32(30,30,30,255),
                new Color32(255,0,0,255),
                new Color32(0,255,0,255),
                new Color32(0,0,255,255),
            };

    //3D stuff:
    [SerializeField] private MeshFilter mesh3D;
    [SerializeField] private float heightScale = 0.05f;  // iterations (height)
    [SerializeField] private float xyScale = 0.5f;   // pixel (spacing)

    private Mesh runtimeMesh;
    private Vector3[] verts3D;
    private Color32[] vertColors3D;
    private int[] tris3D;
    private int meshW = -1;
    private int meshH = -1;

    // can be 6 bit or 14 bit (currently fpga working on 6 bit)
    static void DecodePixel(int pixel, int bitDepth, out int category, out int depth)
    {
        if (bitDepth <= 6)
        {
            category = pixel & 0x3;
            depth = (pixel >> 2) & 0xF;
        }
        else
        {
            category = (pixel >> 12) & 0x3;
            depth = pixel & 0xFFF;
        }
    }

    static int DepthMax(int bitDepth) =>
        bitDepth <= 6 ? 15 : ((1 << (bitDepth - 2)) - 1);

    static float DepthToWorldScale(int bitDepth, float heightScale) =>
        bitDepth <= 6 ? heightScale * 250f : heightScale;

    void Start()
    {
        if (PynqConnection.Instance != null)
        {
            PynqConnection.Instance.ImageReceived += ApplyImage;
            //render immediately if a frame already arrived before we subscribed
            if (PynqConnection.Instance.LatestImage != null)
                ApplyImage(PynqConnection.Instance.LatestImage);
        }
    }

    void OnDestroy()
    {
        if (PynqConnection.Instance != null)
            PynqConnection.Instance.ImageReceived -= ApplyImage;
    }

    void BuildMeshSkeleton(int w, int h)
    {
        int vCount = w * h;
        verts3D = new Vector3[vCount];
        vertColors3D = new Color32[vCount];

        int quadCount = (w - 1) * (h - 1);
        tris3D = new int[quadCount * 6];
        int t = 0;
        for (int y = 0; y < h - 1; y++)
        {
            for (int x = 0; x < w - 1; x++)
            {
                int bl = y * w + x;
                int br = bl + 1;
                int tl = bl + w;
                int tr = tl + 1;
                tris3D[t++] = bl; tris3D[t++] = tl; tris3D[t++] = br;
                tris3D[t++] = br; tris3D[t++] = tl; tris3D[t++] = tr;
            }
        }

        runtimeMesh = new Mesh();
        runtimeMesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
        mesh3D.mesh = runtimeMesh;
        meshW = w;
        meshH = h;
    }

    void ApplyImage(ImageMessage msg)
    {
        if (PynqConnection.Instance != null &&
            msg.version <= PynqConnection.Instance.MinAcceptedImageVersion)
            return;

        ImagePostToFrameTimer.OnImageReceived(msg);
        SliderToImageTimer.OnImageFetched(msg.version);

        int width = msg.width;
        int height = msg.height;
        int displaySourceHeight = Mathf.Max(BandHeight, (height / BandHeight) * BandHeight);
        displaySourceHeight = Mathf.Min(displaySourceHeight, height);
        int displaySize = Mathf.Max(width, height);

        if (width != meshW || height != meshH)
            BuildMeshSkeleton(width, height);

        var texCategory = new Texture2D(displaySize, displaySize, TextureFormat.RGBA32, false);
        var texValue = new Texture2D(displaySize, displaySize, TextureFormat.RGBA32, false);
        texCategory.filterMode = FilterMode.Point;
        texValue.filterMode = FilterMode.Point;

        var catPixels = new Color32[displaySize * displaySize];
        var valPixels = new Color32[displaySize * displaySize];
        int depthMax = DepthMax(msg.bitDepth);
        float depthScale = DepthToWorldScale(msg.bitDepth, heightScale);

        //in fss mode the low bits encode sensitive/timeout instead of magnet id
        bool fss = PynqConnection.Instance != null && PynqConnection.Instance.FssMode;

        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                int pixel = msg.pixels[y * width + x];
                DecodePixel(pixel, msg.bitDepth, out int category, out int depth);

                Color32 catColor = fss ? FssColorizer.Colorize(pixel) : palette[category];

                //3d:
                int meshIdx = y * width + x;
                verts3D[meshIdx] = new Vector3(x * xyScale, depth * depthScale, y * xyScale);
                vertColors3D[meshIdx] = catColor;
            }
        }

        // The simulation domain is square even when the sample grid is not, so
        // stretch non-square buffers into a square texture for display.
        for (int sy = 0; sy < displaySize; sy++)
        {
            int sourceY = Mathf.Min((sy * displaySourceHeight) / displaySize, displaySourceHeight - 1);
            int dstY = displaySize - sy - 1; //array buffer is inverted in unity, flip y

            for (int sx = 0; sx < displaySize; sx++)
            {
                int sourceX = Mathf.Min((sx * width) / displaySize, width - 1);
                int pixel = msg.pixels[sourceY * width + sourceX];

                DecodePixel(pixel, msg.bitDepth, out int category, out int depth);
                Color32 catColor = fss ? FssColorizer.Colorize(pixel) : palette[category];
                byte intensity = (byte)((depth * 255) / depthMax);

                int bufPos = dstY * displaySize + sx;
                catPixels[bufPos] = catColor;
                valPixels[bufPos] = PlasmaColor(intensity);
            }
        }

        texCategory.SetPixels32(catPixels);
        texValue.SetPixels32(valPixels);
        texCategory.Apply();
        texValue.Apply();

        categoryImage.texture = texCategory;
        valueImage.texture = texValue;

        //3d:
        runtimeMesh.vertices = verts3D;
        runtimeMesh.colors32 = vertColors3D;
        runtimeMesh.triangles = tris3D;
        runtimeMesh.RecalculateNormals();
        runtimeMesh.RecalculateBounds();

        LastFetchedVersion = msg.version;
        ImagePostToFrameTimer.OnFrameOutput(msg);
    }

    public void SetCategoryVisible(bool visible)
    {
        valueImage.gameObject.SetActive(visible);
    }

    Color32 PlasmaColor(byte value)
    {
        float t = value / 255f;
        Color32[] stops =
        {
            new Color32(13, 8, 135, 255),
            new Color32(75, 3, 161, 255),
            new Color32(125, 3, 168, 255),
            new Color32(168, 34, 150, 255),
            new Color32(203, 70, 121, 255),
            new Color32(229, 107, 93, 255),
            new Color32(248, 148, 65, 255),
            new Color32(253, 195, 40, 255),
            new Color32(240, 249, 33, 255)

        };

        float scaled = t * (stops.Length - 1);
        int i = Mathf.FloorToInt(scaled);
        int j = Mathf.Min(i + 1, stops.Length - 1);
        float localT = scaled - i;

        return LerpColor32(stops[i], stops[j], localT);
    }

    Color32 LerpColor32(Color32 a, Color32 b, float t)
    {
        return new Color32(
                (byte)Mathf.Lerp(a.r, b.r, t),
                (byte)Mathf.Lerp(a.g, b.g, t),
                (byte)Mathf.Lerp(a.b, b.b, t),
                255);
    }

}
