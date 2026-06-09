using UnityEngine;

[System.Serializable]
public class ControlData
{
    public float dampingFactor;
    public float magneticStrength;
    public float pendulumLength;
    public float pendulumHeight;
}

// Throttled slider → FPGA path via PynqConnection TCP (no Flask).
public class PynqParamController : MonoBehaviour
{
    static PynqParamController instance;

    [SerializeField] GameObject dampingFactorController;
    [SerializeField] GameObject magneticStrengthController;
    [SerializeField] GameObject lengthController;
    [SerializeField] GameObject pendulumHeightController;
    [SerializeField] GameObject panZoomController;

    [Tooltip("Minimum seconds between PARAMS sends while dragging.")]
    [SerializeField] private float sendInterval = 0.1f;

    private SliderTextDisplay dampingSlider;
    private SliderTextDisplay magneticSlider;
    private SliderTextDisplay lengthSlider;
    private SliderTextDisplay heightSlider;
    private PanZoom panZoom;

    private ControlData data = new ControlData();
    private bool slidersDirty;
    private bool viewportDirty;
    private float nextSendAllowedTime;

    void Awake()
    {
        instance = this;
    }

    void Start()
    {
        dampingSlider = dampingFactorController.GetComponent<SliderTextDisplay>();
        magneticSlider = magneticStrengthController.GetComponent<SliderTextDisplay>();
        lengthSlider = lengthController.GetComponent<SliderTextDisplay>();
        heightSlider = pendulumHeightController.GetComponent<SliderTextDisplay>();
        panZoom = panZoomController.GetComponent<PanZoom>();

        nextSendAllowedTime = 0f;
        SendNow();
    }

    void OnDestroy()
    {
        if (instance == this) instance = null;
    }

    void Update()
    {
        if (!(slidersDirty || viewportDirty) || Time.time < nextSendAllowedTime)
            return;

        SendNow();
    }

    public static void NotifySliderChanged()
    {
        if (instance == null) return;
        instance.slidersDirty = true;
    }

    public static void NotifyViewportChanged()
    {
        if (instance == null) return;
        instance.viewportDirty = true;
    }

    public static void NotifySliderReleased()
    {
        if (instance == null) return;
        instance.slidersDirty = true;
        instance.nextSendAllowedTime = 0f; // forcing an immediate send on slider release
    }

    //fss mode toggled: resend params now so the board re-renders in the new mode
    public static void NotifyFssChanged()
    {
        if (instance == null) return;
        instance.slidersDirty = true;
        instance.nextSendAllowedTime = 0f;
    }

    private void SnapshotSliders()
    {
        data.dampingFactor = dampingSlider.displayValue;
        data.magneticStrength = magneticSlider.displayValue;
        data.pendulumLength = lengthSlider.displayValue;
        data.pendulumHeight = heightSlider.displayValue;
    }

    private void SendNow()
    {
        if (PynqConnection.Instance == null) return;

        nextSendAllowedTime = Time.time + sendInterval;

        if (slidersDirty)
        {
            SnapshotSliders();
            PynqConnection.Instance.SendParams(
                data.dampingFactor,
                data.magneticStrength,
                data.pendulumLength,
                data.pendulumHeight);
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
