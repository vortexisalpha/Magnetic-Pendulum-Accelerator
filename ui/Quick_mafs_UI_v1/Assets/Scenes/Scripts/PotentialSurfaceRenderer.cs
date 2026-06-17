using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

public class PotentialSurfaceRenderer : MonoBehaviour, IDragHandler, IScrollHandler
{
    [SerializeField] private RawImage display;

    [Header("Camera controls")]
    [SerializeField] private float orbitSensitivity = 0.35f;
    [SerializeField] private float panSensitivity = 0.004f;
    [SerializeField] private float zoomSensitivity = 0.1f;

    [Header("Trajectory")]
    [Tooltip("Width of the lifted trajectory line, in surface units.")]
    [SerializeField] private float trajectoryWidth = 0.03f;
    [Tooltip("Small lift above the surface so the line doesn't z-fight with it.")]
    [SerializeField] private float trajectoryZOffset = 0.02f;
    [Tooltip("Trajectory line colour (drawn on top of the surface for contrast).")]
    [SerializeField] private Color trajectoryColor = Color.white;
    [Tooltip("Seconds for the trajectory to draw from start to end.")]
    [SerializeField] private float trajectoryDrawSeconds = 3f;
    [Tooltip("Restart the animation once it reaches the end.")]
    [SerializeField] private bool trajectoryLoop = true;
    [Tooltip("Radius of the moving marker that travels along the trajectory.")]
    [SerializeField] private float markerRadius = 0.06f;
    [SerializeField] private Color markerColor = Color.white;

    private RenderTexture renderTexture;
    private GameObject sceneRoot;
    private Camera previewCamera;
    private GameObject surfaceObject;
    private bool cameraFramed;

    private float currentOmega, currentMu, currentH;
    private Vector2[] currentMagnets;

    private GameObject trajectoryObject;
    private LineRenderer trajectoryLine;
    private Vector2[] trajectoryPoints;
    private Vector3[] trajectoryPositions;
    private GameObject markerObject;
    private float trajectoryAnimTime;
    private bool trajectoryAnimating;

    private Vector3 cameraTarget;
    private float cameraDistance = 8f;
    private float yawDegrees = -90f;
    private float pitchDegrees = 33f;
    private float minCameraDistance = 1f;
    private float maxCameraDistance = 200f;

    void Start()
    {
        if (display == null)
        {
            Debug.LogWarning("[PotentialSurface] Display not assigned; renderer disabled. " +
                             "Wire a RawImage into the Display slot.");
            enabled = false;
            return;
        }

        BuildScene();

        if (PynqConnection.Instance != null)
            PynqConnection.Instance.TrajectoryReceived += OnTrajectoryReceived;

        if (PynqConnection.Instance != null && PynqConnection.Instance.LatestTrajectory != null)
            trajectoryPoints = PynqConnection.Instance.LatestTrajectory.points;
    }

    void OnEnable()
    {
        if (sceneRoot != null)
        {
            BuildMesh();
            BuildTrajectoryLine();
        }
    }

    void Update()
    {
        if (sceneRoot == null) return;

        Vector2[] latest = GetMagnetPositions();
        if (MagnetsChanged(latest, currentMagnets))
        {
            BuildMesh();
            BuildTrajectoryLine();
        }

        AnimateTrajectory();
    }

    private static bool MagnetsChanged(Vector2[] a, Vector2[] b)
    {
        if (a == null || b == null || a.Length != b.Length) return true;
        const float epsSqr = 0.0001f;
        for (int i = 0; i < a.Length; i++)
            if ((a[i] - b[i]).sqrMagnitude > epsSqr) return true;
        return false;
    }

    void OnDestroy()
    {
        if (PynqConnection.Instance != null)
            PynqConnection.Instance.TrajectoryReceived -= OnTrajectoryReceived;
        if (sceneRoot != null) Destroy(sceneRoot);
        if (renderTexture != null) { renderTexture.Release(); Destroy(renderTexture); }
    }

