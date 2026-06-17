using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public sealed class UiTooltipInstaller : MonoBehaviour
{
    private const float RescanSeconds = 0.75f;
    private const string HelpHintName = "TooltipHelpHint";
    private const string HelpHintText = "Right-click anything for more info.";

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void CreateInstaller()
    {
        if (FindFirstObjectByType<UiTooltipInstaller>() != null)
            return;

        new GameObject("UiTooltipInstaller").AddComponent<UiTooltipInstaller>();
    }

    void Start()
    {
        StartCoroutine(InstallLoop());
    }

    private IEnumerator InstallLoop()
    {
        while (true)
        {
            InstallTooltips();
            yield return new WaitForSecondsRealtime(RescanSeconds);
        }
    }

    private static void InstallTooltips()
    {
        EnsureHelpHint();

        foreach (SliderTextDisplay display in FindObjectsByType<SliderTextDisplay>(
                     FindObjectsInactive.Include,
                     FindObjectsSortMode.None))
        {
            string text = GetParameterTooltip(display.ParamLabel);
            if (!string.IsNullOrEmpty(text))
                AttachToControl(GetTooltipRoot(display.transform), text);
        }

        foreach (ResolutionSlider slider in FindObjectsByType<ResolutionSlider>(
                     FindObjectsInactive.Include,
                     FindObjectsSortMode.None))
        {
            string text = GetParameterTooltip(slider.ParamLabel);
            if (!string.IsNullOrEmpty(text))
                AttachToControl(GetTooltipRoot(slider.transform), text);
        }

        foreach (Selectable selectable in FindObjectsByType<Selectable>(
                     FindObjectsInactive.Include,
                     FindObjectsSortMode.None))
        {
            string text = GetSelectableTooltip(selectable);
            if (!string.IsNullOrEmpty(text))
                AttachToControl(selectable.gameObject, text);
        }

        foreach (Graphic graphic in FindObjectsByType<Graphic>(
                     FindObjectsInactive.Include,
                     FindObjectsSortMode.None))
        {
            string text = GetGraphicTooltip(graphic);
            if (!string.IsNullOrEmpty(text))
                AttachToControl(graphic.gameObject, text);
        }
    }

    private static string GetParameterTooltip(string label)
    {
        if (string.IsNullOrWhiteSpace(label))
            return null;

        string key = label.Trim().ToLowerInvariant();
        if (key.Contains("damping"))
            return "Damping factor gamma. Higher values remove energy faster and shorten the pendulum motion.";
        if (key.Contains("magnetic"))
            return "Magnetic strength mu. Higher values make the magnets pull the pendulum more strongly.";
        if (key.Contains("length"))
            return "Pendulum length L used by the simulation model.";
        if (key.Contains("height"))
            return "Pendulum height h above the magnet plane. Larger heights weaken the magnetic effect.";
        if (key.Contains("res x"))
            return "Horizontal render resolution. Higher values add detail but make the board render more slowly.";
        if (key.Contains("res y"))
            return "Vertical render resolution. Higher values add detail but make the board render more slowly.";
        if (key.Contains("trajectory"))
            return "Trajectory sample count. Move this to choose how much of the selected pendulum path is drawn.";

        return null;
    }

    private static string GetSelectableTooltip(Selectable selectable)
    {
        string objectName = selectable.gameObject.name.Trim();

        if (selectable is TMP_Dropdown && objectName == "MapDropdown")
            return "Choose the visualisation: 2D maps, the 3D potential well, combined 3D view, or uncertainty dimension sweep.";

        if (selectable is Toggle && objectName == "LatencyStatsToggle")
            return "Show or hide timing measurements for camera processing, transfer, FPGA compute, image send, and total latency.";

        if (selectable is Button)
        {
            if (objectName == "View2DButton")
                return "Return to the interactive 2D map view and parameter controls.";
            if (objectName == "ButtonHome" || objectName == "ButtonHome2")
                return "Return to the home 2D map view.";
            if (objectName == "ConfirmButton")
                return "Send the pending high-resolution render request to the PYNQ board.";
            if (objectName == "TrajectoryCloseButton")
                return "Close the trajectory overlay.";
        }

        return null;
    }

    private static string GetGraphicTooltip(Graphic graphic)
    {
        string objectName = graphic.gameObject.name.Trim();

        if (objectName == "MagnetRendererWindow")
            return "Magnetic 3D visualisation showing the detected magnet positions used by the simulation.";

        return null;
    }

    private static GameObject GetTooltipRoot(Transform source)
    {
        if (source == null)
            return null;

        if (source is RectTransform)
            return source.gameObject;

        RectTransform parentRect = source.GetComponentInParent<RectTransform>(true);
        return parentRect != null ? parentRect.gameObject : source.gameObject;
    }

    private static void AttachToControl(GameObject root, string tooltip)
    {
        if (root == null || string.IsNullOrWhiteSpace(tooltip))
            return;

        EnsureRootHitTarget(root);
        Configure(root, tooltip);

        foreach (Selectable selectable in root.GetComponentsInChildren<Selectable>(true))
            Configure(selectable.gameObject, tooltip);

        foreach (Graphic graphic in root.GetComponentsInChildren<Graphic>(true))
        {
            if (graphic.raycastTarget)
                Configure(graphic.gameObject, tooltip);
        }
    }

    private static void Configure(GameObject target, string tooltip)
    {
        Configure(target, tooltip, false, false);
    }

    private static void Configure(GameObject target, string tooltip, bool hoverToShow)
    {
        Configure(target, tooltip, hoverToShow, false);
    }

    private static void Configure(GameObject target, string tooltip, bool hoverToShow, bool placeLeftOfOwner)
    {
        if (target.GetComponent<RectTransform>() == null)
            return;

        UiTooltipTrigger trigger = target.GetComponent<UiTooltipTrigger>();
        if (trigger == null)
            trigger = target.AddComponent<UiTooltipTrigger>();

        trigger.SetTooltip(tooltip, hoverToShow, placeLeftOfOwner);
    }

    private static void EnsureRootHitTarget(GameObject root)
    {
        if (root.GetComponent<RectTransform>() == null || root.GetComponent<Graphic>() != null)
            return;

        Image hitTarget = root.AddComponent<Image>();
        hitTarget.color = Color.clear;
        hitTarget.raycastTarget = true;
    }

    private static void EnsureHelpHint()
    {
        Canvas canvas = GameObject.Find("Canvas")?.GetComponent<Canvas>() ?? FindFirstObjectByType<Canvas>();
        if (canvas == null)
            return;

        Transform existing = canvas.transform.Find(HelpHintName);
        GameObject hintObject = existing != null
            ? existing.gameObject
            : new GameObject(HelpHintName, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));

        RectTransform hintRect = hintObject.GetComponent<RectTransform>();
        hintRect.SetParent(canvas.transform, false);
        hintRect.anchorMin = hintRect.anchorMax = new Vector2(1f, 0f);
        hintRect.pivot = new Vector2(1f, 0f);
        hintRect.anchoredPosition = new Vector2(-12f, 12f);
        hintRect.sizeDelta = new Vector2(28f, 28f);
        hintRect.SetAsLastSibling();

        Image background = hintObject.GetComponent<Image>();
        background.color = new Color(0.06f, 0.06f, 0.06f, 0.92f);
        background.raycastTarget = true;

        TextMeshProUGUI label = hintObject.GetComponentInChildren<TextMeshProUGUI>(true);
        if (label == null)
        {
            GameObject labelObject = new GameObject("QuestionMark", typeof(RectTransform), typeof(TextMeshProUGUI));
            RectTransform labelRect = labelObject.GetComponent<RectTransform>();
            labelRect.SetParent(hintRect, false);
            labelRect.anchorMin = Vector2.zero;
            labelRect.anchorMax = Vector2.one;
            labelRect.offsetMin = Vector2.zero;
            labelRect.offsetMax = Vector2.zero;
            label = labelObject.GetComponent<TextMeshProUGUI>();
        }

        label.text = "?";
        label.color = Color.white;
        label.fontSize = 20f;
        label.fontStyle = FontStyles.Bold;
        label.alignment = TextAlignmentOptions.Center;
        label.raycastTarget = false;

        Configure(hintObject, HelpHintText, true, true);
    }
}
