using TMPro;
using UnityEngine;
using UnityEngine.UI;

public static class Win95Ui
{
    public static readonly Color Face = new Color(0.753f, 0.753f, 0.753f, 1f);
    public static readonly Color ActiveTitle = new Color(0f, 0f, 0.502f, 1f);
    public static readonly Color Recessed = new Color(0.5f, 0.5f, 0.5f, 1f);
    public static readonly Color Disabled = new Color(0.5f, 0.5f, 0.5f, 1f);
    public static readonly Color TooltipYellow = new Color(1f, 1f, 0.878f, 1f);

    private static Sprite squareSprite;

    public static void ApplyRaised(Image image, bool dropShadow)
    {
        ApplyFlat(image, Face);
        EnsureBevel(image.rectTransform, true);
        SetDropShadow(image, dropShadow);
    }

    public static void ApplyRecessed(Image image) => ApplyRecessed(image, Face);

    public static void ApplyRecessed(Image image, Color color)
    {
        ApplyFlat(image, color);
        EnsureBevel(image.rectTransform, false);
        SetDropShadow(image, false);
    }

    public static void ApplyFlat(Image image, Color color)
    {
        EnsureSprite();
        image.sprite = squareSprite;
        image.type = Image.Type.Sliced;
        image.color = color;
    }

    public static void ApplyTooltip(Image image)
    {
        ApplyFlat(image, TooltipYellow);
        EnsureBevel(image.rectTransform, true);
        SetDropShadow(image, true);
    }

    public static Image FindOrCreateImage(string name, RectTransform parent, int siblingIndex)
    {
        Transform existing = parent.Find(name);
        Image image = existing != null ? existing.GetComponent<Image>() : null;
        if (image == null)
        {
            var go = existing != null ? existing.gameObject : new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer));
            image = go.AddComponent<Image>();
        }

        image.transform.SetParent(parent, false);
        image.transform.SetSiblingIndex(Mathf.Clamp(siblingIndex, 0, parent.childCount - 1));
        return image;
    }

    public static TextMeshProUGUI FindOrCreateText(RectTransform parent, string name)
    {
        Transform existing = parent.Find(name);
        TextMeshProUGUI text = existing != null ? existing.GetComponent<TextMeshProUGUI>() : null;
        if (text != null) return text;

        var go = new GameObject(name, typeof(RectTransform), typeof(TextMeshProUGUI));
        go.transform.SetParent(parent, false);
        return go.GetComponent<TextMeshProUGUI>();
    }

    public static void Anchor(RectTransform rect, Vector2 min, Vector2 max, Vector2 pivot)
    {
        rect.anchorMin = min;
        rect.anchorMax = max;
        rect.pivot = pivot;
    }

    public static void Stretch(RectTransform rect) => Stretch(rect, Vector2.zero, Vector2.zero);

    public static void Stretch(RectTransform rect, Vector2 minOffset, Vector2 maxOffset)
    {
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = minOffset;
        rect.offsetMax = maxOffset;
    }

    private static void EnsureBevel(RectTransform parent, bool raised)
    {
        Color topLeft = raised ? Color.white : Color.black;
        Color bottomRight = raised ? Color.black : Color.white;
        AddEdge(parent, "Win95Top", new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, 1f), topLeft);
        AddEdge(parent, "Win95Left", Vector2.zero, new Vector2(0f, 1f), new Vector2(0f, 0.5f), new Vector2(1f, 0f), topLeft);
        AddEdge(parent, "Win95Bottom", Vector2.zero, new Vector2(1f, 0f), new Vector2(0.5f, 0f), new Vector2(0f, 1f), bottomRight);
        AddEdge(parent, "Win95Right", new Vector2(1f, 0f), Vector2.one, new Vector2(1f, 0.5f), new Vector2(1f, 0f), bottomRight);
    }

    private static void AddEdge(RectTransform parent, string name, Vector2 min, Vector2 max, Vector2 pivot, Vector2 size, Color color)
    {
        Image edge = FindOrCreateImage(name, parent, parent.childCount);
        RectTransform rect = edge.rectTransform;
        rect.anchorMin = min;
        rect.anchorMax = max;
        rect.pivot = pivot;
        rect.anchoredPosition = Vector2.zero;
        rect.sizeDelta = size;
        edge.sprite = null;
        edge.color = color;
        edge.raycastTarget = false;
        edge.transform.SetAsLastSibling();
    }

    private static void SetDropShadow(Image image, bool enabled)
    {
        Shadow shadow = image.GetComponent<Shadow>();
        if (!enabled)
        {
            if (shadow != null) DestroyComponent(shadow);
            return;
        }

        if (shadow == null) shadow = image.gameObject.AddComponent<Shadow>();
        shadow.effectColor = new Color(0f, 0f, 0f, 0.65f);
        shadow.effectDistance = new Vector2(3f, -3f);
        shadow.useGraphicAlpha = true;
    }

    private static void EnsureSprite()
    {
        if (squareSprite == null)
            squareSprite = ModernUiSprites.CreateRoundedSprite(12, 1);
    }

    private static void DestroyComponent(Component component)
    {
        if (Application.isPlaying) Object.Destroy(component);
        else Object.DestroyImmediate(component);
    }
}
