using System.Collections;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.Networking;
using Newtonsoft.Json;

// Magnet positions come from the Flask server (fed live by the ArUco camera
// detection), NOT over the PYNQ TCP link. Image + sliders use TCP; only the
// magnet dots are pulled from Flask /info here.
public class MagnetRenderer : MonoBehaviour
{
    [SerializeField] private RawImage miniDisplay;
    [SerializeField] private float pollIntervalSeconds = 0.03f;
    [SerializeField] private string flaskURL = "http://35.179.111.223:5000/";

    private MagnetPendulumPreview preview;

    void Start()
    {
        if (miniDisplay != null)
        {
            preview = miniDisplay.GetComponent<MagnetPendulumPreview>();
            if (preview == null)
                preview = miniDisplay.gameObject.AddComponent<MagnetPendulumPreview>();
            preview.Initialize(miniDisplay);
        }

        StartCoroutine(PollLoop());
    }

    void OnDestroy()
    {
        StopAllCoroutines();
    }

    private float oldMagnetSendTime = -1f;
    private float newMagnetSendTime;
    private float oldRenderTime = -1f;
    private float newRenderTime;

    IEnumerator PollLoop()
    {
        var wait = new WaitForSeconds(pollIntervalSeconds);
        while (true)
        {
            yield return FetchAndSendInfo();
            yield return wait;
        }
    }

    IEnumerator FetchAndSendInfo()
    {
        using (var req = UnityWebRequest.Get(flaskURL + "info"))
        {
            yield return req.SendWebRequest();
            if (req.result != UnityWebRequest.Result.Success) yield break;

            var info = JsonConvert.DeserializeObject<InfoMessage>(req.downloadHandler.text);
            if (info == null || info.magnets == null) yield break;


            ApplyInfo(info);
            newRenderTime = Time.time;
            if (oldRenderTime > 0f)
            {
                Debug.Log($"Time between magnet position renders: {newRenderTime - oldRenderTime}");
            }
            oldRenderTime = newRenderTime;

            // Relay the same magnet positions to the board over TCP so the FPGA
            // basin is computed from what we display.
            if (PynqConnection.Instance != null)
            {
                PynqConnection.Instance.SendMagnets(info.magnets);
                newMagnetSendTime = Time.time;
                if (oldMagnetSendTime > 0f)
                {
                    Debug.Log($"Time between magnet position TCP sends: {newMagnetSendTime - oldMagnetSendTime}");
                }
                oldMagnetSendTime = newMagnetSendTime;
            }
        }
    }

    void ApplyInfo(InfoMessage info)
    {
        if (preview != null)
            preview.UpdateMagnets(info.magnets);
    }
}
