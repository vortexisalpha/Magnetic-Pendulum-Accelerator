using System;
using System.Threading;
using UnityEngine;
using UnityEngine.UI;

//listens for 0x14 traj replies, verifies the echoed pixel id, and draws the
//trajectory as an overlay on top of the pendulum image. the overlay opens with
//a close button in its top-left corner. the overlay graphic and close button are
//created at runtime so only a few references need wiring in the inspector.
public class TrajectoryRenderer : MonoBehaviour
{
    [Header("References")]
    [Tooltip("Pendulum display the trajectory is drawn over (e.g. categoryImage).")]
    [SerializeField] private RawImage displayImage;
    [Tooltip("Provides the viewport bounds the trajectory points are mapped through.")]
    [SerializeField] private PanZoom panZoom;
    [Tooltip("Image toggle. When it is off the line is drawn white instead of lineColor.")]
    [SerializeField] private Toggle imageToggle;

    [Header("Trajectory line")]
    [SerializeField] private int overlayResolution = 512;
    [Tooltip("Colour at the trajectory start position.")]
    [SerializeField] private Color startColor = new Color(0f, 1f, 1f, 1f);
    [Tooltip("Colour at the trajectory end position.")]
    [SerializeField] private Color endColor = new Color(1f, 0f, 1f, 1f);
    [SerializeField] private int lineThickness = 2;

    [Header("Close button")]
    [SerializeField] private Vector2 closeButtonSize = new Vector2(36f, 36f);
    [SerializeField] private Vector2 closeButtonMargin = new Vector2(6f, 6f);
    [SerializeField] private Color closeButtonColor = new Color(0.1f, 0.1f, 0.1f, 0.85f);


    [Header("Playback")]
    [SerializeField] private Slider trajectorySlider;

    [Header("Moving Marker")]
    [SerializeField] private float markerSize = 14f;
    [SerializeField] private Color markerColour = Color.black;

    private RawImage markerImage;

    private Texture2D markerTexture;

    private RectTransform canvasRoot;
    private RawImage overlayImage;
    private RectTransform closeButton;
    private Texture2D overlayTexture;

    private Vector2[] storedPoints;
    private bool lastImageToggleOn;

    void Start()
    {
        if (displayImage == null)
        {
            Debug.LogWarning("[Trajectory] displayImage not assigned; renderer disabled.");
            enabled = false;
            return;
        }

        BuildOverlay();
        SetVisible(false);

        if (PynqConnection.Instance != null)
            PynqConnection.Instance.TrajectoryReceived += OnTrajectoryReceived;

        PynqParamController.ViewportChanged += OnViewportChanged;
    }

    void OnDestroy()
    {
        if (PynqConnection.Instance != null)
            PynqConnection.Instance.TrajectoryReceived -= OnTrajectoryReceived;

        PynqParamController.ViewportChanged -= OnViewportChanged;
    }

    private void OnViewportChanged()
    {
        storedPoints = null;
        SetVisible(false);
    }

    void OnDisable()
    {
        // Ensure the overlay and related UI are hidden when this component
        // or its GameObject is deactivated.
        SetVisible(false);
    }

    void Update()
    {
        //repaint live if the image toggle flips while a trajectory is on screen
        if (imageToggle != null && imageToggle.isOn != lastImageToggleOn &&
            overlayImage != null && overlayImage.gameObject.activeSelf)
        {

            int count = trajectorySlider != null
                ? Mathf.RoundToInt(trajectorySlider.value)
                : (storedPoints != null ? storedPoints.Length : 0);
            DrawSlice(count);
        }
    }


