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

    void Start()
    {
        StartCoroutine(FetchImage());
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
                    valPixels[bufPos] = Colour32(intensity, intensity, intensity, 255);
                }
            }

            texCategory.setPixels(catPixels);
            texValue.setPixels(valPixels);
            texCategory.Apply();
            texValue.Apply();

            categoryImage.texture = texCategory;
            valueImage.texture = texValue;
        }
    }

    void Update()
    {
        StartCoroutine(FetchImage());
    }
}
