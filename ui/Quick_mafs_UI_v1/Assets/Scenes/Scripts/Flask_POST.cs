using UnityEngine;

public class ControlData 
{
    public float dampingFactor;
    public float magneticStrength;
    public float pendulumLength;
    public float pendulumHeight;
}

public class FlaskManager : MonoBehaviour
{

    [SerializeField] GameObject dampingFactorController;
    [SerializeField] GameObject magneticStrengthController;
    [SerializeField] GameObject lengthController;
    [SerializeField] GameObject pendulumHeightController;
    
    ControlData data = new ControlData();

    private string compileJson(float dampingFactor, float magneticStrength, float pendulumLength, float pendulumHeight) {
        data.dampingFactor = dampingFactor;
        data.magneticStrength = magneticStrength;
        data.pendulumLength = pendulumLength;
        data.pendulumHeight = pendulumHeight;

        string json = JsonUtility.ToJson(data, true);
        return json;
    }

    void Update() {
        //compile data:
        float dampingFactor = dampingFactorController.displayValue;
        float magneticStrength = magneticStrengthController.displayValue;
        float pendulumLength = lengthController.displayValue;
        float pendulumHeight = pendulumHeightController.displayValue;

        string json = compileJson(dampingFactor, magneticStrength, pendulumLength, pendulumHeight);
        
        //post to flask

    }
}
