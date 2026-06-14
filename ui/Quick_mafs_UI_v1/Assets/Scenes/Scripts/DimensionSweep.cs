using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using UnityEngine;

public class DimensionSweep : MonoBehaviour
{
    [Header("Sweep settings")]
    [SerializeField] private float[] bValues = { 0.100f, 0.125f, 0.150f, 0.175f };
    [SerializeField] private float epsilonMin = 1e-6f;
    [SerializeField] private float epsilonMax = 1e-1f;
    [SerializeField] private int epsilonCount = 12;
    [SerializeField] private int resX = 240;
    [SerializeField] private int resY = 240;

    [Tooltip("Drop points with f above this when fitting the slope (saturated tail).")]
    [SerializeField] private float fitMaxFraction = 0.5f;

    [Tooltip("Seconds to wait for a single frame before giving up.")]
    [SerializeField] private float frameTimeout = 30f;

    [Header("Output")]
    [SerializeField] private UncertaintyPlot plot;

    private bool running;

    public void StartSweep()
    {
        if (running)
        {
            Debug.LogWarning("[DimensionSweep] already running.");
            return;
        }
        if (PynqConnection.Instance == null || !PynqConnection.Instance.IsConnected)
        {
            Debug.LogWarning("[DimensionSweep] not connected to the board.");
            return;
        }
        StartCoroutine(RunSweep());
    }

    private IEnumerator RunSweep()
    {
        running = true;

        ControlData baseData = PynqParamController.CurrentData;
        float[] epsilons = LogSpace(epsilonMin, epsilonMax, epsilonCount);
        var allSeries = new List<DimensionSeries>();

        float previousEpsilon = PynqConnection.Instance.Epsilon;
        PynqConnection.Instance.SetFssMode(true);

        foreach (float b in bValues)
        {
            var series = new DimensionSeries { b = b };
            allSeries.Add(series);

            foreach (float eps in epsilons)
            {
                PynqConnection.Instance.SetEpsilon(eps);
                PynqConnection.Instance.SendParams(
                    b,
                    baseData.magneticStrength,
                    baseData.pendulumLength,
                    baseData.pendulumHeight,
                    resX, resY,
                    force: true);

                int wantVersion = PynqConnection.Instance.LatestSentParamVersion;

                bool got = false;
                void OnImg(ImageMessage m) { if (m.version >= wantVersion) got = true; }
                PynqConnection.Instance.ImageReceived += OnImg;

                float waited = 0f;
                while (!got && waited < frameTimeout)
                {
                    waited += Time.deltaTime;
                    yield return null;
                }
                PynqConnection.Instance.ImageReceived -= OnImg;

                if (!got)
                {
                    Debug.LogWarning($"[DimensionSweep] timed out at b={b} eps={eps:g3}.");
                    continue;
                }

                float f = SensitiveFraction(PynqConnection.Instance.LatestImage);
                series.points.Add(new Vector2(eps, f));
                Debug.Log($"[DimensionSweep] b={b} eps={eps:g3} f={f:g4}");

                if (plot != null) plot.Render(allSeries, epsilonMin, epsilonMax);
            }

            series.D = 2f - FitLogLogSlope(series.points, fitMaxFraction);
            Debug.Log($"[DimensionSweep] b={b}: D={series.D:F3}");

            if (plot != null) plot.Render(allSeries, epsilonMin, epsilonMax);
        }

        PynqConnection.Instance.SetEpsilon(previousEpsilon);
        PynqConnection.Instance.SetFssMode(false);
        ExportCsv(allSeries);
        if (plot != null) plot.Render(allSeries, epsilonMin, epsilonMax);
        running = false;
    }

    private static float[] LogSpace(float lo, float hi, int n)
    {
        var a = new float[n];
        float logLo = Mathf.Log10(lo), logHi = Mathf.Log10(hi);
        for (int i = 0; i < n; i++)
            a[i] = Mathf.Pow(10f, Mathf.Lerp(logLo, logHi, i / (float)(n - 1)));
        return a;
    }

    private static float SensitiveFraction(ImageMessage img)
    {
        if (img == null || img.pixels == null || img.pixels.Length == 0)
            return 0f;

        int sensitive = 0;
        foreach (int p in img.pixels)
            if ((p & 0x1) != 0) sensitive++;
        return sensitive / (float)img.pixels.Length;
    }

    private static float FitLogLogSlope(List<Vector2> pts, float maxFraction)
    {
        int n = 0;
        float sx = 0, sy = 0, sxx = 0, sxy = 0;
        foreach (var p in pts)
        {
            if (p.y <= 0f || p.y > maxFraction) continue;
            float x = Mathf.Log10(p.x), y = Mathf.Log10(p.y);
            sx += x; sy += y; sxx += x * x; sxy += x * y;
            n++;
        }
        if (n < 2) return 0f;
        return (n * sxy - sx * sy) / (n * sxx - sx * sx);
    }

    private static void ExportCsv(List<DimensionSeries> allSeries)
    {
        var sb = new StringBuilder();
        sb.AppendLine("b,epsilon,f");
        foreach (var s in allSeries)
            foreach (var p in s.points)
                sb.AppendLine(string.Format(CultureInfo.InvariantCulture,
                    "{0},{1:g6},{2:g6}", s.b, p.x, p.y));

        sb.AppendLine();
        sb.AppendLine("b,D");
        foreach (var s in allSeries)
            sb.AppendLine(string.Format(CultureInfo.InvariantCulture, "{0},{1:g6}", s.b, s.D));

        string path = Path.Combine(Application.persistentDataPath, "dimension_sweep.csv");
        File.WriteAllText(path, sb.ToString());
        Debug.Log($"[DimensionSweep] wrote {path}");
    }
}
