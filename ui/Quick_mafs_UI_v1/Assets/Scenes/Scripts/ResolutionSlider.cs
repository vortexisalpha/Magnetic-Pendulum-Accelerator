using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using TMPro;

//resolution slider that only takes values that are multiples of `step` (default
//12) and never 0. the slider runs in step-count units (minSteps..maxSteps) so
//every value is an exact multiple; the reported Resolution is stepCount * step.
public class ResolutionSlider : MonoBehaviour
{
    [SerializeField] private TextMeshProUGUI sliderVal = null;
    [SerializeField] private TextMeshProUGUI paramName = null;
    [SerializeField] private Slider slider = null;
    [SerializeField] private string param = "Resolution";

    public string ParamLabel => param;

    [Tooltip("Resolution increment; values are multiples of this, excluding 0.")]
    [SerializeField] private int step = 12;
    [Tooltip("Smallest multiplier (>=1 so 0 is excluded).")]
    [SerializeField] private int minSteps = 1;
    [SerializeField] private int maxSteps = 50;
    [SerializeField] private int defaultSteps = 10;

    public int Resolution { get; private set; }

    void Awake()
    {
        Resolution = Mathf.Clamp(defaultSteps, Mathf.Max(1, minSteps), maxSteps) * step;

        // Param prefab ships with SliderTextDisplay on ParamControl; disable it so
        // it doesn't reset the slider or overwrite SliderVal on Start.
        foreach (var legacy in GetComponentsInChildren<SliderTextDisplay>(true))
            legacy.enabled = false;
    }

    void Start()
    {
        if (paramName != null) paramName.text = param;
        if (slider == null) slider = GetComponent<Slider>();

        if (slider != null)
        {
            slider.wholeNumbers = true;
            slider.minValue = Mathf.Max(1, minSteps);
            slider.maxValue = Mathf.Max(slider.minValue, maxSteps);
            slider.onValueChanged.RemoveAllListeners();
            slider.onValueChanged.AddListener(valChange);
            slider.SetValueWithoutNotify(Mathf.Clamp(defaultSteps, (int)slider.minValue, (int)slider.maxValue));
            BindSliderRelease(slider.gameObject);
        }

        UpdateResolution(slider != null ? slider.value : defaultSteps);
    }

    void BindSliderRelease(GameObject sliderObject)
    {
        var trigger = sliderObject.GetComponent<EventTrigger>();
        if (trigger == null)
            trigger = sliderObject.AddComponent<EventTrigger>();

        var entry = new EventTrigger.Entry { eventID = EventTriggerType.PointerUp };
        entry.callback.AddListener(_ => OnPointerUp());
        trigger.triggers.Add(entry);
    }

    void OnPointerUp()
    {
        SliderToImageTimer.OnSliderChanged();
        PynqParamController.NotifySliderReleased();
    }

    // Hook to the Slider's On Value Changed (Single) — UI only; TCP send happens in OnPointerUp.
    public void valChange(float value) => UpdateResolution(value);

    void UpdateResolution(float value)
    {
        int steps = Mathf.Clamp(Mathf.RoundToInt(value), Mathf.Max(1, minSteps), maxSteps);
        Resolution = steps * step;
        if (sliderVal != null) sliderVal.text = Resolution.ToString();
    }
}
