using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

public class DimensionSeries
{
    public float b;
    public readonly List<Vector2> points = new List<Vector2>();
    public float D;
}

public class UncertaintyPlot : MonoBehaviour
{
    [SerializeField] private RawImage display;
    [SerializeField] private int textureSize = 800;

    [Header("Plot margins (texture px)")]
    [SerializeField] private int marginLeft = 120;
    [SerializeField] private int marginRight = 50;
    [SerializeField] private int marginTop = 250;
    [SerializeField] private int marginBottom = 120;

    [Tooltip("Vertical axis range in decades of f, e.g. -4 -> f from 1e-4 to 1.")]
    [SerializeField] private float logFMin = -4f;
    [SerializeField] private int lineWidth = 2;

    [Header("Text content")]
    [SerializeField] private string title = "Basin boundary uncertainty dimension";
    [SerializeField, TextArea]
    private string explanation =
        "Fraction of pixels f whose final magnet flips under an epsilon perturbation, " +
        "plotted against epsilon on log-log axes. The slope is the uncertainty exponent; " +
        "D = 2 - slope measures how fractal the basin boundary is.";
    [SerializeField] private string xAxisLabel = "ε   perturbation size";
    [SerializeField] private string yAxisLabel = "f   fraction sensitive";

    [Header("Optional UI (sidebar/header) - populated if assigned")]
    [Tooltip("Header title text element.")]
    [SerializeField] private Text titleText;
    [Tooltip("About/description text element in the sidebar.")]
    [SerializeField] private Text aboutText;
    [Tooltip("Container with a Vertical Layout Group; legend rows are added here.")]
    [SerializeField] private RectTransform legendContainer;

    [SerializeField]
    private Color[] seriesColors =
    {
        new Color(0.13f, 0.45f, 0.80f),
        new Color(0.20f, 0.55f, 0.13f),
        new Color(0.80f, 0.20f, 0.18f),
        new Color(0.70f, 0.42f, 0.05f),
    };

    private Texture2D tex;
    private readonly List<GameObject> labels = new List<GameObject>();

    private static readonly Color32 Bg = new Color32(252, 252, 252, 255);
    private static readonly Color32 Frame = new Color32(60, 60, 64, 255);
    private static readonly Color32 Grid = new Color32(228, 228, 230, 255);
    private static readonly Color TextDark = new Color(0.12f, 0.12f, 0.14f);
    private static readonly Color TextMuted = new Color(0.42f, 0.42f, 0.46f);

    public void Render(List<DimensionSeries> all, float epsMin, float epsMax)
    {
        if (display == null || all == null || all.Count == 0)
            return;

        EnsureTexture();
        Fill(Bg);

        float logXMin = Mathf.Log10(epsMin);
        float logXMax = Mathf.Log10(epsMax);

        int x0 = marginLeft, x1 = textureSize - marginRight;
        int y0 = marginBottom, y1 = textureSize - marginTop;

        for (int e = Mathf.CeilToInt(logXMin); e <= Mathf.FloorToInt(logXMax); e++)
            VLine(MapX(e, logXMin, logXMax, x0, x1), y0, y1, Grid);
        for (int e = Mathf.CeilToInt(logFMin); e <= 0; e++)
            HLine(x0, x1, MapY(e, y0, y1), Grid);

        VLine(x0, y0, y1, Frame); VLine(x1, y0, y1, Frame);
        HLine(x0, x1, y0, Frame); HLine(x0, x1, y1, Frame);

        for (int e = Mathf.CeilToInt(logXMin); e <= Mathf.FloorToInt(logXMax); e++)
        {
            int px = MapX(e, logXMin, logXMax, x0, x1);
            VLine(px, y0 - 7, y0, Frame);
        }
        for (int e = Mathf.CeilToInt(logFMin); e <= 0; e++)
        {
            int py = MapY(e, y0, y1);
            HLine(x0 - 7, x0, py, Frame);
        }

        for (int s = 0; s < all.Count; s++)
        {
            Color32 col = seriesColors[s % seriesColors.Length];
            int prevX = 0, prevY = 0;
            bool havePrev = false;
            foreach (var p in all[s].points)
            {
                if (p.y <= 0f) { havePrev = false; continue; }
                int px = MapX(Mathf.Log10(p.x), logXMin, logXMax, x0, x1);
                int py = MapY(Mathf.Log10(p.y), y0, y1);
                if (havePrev) ThickLine(prevX, prevY, px, py, col);
                Dot(px, py, lineWidth + 1, col);
                prevX = px; prevY = py; havePrev = true;
            }
        }

        tex.Apply();
        BuildLabels(all, logXMin, logXMax, x0, x1, y0, y1);
    }

