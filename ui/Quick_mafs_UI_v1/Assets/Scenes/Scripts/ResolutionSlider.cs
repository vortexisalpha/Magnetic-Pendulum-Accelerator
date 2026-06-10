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

    [Tooltip("Resolution increment; values are multiples of this, excluding 0.")]
    [SerializeField] private int step = 12;
    [Tooltip("Smallest multiplier (>=1 so 0 is excluded).")]
    [SerializeField] private int minSteps = 1;
    [SerializeField] private int maxSteps = 30;
    [SerializeField] private int defaultSteps = 10;

    public int Resolution { get; private set; }

    void Start()
    {
        if (paramName != null) paramName.text = param;
        if (slider == null) slider = GetComponentInParent<Slider>();

        if (slider != null)
        {
            //force discrete, integer step-count values on the slider itself
            slider.wholeNumbers = true;
            slider.minValue = Mathf.Max(1, minSteps);
            slider.maxValue = Mathf.Max(slider.minValue, maxSteps);
            slider.SetValueWithoutNotify(Mathf.Clamp(defaultSteps, (int)slider.minValue, (int)slider.maxValue));
            BindSliderRelease(slider.gameObject);
        }

        valChange(slider != null ? slider.value : defaultSteps);
    }

    static void BindSliderRelease(GameObject sliderObject)
    {
        var trigger = sliderObject.GetComponent<EventTrigger>();
        if (trigger == null)
            trigger = sliderObject.AddComponent<EventTrigger>();

        var entry = new EventTrigger.Entry { eventID = EventTriggerType.PointerUp };
        entry.callback.AddListener(_ => PynqParamController.NotifySliderReleased());
        trigger.triggers.Add(entry);
    }

    //hook to the Slider's On Value Changed (Single)
    public void valChange(float value)
    {
        int steps = Mathf.Clamp(Mathf.RoundToInt(value), Mathf.Max(1, minSteps), maxSteps);
        Resolution = steps * step;

        if (sliderVal != null) sliderVal.text = Resolution.ToString();
        SliderToImageTimer.OnSliderChanged();
        PynqParamController.NotifySliderChanged();
    }
}
