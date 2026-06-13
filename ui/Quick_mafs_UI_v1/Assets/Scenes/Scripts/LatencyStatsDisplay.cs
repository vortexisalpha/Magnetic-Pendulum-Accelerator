using TMPro;
using UnityEngine;
using UnityEngine.UI;

[ExecuteAlways]
public sealed class LatencyStatsDisplay : MonoBehaviour
{
    [SerializeField] private Vector2 toggleAnchoredPosition = new Vector2(140f, -6f);
    [SerializeField] private Vector2 panelAnchoredPosition = new Vector2(16f, -16f);
    [SerializeField] private Vector2 panelSize = new Vector2(360f, 118f);
    [SerializeField] private Vector2 toggleSize = new Vector2(160f, 20f);
    [SerializeField] private bool visibleOnStart = true;

    private GameObject panel;
    private TextMeshProUGUI label;
    private Toggle visibilityToggle;
    private bool statsVisible;

    void OnEnable()
    {
        EnsureUi();

        if (Application.isPlaying && PynqConnection.Instance != null)
        {
            PynqConnection.Instance.LatencyStatsReceived += UpdateLabel;
            UpdateLabel(PynqConnection.Instance.LatestLatencyStats);
        }
    }

    void OnDisable()
    {
        if (Application.isPlaying && PynqConnection.Instance != null)
            PynqConnection.Instance.LatencyStatsReceived -= UpdateLabel;
    }

    void OnValidate()
    {
        EnsureUi();
    }

    void Update()
    {
        if (visibilityToggle == null || panel == null)
        {
            EnsureUi();
            return;
        }

        if (!Application.isPlaying)
        {
            SyncToggleLayoutFromScene();
            return;
        }

        if (statsVisible != visibilityToggle.isOn)
            SetStatsVisibility(visibilityToggle.isOn);
    }

    private void EnsureUi()
    {
        statsVisible = visibilityToggle != null ? visibilityToggle.isOn : visibleOnStart;

        Canvas canvas = GameObject.Find("Canvas")?.GetComponent<Canvas>();
        if (canvas == null)
            canvas = FindFirstObjectByType<Canvas>();
        if (canvas == null)
            canvas = CreateCanvas();

        BuildOrUpdateVisibilityToggle(canvas.transform);
        BuildOrUpdatePanel(canvas.transform);
        ApplyVisibility();
    }

    private void BuildOrUpdatePanel(Transform canvasTransform)
    {
        Transform existingPanel = canvasTransform.Find("LatencyStatsPanel");
        if (existingPanel != null)
        {
            panel = existingPanel.gameObject;
            label = panel.GetComponentInChildren<TextMeshProUGUI>(true);
        }
        else
        {
            panel = new GameObject("LatencyStatsPanel", typeof(RectTransform), typeof(Image));
            panel.transform.SetParent(canvasTransform, false);
        }

        var panelRect = panel.GetComponent<RectTransform>();
        panelRect.anchorMin = new Vector2(0f, 1f);
        panelRect.anchorMax = new Vector2(0f, 1f);
        panelRect.pivot = new Vector2(0f, 1f);
        panelRect.anchoredPosition = panelAnchoredPosition;
        panelRect.sizeDelta = panelSize;
        panel.transform.SetAsLastSibling();

        var image = panel.GetComponent<Image>();
        image.color = new Color(0f, 0f, 0f, 0.62f);
        image.raycastTarget = false;

        if (label == null)
        {
            var labelGo = new GameObject("LatencyStatsText", typeof(RectTransform), typeof(TextMeshProUGUI));
            label = labelGo.GetComponent<TextMeshProUGUI>();
            label.rectTransform.SetParent(panelRect, false);
        }

        var labelRect = label.rectTransform;
        labelRect.anchorMin = Vector2.zero;
        labelRect.anchorMax = Vector2.one;
        labelRect.offsetMin = new Vector2(12f, 8f);
        labelRect.offsetMax = new Vector2(-12f, -8f);

        label.fontSize = 18f;
        label.color = Color.white;
        label.alignment = TextAlignmentOptions.TopLeft;
        label.raycastTarget = false;
        if (string.IsNullOrEmpty(label.text))
            label.text = "Latency statistics\nAruCo Flask: --\nTCP transfer: --\nFPGA compute: --";
    }

