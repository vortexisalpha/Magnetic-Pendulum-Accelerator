using UnityEngine;
using TMPro;

public class SliderTextDisplay : MonoBehaviour
{
    [SerializeField] private TextMeshProUGUI sliderVal = null;
    [SerializeField] private TextMeshProUGUI paramName = null;
    [SerializeField] private float paramMin, paramMax;
    [SerializeField] private string param;

    public float displayValue { get; private set; }

    void Start()
    {
        sliderVal.text = paramMin.ToString(paramMin);
        paramName.text = paramMin;
    }
    public void valChange(float value)
    {
        displayValue = paramMin + (paramMax - paramMin) * value;
        sliderVal.text = displayValue.ToString("0.00");
        SliderToImageTimer.OnSliderChanged();
        FlaskManager.PostControllerDataNow();
    }


}
