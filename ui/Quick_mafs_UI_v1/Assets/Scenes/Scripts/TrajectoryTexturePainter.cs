using UnityEngine;

//draws a trajectory polyline onto a transparent texture. trajectory points are
//simulation coordinates mapped through the current viewport bounds, matching the
//region the pendulum image covers.
public static class TrajectoryTexturePainter
{
    public struct Viewport
    {
        public float xMin, xMax, yMin, yMax;
    }

    public static void Paint(Texture2D tex, Vector2[] points, Viewport view,
                             Color32 lineColor, int thickness)
    {
        int w = tex.width;
        int h = tex.height;

        var pixels = new Color32[w * h];
        Clear(pixels);

        if (points != null && points.Length > 0)
        {
            float spanX = Mathf.Max(view.xMax - view.xMin, 1e-6f);
            float spanY = Mathf.Max(view.yMax - view.yMin, 1e-6f);

            Vector2Int prev = ToPixel(points[0], view, spanX, spanY, w, h);
            DrawDisc(pixels, w, h, prev.x, prev.y, thickness, lineColor);

            for (int i = 1; i < points.Length; i++)
            {
                Vector2Int cur = ToPixel(points[i], view, spanX, spanY, w, h);
                DrawLine(pixels, w, h, prev.x, prev.y, cur.x, cur.y, thickness, lineColor);
                prev = cur;
            }
        }

        tex.SetPixels32(pixels);
        tex.Apply();
    }

    private static Vector2Int ToPixel(Vector2 p, Viewport v, float spanX, float spanY, int w, int h)
    {
        int col = Mathf.RoundToInt((p.x - v.xMin) / spanX * (w - 1));
        int row = Mathf.RoundToInt((p.y - v.yMin) / spanY * (h - 1)); //row 0 = bottom
        return new Vector2Int(col, row);
    }

    private static void Clear(Color32[] pixels)
    {
        var clear = new Color32(0, 0, 0, 0);
        for (int i = 0; i < pixels.Length; i++)
            pixels[i] = clear;
    }

    //bresenham line, thickened with a disc at each step
    private static void DrawLine(Color32[] px, int w, int h, int x0, int y0, int x1, int y1,
                                 int thickness, Color32 color)
    {
        int dx = Mathf.Abs(x1 - x0);
        int dy = -Mathf.Abs(y1 - y0);
        int sx = x0 < x1 ? 1 : -1;
        int sy = y0 < y1 ? 1 : -1;
        int err = dx + dy;

        while (true)
        {
            DrawDisc(px, w, h, x0, y0, thickness, color);
            if (x0 == x1 && y0 == y1) break;
            int e2 = 2 * err;
            if (e2 >= dy) { err += dy; x0 += sx; }
            if (e2 <= dx) { err += dx; y0 += sy; }
        }
    }

    private static void DrawDisc(Color32[] px, int w, int h, int cx, int cy, int radius, Color32 color)
    {
        int r = Mathf.Max(radius, 1) - 1;
        int r2 = r * r;
        for (int dy = -r; dy <= r; dy++)
        {
            for (int dx = -r; dx <= r; dx++)
            {
                if (dx * dx + dy * dy > r2 && r > 0) continue;
                int x = cx + dx;
                int y = cy + dy;
                if (x < 0 || x >= w || y < 0 || y >= h) continue;
                px[y * w + x] = color;
            }
        }
    }
}
