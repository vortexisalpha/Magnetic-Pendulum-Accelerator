using UnityEngine;
using UnityEngine.UI;
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
            slider.SetValueWithoutNotify(0f);

        valChange(0f);
    }
    public void valChange(float value)
    {
        displayValue = paramMin + (paramMax - paramMin) * value;
        sliderVal.text = displayValue.ToString("0.00");
        SliderToImageTimer.OnSliderChanged();
        FlaskManager.PostControllerDataNow();
    }


}
