using TMPro;
using UnityEngine;
using UnityEngine.UI;

public sealed class UiTooltipSystem : MonoBehaviour
{
    private const float MaxWidth = 320f;
    private static readonly Vector2 Padding = new Vector2(12f, 8f);
    private static readonly Vector2 CursorOffset = new Vector2(18f, -18f);

    private static UiTooltipSystem instance;

    private RectTransform panel;
    private TextMeshProUGUI label;
    private Canvas currentCanvas;

    public static UiTooltipSystem Instance
    {
        get
        {
            if (instance == null)
            {
                var go = new GameObject("UiTooltipSystem");
                DontDestroyOnLoad(go);
                instance = go.AddComponent<UiTooltipSystem>();
            }

            return instance;
        }
    }

    public static bool HasInstance => instance != null;

    void Awake()
    {
        if (instance != null && instance != this)
        {
            Destroy(gameObject);
            return;
        }

        instance = this;
        DontDestroyOnLoad(gameObject);
    }

    public void Show(string message, RectTransform owner, Vector2 screenPosition)
    {
        if (string.IsNullOrWhiteSpace(message) || owner == null)
            return;

        Canvas canvas = owner.GetComponentInParent<Canvas>();
        if (canvas == null)
            return;

        EnsurePanel(canvas);
        label.text = message;

        Vector2 preferred = label.GetPreferredValues(message, MaxWidth, 0f);
        panel.sizeDelta = new Vector2(
            Mathf.Min(preferred.x, MaxWidth) + Padding.x * 2f,
            preferred.y + Padding.y * 2f);

        panel.gameObject.SetActive(true);
        Move(screenPosition);
    }

    public void Move(Vector2 screenPosition)
    {
        if (panel == null || currentCanvas == null || !panel.gameObject.activeSelf)
            return;

        RectTransform canvasRect = currentCanvas.transform as RectTransform;
        Camera camera = currentCanvas.renderMode == RenderMode.ScreenSpaceOverlay
            ? null
            : currentCanvas.worldCamera;

        if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(
                canvasRect,
                screenPosition + CursorOffset,
                camera,
                out Vector2 localPoint))
            return;

        Vector2 halfCanvas = canvasRect.rect.size * 0.5f;
        Vector2 halfPanel = panel.sizeDelta * 0.5f;
        localPoint.x = Mathf.Clamp(localPoint.x, -halfCanvas.x + halfPanel.x, halfCanvas.x - halfPanel.x);
        localPoint.y = Mathf.Clamp(localPoint.y, -halfCanvas.y + halfPanel.y, halfCanvas.y - halfPanel.y);
        panel.anchoredPosition = localPoint;
    }

    public void Hide()
    {
        if (panel != null)
            panel.gameObject.SetActive(false);
    }

    private void EnsurePanel(Canvas canvas)
    {
        if (panel != null && currentCanvas == canvas)
            return;

        if (panel != null)
            Destroy(panel.gameObject);

        currentCanvas = canvas;

        var go = new GameObject("Tooltip", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        panel = go.GetComponent<RectTransform>();
        panel.SetParent(canvas.transform, false);
        panel.anchorMin = panel.anchorMax = new Vector2(0.5f, 0.5f);
        panel.pivot = new Vector2(0.5f, 0.5f);
        panel.SetAsLastSibling();

        Image background = go.GetComponent<Image>();
        background.color = new Color(0.06f, 0.06f, 0.06f, 0.94f);
        background.raycastTarget = false;

        var labelGo = new GameObject("Label", typeof(RectTransform), typeof(TextMeshProUGUI));
        RectTransform labelRect = labelGo.GetComponent<RectTransform>();
        labelRect.SetParent(panel, false);
        labelRect.anchorMin = Vector2.zero;
        labelRect.anchorMax = Vector2.one;
        labelRect.offsetMin = Padding;
        labelRect.offsetMax = -Padding;

        label = labelGo.GetComponent<TextMeshProUGUI>();
        label.color = Color.white;
        label.fontSize = 13f;
        label.alignment = TextAlignmentOptions.MidlineLeft;
        label.enableWordWrapping = true;
        label.raycastTarget = false;

        panel.gameObject.SetActive(false);
    }
}
