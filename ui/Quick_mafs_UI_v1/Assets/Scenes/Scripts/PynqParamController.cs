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

    [Tooltip("Minimum seconds between PARAMS sends while dragging.")]
    [SerializeField] private float sendInterval = 0.1f;

    private SliderTextDisplay dampingSlider;
    private SliderTextDisplay magneticSlider;
    private SliderTextDisplay lengthSlider;
    private SliderTextDisplay heightSlider;

    private ControlData data = new ControlData();
    private bool slidersDirty;
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

        nextSendAllowedTime = 0f;
        SendParamsNow();
    }

    void OnDestroy()
    {
        if (instance == this) instance = null;
    }

    void Update()
    {
        if (!slidersDirty || Time.time < nextSendAllowedTime)
            return;

        SendParamsNow();
    }

    public static void NotifySliderChanged()
    {
        if (instance == null) return;
        instance.slidersDirty = true;
    }

    public static void NotifySliderReleased()
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

    private void SendParamsNow()
    {
        SnapshotSliders();
        slidersDirty = false;
        nextSendAllowedTime = Time.time + sendInterval;

        if (PynqConnection.Instance == null) return;
        PynqConnection.Instance.SendParams(
            data.dampingFactor,
            data.magneticStrength,
            data.pendulumLength,
            data.pendulumHeight);
    }
}
