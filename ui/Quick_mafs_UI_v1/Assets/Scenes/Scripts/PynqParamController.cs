using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

[System.Serializable]
public class ControlData
{
    public float dampingFactor = 0.2f;
    public float magneticStrength = 1.0f;
    public float pendulumLength = 1.0f;
    public float pendulumHeight = 0.5f;
    public int resX = 120;
    public int resY = 120;
}

public class PynqParamController : MonoBehaviour
{
    public const int HighResThreshold = 360;
    private const int PreviewMaxResolution = 120;
    private const int ResolutionStep = 12;
    private const int PreviewHeightStep = 60;
    private const float PreviewTimeoutSeconds = 0.35f;

    public static event Action<bool, int, int, bool> HighResGateChanged;
    public static event Action<ControlData> ParametersChanged;
    public static event Action ViewportChanged;
    public static bool IsHighResGateActive { get; private set; }
    public static bool IsHighResRenderPending { get; private set; }
    public static int PendingResX { get; private set; }
    public static int PendingResY { get; private set; }

    static PynqParamController instance;

    [SerializeField] GameObject dampingFactorController;
    [SerializeField] GameObject magneticStrengthController;
    [SerializeField] GameObject lengthController;
    [SerializeField] GameObject pendulumHeightController;
    [SerializeField] GameObject resXController;
    [SerializeField] GameObject resYController;
    [SerializeField] GameObject resolutionDropdownController;
    [SerializeField] GameObject panZoomController;

    private SliderTextDisplay dampingSlider;
    private SliderTextDisplay magneticSlider;
    private SliderTextDisplay lengthSlider;
    private SliderTextDisplay heightSlider;
    private ResolutionSlider resXSlider;
    private ResolutionSlider resYSlider;
    private ResolutionDropdown resolutionDropdown;
    private PanZoom panZoom;

    private ControlData data = new ControlData();
    private bool slidersDirty;
    private bool viewportDirty;
    private bool syncScheduled;
    private bool slidersResolved;
    private bool bypassHighResGate;
    private bool previewInFlight;
    private bool previewPending;
    private int previewVersionFloor;
    private Coroutine previewTimeoutRoutine;
    private readonly HashSet<Slider> previewBoundSliders = new HashSet<Slider>();

    public static ControlData CurrentData
    {
        get
        {
            if (instance == null) return new ControlData();
            instance.SnapshotSliders();
            return CopyData(instance.data);
        }
    }

    void Awake()
    {
        instance = this;
    }

    void Start()
    {
        ResolveSliders();

        if (GetComponent<HighResRenderGate>() == null)
            gameObject.AddComponent<HighResRenderGate>();

        if (PynqConnection.Instance != null)
        {
            PynqConnection.Instance.Connected += OnPynqConnected;
            PynqConnection.Instance.ImageReceived += OnImageReceived;
        }

        ScheduleFullSync();
    }

    void OnDestroy()
    {
        if (PynqConnection.Instance != null)
        {
            PynqConnection.Instance.Connected -= OnPynqConnected;
            PynqConnection.Instance.ImageReceived -= OnImageReceived;
        }
        if (instance == this) instance = null;
    }

    void OnPynqConnected() => ScheduleFullSync();

    void ScheduleFullSync()
    {
        if (syncScheduled) return;
        syncScheduled = true;
        StartCoroutine(DeferredFullSync());
    }

    IEnumerator DeferredFullSync()
    {
        yield return null;
        syncScheduled = false;
        ResolveSliders();
        RequestFullSync();
    }