    private void OnTrajectoryReceived(TrajectoryMessage msg)
    {
        if (msg == null || msg.points == null || msg.points.Length == 0)
            return;

        trajectoryPoints = msg.points;
        if (sceneRoot != null)
            BuildTrajectoryLine();
    }

    void BuildScene()
    {
        renderTexture = new RenderTexture(512, 512, 16, RenderTextureFormat.ARGB32);
        renderTexture.Create();
        display.texture = renderTexture;

        sceneRoot = new GameObject("PotentialSurfaceScene");
        sceneRoot.transform.position = new Vector3(-10000f, -10000f, -10000f);

        var lightGo = new GameObject("PotentialLight");
        lightGo.transform.SetParent(sceneRoot.transform, false);
        lightGo.transform.localPosition = new Vector3(0f, -2.5f, 4f);
        var light = lightGo.AddComponent<Light>();
        light.type = LightType.Point;
        light.intensity = 2.2f;
        light.range = 20f;

        var camGo = new GameObject("PotentialCamera");
        camGo.transform.SetParent(sceneRoot.transform, false);
        previewCamera = camGo.AddComponent<Camera>();
        previewCamera.targetTexture = renderTexture;
        previewCamera.clearFlags = CameraClearFlags.SolidColor;
        previewCamera.backgroundColor = new Color(0.05f, 0.05f, 0.06f);
        previewCamera.nearClipPlane = 0.1f;
        previewCamera.farClipPlane = 2000f;
        previewCamera.fieldOfView = 40f;
        BuildMesh();
    }
    
    private const float ZScale = 0.2f;
    private static readonly Vector2[] TestMagnets =
    {
        new Vector2( 0.0f,  1.0f),
        new Vector2(-0.87f, -0.5f),
        new Vector2( 0.87f, -0.5f),
    };

    private void BuildMesh()
    {
        if (surfaceObject != null) Destroy(surfaceObject);

        ControlData data = PynqParamController.CurrentData;
        const float Gravity = 9.81f;
        float omega = Mathf.Sqrt(Gravity / Mathf.Max(data.pendulumLength, 0.01f));
        float mu = data.magneticStrength;
        float h = data.pendulumHeight;
        Vector2[] magnets = GetMagnetPositions();

        currentOmega = omega;
        currentMu = mu;
        currentH = h;
        currentMagnets = magnets;

        int n = 48;
        float range = 1.8f;

        //first pass: evaluate V everywhere and track its min/max
        var raw = new float[n * n];
        float vMin = float.MaxValue, vMax = float.MinValue;
        for (int yi = 0; yi < n; yi++)
        for (int xi = 0; xi < n; xi++)
        {
            float x = Mathf.Lerp(-range, range, xi / (float)(n - 1));
            float y = Mathf.Lerp(-range, range, yi / (float)(n - 1));
            float v = PotentialEvaluator.Evaluate(x, y, magnets, omega, mu, h);
            raw[yi * n + xi] = v;
            if (v < vMin) vMin = v;
            if (v > vMax) vMax = v;
        }

        float span = Mathf.Max(vMax - vMin, 1e-4f);
        var vertices = new Vector3[n * n];
        var colors = new Color[n * n];
        for (int yi = 0; yi < n; yi++)
        for (int xi = 0; xi < n; xi++)
        {
            int idx = yi * n + xi;
            float x = Mathf.Lerp(-range, range, xi / (float)(n - 1));
            float y = Mathf.Lerp(-range, range, yi / (float)(n - 1));
            float t = (raw[idx] - vMin) / span;
            vertices[idx] = new Vector3(x, y, raw[idx] * ZScale);
            colors[idx] = SampleGradient(t);
        }

        var triangles = new int[(n - 1) * (n - 1) * 6];
        int tri = 0;
        for (int yi = 0; yi < n - 1; yi++)
        for (int xi = 0; xi < n - 1; xi++)
        {
            int i = yi * n + xi;
            triangles[tri++] = i;         triangles[tri++] = i + n;     triangles[tri++] = i + 1;
            triangles[tri++] = i + 1;     triangles[tri++] = i + n;     triangles[tri++] = i + n + 1;
        }

        var mesh = new Mesh();
        mesh.vertices = vertices;
        mesh.colors = colors;
        mesh.triangles = triangles;
        mesh.RecalculateNormals();

        surfaceObject = new GameObject("PotentialSurface");
        var go = surfaceObject;
        go.transform.SetParent(sceneRoot.transform, false);
        go.AddComponent<MeshFilter>().mesh = mesh;
        go.AddComponent<MeshRenderer>().material = CreateMaterial();

        FrameCamera(mesh.bounds);
        BuildTrajectoryLine();
    }

