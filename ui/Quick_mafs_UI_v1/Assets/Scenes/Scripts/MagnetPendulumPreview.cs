using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

// Runtime 3D inset for the magnet/pendulum setup. The simulation x/y values are
// placed directly onto the local x/y plane; all magnets share z = 0.
public sealed class MagnetPendulumPreview : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler, IBeginDragHandler, IDragHandler, IEndDragHandler, IScrollHandler
{
    public static bool IsPointerOverPreview { get; private set; }
    public static bool IsPointerControllingPreview { get; private set; }

    private const float SimHalfSize = 1.8f;
    private const int RenderTextureSize = 512;
    private const string MaterialResourceRoot = "MagnetPreviewMaterials/";
    private const string MagnetKeyPrefix = "magnet_";

    [SerializeField] private float magnetRadius = 0.11f;
    [SerializeField] private float magnetHeight = 0.12f;
    [SerializeField] private float pendulumPivotHeight = 1.8f;
    [SerializeField] private float pendulumBobHeight = 0.34f;
    [SerializeField] private float pendulumBobRadius = 0.16f;
    [SerializeField] private Vector2 magneticStrengthRange = new Vector2(0.7f, 7f);
    [SerializeField] private Vector2 pendulumLengthRange = new Vector2(6.6f, 19.6f);
    [SerializeField] private Vector2 pendulumHeightRange = new Vector2(0.01f, 1f);
    [SerializeField] private float minMagnetRadius = 0.08f;
    [SerializeField] private float maxMagnetRadius = 0.24f;
    [SerializeField] private float minVisualPendulumLength = 0.9f;
    [SerializeField] private float maxVisualPendulumLength = 2.1f;
    [SerializeField] private float minVisualPendulumHeight = 0.2f;
    [SerializeField] private float maxVisualPendulumHeight = 0.95f;
    [SerializeField] private float orbitSensitivity = 0.35f;
    [SerializeField] private float panSensitivity = 0.004f;
    [SerializeField] private float zoomSensitivity = 0.35f;
    [SerializeField] private float minCameraDistance = 2.0f;
    [SerializeField] private float maxCameraDistance = 8.0f;

    private readonly Dictionary<string, GameObject> magnetObjects = new Dictionary<string, GameObject>();
    private readonly Dictionary<string, MagnetCoords> currentMagnets = new Dictionary<string, MagnetCoords>();
    private readonly HashSet<string> seenMagnetKeys = new HashSet<string>();

    private RawImage display;
    private RenderTexture renderTexture;
    private GameObject sceneRoot;
    private Transform previewRoot;
    private Camera previewCamera;
    private Material baseMaterial;
    private Material rodMaterial;
    private Material bobMaterial;
    private Material pivotMaterial;
    private Material[] magnetMaterials;
    private Transform pivotTransform;
    private Transform rodTransform;
    private Transform bobTransform;

    private Vector3 cameraTarget = new Vector3(0f, 0f, 0.45f);
    private float cameraDistance = 4.4f;
    private float yawDegrees = -45f;
    private float pitchDegrees = 32f;
    private int activePointerId = int.MinValue;
    private string draggedMagnetKey;
    private bool manualOverrideEnabled;
    private float currentMagnetRadius;
    private float currentPendulumLength;
    private float currentPendulumHeight;

    public event Action<Dictionary<string, MagnetCoords>> ManualMagnetsChanged;

    private static readonly Color[] MagnetPalette =
    {
        Color.red,
        Color.green,
        Color.blue,
    };

    public void Initialize(RawImage targetDisplay)
    {
        display = targetDisplay;
        currentMagnetRadius = magnetRadius;
        currentPendulumLength = pendulumPivotHeight - pendulumBobHeight;
        currentPendulumHeight = pendulumBobHeight;
        BuildPreviewScene();
        ApplyPendulumGeometry();
        ApplyCameraTransform();
    }