    private void OnTrajectoryReceived(TrajectoryMessage msg)
    {
        var conn = PynqConnection.Instance;

        //verify we actually asked for this pixel (matches against all outstanding
        //requests, so rapid clicks don't drop earlier valid trajectories)
        if (conn != null && conn.HasRequestedTrajectory &&
            !conn.TryConsumeTrajectoryRequest(msg.pixelId))
        {
            Debug.LogWarning($"[Trajectory] unrequested pixel id {msg.pixelId}; ignoring.");
            return;
        }

        // If the display image isn't active we explicitly ignore the incoming
        // trajectory — do not store it or show the overlay. This prevents a
        // trajectory requested via a different image from appearing later when
        // this display is re-enabled.
        if (displayImage == null || !displayImage.gameObject.activeInHierarchy)
        {
            Debug.Log("[Trajectory] displayImage inactive; ignoring received trajectory.");
            return;
        }

        storedPoints = msg.points;

        if (storedPoints == null || storedPoints.Length == 0)
        {
            Debug.LogWarning("[Trajectory] received 0 points; nothing to draw.");
            return;
        }

        if (trajectorySlider != null)
        {
            trajectorySlider.onValueChanged.RemoveAllListeners();
            trajectorySlider.minValue = 0;
            trajectorySlider.maxValue = storedPoints.Length;
            trajectorySlider.value = storedPoints.Length;
            trajectorySlider.onValueChanged.AddListener(OnSliderChanged);
        }

        DrawSlice(storedPoints.Length);
        SetVisible(true);
    }

    private void OnSliderChanged(float value)
    {
        if (storedPoints == null) { return; }
        DrawSlice(Mathf.RoundToInt(value));
    }

    private void DrawSlice(int count)
    {
        if (storedPoints == null) return;
        count = Mathf.Clamp(count, 1, storedPoints.Length);

        var view = new TrajectoryTexturePainter.Viewport
        {
            xMin = -1.8f,
            yMin = -1.8f,
            xMax = 1.8f,
            yMax = 1.8f

        };

        if (panZoom != null)
            panZoom.GetViewportBounds(out view.xMin, out view.xMax, out view.yMin, out view.yMax);

        if (count > 0)
            Debug.Log($"[Trajectory] drawing {count}/{storedPoints.Length} pts, " +
                    $"first={storedPoints[0]}, last={storedPoints[count - 1]}, " +
                    $"view x[{view.xMin:F2},{view.xMax:F2}] y[{view.yMin:F2},{view.yMax:F2}]");

        lastImageToggleOn = imageToggle == null || imageToggle.isOn;
        Color start = lastImageToggleOn ? startColor : Color.white;
        Color end = lastImageToggleOn ? endColor : Color.white;

        var slice = new Vector2[count];
        Array.Copy(storedPoints, slice, count);

        TrajectoryTexturePainter.Paint(overlayTexture, slice, view, start, end, lineThickness);
        AlignOverlay();

        MoveMarkerToPoint(storedPoints[count - 1], view);

    }

    private void MoveMarkerToPoint(Vector2 point, TrajectoryTexturePainter.Viewport view)
    {
        if (markerImage == null || overlayImage == null) return;

        RectTransform overlayRt = overlayImage.rectTransform;
        RectTransform markerRt = markerImage.rectTransform;

        float u = Mathf.InverseLerp(view.xMin, view.xMax, point.x);
        float v = Mathf.InverseLerp(view.yMin, view.yMax, point.y);

        float localX = (u - 0.5f) * overlayRt.sizeDelta.x;
        float localY = (v - 0.5f) * overlayRt.sizeDelta.y;

        markerRt.anchorMin = markerRt.anchorMax = new Vector2(0.5f, 0.5f);
        markerRt.pivot = new Vector2(0.5f, 0.5f);

        markerRt.anchoredPosition = overlayRt.anchoredPosition + new Vector2(localX, localY);

    }

    private void SetVisible(bool visible)
    {
        if (overlayImage != null) overlayImage.gameObject.SetActive(visible);
        if (closeButton != null) closeButton.gameObject.SetActive(visible);
        if (markerImage != null) markerImage.gameObject.SetActive(visible);
    }

    public void Close() => SetVisible(false);

