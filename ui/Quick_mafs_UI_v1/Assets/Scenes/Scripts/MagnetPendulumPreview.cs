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

    [SerializeField] private float magnetRadius = 0.11f;
    [SerializeField] private float magnetHeight = 0.12f;
    [SerializeField] private float pendulumPivotHeight = 1.8f;
    [SerializeField] private float pendulumBobHeight = 0.34f;
    [SerializeField] private float pendulumBobRadius = 0.16f;
    [SerializeField] private float orbitSensitivity = 0.35f;
    [SerializeField] private float panSensitivity = 0.004f;
    [SerializeField] private float zoomSensitivity = 0.35f;
    [SerializeField] private float minCameraDistance = 2.0f;
    [SerializeField] private float maxCameraDistance = 8.0f;

    private readonly Dictionary<string, GameObject> magnetObjects = new Dictionary<string, GameObject>();
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

    private Vector3 cameraTarget = new Vector3(0f, 0f, 0.45f);
    private float cameraDistance = 4.4f;
    private float yawDegrees = -45f;
    private float pitchDegrees = 32f;
    private int activePointerId = int.MinValue;

    private static readonly Color[] MagnetPalette =
    {
        Color.red,
        Color.green,
        Color.blue,
    };

    public void Initialize(RawImage targetDisplay)
    {
        display = targetDisplay;
        BuildPreviewScene();
        ApplyCameraTransform();
    }

    public void UpdateMagnets(IDictionary<string, MagnetCoords> magnets)
    {
        if (magnets == null || previewRoot == null)
            return;

        seenMagnetKeys.Clear();

        foreach (var magnet in magnets)
        {
            if (!TryParseMagnetIndex(magnet.Key, out int magnetIndex))
                continue;

            GameObject magnetObject = GetOrCreateMagnet(magnet.Key, magnetIndex);
            magnetObject.transform.localPosition = new Vector3(
                Mathf.Clamp(magnet.Value.x, -SimHalfSize, SimHalfSize),
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
        IsPointerOverPreview = true;
        IsPointerControllingPreview = true;
        eventData.Use();
    }

    public void OnDrag(PointerEventData eventData)
    {
        if (eventData.pointerId != activePointerId)
            return;

        if (eventData.button == PointerEventData.InputButton.Left)
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
        baseMaterial = CreateMaterial(new Color(0.13f, 0.13f, 0.13f));
        rodMaterial = CreateMaterial(new Color(0.82f, 0.82f, 0.82f));
        bobMaterial = CreateMaterial(new Color(0.95f, 0.72f, 0.23f));
        pivotMaterial = CreateMaterial(new Color(0.95f, 0.95f, 0.95f));

        magnetMaterials = new Material[MagnetPalette.Length];
        for (int i = 0; i < magnetMaterials.Length; i++)
            magnetMaterials[i] = CreateMaterial(MagnetPalette[i]);
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
        Vector3 pivotPosition = new Vector3(0f, 0f, pendulumPivotHeight);
        Vector3 bobPosition = new Vector3(0f, 0f, pendulumBobHeight);

        GameObject pivot = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        pivot.name = "Pendulum Pivot";
        pivot.transform.SetParent(previewRoot, false);
        pivot.transform.localPosition = pivotPosition;
        pivot.transform.localScale = Vector3.one * 0.12f;
        pivot.GetComponent<Renderer>().material = pivotMaterial;

        GameObject rod = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        rod.name = "Pendulum Rod";
        rod.transform.SetParent(previewRoot, false);
        rod.transform.localPosition = (pivotPosition + bobPosition) * 0.5f;
        rod.transform.localRotation = Quaternion.FromToRotation(Vector3.up, Vector3.forward);
        rod.transform.localScale = new Vector3(0.025f, Vector3.Distance(pivotPosition, bobPosition) * 0.5f, 0.025f);
        rod.GetComponent<Renderer>().material = rodMaterial;

        GameObject bob = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        bob.name = "Pendulum Bob";
        bob.transform.SetParent(previewRoot, false);
        bob.transform.localPosition = bobPosition;
        bob.transform.localScale = Vector3.one * (pendulumBobRadius * 2f);
        bob.GetComponent<Renderer>().material = bobMaterial;
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
        magnet.transform.localScale = new Vector3(magnetRadius, magnetHeight * 0.5f, magnetRadius);
        magnet.GetComponent<Renderer>().material = magnetMaterials[magnetIndex % magnetMaterials.Length];
        magnetObjects[key] = magnet;
        return magnet;
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

    private Material CreateMaterial(Color color)
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
        if (string.IsNullOrEmpty(key) || !key.StartsWith("magnet_"))
            return false;
        return int.TryParse(key.Substring("magnet_".Length), out index) && index >= 0;
    }

    private static void ReleasePointerState()
    {
        IsPointerOverPreview = false;
        IsPointerControllingPreview = false;
    }
}
