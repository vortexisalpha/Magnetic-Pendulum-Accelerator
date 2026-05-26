using UnityEngine;
using TMPro;

public class SliderTextDisplay : MonoBehaviour
{
    [SerializeField] private TextMeshProUGUI sliderVal = null;
    [SerializeField] private TextMeshProUGUI paramName = null;
    [SerializeField] private float param_min, param_max;
    [SerializeField] private string param;

    public float displayValue { get ; private set ; }

    void Start()
    {
        sliderVal.text = param_min.ToString("0.00");
        paramName.text = param;
    }
    public void ValChange(float value)
    {
        displayValue = param_min + (param_max - param_min) * value;
        sliderVal.text = displayValue.ToString("0.00");
    }


}
