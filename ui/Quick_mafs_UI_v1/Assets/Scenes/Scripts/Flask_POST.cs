using System.Collections;
using System.Text;
using UnityEngine;
using UnityEngine.Networking;

[System.Serializable]
public class ControlData 
{
    public float dampingFactor;
    public float magneticStrength;
    public float pendulumLength;
    public float pendulumHeight;
}

public class FlaskManager : MonoBehaviour
{   
    private string endpoint = "controller_data";

    [SerializeField] GameObject dampingFactorController;
    [SerializeField] GameObject magneticStrengthController;
    [SerializeField] GameObject lengthController;
    [SerializeField] GameObject pendulumHeightController;

    [SerializeField] private float postInterval = 0.1f; 

    private ControlData data = new ControlData();
    private float timer = 0f;
    
    //on start, establish url and coroutine (async function)
    void Start()
    {
        string URL = "http://127.0.0.1:5000/" + endpoint;
        StartCoroutine(PostLoop());
    }   

    //compile data every frame
    void Update() 
    {
        float dampingFactor = dampingFactorController.displayValue;
        float magneticStrength = magneticStrengthController.displayValue;
        float pendulumLength = lengthController.displayValue;
        float pendulumHeight = pendulumHeightController.displayValue;

        string json = compileJson(dampingFactor, magneticStrength, pendulumLength, pendulumHeight);
    }

    private IEnumerator postLoop() 
    {
        while (true) 
        {
            string json = CompileJson();
            yield return PostJson(json);

            yield return new WaitForSeconds(postInterval);
        }
    }

    private IEnumerator PostJson(string json) {

    }

    private string compileJson(float dampingFactor, float magneticStrength, float pendulumLength, float pendulumHeight) 
    {
        data.dampingFactor = dampingFactor;
        data.magneticStrength = magneticStrength;
        data.pendulumLength = pendulumLength;
        data.pendulumHeight = pendulumHeight;

        string json = JsonUtility.ToJson(data, true);
        return json;
    }


}