    private void BuildTrajectoryLine()
    {
        if (trajectoryObject != null) Destroy(trajectoryObject);
        trajectoryLine = null;

        if (trajectoryPoints == null || trajectoryPoints.Length < 2 || sceneRoot == null)
            return;

        trajectoryObject = new GameObject("PotentialTrajectory");
        trajectoryObject.transform.SetParent(sceneRoot.transform, false);

        trajectoryLine = trajectoryObject.AddComponent<LineRenderer>();
        trajectoryLine.useWorldSpace = false;
        trajectoryLine.widthMultiplier = trajectoryWidth;
        trajectoryLine.numCornerVertices = 2;
        trajectoryLine.numCapVertices = 2;
        trajectoryLine.material = CreateTrajectoryMaterial();
        trajectoryLine.startColor = trajectoryColor;
        trajectoryLine.endColor = trajectoryColor;

        trajectoryPositions = new Vector3[trajectoryPoints.Length];
        for (int i = 0; i < trajectoryPoints.Length; i++)
        {
            Vector2 p = trajectoryPoints[i];
            float v = PotentialEvaluator.Evaluate(p.x, p.y, currentMagnets, currentOmega, currentMu, currentH);
            trajectoryPositions[i] = new Vector3(p.x, p.y, v * ZScale + trajectoryZOffset);
        }

        BuildMarker();
        trajectoryAnimTime = 0f;
        trajectoryAnimating = true;
        trajectoryLine.positionCount = 0;
    }

    private void BuildMarker()
    {
        if (markerObject != null) return;

        markerObject = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        markerObject.name = "TrajectoryMarker";
        Destroy(markerObject.GetComponent<Collider>());
        markerObject.transform.SetParent(sceneRoot.transform, false);
        markerObject.transform.localScale = Vector3.one * (markerRadius * 2f);

        var mat = markerObject.GetComponent<MeshRenderer>().material;
        mat.color = markerColor;
    }

    private void AnimateTrajectory()
    {
        if (!trajectoryAnimating || trajectoryLine == null ||
            trajectoryPositions == null || trajectoryPositions.Length < 2)
            return;

        trajectoryAnimTime += Time.deltaTime;
        float duration = Mathf.Max(0.01f, trajectoryDrawSeconds);
        float progress = Mathf.Clamp01(trajectoryAnimTime / duration);

        int count = Mathf.Max(2, Mathf.CeilToInt(progress * trajectoryPositions.Length));
        count = Mathf.Min(count, trajectoryPositions.Length);

        trajectoryLine.positionCount = count;
        for (int i = 0; i < count; i++)
            trajectoryLine.SetPosition(i, trajectoryPositions[i]);

        if (markerObject != null)
            markerObject.transform.localPosition = trajectoryPositions[count - 1];

        if (progress >= 1f)
        {
            if (trajectoryLoop) trajectoryAnimTime = 0f;
            else trajectoryAnimating = false;
        }
    }

