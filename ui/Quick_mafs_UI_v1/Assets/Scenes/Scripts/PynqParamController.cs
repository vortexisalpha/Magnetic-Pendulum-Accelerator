using System;
using System.Collections;
using UnityEngine;

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

// Slider/viewport → FPGA path via PynqConnection TCP. Renders are committed on
// mouse-up (or explicit confirm), never while dragging.
public class PynqParamController : MonoBehaviour
{
    public const int HighResThreshold = 360;

    public static event Action<bool, int, int, bool> HighResGateChanged;
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
    [SerializeField] GameObject panZoomController;

    private SliderTextDisplay dampingSlider;
    private SliderTextDisplay magneticSlider;
    private SliderTextDisplay lengthSlider;
    private SliderTextDisplay heightSlider;
    private ResolutionSlider resXSlider;
    private ResolutionSlider resYSlider;
    private PanZoom panZoom;

    private ControlData data = new ControlData();
    private bool slidersDirty;
    private bool viewportDirty;
    private bool syncScheduled;
    private bool slidersResolved;
    private bool bypassHighResGate;

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
            PynqConnection.Instance.Connected += OnPynqConnected;

        ScheduleFullSync();
    }

    void OnDestroy()
    {
        if (PynqConnection.Instance != null)
            PynqConnection.Instance.Connected -= OnPynqConnected;
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

        if (resXController != null)
            resXSlider = FindResolutionSlider(resXController) ?? FindResolutionByLabel("Res X");
        if (resYController != null)
            resYSlider = FindResolutionSlider(resYController) ?? FindResolutionByLabel("Res Y");

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
        instance.slidersDirty = true;
        instance.viewportDirty = true;
        instance.SendNow();
    }

    public static void NotifyViewportReleased()
    {
        if (instance == null) return;
        instance.viewportDirty = true;
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
    }

    private void SendNow()
    {
        if (PynqConnection.Instance == null) return;

        if (!(slidersDirty || viewportDirty))
            return;

        SnapshotSliders();

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
            Debug.Log($"[ParamManager] send γ={data.dampingFactor} μ={data.magneticStrength} L={data.pendulumLength} h={data.pendulumHeight} res={data.resX}x{data.resY}");
            PynqConnection.Instance.SendParams(
                data.dampingFactor,
                data.magneticStrength,
                data.pendulumLength,
                data.pendulumHeight,
                data.resX,
                data.resY);
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
}
