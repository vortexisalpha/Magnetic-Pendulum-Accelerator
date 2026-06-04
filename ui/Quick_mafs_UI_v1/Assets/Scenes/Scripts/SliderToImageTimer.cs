using UnityEngine;

/// <summary>
/// Measures elapsed time from slider movement until the displayed image changes.
/// </summary>
public static class SliderToImageTimer
{
    static bool waiting;
    static float startTime;
    static int versionAtSliderChange;

    /// <summary>True while waiting for a newer /image version after a slider move.</summary>
    public static bool IsWaitingForUpdate => waiting;

    public static void OnSliderChanged()
    {
        waiting = true;
        startTime = Time.realtimeSinceStartup;
        versionAtSliderChange = PendulumRenderer.LastFetchedVersion;
    }

    public static void OnImageFetched(int[][] image, int version)
    {
        if (!waiting)
            return;

        if (version > versionAtSliderChange)
        {
            float elapsedMs = (Time.realtimeSinceStartup - startTime) * 1000f;
            Debug.Log($"[SliderToImage] {elapsedMs:F1} ms (slider change → image update, v{version})");
            waiting = false;
        }
    }

}