    private void BuildOrUpdateVisibilityToggle(Transform canvasTransform)
    {
        Transform existingToggle = canvasTransform.Find("LatencyStatsToggle");
        bool createdToggle = existingToggle == null;
        GameObject toggleGo = existingToggle != null
            ? existingToggle.gameObject
            : new GameObject("LatencyStatsToggle", typeof(RectTransform), typeof(Toggle));

        var toggleRect = toggleGo.GetComponent<RectTransform>();
        if (createdToggle)
        {
            toggleRect.SetParent(canvasTransform, false);
            toggleRect.anchorMin = new Vector2(0f, 1f);
            toggleRect.anchorMax = new Vector2(0f, 1f);
            toggleRect.pivot = new Vector2(0f, 1f);
            toggleRect.anchoredPosition = toggleAnchoredPosition;
            toggleRect.sizeDelta = toggleSize;
        }
        else if (!Application.isPlaying)
        {
            toggleAnchoredPosition = toggleRect.anchoredPosition;
            toggleSize = toggleRect.sizeDelta;
        }

        toggleGo.transform.SetAsLastSibling();

        var backgroundGo = FindOrCreateChild(toggleRect, "Background", typeof(RectTransform), typeof(Image));
        var backgroundRect = backgroundGo.GetComponent<RectTransform>();
        backgroundRect.anchorMin = new Vector2(0f, 0.5f);
        backgroundRect.anchorMax = new Vector2(0f, 0.5f);
        backgroundRect.pivot = new Vector2(0.5f, 0.5f);
        backgroundRect.anchoredPosition = new Vector2(10f, 0f);
        backgroundRect.sizeDelta = new Vector2(20f, 20f);

        var backgroundImage = backgroundGo.GetComponent<Image>();
        backgroundImage.color = Color.white;

        var checkmarkGo = FindOrCreateChild(backgroundRect, "Checkmark", typeof(RectTransform), typeof(Image));
        var checkmarkRect = checkmarkGo.GetComponent<RectTransform>();
        checkmarkRect.anchorMin = new Vector2(0.5f, 0.5f);
        checkmarkRect.anchorMax = new Vector2(0.5f, 0.5f);
        checkmarkRect.pivot = new Vector2(0.5f, 0.5f);
        checkmarkRect.anchoredPosition = Vector2.zero;
        checkmarkRect.sizeDelta = new Vector2(12f, 12f);

        var checkmarkImage = checkmarkGo.GetComponent<Image>();
        checkmarkImage.color = new Color(0.2f, 0.2f, 0.2f, 1f);

        var labelGo = FindOrCreateChild(toggleRect, "Label", typeof(RectTransform), typeof(TextMeshProUGUI));
        var toggleLabel = labelGo.GetComponent<TextMeshProUGUI>();
        var labelRect = toggleLabel.rectTransform;
        labelRect.anchorMin = new Vector2(0f, 0f);
        labelRect.anchorMax = new Vector2(1f, 1f);
        labelRect.anchoredPosition = new Vector2(9f, -0.5f);
        labelRect.sizeDelta = new Vector2(-28f, -3f);

        toggleLabel.fontSize = 14f;
        toggleLabel.color = new Color(0.196f, 0.196f, 0.196f, 1f);
        toggleLabel.alignment = TextAlignmentOptions.Left;
        toggleLabel.text = "Latency Stats";
        toggleLabel.raycastTarget = true;

        visibilityToggle = toggleGo.GetComponent<Toggle>();
        visibilityToggle.onValueChanged.RemoveListener(SetStatsVisibility);
        visibilityToggle.targetGraphic = backgroundImage;
        visibilityToggle.graphic = checkmarkImage;
        visibilityToggle.isOn = statsVisible;
        visibilityToggle.onValueChanged.AddListener(SetStatsVisibility);
    }

    private static GameObject FindOrCreateChild(Transform parent, string childName, params System.Type[] components)
    {
        Transform existing = parent.Find(childName);
        if (existing != null)
            return existing.gameObject;

        var child = new GameObject(childName, components);
        child.transform.SetParent(parent, false);
        return child;
    }

    private void SyncToggleLayoutFromScene()
    {
        if (visibilityToggle == null)
            return;

        var toggleRect = visibilityToggle.GetComponent<RectTransform>();
        if (toggleRect == null)
            return;

        if (toggleAnchoredPosition == toggleRect.anchoredPosition &&
            toggleSize == toggleRect.sizeDelta)
            return;

        toggleAnchoredPosition = toggleRect.anchoredPosition;
        toggleSize = toggleRect.sizeDelta;
    }

    private void SetStatsVisibility(bool isVisible)
    {
        statsVisible = isVisible;
        ApplyVisibility();
    }

    private void ApplyVisibility()
    {
        if (panel != null)
        {
            bool shouldShow = !Application.isPlaying || statsVisible;
            panel.SetActive(shouldShow);
            if (shouldShow)
                panel.transform.SetAsLastSibling();
        }
    }

    private static Canvas CreateCanvas()
    {
        var canvasGo = new GameObject("RuntimeLatencyCanvas", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
        var canvas = canvasGo.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 1000;

        var scaler = canvasGo.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);

        return canvas;
    }

    private void UpdateLabel(LatencyStatsMessage stats)
    {
        if (label == null || stats == null)
            return;

        label.text =
            "Latency statistics\n" +
            $"AruCo Flask: {FormatMs(stats.arucoMarkerFlaskLatencyMs)}\n" +
            $"TCP transfer: {FormatMs(stats.tcpConnectionTransferLatencyMs)}\n" +
            $"FPGA compute: {FormatMs(stats.fpgaComputeTimeMs)}";
    }

    private static string FormatMs(float valueMs)
    {
        return valueMs >= 0f ? $"{valueMs:F1} ms" : "--";
    }
}