    void ResolveSliders()
    {
        dampingSlider = FindSliderDisplay(dampingFactorController) ?? FindSliderByLabel("Damping");
        magneticSlider = FindSliderDisplay(magneticStrengthController) ?? FindSliderByLabel("Magnetic");
        lengthSlider = FindSliderDisplay(lengthController) ?? FindSliderByLabel("Length");
        heightSlider = FindSliderDisplay(pendulumHeightController) ?? FindSliderByLabel("Height");
        BindPreviewSlider(dampingSlider);
        BindPreviewSlider(magneticSlider);
        BindPreviewSlider(lengthSlider);
        BindPreviewSlider(heightSlider);

        if (resXController != null)
            resXSlider = FindResolutionSlider(resXController) ?? FindResolutionByLabel("Res X");
        if (resYController != null)
            resYSlider = FindResolutionSlider(resYController) ?? FindResolutionByLabel("Res Y");

        if (resolutionDropdownController != null && resolutionDropdown == null)
            resolutionDropdown = resolutionDropdownController.GetComponent<ResolutionDropdown>()
                ?? resolutionDropdownController.GetComponentInChildren<ResolutionDropdown>(true);

        if (panZoomController != null && panZoom == null)
            panZoom = panZoomController.GetComponent<PanZoom>();

        slidersResolved = dampingSlider != null && magneticSlider != null
            && lengthSlider != null && heightSlider != null;

        if (!slidersResolved)
            Debug.LogWarning("[ParamManager] Could not resolve all SliderTextDisplay refs — check ParamManager wiring.");
    }

    static SliderTextDisplay FindSliderDisplay(GameObject controller)
    {
        if (controller == null) return null;
        return controller.GetComponent<SliderTextDisplay>()
            ?? controller.GetComponentInChildren<SliderTextDisplay>(true)
            ?? controller.GetComponentInParent<SliderTextDisplay>(true);
    }

    void BindPreviewSlider(SliderTextDisplay display)
    {
        if (display == null)
            return;

        Slider slider = display.GetComponent<Slider>()
            ?? display.GetComponentInChildren<Slider>(true)
            ?? display.GetComponentInParent<Slider>();

        if (slider == null || previewBoundSliders.Contains(slider))
            return;

        slider.onValueChanged.AddListener(_ => NotifySliderChanged());
        previewBoundSliders.Add(slider);
    }

    static ResolutionSlider FindResolutionSlider(GameObject controller)
    {
        if (controller == null) return null;
        return controller.GetComponent<ResolutionSlider>()
            ?? controller.GetComponentInChildren<ResolutionSlider>(true)
            ?? controller.GetComponentInParent<ResolutionSlider>(true);
    }

    static SliderTextDisplay FindSliderByLabel(string labelPart)
    {
        var displays = UnityEngine.Object.FindObjectsByType<SliderTextDisplay>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        foreach (var display in displays)
        {
            if (display.ParamLabel.IndexOf(labelPart, System.StringComparison.OrdinalIgnoreCase) >= 0)
                return display;
        }
        return null;
    }

    static ResolutionSlider FindResolutionByLabel(string labelPart)
    {
        var sliders = UnityEngine.Object.FindObjectsByType<ResolutionSlider>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        foreach (var slider in sliders)
        {
            if (slider.ParamLabel.IndexOf(labelPart, System.StringComparison.OrdinalIgnoreCase) >= 0)
                return slider;
        }
        return null;
    }

    public void RequestFullSync()
    {
        slidersDirty = true;
        viewportDirty = true;
        SendNow();
    }

    public static void ConfirmHighResRender()
    {
        if (instance == null) return;
        instance.bypassHighResGate = true;
        instance.slidersDirty = true;
        instance.viewportDirty = true;
        instance.SendNow();
        instance.bypassHighResGate = false;
    }

    static bool RequiresHighResConfirm(int resX, int resY) =>
        resX > HighResThreshold || resY > HighResThreshold;

    void NotifyHighResGate(bool gateActive, int resX, int resY, bool pending)
    {
        IsHighResGateActive = gateActive;
        IsHighResRenderPending = pending;
        PendingResX = resX;
        PendingResY = resY;
        HighResGateChanged?.Invoke(gateActive, resX, resY, pending);
    }

