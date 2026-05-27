using UnityEngine;
using Newtonsoft.Json;

public class ImageResponse
{
    public int width;
    public int height;
    public int bitDepth;
    public int[][] image; 
}

public class Renderer : MonoBehaviour
{
    void Start()
    {
        
    }

    IEnumerator FetchImage()
    {
        using (var req = UnityWebRequest.Get("http://127.0.0.1:5000/image"))
        {
            yield return req.SendWebRequest();
            if (req.result != UnityWebRequest.Result.Success) yield break;

            var resp = JsonConvert.DeserializeObject<ImageResponse>(req.downloadHandler.text);
            int w = resp.width, h = resp.height;

            var texCategory = new Texture2D(w, h, TextureFormat.RGBA32, false);
            var texValue = new Texture2D(w, h, TextureFormat.RGBA32, false);
            texCategory.filterMode = FilterMode.Point;
            texValue.filterMode = FilterMode.Point;

            var catPixels = new Color32[w * h];
            var valPixels = new Color32[w * h];
        }
    }
    void Update()
    {
        
    }
}
