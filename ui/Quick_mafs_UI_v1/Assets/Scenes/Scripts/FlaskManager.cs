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
    private string URL;

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
    
    //on start, establish url and coroutine (async function)
    void Start()
    {
        URL = "http://35.179.111.223:5000/" + endpoint;

        dampingSlider = dampingFactorController.GetComponent<SliderTextDisplay>();
        magneticSlider = magneticStrengthController.GetComponent<SliderTextDisplay>();
        lengthSlider = lengthController.GetComponent<SliderTextDisplay>();
        heightSlider = pendulumHeightController.GetComponent<SliderTextDisplay>();

        StartCoroutine(postLoop());
    }   

    private IEnumerator postLoop() 
    {
        while (true) 
        {
            string json = compileJson();

            yield return PostJson(json);

            yield return new WaitForSeconds(postInterval);
        }
    }

    private IEnumerator PostJson(string json) 
    {
        //initialise raw byte array to be sent as json in post req
        byte[] bodyRaw = Encoding.UTF8.GetBytes(json);

        using (UnityWebRequest request = new UnityWebRequest(URL, "POST"))
        {
            request.uploadHandler = new UploadHandlerRaw(bodyRaw);
            request.downloadHandler = new DownloadHandlerBuffer();
            request.SetRequestHeader("Content-Type", "application/json");

            yield return request.SendWebRequest();

            //can add error checking here
        }
    }

    private string compileJson() 
    {
        data.dampingFactor = dampingSlider.displayValue;
        data.magneticStrength = magneticSlider.displayValue;
        data.pendulumLength = lengthSlider.displayValue;
        data.pendulumHeight = heightSlider.displayValue;

        return JsonUtility.ToJson(data, true);
    }


}