    public void ApplyParameters(ControlData data)
    {
        if (data == null)
            return;

        currentMagnetRadius = Mathf.Lerp(
            minMagnetRadius,
            maxMagnetRadius,
            Mathf.InverseLerp(magneticStrengthRange.x, magneticStrengthRange.y, data.magneticStrength));
        currentPendulumLength = Mathf.Lerp(
            minVisualPendulumLength,
            maxVisualPendulumLength,
            Mathf.InverseLerp(pendulumLengthRange.x, pendulumLengthRange.y, data.pendulumLength));
        currentPendulumHeight = Mathf.Lerp(
            minVisualPendulumHeight,
            maxVisualPendulumHeight,
            Mathf.InverseLerp(pendulumHeightRange.x, pendulumHeightRange.y, data.pendulumHeight));

        ApplyMagnetScale();
        ApplyPendulumGeometry();
    }

    public void UpdateMagnets(IDictionary<string, MagnetCoords> magnets)
    {
        if (manualOverrideEnabled || magnets == null || previewRoot == null)
            return;

        StoreMagnetSnapshot(magnets);
        ApplyMagnetSnapshot(currentMagnets);
    }

    public void SetManualOverrideEnabled(bool enabled)
    {
        manualOverrideEnabled = enabled;
        draggedMagnetKey = null;

        if (!manualOverrideEnabled)
            return;

        if (currentMagnets.Count == 0)
            SeedDefaultManualMagnets();

        ApplyMagnetSnapshot(currentMagnets);
        ManualMagnetsChanged?.Invoke(CloneMagnetSnapshot());
    }

    private void StoreMagnetSnapshot(IDictionary<string, MagnetCoords> magnets)
    {
        currentMagnets.Clear();
        foreach (var magnet in magnets)
        {
            if (!TryParseMagnetIndex(magnet.Key, out _))
                continue;

            currentMagnets[magnet.Key] = new MagnetCoords
            {
                x = Mathf.Clamp(magnet.Value.x, -SimHalfSize, SimHalfSize),
                y = Mathf.Clamp(magnet.Value.y, -SimHalfSize, SimHalfSize),
            };
        }
    }

    private void ApplyMagnetSnapshot(IDictionary<string, MagnetCoords> magnets)
    {
        seenMagnetKeys.Clear();

        foreach (var magnet in magnets)
        {
            if (!TryParseMagnetIndex(magnet.Key, out int magnetIndex))
                continue;

            GameObject magnetObject = GetOrCreateMagnet(magnet.Key, magnetIndex);
            magnetObject.transform.localPosition = new Vector3(
                -Mathf.Clamp(magnet.Value.x, -SimHalfSize, SimHalfSize),
                Mathf.Clamp(magnet.Value.y, -SimHalfSize, SimHalfSize),
                magnetHeight * 0.5f);
            seenMagnetKeys.Add(magnet.Key);
        }

        foreach (var magnet in magnetObjects)
            magnet.Value.SetActive(seenMagnetKeys.Contains(magnet.Key));
    }

    public void OnPointerEnter(PointerEventData eventData)
    {
        IsPointerOverPreview = true;
    }

    public void OnPointerExit(PointerEventData eventData)
    {
        if (!IsPointerControllingPreview)
            IsPointerOverPreview = false;
    }

    public void OnBeginDrag(PointerEventData eventData)
    {
        if (eventData.button != PointerEventData.InputButton.Left &&
            eventData.button != PointerEventData.InputButton.Right)
            return;

        activePointerId = eventData.pointerId;
        draggedMagnetKey = null;
        IsPointerOverPreview = true;
        IsPointerControllingPreview = true;

        if (manualOverrideEnabled && eventData.button == PointerEventData.InputButton.Left)
            draggedMagnetKey = PickMagnet(eventData.position, eventData.pressEventCamera);

        eventData.Use();
    }

