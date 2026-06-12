using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using TMPro;

public class SliderTextDisplay : MonoBehaviour
{
    [SerializeField] private TextMeshProUGUI sliderVal = null;
    [SerializeField] private TextMeshProUGUI paramName = null;
    [SerializeField] private Slider slider = null;
    [SerializeField] private float paramMin, paramMax;
    [SerializeField] private string param;

    public string ParamLabel => param;
    public float displayValue { get; private set; }
    private bool initialized;

    public float GetCurrentValue()
    {
        ResolveSlider();

        if (slider != null)
            return Mathf.Round((paramMin + (paramMax - paramMin) * slider.value) * 100f) / 100f;

        return displayValue;
    }

    void Start()
    {
        if (paramName != null) paramName.text = param;

        ResolveSlider();

        if (slider != null)
        {
            slider.SetValueWithoutNotify(0f);
            slider.onValueChanged.AddListener(OnSliderValueChanged);
            BindSliderRelease(slider.gameObject);
        }

        valChange(0f);
        initialized = true;
    }

    void ResolveSlider()
    {
        if (slider != null)
            return;

        slider = GetComponent<Slider>()
            ?? GetComponentInChildren<Slider>(true)
            ?? GetComponentInParent<Slider>();
    }

    static void BindSliderRelease(GameObject sliderObject)
    {
        var trigger = sliderObject.GetComponent<EventTrigger>();
        if (trigger == null)
            trigger = sliderObject.AddComponent<EventTrigger>();

        var entry = new EventTrigger.Entry { eventID = EventTriggerType.PointerUp };
        entry.callback.AddListener(_ =>
        {
            PynqParamController.NotifySliderReleased();
        });
        trigger.triggers.Add(entry);
    }

    public void valChange(float value) => UpdateDisplay(value);

    void OnSliderValueChanged(float value)
    {
        UpdateDisplay(value);

        if (initialized)
            PynqParamController.NotifySliderChanged();
    }

    void UpdateDisplay(float value)
    {
        displayValue = Mathf.Round((paramMin + (paramMax - paramMin) * value) * 100f) / 100f;
        if (sliderVal != null) sliderVal.text = displayValue.ToString("0.00");
    }
}