    public static void NotifySliderReleased()
    {
        if (instance == null) return;
        if (!instance.slidersResolved) instance.ResolveSliders();
        SliderToImageTimer.OnSliderChanged();
        instance.previewInFlight = false;
        instance.previewPending = false;
        instance.StopPreviewTimeout();
        instance.slidersDirty = true;
        instance.viewportDirty = true;
        instance.SendNow();
    }

    public static void NotifySliderChanged()
    {
        if (instance == null) return;
        if (!instance.slidersResolved) instance.ResolveSliders();
        if (PynqConnection.Instance == null)
        {
            instance.SnapshotAndPublishSliders();
            return;
        }
        instance.SendPreviewWhileDragging();
    }

    public static void NotifyViewportReleased()
    {
        if (instance == null) return;
        if (!instance.slidersResolved) instance.ResolveSliders();
        instance.previewInFlight = false;
        instance.previewPending = false;
        instance.slidersDirty = true;
        instance.viewportDirty = true;
        instance.SendNow();
    }

    public static void NotifyViewportChanged()
    {
        ViewportChanged?.Invoke();
        if (instance == null || PynqConnection.Instance == null) return;
        if (!instance.slidersResolved) instance.ResolveSliders();
        instance.SendPreviewWhileDragging();
    }

    public static void NotifyMagnetPositionsChanged()
    {
        if (instance == null || PynqConnection.Instance == null) return;
        if (!instance.slidersResolved) instance.ResolveSliders();
        instance.SendPreviewWhileDragging();
    }

    public static void NotifyMagnetPositionsSettled()
    {
        if (instance == null) return;
        if (!instance.slidersResolved) instance.ResolveSliders();
        instance.previewInFlight = false;
        instance.previewPending = false;
        instance.StopPreviewTimeout();
        instance.slidersDirty = true;
        instance.SendNow();
    }

    public static void NotifyFssChanged()
    {
        if (instance == null) return;
        instance.slidersDirty = true;
        instance.SendNow();
    }

    private void SnapshotSliders()
    {
        if (!slidersResolved) ResolveSliders();

        if (dampingSlider != null) data.dampingFactor = dampingSlider.GetCurrentValue();
        if (magneticSlider != null) data.magneticStrength = magneticSlider.GetCurrentValue();
        if (lengthSlider != null) data.pendulumLength = lengthSlider.GetCurrentValue();
        if (heightSlider != null) data.pendulumHeight = heightSlider.GetCurrentValue();
        if (resXSlider != null && resXSlider.Resolution > 0) data.resX = resXSlider.Resolution;
        if (resYSlider != null && resYSlider.Resolution > 0) data.resY = resYSlider.Resolution;

        if (resolutionDropdown != null && resolutionDropdown.ResolutionX > 0)
        {
            data.resX = resolutionDropdown.ResolutionX;
            data.resY = resolutionDropdown.ResolutionY;
        }
    }

    private void SnapshotAndPublishSliders()
    {
        SnapshotSliders();
        ParametersChanged?.Invoke(CopyData(data));
    }

    private void SendNow()
    {
        if (!(slidersDirty || viewportDirty))
            return;

        SnapshotAndPublishSliders();

        if (PynqConnection.Instance == null) return;

        bool highRes = RequiresHighResConfirm(data.resX, data.resY);
        if (highRes && !bypassHighResGate)
        {
            slidersDirty = false;
            viewportDirty = false;
            NotifyHighResGate(true, data.resX, data.resY, true);
            return;
        }

        NotifyHighResGate(highRes, data.resX, data.resY, false);

        if (slidersDirty)
        {
            SendParams(data, "final", true);
            slidersDirty = false;
        }

        if (viewportDirty)
        {
            if (panZoom != null)
            {
                panZoom.GetViewportBounds(out float xMin, out float xMax, out float yMin, out float yMax);
                PynqConnection.Instance.SendViewport(xMin, xMax, yMin, yMax);
            }
            viewportDirty = false;
        }
    }