    private void FrameCamera(Bounds bounds)
    {
        if (previewCamera == null || cameraFramed) return;
        cameraFramed = true;

        cameraTarget = bounds.center;
        float radius = Mathf.Max(bounds.extents.magnitude, 0.5f);
        float halfFov = previewCamera.fieldOfView * 0.5f * Mathf.Deg2Rad;
        cameraDistance = radius / Mathf.Sin(halfFov) * 1.1f;
        maxCameraDistance = cameraDistance * 4f;

        ApplyCameraTransform();
    }

    private void ApplyCameraTransform()
    {
        if (previewCamera == null) return;

        float yaw = yawDegrees * Mathf.Deg2Rad;
        float pitch = pitchDegrees * Mathf.Deg2Rad;
        Vector3 dir = new Vector3(
            Mathf.Cos(pitch) * Mathf.Cos(yaw),
            Mathf.Cos(pitch) * Mathf.Sin(yaw),
            Mathf.Sin(pitch));

        previewCamera.transform.localPosition = cameraTarget + dir * cameraDistance;
        previewCamera.transform.LookAt(
            sceneRoot.transform.TransformPoint(cameraTarget), Vector3.forward);
    }

    public void OnDrag(PointerEventData eventData)
    {
        if (eventData.button == PointerEventData.InputButton.Right)
        {
            Vector3 right = previewCamera.transform.right;
            Vector3 up = previewCamera.transform.up;
            cameraTarget -= (right * eventData.delta.x + up * eventData.delta.y)
                            * (panSensitivity * cameraDistance);
        }
        else
        {
            yawDegrees += eventData.delta.x * orbitSensitivity;
            pitchDegrees = Mathf.Clamp(pitchDegrees - eventData.delta.y * orbitSensitivity, -10f, 89f);
        }

        ApplyCameraTransform();
    }

    public void OnScroll(PointerEventData eventData)
    {
        cameraDistance = Mathf.Clamp(
            cameraDistance - eventData.scrollDelta.y * zoomSensitivity * cameraDistance,
            minCameraDistance, maxCameraDistance);
        ApplyCameraTransform();
    }

    private Material CreateMaterial()
    {
        Shader shader = Shader.Find("Sprites/Default");
        if (shader == null)
            shader = Shader.Find("Unlit/Color");
        return new Material(shader);
    }

    private Material CreateTrajectoryMaterial()
    {
        Shader shader = Shader.Find("Hidden/Internal-Colored");
        if (shader == null)
            shader = Shader.Find("Sprites/Default");

        var mat = new Material(shader);
        mat.SetInt("_ZTest", (int)UnityEngine.Rendering.CompareFunction.Always);
        mat.SetInt("_ZWrite", 0);
        mat.SetInt("_Cull", (int)UnityEngine.Rendering.CullMode.Off);
        mat.renderQueue = 4000;
        return mat;
    }

    private static readonly Color[] GradientStops =
    {
        new Color(0.15f, 0.10f, 0.45f),
        new Color(0.00f, 0.55f, 0.85f),
        new Color(0.10f, 0.75f, 0.30f),
        new Color(0.95f, 0.85f, 0.15f),
        new Color(0.85f, 0.20f, 0.15f),
    };

    private static Vector2[] GetMagnetPositions()
    {
        var info = PynqConnection.Instance != null ? PynqConnection.Instance.LatestInfo : null;
        if (info == null || info.magnets == null || info.magnets.Count == 0)
            return TestMagnets;

        var result = new Vector2[info.magnets.Count];
        int i = 0;
        foreach (var m in info.magnets.Values)
            result[i++] = new Vector2(m.x, m.y);
        return result;
    }

    private static Color SampleGradient(float t)
    {
        float scaled = Mathf.Clamp01(t) * (GradientStops.Length - 1);
        int lo = Mathf.FloorToInt(scaled);
        int hi = Mathf.Min(lo + 1, GradientStops.Length - 1);
        return Color.Lerp(GradientStops[lo], GradientStops[hi], scaled - lo);
    }

}