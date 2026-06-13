using UnityEngine;
using UnityEngine.UI;

public class PendulumRenderer : MonoBehaviour
{
    public static int LastFetchedVersion { get; private set; }
    private const int BandHeight = 60;

    public static void ResetFetchedVersion() => LastFetchedVersion = 0;

    [SerializeField] private RawImage boaImage;
    [SerializeField] private RawImage settlingTimeImage;
    [SerializeField] private RawImage fssImage;

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
        // CHANGE: start with BoA visible, settling-time/FSS hidden.
        // Each RawImage now has a fixed semantic meaning:
        // boaImage          = basin of attraction only
        // settlingTimeImage = settling time only
        // fssImage          = FSS only
        Set2DMap(0);

        if (PynqConnection.Instance != null)
        {
            PynqConnection.Instance.ImageReceived += ApplyImage;

            // Render immediately if a frame already arrived before we subscribed.
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

        int depthMax = DepthMax(msg.bitDepth);

        // SHORT-TERM ROUTING:
        // The message itself does not currently say whether it is normal or FSS.
        // Therefore, for now, classify the incoming frame using the current FssMode.
        // This is acceptable for the demo, but the ideal long-term protocol would tag
        // the image message with its true map type.
        bool incomingImageIsFss = PynqConnection.Instance != null && PynqConnection.Instance.FssMode;

        if (incomingImageIsFss)
        {
            // CHANGE FROM BEFORE:
            // In FSS mode, do NOT decode/update boaImage or settlingTimeImage.
            // This prevents an FSS frame from being accidentally interpreted as a
            // basin-of-attraction or settling-time frame.
            Texture2D texFss = new Texture2D(displaySize, displaySize, TextureFormat.RGBA32, false);
            texFss.filterMode = FilterMode.Point;

            var fssPixels = new Color32[displaySize * displaySize];

            for (int sy = 0; sy < displaySize; sy++)
            {
                int sourceY = Mathf.Min((sy * displaySourceHeight) / displaySize, displaySourceHeight - 1);
                int dstY = displaySize - sy - 1; // Unity texture buffer y-flip.

                for (int sx = 0; sx < displaySize; sx++)
                {
                    int sourceX = Mathf.Min((sx * width) / displaySize, width - 1);
                    int pixel = msg.pixels[sourceY * width + sourceX];

                    int bufPos = dstY * displaySize + sx;

                    // FSS-specific decoding/colouring only.
                    fssPixels[bufPos] = FssColorizer.Colorize(pixel);
                }
            }

            texFss.SetPixels32(fssPixels);
            texFss.Apply();

            // CHANGE FROM BEFORE:
            // FSS data goes only to fssImage, never to boaImage.
            fssImage.texture = texFss;
        }
        else
        {
            // Normal mode:
            // Decode the same normal FPGA frame into two valid visualisations:
            // 1. basin of attraction from category bits
            // 2. settling time from depth bits

            if (width != meshW || height != meshH)
                BuildMeshSkeleton(width, height);

            Texture2D texBoa = new Texture2D(displaySize, displaySize, TextureFormat.RGBA32, false);
            Texture2D texSettling = new Texture2D(displaySize, displaySize, TextureFormat.RGBA32, false);
            texBoa.filterMode = FilterMode.Point;
            texSettling.filterMode = FilterMode.Point;

            var boaPixels = new Color32[displaySize * displaySize];
            var settlingPixels = new Color32[displaySize * displaySize];

            float depthScale = DepthToWorldScale(msg.bitDepth, heightScale);

            // 3D mesh update remains based on normal basin/settling data only.
            // CHANGE FROM BEFORE:
            // The mesh is not recoloured using FSS data, because FSS has different
            // bit semantics from the normal basin-of-attraction frame.
            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    int pixel = msg.pixels[y * width + x];
                    DecodePixel(pixel, msg.bitDepth, out int category, out int depth);

                    Color32 basinColor = palette[category];

                    int meshIdx = y * width + x;
                    verts3D[meshIdx] = new Vector3(x * xyScale, depth * depthScale, y * xyScale);
                    vertColors3D[meshIdx] = basinColor;
                }
            }

            for (int sy = 0; sy < displaySize; sy++)
            {
                int sourceY = Mathf.Min((sy * displaySourceHeight) / displaySize, displaySourceHeight - 1);
                int dstY = displaySize - sy - 1; // Unity texture buffer y-flip.

                for (int sx = 0; sx < displaySize; sx++)
                {
                    int sourceX = Mathf.Min((sx * width) / displaySize, width - 1);
                    int pixel = msg.pixels[sourceY * width + sourceX];

                    DecodePixel(pixel, msg.bitDepth, out int category, out int depth);

                    Color32 basinColor = palette[category];
                    byte intensity = (byte)((depth * 255) / depthMax);

                    int bufPos = dstY * displaySize + sx;

                    boaPixels[bufPos] = basinColor;
                    settlingPixels[bufPos] = PlasmaColor(intensity);
                }
            }

            texBoa.SetPixels32(boaPixels);
            texSettling.SetPixels32(settlingPixels);
            texBoa.Apply();
            texSettling.Apply();

            // CHANGE FROM BEFORE:
            // BoA image always receives basin-of-attraction texture only.
            // Settling image always receives settling-time texture only.
            boaImage.texture = texBoa;
            settlingTimeImage.texture = texSettling;

            // 3D mesh update only in normal mode.
            runtimeMesh.vertices = verts3D;
            runtimeMesh.colors32 = vertColors3D;
            runtimeMesh.triangles = tris3D;
            runtimeMesh.RecalculateNormals();
            runtimeMesh.RecalculateBounds();
        }

        LastFetchedVersion = msg.version;
        ImagePostToFrameTimer.OnFrameOutput(msg);
    }


    public void Set2DMap(int dropdownId)
    {
        Debug.Log($"Selected 2D map dropdown ID: {dropdownId}");

        // CHANGE FROM BEFORE:
        // Dropdown now controls visibility only.
        // Each RawImage has a fixed meaning:
        // 0 -> boaImage
        // 1 -> settlingTimeImage
        // 2 -> fssImage
        SetMapVisibility(dropdownId);

        // CHANGE FROM BEFORE:
        // FSS mode is only used to tell PYNQ what to compute.
        // It no longer changes what boaImage means.
        bool shouldUseFssMode = dropdownId == 2;
        bool currentlyFssMode = PynqConnection.Instance != null && PynqConnection.Instance.FssMode;

        if (shouldUseFssMode != currentlyFssMode)
        {
            PynqConnection.Instance?.SetFssMode(shouldUseFssMode);
            PynqParamController.NotifyFssChanged();
        }
    }

    private void SetMapVisibility(int dropdownId)
    {
        // CHANGE:
        // All three layers occupy the same size and position. Only one is active at a time.
        // IDs 3 and 4 are valid 3D views handled by other managers.
        // Therefore, all 2D RawImages are hidden for those options.
        if (boaImage != null)
            boaImage.gameObject.SetActive(dropdownId == 0);

        if (settlingTimeImage != null)
            settlingTimeImage.gameObject.SetActive(dropdownId == 1);

        if (fssImage != null)
            fssImage.gameObject.SetActive(dropdownId == 2);
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
