using TMPro;
using UnityEngine;
using UnityEngine.UI;

public sealed class MagnetPreviewFullscreenToggle : MonoBehaviour
{
    private const string ToggleButtonName = "MagnetPreviewFullscreenToggle";
    private const string MainMapRegionName = "2DMapRegion";

    [SerializeField] private RawImage previewDisplay;
    [SerializeField] private Vector2 buttonSize = new Vector2(48f, 22f);
    [SerializeField] private Vector2 buttonInset = new Vector2(8f, 8f);
    [SerializeField] private float buttonFontSize = 9f;

    private RectTransform previewRect;
    private RectTransform mainMapRect;
    private Button toggleButton;
    private TextMeshProUGUI toggleLabel;
    private LayoutState miniLayout;
    private LayoutState mainMapLayout;
    private bool swapped;

    public void Initialize(RawImage display)
    {
        previewDisplay = display;
        previewRect = previewDisplay != null ? previewDisplay.rectTransform : null;

        if (previewRect == null)
            return;

        mainMapRect = FindMainMapRegion();
        CaptureMiniLayout();
        CaptureMainMapLayout();
        EnsureToggleButton();
        ApplyButtonLabel();
    }

    void OnDisable()
    {
        if (swapped)
            SetSwapped(false);
    }

    public void ToggleFullscreen()
    {
        SetSwapped(!swapped);
    }

    private void SetSwapped(bool enabled)
    {
        if (previewRect == null)
            return;

        if (mainMapRect == null)
            mainMapRect = FindMainMapRegion();

        if (mainMapRect == null)
            return;

        if (enabled)
        {
            CaptureMiniLayout();
            CaptureMainMapLayout();
            if (!miniLayout.IsValid || !mainMapLayout.IsValid)
                return;

            ApplyLayout(previewRect, mainMapLayout);
            ApplyLayout(mainMapRect, miniLayout);
        }
        else
        {
            if (!miniLayout.IsValid || !mainMapLayout.IsValid)
                return;

            ApplyLayout(previewRect, miniLayout);
            ApplyLayout(mainMapRect, mainMapLayout);
        }

        swapped = enabled;
        EnsureToggleButton();
        ApplyButtonLabel();
    }

    private void CaptureMiniLayout()
    {
        if (previewRect == null || swapped)
            return;

        miniLayout = new LayoutState(previewRect);
    }

    private void CaptureMainMapLayout()
    {
        if (mainMapRect == null || swapped)
            return;

        mainMapLayout = new LayoutState(mainMapRect);
    }

    private static void ApplyLayout(RectTransform rect, LayoutState layout)
    {
        if (rect == null || !layout.IsValid)
            return;

        rect.SetParent(layout.Parent, false);
        rect.SetSiblingIndex(Mathf.Min(layout.SiblingIndex, layout.Parent.childCount - 1));
        rect.anchorMin = layout.AnchorMin;
        rect.anchorMax = layout.AnchorMax;
        rect.pivot = layout.Pivot;
        rect.anchoredPosition = layout.AnchoredPosition;
        rect.sizeDelta = layout.SizeDelta;
        rect.localScale = layout.LocalScale;
    }

    private static RectTransform FindMainMapRegion()
    {
        foreach (RectTransform rect in FindObjectsByType<RectTransform>(
                     FindObjectsInactive.Include,
                     FindObjectsSortMode.None))
        {
            if (rect.name == MainMapRegionName)
                return rect;
        }

        return null;
    }

    private void EnsureToggleButton()
    {
        if (previewRect == null)
            return;

        if (toggleButton == null)
        {
            Transform existing = previewRect.Find(ToggleButtonName);
            GameObject buttonObject = existing != null
                ? existing.gameObject
                : new GameObject(ToggleButtonName, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(Button));

            buttonObject.transform.SetParent(previewRect, false);
            toggleButton = buttonObject.GetComponent<Button>();
            toggleButton.onClick.RemoveListener(ToggleFullscreen);
            toggleButton.onClick.AddListener(ToggleFullscreen);

            Image image = buttonObject.GetComponent<Image>();
            image.color = new Color(0.06f, 0.06f, 0.06f, 0.9f);

            toggleLabel = buttonObject.GetComponentInChildren<TextMeshProUGUI>(true);
            if (toggleLabel == null)
            {
                GameObject labelObject = new GameObject("Label", typeof(RectTransform), typeof(TextMeshProUGUI));
                labelObject.transform.SetParent(buttonObject.transform, false);
                RectTransform labelRect = labelObject.GetComponent<RectTransform>();
                labelRect.anchorMin = Vector2.zero;
                labelRect.anchorMax = Vector2.one;
                labelRect.offsetMin = Vector2.zero;
                labelRect.offsetMax = Vector2.zero;
                toggleLabel = labelObject.GetComponent<TextMeshProUGUI>();
            }

            toggleLabel.color = Color.white;
            toggleLabel.fontSize = buttonFontSize;
            toggleLabel.alignment = TextAlignmentOptions.Center;
            toggleLabel.raycastTarget = false;
        }

        RectTransform buttonRect = toggleButton.GetComponent<RectTransform>();
        buttonRect.anchorMin = buttonRect.anchorMax = new Vector2(1f, 1f);
        buttonRect.pivot = new Vector2(1f, 1f);
        buttonRect.anchoredPosition = new Vector2(-buttonInset.x, -buttonInset.y);
        buttonRect.sizeDelta = buttonSize;
        toggleButton.transform.SetAsLastSibling();
    }

    private void ApplyButtonLabel()
    {
        if (toggleLabel != null)
            toggleLabel.text = swapped ? "Mini" : "Swap";
    }

    private readonly struct LayoutState
    {
        public readonly Transform Parent;
        public readonly int SiblingIndex;
        public readonly Vector2 AnchorMin;
        public readonly Vector2 AnchorMax;
        public readonly Vector2 Pivot;
        public readonly Vector2 AnchoredPosition;
        public readonly Vector2 SizeDelta;
        public readonly Vector3 LocalScale;

        public bool IsValid => Parent != null;

        public LayoutState(RectTransform rect)
        {
            Parent = rect.parent;
            SiblingIndex = rect.GetSiblingIndex();
            AnchorMin = rect.anchorMin;
            AnchorMax = rect.anchorMax;
            Pivot = rect.pivot;
            AnchoredPosition = rect.anchoredPosition;
            SizeDelta = rect.sizeDelta;
            LocalScale = rect.localScale;
        }
    }
}
