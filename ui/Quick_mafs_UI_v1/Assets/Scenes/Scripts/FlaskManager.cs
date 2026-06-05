using UnityEngine;

[System.Serializable]
public class ControlData
{
    public float dampingFactor;
    public float magneticStrength;
    public float pendulumLength;
    public float pendulumHeight;
}

// Debounced, event-driven slider → PYNQ PARAMS over TCP.
public class FlaskManager : MonoBehaviour
{
    static FlaskManager instance;

    [SerializeField] GameObject dampingFactorController;
    [SerializeField] GameObject magneticStrengthController;
    [SerializeField] GameObject lengthController;
    [SerializeField] GameObject pendulumHeightController;

    [Tooltip("Seconds to wait after the last slider tick before sending while dragging.")]
    [SerializeField] private float sendDebounce = 0.15f;

    private SliderTextDisplay dampingSlider;
    private SliderTextDisplay magneticSlider;
    private SliderTextDisplay lengthSlider;
    private SliderTextDisplay heightSlider;

    private ControlData data = new ControlData();
    private bool slidersDirty;
    private float debounceTimer;

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

        SendParamsNow();
    }

    void OnDestroy()
    {
        if (instance == this) instance = null;
    }

    void Update()
    {
        if (!slidersDirty)
            return;

        debounceTimer -= Time.deltaTime;
        if (debounceTimer <= 0f)
            SendParamsNow();
    }

    public static void NotifySliderChanged()
    {
        if (instance == null) return;
        instance.slidersDirty = true;
        instance.debounceTimer = instance.sendDebounce;
    }

    public static void NotifySliderReleased()
    {
        if (instance == null) return;
        instance.slidersDirty = true;
        instance.debounceTimer = 0f;
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

        if (PynqConnection.Instance == null) return;
        PynqConnection.Instance.SendParams(
            data.dampingFactor,
            data.magneticStrength,
            data.pendulumLength,
            data.pendulumHeight);
    }
}
