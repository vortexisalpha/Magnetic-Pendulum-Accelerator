using UnityEngine;

/// <summary>
/// [TcpReceiveToRender] PYNQ frame received on main thread → textures/mesh (local stopwatch).
/// </summary>
public static class ImagePostToFrameTimer
{
    static int lastRenderedVersion;
    static float receiveStartTime;

    public static void OnImageReceived(ImageMessage msg)
    {
        if (msg.version <= 0 || msg.version <= lastRenderedVersion)
            return;

        receiveStartTime = Time.realtimeSinceStartup;
    }

    public static void OnFrameOutput(ImageMessage msg)
    {
        if (msg.version <= 0 || msg.version <= lastRenderedVersion)
            return;

        lastRenderedVersion = msg.version;

        float processMs = (Time.realtimeSinceStartup - receiveStartTime) * 1000f;
        Debug.Log($"[TcpReceiveToRender] {processMs:F1} ms (TCP frame → textures/mesh, v{msg.version})");
    }
}
