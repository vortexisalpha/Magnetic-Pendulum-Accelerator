using UnityEngine;

public static class ModernUiSprites
{
    public static Sprite CreateRoundedSprite(int size, int radius)
    {
        var texture = new Texture2D(size, size, TextureFormat.ARGB32, false)
        {
            name = "Retro UI Square Sprite",
            hideFlags = HideFlags.HideAndDontSave,
            filterMode = FilterMode.Bilinear,
            wrapMode = TextureWrapMode.Clamp
        };

        float edge = radius - 0.5f;
        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                float dx = Mathf.Max(edge - x, x - (size - 1 - edge), 0f);
                float dy = Mathf.Max(edge - y, y - (size - 1 - edge), 0f);
                float distance = Mathf.Sqrt(dx * dx + dy * dy);
                float alpha = Mathf.Clamp01(radius - distance);
                texture.SetPixel(x, y, new Color(1f, 1f, 1f, alpha));
            }
        }

        texture.Apply();
        var rect = new Rect(0f, 0f, size, size);
        var pivot = new Vector2(0.5f, 0.5f);
        return Sprite.Create(texture, rect, pivot, 100f, 0, SpriteMeshType.FullRect, Vector4.one * radius);
    }
}
