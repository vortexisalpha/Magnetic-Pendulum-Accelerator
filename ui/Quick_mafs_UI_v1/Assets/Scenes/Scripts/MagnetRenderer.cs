using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.Networking;
using Newtonsoft.Json;
using TMPro;

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
    private Button overrideButton;
    private TextMeshProUGUI overrideButtonLabel;
    private readonly Dictionary<string, Vector2> lastSentMagnetPositions = new Dictionary<string, Vector2>();
    private readonly Dictionary<string, Vector2> lastObservedMagnetPositions = new Dictionary<string, Vector2>();
    private readonly Dictionary<string, MagnetCoords> manualMagnets = new Dictionary<string, MagnetCoords>();
    private float lastPynqMagnetSendTime = -1f;
    private bool hasObservedMagnetPositions;
    private Coroutine magnetSettledRoutine;
    private bool manualOverrideEnabled;

    void Start()
    {
        if (miniDisplay != null)
        {
            preview = miniDisplay.GetComponent<MagnetPendulumPreview>();
            if (preview == null)
                preview = miniDisplay.gameObject.AddComponent<MagnetPendulumPreview>();
            preview.Initialize(miniDisplay);
            preview.ApplyParameters(PynqParamController.CurrentData);
            preview.ManualMagnetsChanged += OnManualMagnetsChanged;

            fullscreenToggle = miniDisplay.GetComponent<MagnetPreviewFullscreenToggle>();
            if (fullscreenToggle == null)
                fullscreenToggle = miniDisplay.gameObject.AddComponent<MagnetPreviewFullscreenToggle>();
            fullscreenToggle.Initialize(miniDisplay);

            EnsureOverrideButton();
        }

        PynqParamController.ParametersChanged += OnParametersChanged;
        StartCoroutine(PollLoop());
    }

    void OnDestroy()
    {
        if (preview != null)
            preview.ManualMagnetsChanged -= OnManualMagnetsChanged;
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
        if (manualOverrideEnabled)
            yield break;

        float requestStartTime = Time.realtimeSinceStartup;
        using (var req = UnityWebRequest.Get(flaskURL + "info"))
        {
            req.timeout = 1;
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

    void ToggleManualOverride()
    {
        manualOverrideEnabled = !manualOverrideEnabled;
        if (preview != null)
            preview.SetManualOverrideEnabled(manualOverrideEnabled);

        ApplyOverrideButtonState();

        if (!manualOverrideEnabled)
        {
            manualMagnets.Clear();
            hasObservedMagnetPositions = false;
        }
    }

    void OnManualMagnetsChanged(Dictionary<string, MagnetCoords> magnets)
    {
        if (!manualOverrideEnabled || magnets == null)
            return;

        CopyMagnets(magnets, manualMagnets);
        PynqConnection.Instance?.SetLatestInfo(new InfoMessage { magnets = CloneMagnets(manualMagnets) });
        PynqParamController.NotifyMagnetPositionsChanged();

        if (PynqConnection.Instance != null && ShouldSendMagnetsToPynq(manualMagnets))
            PynqConnection.Instance.SendMagnets(manualMagnets);

        RestartMagnetSettledTimer();
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

    static void CopyMagnets(Dictionary<string, MagnetCoords> source, Dictionary<string, MagnetCoords> destination)
    {
        destination.Clear();
        foreach (var magnet in source)
            destination[magnet.Key] = new MagnetCoords { x = magnet.Value.x, y = magnet.Value.y };
    }

    static Dictionary<string, MagnetCoords> CloneMagnets(Dictionary<string, MagnetCoords> source)
    {
        var clone = new Dictionary<string, MagnetCoords>();
        CopyMagnets(source, clone);
        return clone;
    }

    void EnsureOverrideButton()
    {
        if (miniDisplay == null)
            return;

        const string buttonName = "MagnetManualOverrideToggle";
        Transform existing = miniDisplay.rectTransform.Find(buttonName);
        GameObject buttonObject = existing != null
            ? existing.gameObject
            : new GameObject(buttonName, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(Button));

        buttonObject.transform.SetParent(miniDisplay.rectTransform, false);
        RectTransform buttonRect = buttonObject.GetComponent<RectTransform>();
        buttonRect.anchorMin = buttonRect.anchorMax = new Vector2(0f, 1f);
        buttonRect.pivot = new Vector2(0f, 1f);
        buttonRect.anchoredPosition = new Vector2(8f, -8f);
        buttonRect.sizeDelta = new Vector2(62f, 22f);

        overrideButton = buttonObject.GetComponent<Button>();
        overrideButton.onClick.RemoveListener(ToggleManualOverride);
        overrideButton.onClick.AddListener(ToggleManualOverride);

        overrideButtonLabel = buttonObject.GetComponentInChildren<TextMeshProUGUI>(true);
        if (overrideButtonLabel == null)
        {
            GameObject labelObject = new GameObject("Label", typeof(RectTransform), typeof(TextMeshProUGUI));
            labelObject.transform.SetParent(buttonObject.transform, false);
            RectTransform labelRect = labelObject.GetComponent<RectTransform>();
            labelRect.anchorMin = Vector2.zero;
            labelRect.anchorMax = Vector2.one;
            labelRect.offsetMin = Vector2.zero;
            labelRect.offsetMax = Vector2.zero;
            overrideButtonLabel = labelObject.GetComponent<TextMeshProUGUI>();
        }

        overrideButtonLabel.fontSize = 9f;
        overrideButtonLabel.alignment = TextAlignmentOptions.Center;
        overrideButtonLabel.raycastTarget = false;
        buttonObject.transform.SetAsLastSibling();
        ApplyOverrideButtonState();
    }

    void ApplyOverrideButtonState()
    {
        if (overrideButtonLabel != null)
        {
            overrideButtonLabel.text = manualOverrideEnabled ? "Manual" : "Override";
            overrideButtonLabel.color = Color.white;
        }

        if (overrideButton != null)
        {
            Image image = overrideButton.GetComponent<Image>();
            if (image != null)
                image.color = manualOverrideEnabled
                    ? new Color(0.12f, 0.36f, 0.9f, 0.95f)
                    : new Color(0.06f, 0.06f, 0.06f, 0.9f);
        }
    }
}
