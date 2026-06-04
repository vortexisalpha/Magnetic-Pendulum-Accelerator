using System.Collections;
using UnityEngine;

[System.Serializable]
public class ControlData
{
    public float dampingFactor;
    public float magneticStrength;
    public float pendulumLength;
    public float pendulumHeight;
}

// Pushes the controller sliders to the PYNQ board over the shared TCP link.
// (Formerly POSTed JSON to the Flask /controller_data endpoint.)
public class FlaskManager : MonoBehaviour
{
    [SerializeField] GameObject dampingFactorController;
    [SerializeField] GameObject magneticStrengthController;
    [SerializeField] GameObject lengthController;
    [SerializeField] GameObject pendulumHeightController;

    [SerializeField] private float postInterval = 0.1f;

    //must serialize feild on gameobjects so cast to sliders to receive display values
    private SliderTextDisplay dampingSlider;
    private SliderTextDisplay magneticSlider;
    private SliderTextDisplay lengthSlider;
    private SliderTextDisplay heightSlider;

    private ControlData data = new ControlData();

    void Start()
    {
        dampingSlider = dampingFactorController.GetComponent<SliderTextDisplay>();
        magneticSlider = magneticStrengthController.GetComponent<SliderTextDisplay>();
        lengthSlider = lengthController.GetComponent<SliderTextDisplay>();
        heightSlider = pendulumHeightController.GetComponent<SliderTextDisplay>();

        StartCoroutine(SendLoop());
    }

    void OnDestroy()
    {
        StopAllCoroutines();
    }

    private IEnumerator SendLoop()
    {
        var wait = new WaitForSeconds(postInterval);
        while (true)
        {
            SnapshotSliders();

            if (PynqConnection.Instance != null)
            {
                PynqConnection.Instance.SendParams(
                    data.dampingFactor,
                    data.magneticStrength,
                    data.pendulumLength,
                    data.pendulumHeight);
            }

            yield return wait;
        }
    }

    private void SnapshotSliders()
    {
        data.dampingFactor = dampingSlider.displayValue;
        data.magneticStrength = magneticSlider.displayValue;
        data.pendulumLength = lengthSlider.displayValue;
        data.pendulumHeight = heightSlider.displayValue;
    }
}