    private int MapX(float logx, float lo, float hi, int x0, int x1) =>
        Mathf.RoundToInt(Mathf.Lerp(x0, x1, Mathf.InverseLerp(lo, hi, logx)));

    private int MapY(float logy, int y0, int y1) =>
        Mathf.RoundToInt(Mathf.Lerp(y0, y1, Mathf.InverseLerp(logFMin, 0f, logy)));

    private void BuildLabels(List<DimensionSeries> all, float logXMin, float logXMax,
                             int x0, int x1, int y0, int y1)
    {
        foreach (var go in labels) Destroy(go);
        labels.Clear();

        float t = textureSize;

        if (titleText != null) titleText.text = title;
        if (aboutText != null) aboutText.text = explanation;
        BuildLegendUI(all);

        AddLabel(xAxisLabel, (x0 + x1) * 0.5f / t, (y0 * 0.34f) / t, new Vector2(0.5f, 0.5f),
                 32, TextDark, TextAnchor.MiddleCenter, 0f, 520);
        AddLabel(yAxisLabel, (x0 * 0.24f) / t, (y0 + y1) * 0.5f / t, new Vector2(0.5f, 0.5f),
                 32, TextDark, TextAnchor.MiddleCenter, 90f, 520);

        for (int e = Mathf.CeilToInt(logXMin); e <= Mathf.FloorToInt(logXMax); e++)
            AddLabel($"10{Sup(e)}", MapX(e, logXMin, logXMax, x0, x1) / t, (y0 - 16) / t,
                     new Vector2(0.5f, 1f), 24, TextMuted, TextAnchor.UpperCenter, 0f, 120);

        for (int e = Mathf.CeilToInt(logFMin); e <= 0; e++)
            AddLabel(e == 0 ? "1" : $"10{Sup(e)}", (x0 - 16) / t, MapY(e, y0, y1) / t,
                     new Vector2(1f, 0.5f), 24, TextMuted, TextAnchor.MiddleRight, 0f, 120);
    }

    private readonly List<GameObject> legendRows = new List<GameObject>();

    private void BuildLegendUI(List<DimensionSeries> all)
    {
        if (legendContainer == null) return;

        foreach (var go in legendRows) Destroy(go);
        legendRows.Clear();

        var font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        for (int s = 0; s < all.Count; s++)
        {
            Color col = seriesColors[s % seriesColors.Length];

            var row = new GameObject($"legend_{s}", typeof(RectTransform));
            row.transform.SetParent(legendContainer, false);

            var swatch = new GameObject("swatch", typeof(Image));
            swatch.transform.SetParent(row.transform, false);
            swatch.GetComponent<Image>().color = col;
            var srt = swatch.GetComponent<RectTransform>();
            srt.anchorMin = new Vector2(0f, 0.5f); srt.anchorMax = new Vector2(0f, 0.5f);
            srt.pivot = new Vector2(0f, 0.5f);
            srt.sizeDelta = new Vector2(28f, 4f);
            srt.anchoredPosition = new Vector2(0f, 0f);

            var label = new GameObject("text", typeof(Text));
            label.transform.SetParent(row.transform, false);
            var txt = label.GetComponent<Text>();
            txt.font = font;
            txt.fontSize = 22;
            txt.color = col;
            txt.alignment = TextAnchor.MiddleLeft;
            txt.text = $"b = {all[s].b:0.000}    D = {all[s].D:0.00}";
            var lrt = txt.rectTransform;
            lrt.anchorMin = new Vector2(0f, 0.5f); lrt.anchorMax = new Vector2(1f, 0.5f);
            lrt.pivot = new Vector2(0f, 0.5f);
            lrt.offsetMin = new Vector2(40f, -14f);
            lrt.offsetMax = new Vector2(0f, 14f);

            var rrt = row.GetComponent<RectTransform>();
            rrt.sizeDelta = new Vector2(0f, 28f);
            legendRows.Add(row);
        }
    }

