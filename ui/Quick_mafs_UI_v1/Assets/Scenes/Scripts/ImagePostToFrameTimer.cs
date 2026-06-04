using System;
using UnityEngine;

/// <summary>
/// [ImagePostToFrame] Flask POST /image → Unity textures/mesh (server UTC clocks).
/// [UnityReceiveToRender] GET response deserialized → textures/mesh (local stopwatch).
/// </summary>
public static class ImagePostToFrameTimer
{
    static int lastRenderedVersion;
    static float receiveStartTime;

    public static void OnImageReceived(ImageResponse resp)
    {
        if (resp.version <= 0 || resp.version <= lastRenderedVersion)
            return;

        receiveStartTime = Time.realtimeSinceStartup;
    }

    public static void OnFrameOutput(ImageResponse resp)
    {
        if (resp.version <= 0 || resp.version <= lastRenderedVersion)
            return;

        lastRenderedVersion = resp.version;

        float processMs = (Time.realtimeSinceStartup - receiveStartTime) * 1000f;
        Debug.Log($"[UnityReceiveToRender] {processMs:F1} ms (GET response → textures/mesh, v{resp.version})");

        if (resp.receivedAt > 0)
        {
            double nowUnix = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() / 1000.0;
            float postToFrameMs = (float)((nowUnix - resp.receivedAt) * 1000.0);
            Debug.Log($"[ImagePostToFrame] {postToFrameMs:F1} ms (Flask POST /image → textures/mesh, v{resp.version})");
        }
    }
}
