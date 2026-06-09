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

    [Header("Trajectory line")]
    [SerializeField] private int overlayResolution = 512;
    [SerializeField] private Color lineColor = new Color(1f, 1f, 1f, 1f);
    [SerializeField] private int lineThickness = 3;

    [Header("Close button")]
    [SerializeField] private Vector2 closeButtonSize = new Vector2(36f, 36f);
    [SerializeField] private Vector2 closeButtonMargin = new Vector2(6f, 6f);
    [SerializeField] private Color closeButtonColor = new Color(0.1f, 0.1f, 0.1f, 0.85f);

    private RectTransform canvasRoot;
    private RawImage overlayImage;
    private RectTransform closeButton;
    private Texture2D overlayTexture;

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
    }

    void OnDestroy()
    {
        if (PynqConnection.Instance != null)
            PynqConnection.Instance.TrajectoryReceived -= OnTrajectoryReceived;
    }

    private void OnTrajectoryReceived(TrajectoryMessage msg)
    {
        var conn = PynqConnection.Instance;

        //verify we are visualising the pixel the user asked for
        if (conn != null && conn.HasRequestedTrajectory &&
            msg.pixelId != conn.LastRequestedTrajectoryPixelId)
        {
            Debug.LogWarning($"[Trajectory] pixel id mismatch: got {msg.pixelId}, " +
                             $"expected {conn.LastRequestedTrajectoryPixelId}; ignoring.");
            return;
        }

        DrawTrajectory(msg.points);
        SetVisible(true);
    }

    private void DrawTrajectory(Vector2[] points)
    {
        var view = new TrajectoryTexturePainter.Viewport
        {
            xMin = -1.8f, xMax = 1.8f, yMin = -1.8f, yMax = 1.8f
        };
        if (panZoom != null)
            panZoom.GetViewportBounds(out view.xMin, out view.xMax, out view.yMin, out view.yMax);

        TrajectoryTexturePainter.Paint(overlayTexture, points, view, lineColor, lineThickness);
        AlignOverlay();
    }

    private void SetVisible(bool visible)
    {
        if (overlayImage != null) overlayImage.gameObject.SetActive(visible);
        if (closeButton != null) closeButton.gameObject.SetActive(visible);
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

        BuildCloseButton();
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
        displayImage.rectTransform.GetWorldCorners(corners); //0 BL, 1 TL, 2 TR, 3 BR
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