    private static string Sup(int n)
    {
        const string digits = "⁰¹²³⁴⁵⁶⁷⁸⁹";
        string s = n < 0 ? "⁻" : "";
        foreach (char c in Mathf.Abs(n).ToString()) s += digits[c - '0'];
        return s;
    }

    private void AddLabel(string text, float nx, float ny, Vector2 pivot, int size,
                          Color color, TextAnchor align, float rotation, float width)
    {
        var go = new GameObject("label", typeof(Text));
        var txt = go.GetComponent<Text>();
        txt.transform.SetParent(display.rectTransform, false);
        txt.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        txt.fontSize = size;
        txt.color = color;
        txt.alignment = align;
        txt.horizontalOverflow = HorizontalWrapMode.Wrap;
        txt.verticalOverflow = VerticalWrapMode.Overflow;
        txt.text = text;

        var rt = txt.rectTransform;
        rt.anchorMin = rt.anchorMax = new Vector2(nx, ny);
        rt.pivot = pivot;
        rt.sizeDelta = new Vector2(width, size * 3f);
        rt.anchoredPosition = Vector2.zero;
        rt.localRotation = Quaternion.Euler(0f, 0f, rotation);
        labels.Add(go);
    }

    private void EnsureTexture()
    {
        if (tex == null)
        {
            tex = new Texture2D(textureSize, textureSize, TextureFormat.RGBA32, false)
            {
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp,
            };
            display.texture = tex;
        }
    }

    private void Fill(Color32 c)
    {
        var arr = new Color32[textureSize * textureSize];
        for (int i = 0; i < arr.Length; i++) arr[i] = c;
        tex.SetPixels32(arr);
    }

    private void VLine(int x, int yA, int yB, Color32 c)
    {
        if (x < 0 || x >= textureSize) return;
        for (int y = Mathf.Min(yA, yB); y <= Mathf.Max(yA, yB); y++)
            if (y >= 0 && y < textureSize) tex.SetPixel(x, y, c);
    }

    private void HLine(int xA, int xB, int y, Color32 c)
    {
        if (y < 0 || y >= textureSize) return;
        for (int x = Mathf.Min(xA, xB); x <= Mathf.Max(xA, xB); x++)
            if (x >= 0 && x < textureSize) tex.SetPixel(x, y, c);
    }

    private void Dot(int cx, int cy, int r, Color32 c)
    {
        for (int dy = -r; dy <= r; dy++)
        for (int dx = -r; dx <= r; dx++)
        {
            int x = cx + dx, y = cy + dy;
            if (x >= 0 && x < textureSize && y >= 0 && y < textureSize && dx * dx + dy * dy <= r * r)
                tex.SetPixel(x, y, c);
        }
    }

    private void ThickLine(int x0, int y0, int x1, int y1, Color32 c)
    {
        int dx = Mathf.Abs(x1 - x0), dy = -Mathf.Abs(y1 - y0);
        int sx = x0 < x1 ? 1 : -1, sy = y0 < y1 ? 1 : -1;
        int err = dx + dy;
        int r = Mathf.Max(0, lineWidth - 1);
        while (true)
        {
            Dot(x0, y0, r, c);
            if (x0 == x1 && y0 == y1) break;
            int e2 = 2 * err;
            if (e2 >= dy) { err += dy; x0 += sx; }
            if (e2 <= dx) { err += dx; y0 += sy; }
        }
    }
}
