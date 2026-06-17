using System.Collections;
using System.Collections.Generic;
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
    [SerializeField] private float pynqSendMinIntervalSeconds = 0.15f;
    [SerializeField] private float pynqSendPositionEpsilon = 0.01f;
    [SerializeField] private float magnetSettledSeconds = 0.45f;
    [SerializeField] private string flaskURL = "http://35.179.111.223:5000/";

    private MagnetPendulumPreview preview;
    private MagnetPreviewFullscreenToggle fullscreenToggle;
    private readonly Dictionary<string, Vector2> lastSentMagnetPositions = new Dictionary<string, Vector2>();
    private readonly Dictionary<string, Vector2> lastObservedMagnetPositions = new Dictionary<string, Vector2>();
    private float lastPynqMagnetSendTime = -1f;
    private bool hasObservedMagnetPositions;
    private Coroutine magnetSettledRoutine;

    void Start()
    {
        if (miniDisplay != null)
        {
            preview = miniDisplay.GetComponent<MagnetPendulumPreview>();
            if (preview == null)
                preview = miniDisplay.gameObject.AddComponent<MagnetPendulumPreview>();
            preview.Initialize(miniDisplay);
            preview.ApplyParameters(PynqParamController.CurrentData);

            fullscreenToggle = miniDisplay.GetComponent<MagnetPreviewFullscreenToggle>();
            if (fullscreenToggle == null)
                fullscreenToggle = miniDisplay.gameObject.AddComponent<MagnetPreviewFullscreenToggle>();
            fullscreenToggle.Initialize(miniDisplay);
        }

        PynqParamController.ParametersChanged += OnParametersChanged;
        StartCoroutine(PollLoop());
    }

    void OnDestroy()
    {
        PynqParamController.ParametersChanged -= OnParametersChanged;
        StopAllCoroutines();
    }

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
        float requestStartTime = Time.realtimeSinceStartup;
        using (var req = UnityWebRequest.Get(flaskURL + "info"))
        {
            yield return req.SendWebRequest();
            if (req.result != UnityWebRequest.Result.Success) yield break;

            float flaskLatencyMs = (Time.realtimeSinceStartup - requestStartTime) * 1000f;
            PynqConnection.Instance?.UpdateArucoMarkerFlaskLatency(flaskLatencyMs);

            var info = JsonConvert.DeserializeObject<InfoMessage>(req.downloadHandler.text);
            if (info == null || info.magnets == null) yield break;


            ApplyInfo(info);
            TrackMagnetMotion(info.magnets);

            // Relay the same magnet positions to the board over TCP so the FPGA
            // basin is computed from what we display.
            if (PynqConnection.Instance != null && ShouldSendMagnetsToPynq(info.magnets))
                PynqConnection.Instance.SendMagnets(info.magnets);
        }
    }

    void ApplyInfo(InfoMessage info)
    {
        if (preview != null)
            preview.UpdateMagnets(info.magnets);

        PynqConnection.Instance?.SetLatestInfo(info);
    }

    void OnParametersChanged(ControlData data)
    {
        if (preview != null)
            preview.ApplyParameters(data);
    }

    void TrackMagnetMotion(Dictionary<string, MagnetCoords> magnets)
    {
        if (magnets == null)
            return;

        if (!hasObservedMagnetPositions)
        {
            StoreMagnetSnapshot(magnets, lastObservedMagnetPositions);
            hasObservedMagnetPositions = true;
            return;
        }

        if (!HaveMagnetPositionsChanged(magnets, lastObservedMagnetPositions))
            return;

        StoreMagnetSnapshot(magnets, lastObservedMagnetPositions);
        PynqParamController.NotifyMagnetPositionsChanged();
        RestartMagnetSettledTimer();
    }

    void RestartMagnetSettledTimer()
    {
        if (magnetSettledRoutine != null)
            StopCoroutine(magnetSettledRoutine);

        magnetSettledRoutine = StartCoroutine(NotifyMagnetSettledAfterIdle());
    }

    IEnumerator NotifyMagnetSettledAfterIdle()
    {
        yield return new WaitForSeconds(magnetSettledSeconds);
        magnetSettledRoutine = null;
        PynqParamController.NotifyMagnetPositionsSettled();
    }

    bool ShouldSendMagnetsToPynq(Dictionary<string, MagnetCoords> magnets)
    {
        if (magnets == null)
            return false;

        bool changed = HaveMagnetPositionsChanged(magnets, lastSentMagnetPositions);

        if (!changed)
            return false;

        if (lastPynqMagnetSendTime >= 0f &&
            Time.time - lastPynqMagnetSendTime < pynqSendMinIntervalSeconds)
            return false;

        StoreMagnetSnapshot(magnets, lastSentMagnetPositions);
        lastPynqMagnetSendTime = Time.time;
        return true;
    }

    bool HaveMagnetPositionsChanged(Dictionary<string, MagnetCoords> magnets, Dictionary<string, Vector2> previousPositions)
    {
        if (magnets.Count != previousPositions.Count)
            return true;

        float epsilonSqr = pynqSendPositionEpsilon * pynqSendPositionEpsilon;
        foreach (var magnet in magnets)
        {
            Vector2 position = new Vector2(magnet.Value.x, magnet.Value.y);
            if (!previousPositions.TryGetValue(magnet.Key, out Vector2 previousPosition) ||
                (position - previousPosition).sqrMagnitude > epsilonSqr)
            {
                return true;
            }
        }

        return false;
    }

    static void StoreMagnetSnapshot(Dictionary<string, MagnetCoords> magnets, Dictionary<string, Vector2> snapshot)
    {
        snapshot.Clear();
        foreach (var magnet in magnets)
            snapshot[magnet.Key] = new Vector2(magnet.Value.x, magnet.Value.y);
    }
}