    void SendPreviewWhileDragging()
    {
        SnapshotAndPublishSliders();

        if (previewInFlight)
        {
            previewPending = true;
            return;
        }

        GetPreviewResolution(data.resX, data.resY, out int previewResX, out int previewResY);

        var previewData = CopyData(data);
        previewData.resX = previewResX;
        previewData.resY = previewResY;

        SendParams(previewData, "drag-preview");
        SendViewportIfNeeded(true);
        previewInFlight = true;
        previewPending = false;
        previewVersionFloor = PendulumRenderer.LastFetchedVersion;
        RestartPreviewTimeout();
    }

    void OnImageReceived(ImageMessage msg)
    {
        if (!previewInFlight || msg.version <= previewVersionFloor)
            return;

        previewInFlight = false;
        StopPreviewTimeout();

        if (previewPending)
            SendPreviewWhileDragging();
    }

    IEnumerator ClearPreviewInFlightAfterTimeout()
    {
        yield return new WaitForSeconds(PreviewTimeoutSeconds);
        previewTimeoutRoutine = null;

        if (!previewInFlight)
            yield break;

        previewInFlight = false;
        if (previewPending)
            SendPreviewWhileDragging();
    }

    void RestartPreviewTimeout()
    {
        StopPreviewTimeout();
        previewTimeoutRoutine = StartCoroutine(ClearPreviewInFlightAfterTimeout());
    }

    void StopPreviewTimeout()
    {
        if (previewTimeoutRoutine == null)
            return;

        StopCoroutine(previewTimeoutRoutine);
        previewTimeoutRoutine = null;
    }

    void SendParams(ControlData paramsData, string phase, bool force = false)
    {
        Debug.Log($"[ParamManager] send {phase} gamma={paramsData.dampingFactor} mu={paramsData.magneticStrength} L={paramsData.pendulumLength} h={paramsData.pendulumHeight} res={paramsData.resX}x{paramsData.resY}");
        PynqConnection.Instance.SendParams(
            paramsData.dampingFactor,
            paramsData.magneticStrength,
            paramsData.pendulumLength,
            paramsData.pendulumHeight,
            paramsData.resX,
            paramsData.resY,
            force);
    }

    void SendViewportIfNeeded(bool shouldSend)
    {
        if (!shouldSend || panZoom == null)
            return;

        panZoom.GetViewportBounds(out float xMin, out float xMax, out float yMin, out float yMax);
        PynqConnection.Instance.SendViewport(xMin, xMax, yMin, yMax);
    }

    static ControlData CopyData(ControlData source)
    {
        return new ControlData
        {
            dampingFactor = source.dampingFactor,
            magneticStrength = source.magneticStrength,
            pendulumLength = source.pendulumLength,
            pendulumHeight = source.pendulumHeight,
            resX = source.resX,
            resY = source.resY,
        };
    }

    static void GetPreviewResolution(int finalResX, int finalResY, out int previewResX, out int previewResY)
    {
        int maxRes = Mathf.Max(finalResX, finalResY);
        if (maxRes <= PreviewMaxResolution)
        {
            previewResX = finalResX;
            previewResY = finalResY;
            return;
        }

        float scale = PreviewMaxResolution / (float)maxRes;
        previewResX = RoundToStep(Mathf.Max(ResolutionStep, Mathf.RoundToInt(finalResX * scale)), ResolutionStep);
        previewResY = RoundToStep(Mathf.Max(PreviewHeightStep, Mathf.RoundToInt(finalResY * scale)), PreviewHeightStep);
        previewResX = Mathf.Min(previewResX, finalResX);
        previewResY = Mathf.Min(previewResY, finalResY);
    }

    static int RoundToStep(int value, int step)
    {
        return Mathf.Max(step, Mathf.RoundToInt(value / (float)step) * step);
    }

}