    public void OnDrag(PointerEventData eventData)
    {
        if (eventData.pointerId != activePointerId)
            return;

        if (manualOverrideEnabled && !string.IsNullOrEmpty(draggedMagnetKey))
        {
            if (TryGetMagnetPlanePosition(eventData.position, eventData.pressEventCamera, out Vector2 simPosition))
            {
                currentMagnets[draggedMagnetKey] = new MagnetCoords { x = simPosition.x, y = simPosition.y };
                ApplyMagnetSnapshot(currentMagnets);
                ManualMagnetsChanged?.Invoke(CloneMagnetSnapshot());
            }
        }
        else if (eventData.button == PointerEventData.InputButton.Left)
        {
            yawDegrees += eventData.delta.x * orbitSensitivity;
            pitchDegrees -= eventData.delta.y * orbitSensitivity;
            pitchDegrees = Mathf.Clamp(pitchDegrees, -10f, 80f);
        }
        else if (eventData.button == PointerEventData.InputButton.Right)
        {
            Vector3 right = previewCamera.transform.right;
            Vector3 up = previewCamera.transform.up;
            cameraTarget -= (right * eventData.delta.x + up * eventData.delta.y) * (panSensitivity * cameraDistance);
        }

        ApplyCameraTransform();
        eventData.Use();
    }

    public void OnEndDrag(PointerEventData eventData)
    {
        if (eventData.pointerId != activePointerId)
            return;

        activePointerId = int.MinValue;
        draggedMagnetKey = null;
        IsPointerControllingPreview = false;
        IsPointerOverPreview = RectTransformUtility.RectangleContainsScreenPoint(
            display.rectTransform,
            eventData.position,
            eventData.pressEventCamera);
        eventData.Use();
    }

    public void OnScroll(PointerEventData eventData)
    {
        cameraDistance = Mathf.Clamp(
            cameraDistance - eventData.scrollDelta.y * zoomSensitivity,
            minCameraDistance,
            maxCameraDistance);
        ApplyCameraTransform();

        IsPointerOverPreview = true;
        eventData.Use();
    }

    private void OnDisable()
    {
        ReleasePointerState();
    }

    private void OnDestroy()
    {
        ReleasePointerState();

        if (display != null && display.texture == renderTexture)
            display.texture = null;

        if (renderTexture != null)
        {
            renderTexture.Release();
            Destroy(renderTexture);
        }

        if (sceneRoot != null)
            Destroy(sceneRoot);
    }

    private void BuildPreviewScene()
    {
        if (display == null || sceneRoot != null)
            return;

        renderTexture = new RenderTexture(RenderTextureSize, RenderTextureSize, 16, RenderTextureFormat.ARGB32)
        {
            name = "MagnetPendulumPreviewRT",
            antiAliasing = 4,
        };
        renderTexture.Create();
        display.texture = renderTexture;

        sceneRoot = new GameObject("RuntimeMagnetPendulumPreview");
        sceneRoot.transform.position = new Vector3(10000f, 10000f, 10000f);
        previewRoot = new GameObject("Setup").transform;
        previewRoot.SetParent(sceneRoot.transform, false);

        CreateMaterials();
        CreateBasePlane();
        CreatePendulum();
        CreatePreviewCamera();
        CreateLighting();
    }

    private void CreateMaterials()
    {
        baseMaterial = LoadMaterial("MagnetPreview_Base", new Color(0.13f, 0.13f, 0.13f));
        rodMaterial = LoadMaterial("MagnetPreview_Rod", new Color(0.82f, 0.82f, 0.82f));
        bobMaterial = LoadMaterial("MagnetPreview_Bob", new Color(0.95f, 0.72f, 0.23f));
        pivotMaterial = LoadMaterial("MagnetPreview_Pivot", new Color(0.95f, 0.95f, 0.95f));

        magnetMaterials = new Material[MagnetPalette.Length];
        magnetMaterials[0] = LoadMaterial("MagnetPreview_Red", MagnetPalette[0]);
        magnetMaterials[1] = LoadMaterial("MagnetPreview_Green", MagnetPalette[1]);
        magnetMaterials[2] = LoadMaterial("MagnetPreview_Blue", MagnetPalette[2]);
    }

    private void CreateBasePlane()
    {
        GameObject basePlane = GameObject.CreatePrimitive(PrimitiveType.Cube);
        basePlane.name = "Magnet Plane";
        basePlane.transform.SetParent(previewRoot, false);
        basePlane.transform.localPosition = new Vector3(0f, 0f, -0.025f);
        basePlane.transform.localScale = new Vector3(SimHalfSize * 2f, SimHalfSize * 2f, 0.05f);
        basePlane.GetComponent<Renderer>().material = baseMaterial;
    }

