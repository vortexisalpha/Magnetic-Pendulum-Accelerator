using System.Collections.Generic;
using System.Collections;
using System.Text;
using TMPro;
using UnityEngine;
using UnityEngine.Networking;
using Newtonsoft.Json;
using UnityEngine.UI;

public class MagnetCoords
{
    public float x;
    public float y;
}

public class InfoResponse
{
    public Dictionary<string, MagnetCoords> magnets;
}

public class MagnetRenderer : MonoBehaviour
{
    [SerializeField] private RawImage miniDisplay;
    [SerializeField] private float pollIntervalSeconds = 0.5f;
    [SerializeField] private int magnetRadius = 4;

    private const int W = 160;
    private const int H = 120;

    private Texture2D tex;
    private Color32[] pixels;
    private readonly Color32 bg = new Color32(40, 40, 40, 255);

    private Color32[] palette =
    {
        new Color32(0,0,255,255),
        new Color32(0,255,0,255),
        new Color32(255,0,0,255),
        new Color32(0,0,0,0)
    };

    void Start()
    {
        tex = new Texture2D(W, H, TextureFormat.RGBA32, false);
        tex.filterMode = FilterMode.Point;
        pixels = new Color32[W * H];
        miniDisplay.texture = tex;

        ClearAndApply();
        StartCoroutine(PollLoop());
    }

    IEnumerator PollLoop()
    {
        var wait = new WaitForSeconds(pollIntervalSeconds);
        while (true)
        {
            yield return FetchInfo();
            yield return wait;
        }
    }

    IEnumerator FetchInfo()
    {
        using (var req = UnityWebRequest.Get("http://localhost:5000/info"))
        {
            yield return req.SendWebRequest();
            if (req.result != UnityWebRequest.Result.Success) yield break;

            var info = JsonConvert.DeserializeObject<InfoResponse>(req.downloadHandler.text);

            ClearPixels();
            int idx = 0;
            foreach (var coord in info.magnets)
            {
                var color = palette[idx % palette.Length]; // % is just for safety realistically we never have more than 4 magnets (for now).
                DrawCircle((int)coord.Value.x, (int)coord.Value.y, magnetRadius, color);
                idx++;
            }
            tex.SetPixels32(pixels);
            tex.Apply();
        }
    }

    void ClearAndApply()
    {
        ClearPixels();
        tex.SetPixels32(pixels);
        tex.Apply();
    }

    void ClearPixels()
    {
        for (int i = 0; i < pixels.Length; i++) pixels[i] = bg;
    }

    void DrawCircle(int cx, int cy, int r, Color32 color)
    {
        int r2 = r * r;
        //loop over +- delta x and +- delta y and draw if not out of bounds and not greater than r^2 (pythag)
        for (int dy = -r; dy <= r; dy++){
            for (int dx = -r; dx <= r; dx++){
                if (dx * dx + dy * dy > r2) continue;
                int x = cx + dx;
                int y = cy + dy;
                if (x < 0 || x >= W || y < 0 || y >= H) continue;
                int dstY = H - 1 - y; //invert y as always
                pixels[dstY * W + x] = color;
            }
        }
    }
}


