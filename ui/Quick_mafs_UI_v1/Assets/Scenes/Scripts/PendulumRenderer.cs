using UnityEngine;
using UnityEngine.UI;
using System;
using System.Collections;
using UnityEngine.Networking;
using Newtonsoft.Json; //needed for 2d arr json handling

public class ImageResponse
{
    public int width;
    public int height;
    public int bitDepth;
    public int[][] image; 
}

public class PendulumRenderer : MonoBehaviour
{    
    [SerializeField] private RawImage categoryImage;
    [SerializeField] private RawImage valueImage;

    Color32[] palette = {
                new Color32(0,0,255,255),  //00
                new Color32(0,255,0,255),  //01
                new Color32(255,0,0,255),  //10
                new Color32(0,0,0,255), //11
            };

    //3D stuff:
    [SerializeField] private MeshFilter mesh3D;
    [SerializeField] private float heightScale = 0.05f;  // iterations (height)
    [SerializeField] private float xyScale     = 0.5f;   // pixel (spacing)

    private Mesh runtimeMesh;
    private Vector3[] verts3D;
    private Color32[] vertColors3D;
    private int[] tris3D;

    void Start()
    {
        BuildMeshSkeleton(160, 120);
        StartCoroutine(FetchImage());
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
    }

    IEnumerator FetchImage()
    {
        using (var req = UnityWebRequest.Get("http://127.0.0.1:5000/image"))
        {
            yield return req.SendWebRequest();
            if (req.result != UnityWebRequest.Result.Success) yield break;

            var resp = JsonConvert.DeserializeObject<ImageResponse>(req.downloadHandler.text);
            int width = resp.width;
            int height = resp.height;

            var texCategory = new Texture2D(width, height, TextureFormat.RGBA32, false);
            var texValue = new Texture2D(width, height, TextureFormat.RGBA32, false);
            texCategory.filterMode = FilterMode.Point;
            texValue.filterMode = FilterMode.Point;

            var catPixels = new Color32[width * height];
            var valPixels = new Color32[width * height];

            for (int y = 0; y < height; y++){
                for (int x = 0; x < width; x++){
                    int pixel = resp.image[y][x];

                    int top2 = (pixel >> (resp.bitDepth - 2)) & 0x3; // right shifted by all but 2 and then & with ...011
                    int bottom12 = pixel & 0xFFF;

                    int bufPos = (height - y - 1) * width + x; //array buffer is inverted in unity, flip y

                    catPixels[bufPos] = palette[top2];
                    
                    //calculate intensity from 0-255 for iterations
                    byte intensity = (byte)((bottom12 * 255) / 4095);
                    valPixels[bufPos] = new Color32(intensity, intensity, intensity, 255);

                    //3d:
                    int meshIdx = y * width + x;
                    verts3D[meshIdx] = new Vector3(x * xyScale, bottom12 * heightScale, y * xyScale);
                    vertColors3D[meshIdx] = palette[top2];
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
            runtimeMesh.RecalculateBounds();
        }
    }

    public void SetCategoryVisible(bool visible)
    {
        valueImage.gameObject.SetActive(visible);
    }

    void Update()
    {
        StartCoroutine(FetchImage());
    }
}
