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

    public float displayValue { get; private set; }

    void Start()
    {
        paramName.text = param;

        if (slider == null)
            slider = GetComponentInParent<Slider>();

        if (slider != null)
        {
            slider.SetValueWithoutNotify(0f);
            BindSliderRelease(slider.gameObject);
        }

        valChange(0f);
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

    public void valChange(float value)
    {
        displayValue = Mathf.Round((paramMin + (paramMax - paramMin) * value) * 100f) / 100f;
        sliderVal.text = displayValue.ToString("0.00");
        SliderToImageTimer.OnSliderChanged();
        PynqParamController.NotifySliderChanged();
    }
}
