using System.Collections;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.Networking;
using Newtonsoft.Json;

// Magnet positions come from the Flask server (fed live by the ArUco camera
// detection), NOT over the PYNQ TCP link. Image + sliders use TCP; only the
// magnet dots are pulled from Flask /info here.
public class MagnetRenderer : MonoBehaviour
{
    [SerializeField] private RawImage miniDisplay;
    [SerializeField] private float pollIntervalSeconds = 0.03f;
    [SerializeField] private int magnetRadius = 4;
    [SerializeField] private string flaskURL = "http://35.179.111.223:5000/";

    private const float SIM_CORNER = 1.8f;
    private const int W = 130;
    private const int H = 130;

    private Texture2D tex;
    private Color32[] pixels;
    private readonly Color32 bg = new Color32(40, 40, 40, 255);

    private static readonly Color32[] MagnetPalette =
    {
        new Color32(255, 0, 0, 255),   // magnet_0 — red
        new Color32(0, 255, 0, 255),   // magnet_1 — green
        new Color32(0, 0, 255, 255),   // magnet_2 — blue
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

    void OnDestroy()
    {
        StopAllCoroutines();
    }

    private float oldMagnetSendTime = -1f;
    private float newMagnetSendTime;
    private float oldRenderTime = -1f;
    private float newRenderTime;

    IEnumerator PollLoop()
    {
        var wait = new WaitForSeconds(pollIntervalSeconds);
        while (true)
        {
            yield return FetchAndSendInfo();
            yield return wait;
        }
    }

    IEnumerator FetchAndSendInfo()
    {
        using (var req = UnityWebRequest.Get(flaskURL + "info"))
        {
            yield return req.SendWebRequest();
            if (req.result != UnityWebRequest.Result.Success) yield break;

            var info = JsonConvert.DeserializeObject<InfoMessage>(req.downloadHandler.text);
            if (info == null || info.magnets == null) yield break;


            ApplyInfo(info);
            newRenderTime = Time.time;
            if (oldRenderTime > 0f)
            {
                Debug.Log($"Time between magnet position renders: {newRenderTime - oldRenderTime}");
            }
            oldRenderTime = newRenderTime;

            // Relay the same magnet positions to the board over TCP so the FPGA
            // basin is computed from what we display.
            if (PynqConnection.Instance != null)
            {
                PynqConnection.Instance.SendMagnets(info.magnets);
                newMagnetSendTime = Time.time;
                if (oldMagnetSendTime > 0f)
                {
                    Debug.Log($"Time between magnet position TCP sends: {newMagnetSendTime - oldMagnetSendTime}");
                }
                oldMagnetSendTime = newMagnetSendTime;
            }
        }
    }

    void ApplyInfo(InfoMessage info)
    {
        ClearPixels();
        foreach (var coord in info.magnets)
        {
            if (!TryParseMagnetIndex(coord.Key, out int magnetIdx))
                continue;

            //Flask server holds simulation coord values
            //rawImage on MagnetSim uses pixel coordinates, mapping is required
            int px = (int)Mathf.Round(W / (SIM_CORNER * 2) * (coord.Value.x + 1.8f));
            int py = (int)Mathf.Round(-W / (SIM_CORNER * 2) * (coord.Value.y - 1.8f));

            var color = MagnetPalette[magnetIdx % MagnetPalette.Length];
            DrawCircle(px, py, magnetRadius, color);
        }
        tex.SetPixels32(pixels);
        tex.Apply();
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

    static bool TryParseMagnetIndex(string key, out int index)
    {
        index = -1;
        if (string.IsNullOrEmpty(key) || !key.StartsWith("magnet_"))
            return false;
        return int.TryParse(key.Substring("magnet_".Length), out index) && index >= 0;
    }

    void DrawCircle(int cx, int cy, int r, Color32 color)
    {
        int r2 = r * r;
        //loop over +- delta x and +- delta y and draw if not out of bounds and not greater than r^2 (pythag)
        for (int dy = -r; dy <= r; dy++)
        {
            for (int dx = -r; dx <= r; dx++)
            {
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
