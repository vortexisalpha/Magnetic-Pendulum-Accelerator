using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;

//attach to a pendulum display RawImage (categoryImage / valueImage). double
//clicking a pixel converts the hit point into the board's row-major pixel id and
//asks for its trajectory via 0x04 traj_req.
[RequireComponent(typeof(RawImage))]
public class TrajectoryClicker : MonoBehaviour, IPointerClickHandler
{
    private RectTransform rect;

    void Awake()
    {
        rect = (RectTransform)transform;
    }

    public void OnPointerClick(PointerEventData eventData)
    {
        if (eventData.clickCount != 2)
            return;

        Debug.Log($"[Trajectory] image double-clicked at screen {eventData.position}");
        var conn = PynqConnection.Instance;
        if (conn == null || conn.LatestImage == null)
        {
            Debug.LogWarning("[Trajectory] click ignored: connection or LatestImage is null");
            return;
        }

        int width = conn.LatestImage.width;
        int height = conn.LatestImage.height;
        if (width <= 0 || height <= 0)
        {
            Debug.LogWarning($"[Trajectory] click ignored: bad image size {width}x{height}");
            return;
        }

        if (!TryGetPixel(eventData, width, height, out int x, out int y))
        {
            Debug.LogWarning("[Trajectory] click ignored: click was outside the image rect");
            return;
        }

        //pixel id matches PendulumRenderer's source ordering (row 0 = top)
        uint pixelId = (uint)(y * width + x);
        Debug.Log($"[Trajectory] requesting trajectory for pixel {pixelId} (x={x}, y={y})");
        conn.SendTrajectoryRequest(pixelId);
    }

    //map a click into source pixel coordinates (x left->right, y top->bottom)
    private bool TryGetPixel(PointerEventData eventData, int width, int height, out int x, out int y)
    {
        x = y = 0;

        if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(
                rect, eventData.position, eventData.pressEventCamera, out Vector2 local))
            return false;

        Rect r = rect.rect;
        float u = (local.x - r.xMin) / r.width;  //0..1 left->right
        float v = (local.y - r.yMin) / r.height; //0..1 bottom->top
        if (u < 0f || u > 1f || v < 0f || v > 1f)
            return false;

        x = Mathf.Clamp((int)(u * width), 0, width - 1);
        int textureRow = Mathf.Clamp((int)(v * height), 0, height - 1); //0 = bottom
        y = height - 1 - textureRow; //undo the y-flip done when the texture is built
        return true;
    }
}