    private void CreatePendulum()
    {
        GameObject pivot = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        pivot.name = "Pendulum Pivot";
        pivot.transform.SetParent(previewRoot, false);
        pivot.transform.localScale = Vector3.one * 0.12f;
        pivot.GetComponent<Renderer>().material = pivotMaterial;
        pivotTransform = pivot.transform;

        GameObject rod = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        rod.name = "Pendulum Rod";
        rod.transform.SetParent(previewRoot, false);
        rod.transform.localRotation = Quaternion.FromToRotation(Vector3.up, Vector3.forward);
        rod.GetComponent<Renderer>().material = rodMaterial;
        rodTransform = rod.transform;

        GameObject bob = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        bob.name = "Pendulum Bob";
        bob.transform.SetParent(previewRoot, false);
        bob.transform.localScale = Vector3.one * (pendulumBobRadius * 2f);
        bob.GetComponent<Renderer>().material = bobMaterial;
        bobTransform = bob.transform;
    }

    private void CreatePreviewCamera()
    {
        GameObject cameraObject = new GameObject("Preview Camera");
        cameraObject.transform.SetParent(sceneRoot.transform, false);

        previewCamera = cameraObject.AddComponent<Camera>();
        previewCamera.targetTexture = renderTexture;
        previewCamera.clearFlags = CameraClearFlags.SolidColor;
        previewCamera.backgroundColor = new Color(0.055f, 0.055f, 0.065f, 1f);
        previewCamera.nearClipPlane = 0.01f;
        previewCamera.farClipPlane = 25f;
        previewCamera.fieldOfView = 40f;
    }

    private void CreateLighting()
    {
        GameObject lightObject = new GameObject("Preview Key Light");
        lightObject.transform.SetParent(sceneRoot.transform, false);
        lightObject.transform.localPosition = new Vector3(0f, -2.5f, 3.2f);

        Light light = lightObject.AddComponent<Light>();
        light.type = LightType.Point;
        light.intensity = 2.2f;
        light.range = 7f;
    }

    private GameObject GetOrCreateMagnet(string key, int magnetIndex)
    {
        if (magnetObjects.TryGetValue(key, out GameObject existing))
            return existing;

        GameObject magnet = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        magnet.name = key;
        magnet.transform.SetParent(previewRoot, false);
        magnet.transform.localRotation = Quaternion.FromToRotation(Vector3.up, Vector3.forward);
        magnet.transform.localScale = new Vector3(currentMagnetRadius, magnetHeight * 0.5f, currentMagnetRadius);
        magnet.GetComponent<Renderer>().material = magnetMaterials[magnetIndex % magnetMaterials.Length];
        magnetObjects[key] = magnet;
        return magnet;
    }

    private string PickMagnet(Vector2 screenPosition, Camera eventCamera)
    {
        if (!TryGetMagnetPlanePosition(screenPosition, eventCamera, out Vector2 simPosition))
            return null;

        string closestKey = null;
        float closestDistanceSqr = Mathf.Pow(Mathf.Max(currentMagnetRadius * 1.4f, 0.22f), 2f);

        foreach (var magnet in currentMagnets)
        {
            Vector2 delta = new Vector2(magnet.Value.x, magnet.Value.y) - simPosition;
            float distanceSqr = delta.sqrMagnitude;
            if (distanceSqr >= closestDistanceSqr)
                continue;

            closestDistanceSqr = distanceSqr;
            closestKey = magnet.Key;
        }

        return closestKey;
    }

    private bool TryGetMagnetPlanePosition(Vector2 screenPosition, Camera eventCamera, out Vector2 simPosition)
    {
        simPosition = Vector2.zero;
        if (display == null || previewCamera == null || previewRoot == null)
            return false;

        if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(
                display.rectTransform,
                screenPosition,
                eventCamera,
                out Vector2 localPoint))
            return false;

        Rect rect = display.rectTransform.rect;
        float u = Mathf.InverseLerp(rect.xMin, rect.xMax, localPoint.x);
        float v = Mathf.InverseLerp(rect.yMin, rect.yMax, localPoint.y);
        if (u < 0f || u > 1f || v < 0f || v > 1f)
            return false;

