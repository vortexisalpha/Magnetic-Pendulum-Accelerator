using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

// Shows a confirm button when either resolution axis exceeds the auto-send limit.
// High-res renders are slow, so TCP is only sent after the user clicks.
public class HighResRenderGate : MonoBehaviour
{
    [SerializeField] private RectTransform panel;
    [SerializeField] private Button confirmButton;
    [SerializeField] private TextMeshProUGUI label;

    void Awake()
    {
        EnsureUi();
    }

    void OnEnable()
    {
        PynqParamController.HighResGateChanged += OnGateChanged;
        Refresh(PynqParamController.IsHighResGateActive,
            PynqParamController.PendingResX,
            PynqParamController.PendingResY,
            PynqParamController.IsHighResRenderPending);
    }

    void OnDisable()
    {
        PynqParamController.HighResGateChanged -= OnGateChanged;
    }

    void EnsureUi()
    {
        if (panel != null && confirmButton != null && label != null)
        {
            confirmButton.onClick.AddListener(OnConfirmClicked);
            return;
        }

        var canvas = GameObject.Find("Canvas")?.GetComponent<RectTransform>();
        if (canvas == null)
        {
            Debug.LogWarning("[HighResRenderGate] Canvas not found; assign UI refs in the Inspector.");
            return;
        }

        var root = new GameObject("HighResRenderPanel", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        panel = root.GetComponent<RectTransform>();
        panel.SetParent(canvas, false);
        panel.anchorMin = new Vector2(1f, 1f);
        panel.anchorMax = new Vector2(1f, 1f);
        panel.pivot = new Vector2(1f, 1f);
        panel.anchoredPosition = new Vector2(-12f, -12f);
        panel.sizeDelta = new Vector2(240f, 56f);

        var bg = root.GetComponent<Image>();
        bg.color = new Color(0.08f, 0.08f, 0.08f, 0.92f);

        var labelGo = new GameObject("Label", typeof(RectTransform), typeof(TextMeshProUGUI));
        var labelRect = labelGo.GetComponent<RectTransform>();
        labelRect.SetParent(panel, false);
        labelRect.anchorMin = new Vector2(0f, 0.5f);
        labelRect.anchorMax = new Vector2(1f, 1f);
        labelRect.offsetMin = new Vector2(10f, 0f);
        labelRect.offsetMax = new Vector2(-10f, -4f);
        label = labelGo.GetComponent<TextMeshProUGUI>();
        label.fontSize = 13f;
        label.alignment = TextAlignmentOptions.MidlineLeft;
        label.color = new Color(1f, 0.85f, 0.35f, 1f);
        label.text = "High-res render pending";

        var buttonGo = new GameObject("ConfirmButton", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(Button));
        var buttonRect = buttonGo.GetComponent<RectTransform>();
        buttonRect.SetParent(panel, false);
        buttonRect.anchorMin = new Vector2(0.05f, 0.08f);
        buttonRect.anchorMax = new Vector2(0.95f, 0.46f);
        buttonRect.offsetMin = Vector2.zero;
        buttonRect.offsetMax = Vector2.zero;

        var buttonImage = buttonGo.GetComponent<Image>();
        buttonImage.color = new Color(0.2f, 0.55f, 0.25f, 1f);
        confirmButton = buttonGo.GetComponent<Button>();

        var buttonLabelGo = new GameObject("Text", typeof(RectTransform), typeof(TextMeshProUGUI));
        var buttonLabelRect = buttonLabelGo.GetComponent<RectTransform>();
        buttonLabelRect.SetParent(buttonRect, false);
        buttonLabelRect.anchorMin = Vector2.zero;
        buttonLabelRect.anchorMax = Vector2.one;
        buttonLabelRect.offsetMin = Vector2.zero;
        buttonLabelRect.offsetMax = Vector2.zero;
        var buttonLabel = buttonLabelGo.GetComponent<TextMeshProUGUI>();
        buttonLabel.fontSize = 14f;
        buttonLabel.alignment = TextAlignmentOptions.Center;
        buttonLabel.text = "Render";

        confirmButton.onClick.AddListener(OnConfirmClicked);
        panel.gameObject.SetActive(false);
    }

    void OnGateChanged(bool gateActive, int resX, int resY, bool pending)
    {
        Refresh(gateActive, resX, resY, pending);
    }

    void Refresh(bool gateActive, int resX, int resY, bool pending)
    {
        if (panel == null) return;

        bool show = gateActive && pending;
        panel.gameObject.SetActive(show);
        if (!show) return;

        if (label != null)
            label.text = $"High-res {resX}×{resY} — slow render";

        if (confirmButton != null)
            confirmButton.interactable = PynqConnection.Instance != null && PynqConnection.Instance.IsConnected;
    }

    void OnConfirmClicked()
    {
        PynqParamController.ConfirmHighResRender();
    }
}