    private void BuildOverlay()
    {
        canvasRoot = displayImage.canvas.GetComponent<RectTransform>();

        overlayTexture = new Texture2D(overlayResolution, overlayResolution, TextureFormat.RGBA32, false)
        {
            filterMode = FilterMode.Bilinear,
            wrapMode = TextureWrapMode.Clamp
        };

        //overlay line layer is parented to the canvas root so it draws on top of
        //both the category and value images, then sized to match the image rect
        var overlayGo = new GameObject("TrajectoryOverlay", typeof(RawImage));
        overlayImage = overlayGo.GetComponent<RawImage>();
        overlayImage.rectTransform.SetParent(canvasRoot, false);
        overlayImage.texture = overlayTexture;
        overlayImage.raycastTarget = false;

        BuildMarker();
        BuildCloseButton();

    }

    private void BuildMarker()

    {
        markerTexture = MakeCircleTexture(64, markerColour);

        var markerGo = new GameObject("TrajectoryMarker", typeof(RawImage));
        markerImage = markerGo.GetComponent<RawImage>();
        markerImage.rectTransform.SetParent(canvasRoot, false);
        markerImage.texture = markerTexture;
        markerImage.raycastTarget = false;
        markerImage.rectTransform.sizeDelta = new Vector2(markerSize, markerSize);
    }

    private Texture2D MakeCircleTexture(int size, Color color)
    {
        Texture2D tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
        tex.filterMode = FilterMode.Bilinear;

        Vector2 centre = new Vector2(size / 2f, size / 2f);
        float radius = size / 2f;


        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                float dist = Vector2.Distance(new Vector2(x, y), centre);
                tex.SetPixel(x, y, dist <= radius ? color : Color.clear);
            }
        }
        tex.Apply();
        return tex;
    }

    private void BuildCloseButton()
    {
        var go = new GameObject("TrajectoryCloseButton", typeof(Image), typeof(Button));
        closeButton = go.GetComponent<RectTransform>();
        closeButton.SetParent(canvasRoot, false);
        closeButton.sizeDelta = closeButtonSize;

        go.GetComponent<Image>().color = closeButtonColor;
        go.GetComponent<Button>().onClick.AddListener(Close);

        var labelGo = new GameObject("X", typeof(Text));
        var label = labelGo.GetComponent<Text>();
        label.rectTransform.SetParent(closeButton, false);
        StretchFill(label.rectTransform);
        label.text = "X";
        label.alignment = TextAnchor.MiddleCenter;
        label.color = Color.white;
        label.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        label.fontSize = Mathf.RoundToInt(closeButtonSize.y * 0.6f);
    }

    //size the overlay to the image rect and pin the close button to its top-left
    private void AlignOverlay()
    {
        var corners = new Vector3[4];
        displayImage.rectTransform.GetWorldCorners(corners);
        Vector2 bottomLeft = WorldToCanvas(corners[0]);
        Vector2 topLeft = WorldToCanvas(corners[1]);
        Vector2 topRight = WorldToCanvas(corners[2]);

        var rt = overlayImage.rectTransform;
        rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
        rt.pivot = new Vector2(0.5f, 0.5f);
        rt.sizeDelta = new Vector2(topRight.x - bottomLeft.x, topRight.y - bottomLeft.y);
        rt.anchoredPosition = (bottomLeft + topRight) * 0.5f;

        closeButton.anchorMin = closeButton.anchorMax = new Vector2(0.5f, 0.5f);
        closeButton.pivot = new Vector2(0f, 1f);
        closeButton.anchoredPosition = topLeft + new Vector2(closeButtonMargin.x, -closeButtonMargin.y);
    }

    private Vector2 WorldToCanvas(Vector3 world)
    {
        Camera cam = displayImage.canvas.renderMode == RenderMode.ScreenSpaceOverlay
            ? null
            : displayImage.canvas.worldCamera;

        Vector2 screen = RectTransformUtility.WorldToScreenPoint(cam, world);
        RectTransformUtility.ScreenPointToLocalPointInRectangle(canvasRoot, screen, cam, out Vector2 local);
        return local;
    }

    private static void StretchFill(RectTransform rt)
    {
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = Vector2.zero;
        rt.offsetMax = Vector2.zero;
    }
}
