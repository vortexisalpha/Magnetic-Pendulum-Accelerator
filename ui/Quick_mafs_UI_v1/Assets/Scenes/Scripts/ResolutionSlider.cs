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
    private bool initialized;

    void Awake()
    {
        Resolution = Mathf.Clamp(defaultSteps, Mathf.Max(1, minSteps), maxSteps) * step;
        ResolveRefs();

        // Param prefab ships with SliderTextDisplay on ParamControl; disable it so
        // it doesn't reset the slider or overwrite SliderVal on Start.
        foreach (var legacy in GetComponentsInChildren<SliderTextDisplay>(true))
            legacy.enabled = false;
    }

    void Start()
    {
        ResolveRefs();
        LayoutValueDisplay();

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
        initialized = true;
    }

    void ResolveRefs()
    {
        if (slider == null)
            slider = GetComponent<Slider>() ?? GetComponentInChildren<Slider>(true);

        if (sliderVal == null || paramName == null)
        {
            foreach (var tmp in GetComponentsInChildren<TextMeshProUGUI>(true))
            {
                if (sliderVal == null && tmp.gameObject.name == "SliderVal")
                    sliderVal = tmp;
                if (paramName == null && tmp.gameObject.name == "ParamName")
                    paramName = tmp;
            }
        }
    }

    // Prefab text is laid out for horizontal sliders; stack label + value below
    // the vertical resolution track.
    void LayoutValueDisplay()
    {
        if (paramName != null)
        {
            paramName.text = param;
            paramName.enableWordWrapping = false;
            paramName.overflowMode = TextOverflowModes.Overflow;
            paramName.fontSize = 11f;
            paramName.alignment = TextAlignmentOptions.Center;

            RectTransform nameRt = paramName.rectTransform;
            nameRt.anchorMin = new Vector2(0.5f, 0f);
            nameRt.anchorMax = new Vector2(0.5f, 0f);
            nameRt.pivot = new Vector2(0.5f, 1f);
            nameRt.anchoredPosition = new Vector2(0f, -6f);
            nameRt.sizeDelta = new Vector2(72f, 18f);
        }

        if (sliderVal == null)
            return;

        sliderVal.enableWordWrapping = false;
        sliderVal.overflowMode = TextOverflowModes.Overflow;
        sliderVal.color = Color.white;
        sliderVal.fontStyle = FontStyles.Bold;
        sliderVal.fontSize = 16f;
        sliderVal.alignment = TextAlignmentOptions.Center;

        RectTransform valueRt = sliderVal.rectTransform;
        valueRt.anchorMin = new Vector2(0.5f, 0f);
        valueRt.anchorMax = new Vector2(0.5f, 0f);
        valueRt.pivot = new Vector2(0.5f, 1f);
        valueRt.anchoredPosition = new Vector2(0f, -26f);
        valueRt.sizeDelta = new Vector2(72f, 22f);
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
        PynqParamController.NotifySliderReleased();
    }

    public void valChange(float value) => UpdateResolution(value);

    void UpdateResolution(float value)
    {
        int steps = Mathf.Clamp(Mathf.RoundToInt(value), Mathf.Max(1, minSteps), maxSteps);
        Resolution = steps * step;

        string valueText = Resolution.ToString();
        if (sliderVal != null)
            sliderVal.text = valueText;
        else if (paramName != null)
            paramName.text = $"{param}\n{valueText}";

        if (initialized)
            PynqParamController.NotifySliderChanged();
    }
}