        Ray ray = previewCamera.ScreenPointToRay(new Vector3(u * RenderTextureSize, v * RenderTextureSize, 0f));
        Plane plane = new Plane(
            previewRoot.TransformDirection(Vector3.forward),
            previewRoot.TransformPoint(new Vector3(0f, 0f, magnetHeight * 0.5f)));

        if (!plane.Raycast(ray, out float distance))
            return false;

        Vector3 localHit = previewRoot.InverseTransformPoint(ray.GetPoint(distance));
        simPosition = new Vector2(
            Mathf.Clamp(-localHit.x, -SimHalfSize, SimHalfSize),
            Mathf.Clamp(localHit.y, -SimHalfSize, SimHalfSize));
        return true;
    }

    private void SeedDefaultManualMagnets()
    {
        currentMagnets.Clear();
        currentMagnets["magnet_0"] = new MagnetCoords { x = -0.75f, y = -0.45f };
        currentMagnets["magnet_1"] = new MagnetCoords { x = 0.75f, y = -0.45f };
        currentMagnets["magnet_2"] = new MagnetCoords { x = 0f, y = 0.75f };
    }

    private Dictionary<string, MagnetCoords> CloneMagnetSnapshot()
    {
        var clone = new Dictionary<string, MagnetCoords>();
        foreach (var magnet in currentMagnets)
            clone[magnet.Key] = new MagnetCoords { x = magnet.Value.x, y = magnet.Value.y };
        return clone;
    }

    private void ApplyMagnetScale()
    {
        foreach (var magnet in magnetObjects.Values)
            magnet.transform.localScale = new Vector3(currentMagnetRadius, magnetHeight * 0.5f, currentMagnetRadius);
    }

    private void ApplyPendulumGeometry()
    {
        if (pivotTransform == null || rodTransform == null || bobTransform == null)
            return;

        Vector3 bobPosition = new Vector3(0f, 0f, currentPendulumHeight);
        Vector3 pivotPosition = new Vector3(0f, 0f, currentPendulumHeight + currentPendulumLength);

        bobTransform.localPosition = bobPosition;
        pivotTransform.localPosition = pivotPosition;
        rodTransform.localPosition = (pivotPosition + bobPosition) * 0.5f;
        rodTransform.localScale = new Vector3(0.025f, currentPendulumLength * 0.5f, 0.025f);
    }

    private void ApplyCameraTransform()
    {
        if (previewCamera == null)
            return;

        float yaw = yawDegrees * Mathf.Deg2Rad;
        float pitch = pitchDegrees * Mathf.Deg2Rad;
        Vector3 orbitDirection = new Vector3(
            Mathf.Cos(pitch) * Mathf.Cos(yaw),
            Mathf.Cos(pitch) * Mathf.Sin(yaw),
            Mathf.Sin(pitch));

        previewCamera.transform.position = sceneRoot.transform.TransformPoint(cameraTarget + orbitDirection * cameraDistance);
        previewCamera.transform.LookAt(sceneRoot.transform.TransformPoint(cameraTarget), Vector3.forward);
    }

    private Material LoadMaterial(string resourceName, Color fallbackColor)
    {
        Material source = Resources.Load<Material>(MaterialResourceRoot + resourceName);
        if (source != null)
            return Instantiate(source);

        Debug.LogWarning($"[MagnetPendulumPreview] Missing material resource '{resourceName}'; using runtime fallback.");
        return CreateFallbackMaterial(fallbackColor);
    }

    private Material CreateFallbackMaterial(Color color)
    {
        Shader shader = Shader.Find("Universal Render Pipeline/Lit");
        if (shader == null)
            shader = Shader.Find("Standard");

        Material material = new Material(shader);
        material.color = color;
        return material;
    }

    private static bool TryParseMagnetIndex(string key, out int index)
    {
        index = -1;
        if (string.IsNullOrEmpty(key) || !key.StartsWith(MagnetKeyPrefix))
            return false;
        return int.TryParse(key.Substring(MagnetKeyPrefix.Length), out index) && index >= 0;
    }

    private static void ReleasePointerState()
    {
        IsPointerOverPreview = false;
        IsPointerControllingPreview = false;
    }
}
