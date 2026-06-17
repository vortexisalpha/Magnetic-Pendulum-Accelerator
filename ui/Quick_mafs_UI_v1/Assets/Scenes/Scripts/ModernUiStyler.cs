using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public sealed class ModernUiStyler : MonoBehaviour
{
    private static readonly Color DesktopTeal = new Color(0f, 0.5f, 0.5f, 1f);
    private Canvas canvas;

    public static void EnsureInstalled()
    {
        foreach (Canvas foundCanvas in UnityEngine.Object.FindObjectsByType<Canvas>(FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            if (foundCanvas.GetComponent<ModernUiStyler>() == null)
                foundCanvas.gameObject.AddComponent<ModernUiStyler>();
        }
    }

    void Awake() => canvas = GetComponent<Canvas>();
    void OnEnable() => StartCoroutine(StyleAfterLayout());

    IEnumerator StyleAfterLayout()
    {
        yield return null;
        Apply();
        yield return new WaitForSeconds(0.15f);
        Apply();
    }

    [ContextMenu("Apply Windows 95 UI Style")]
    public void Apply()
    {
        if (canvas == null)
            canvas = GetComponent<Canvas>();
        if (canvas == null)
            return;

        StyleCanvasScaler();
        if (canvas.name == "Canvas")
            EnsureDesktopChrome(transform as RectTransform);

        StyleImages();
        StyleText();
        StyleControls();
    }

    private void StyleCanvasScaler()
    {
        var scaler = GetComponent<CanvasScaler>();
        if (scaler == null)
            return;

        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1280f, 720f);
        scaler.matchWidthOrHeight = 0.5f;
    }

    private void EnsureDesktopChrome(RectTransform canvasRect)
    {
        if (canvasRect == null)
            return;

        Image desktop = Win95Ui.FindOrCreateImage("Win95Desktop", canvasRect, 0);
        Win95Ui.Stretch(desktop.rectTransform);
        desktop.color = DesktopTeal;
        desktop.raycastTarget = false;

        Image window = Win95Ui.FindOrCreateImage("Win95MainWindow", canvasRect, 1);
        RectTransform windowRect = window.rectTransform;
        Win95Ui.Anchor(windowRect, new Vector2(1f, 0.5f), new Vector2(1f, 0.5f), new Vector2(1f, 0.5f));
        windowRect.anchoredPosition = new Vector2(-18f, 9f);
        windowRect.sizeDelta = new Vector2(336f, 524f);
        Win95Ui.ApplyRaised(window, true);

        Image titleBar = Win95Ui.FindOrCreateImage("Win95TitleBar", windowRect, 0);
        RectTransform titleRect = titleBar.rectTransform;
        titleRect.anchorMin = new Vector2(0f, 1f);
        titleRect.anchorMax = new Vector2(1f, 1f);
        titleRect.pivot = new Vector2(0.5f, 1f);
        titleRect.anchoredPosition = new Vector2(0f, -4f);
        titleRect.sizeDelta = new Vector2(-8f, 24f);
        Win95Ui.ApplyFlat(titleBar, Win95Ui.ActiveTitle);

        TextMeshProUGUI title = Win95Ui.FindOrCreateText(titleRect, "Title");
        Win95Ui.Stretch(title.rectTransform, new Vector2(8f, 0f), new Vector2(-76f, 0f));
        title.text = "Magnetic Pendulum Accelerator";
        title.fontSize = 15f;
        title.fontStyle = FontStyles.Bold;
        title.color = Color.white;
        title.alignment = TextAlignmentOptions.MidlineLeft;
        title.raycastTarget = false;

        EnsureWindowButtons(titleRect);
        EnsureTaskbar(canvasRect);
    }

    private static void EnsureWindowButtons(RectTransform titleRect)
    {
        string[] labels = { "X", "□", "_" };
        for (int i = 0; i < labels.Length; i++)
        {
            Image button = Win95Ui.FindOrCreateImage("Win95WindowButton" + i, titleRect, titleRect.childCount);
            RectTransform rect = button.rectTransform;
            Win95Ui.Anchor(rect, new Vector2(1f, 0.5f), new Vector2(1f, 0.5f), new Vector2(1f, 0.5f));
            rect.anchoredPosition = new Vector2(-4f - i * 22f, 0f);
            rect.sizeDelta = new Vector2(20f, 18f);
            Win95Ui.ApplyRaised(button, false);

            TextMeshProUGUI label = Win95Ui.FindOrCreateText(rect, "Label");
            Win95Ui.Stretch(label.rectTransform);
            label.text = labels[i];
            label.fontSize = 12f;
            label.fontStyle = FontStyles.Bold;
            label.color = Color.black;
            label.alignment = TextAlignmentOptions.Center;
            label.raycastTarget = false;
        }
    }

    private static void EnsureTaskbar(RectTransform canvasRect)
    {
        Image taskbar = Win95Ui.FindOrCreateImage("Win95Taskbar", canvasRect, canvasRect.childCount - 1);
        RectTransform taskbarRect = taskbar.rectTransform;
        taskbarRect.anchorMin = Vector2.zero;
        taskbarRect.anchorMax = new Vector2(1f, 0f);
        taskbarRect.pivot = new Vector2(0.5f, 0f);
        taskbarRect.anchoredPosition = Vector2.zero;
        taskbarRect.sizeDelta = new Vector2(0f, 34f);
        Win95Ui.ApplyRaised(taskbar, false);

        Image startButton = Win95Ui.FindOrCreateImage("Win95StartButton", taskbarRect, 0);
        RectTransform startRect = startButton.rectTransform;
        Win95Ui.Anchor(startRect, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(0f, 0.5f));
        startRect.anchoredPosition = new Vector2(6f, 0f);
        startRect.sizeDelta = new Vector2(84f, 24f);
        Win95Ui.ApplyRaised(startButton, false);

        TextMeshProUGUI start = Win95Ui.FindOrCreateText(startRect, "Label");
        Win95Ui.Stretch(start.rectTransform, new Vector2(10f, 0f), Vector2.zero);
        start.text = "Start";
        start.fontSize = 15f;
        start.fontStyle = FontStyles.Bold;
        start.color = Color.black;
        start.alignment = TextAlignmentOptions.MidlineLeft;
        start.raycastTarget = false;

        Image clock = Win95Ui.FindOrCreateImage("Win95ClockTray", taskbarRect, taskbarRect.childCount);
        RectTransform clockRect = clock.rectTransform;
        Win95Ui.Anchor(clockRect, new Vector2(1f, 0.5f), new Vector2(1f, 0.5f), new Vector2(1f, 0.5f));
        clockRect.anchoredPosition = new Vector2(-6f, 0f);
        clockRect.sizeDelta = new Vector2(98f, 24f);
        Win95Ui.ApplyRecessed(clock);

        TextMeshProUGUI clockText = Win95Ui.FindOrCreateText(clockRect, "Label");
        Win95Ui.Stretch(clockText.rectTransform);
        clockText.text = "10:95 PM";
        clockText.fontSize = 13f;
        clockText.color = Color.black;
        clockText.alignment = TextAlignmentOptions.Center;
        clockText.raycastTarget = false;
    }

    private void StyleImages()
    {
        foreach (Image image in GetComponentsInChildren<Image>(true))
        {
            string name = image.gameObject.name;
            if (name.StartsWith("Win95"))
                continue;

            if (name.Contains("Panel") || name == "Template" || name == "Viewport" || name.Contains("Button"))
                Win95Ui.ApplyRaised(image, true);
            else if (name == "Background" || name == "Fill")
                Win95Ui.ApplyRecessed(image, name == "Fill" ? Win95Ui.ActiveTitle : Win95Ui.Recessed);
            else if (name == "Handle" || name == "Checkmark")
                Win95Ui.ApplyRaised(image, false);
        }
    }

    private void StyleText()
    {
        foreach (TMP_Text text in GetComponentsInChildren<TMP_Text>(true))
        {
            if (text.gameObject.name == "Title")
                continue;

            text.color = Color.black;
            text.characterSpacing = 0f;
            text.enableWordWrapping = false;
            if (text.fontSize < 15f) text.fontSize = 15f;
            if (text.gameObject.name.Contains("ParamName")) text.fontSize = 13f;
        }
    }

    private void StyleControls()
    {
        foreach (Selectable selectable in GetComponentsInChildren<Selectable>(true))
        {
            ColorBlock colors = selectable.colors;
            colors.normalColor = Win95Ui.Face;
            colors.highlightedColor = Color.white;
            colors.pressedColor = Win95Ui.Recessed;
            colors.selectedColor = Win95Ui.Face;
            colors.disabledColor = Win95Ui.Disabled;
            colors.fadeDuration = 0.01f;
            selectable.colors = colors;
        }

        foreach (TMP_Dropdown dropdown in GetComponentsInChildren<TMP_Dropdown>(true))
        {
            if (dropdown.captionText != null) dropdown.captionText.color = Color.black;
            if (dropdown.itemText != null) dropdown.itemText.color = Color.black;
        }
    }
}
