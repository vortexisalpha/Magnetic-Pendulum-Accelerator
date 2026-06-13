using System.Collections;
using UnityEngine;
using UnityEngine.InputSystem;

public class PanZoom : MonoBehaviour
{
    private const float simHalfSize = 1.8f;

    private Vector2 center = Vector2.zero;
    private float halfSize = 1.8f;

    private Vector2 lastReportedCenter = Vector2.zero;
    private float lastReportedHalfSize = 1.8f;
    private const float viewportSendEpsilon = 0.0001f;

    private Vector2 deltaMouse = Vector2.zero;
    [SerializeField] private float panningSensitivity = 0.01f;
    [SerializeField] private float zoomCommitDelay = 0.5f;

    private float zoomFactor = 0.9f;
    private float maxHalfSize = 1.8f;
    [SerializeField] private float minHalfSize = 0.01f;
    [SerializeField] RectTransform mapRegion; //If mouse is over this area, allow panning and zooming

    private bool viewportPending;
    private Coroutine zoomCommitRoutine;

    void Update()
    {
        if (!IsPointerOverMapRegion())
        {
            deltaMouse = Vector2.zero;
            return;
        }

        center = ApplyPan(center, deltaMouse);
        deltaMouse = Vector2.zero;

        bool centerChanged = Vector2.Distance(center, lastReportedCenter) > viewportSendEpsilon;
        bool sizeChanged = Mathf.Abs(halfSize - lastReportedHalfSize) > viewportSendEpsilon;

        if (centerChanged || sizeChanged)
        {
            viewportPending = true;
            lastReportedCenter = center;
            lastReportedHalfSize = halfSize;
            PynqParamController.NotifyViewportChanged();
        }

        if (viewportPending && Mouse.current != null && Mouse.current.leftButton.wasReleasedThisFrame)
            CommitViewport();
    }

    public void GetViewportBounds(out float xMin, out float xMax, out float yMin, out float yMax)
    {
        xMin = center.x - halfSize;
        xMax = center.x + halfSize;
        yMin = center.y - halfSize;
        yMax = center.y + halfSize;
    }

    private bool IsPointerOverMapRegion()
    {
        Vector2 mousePos = Mouse.current.position.ReadValue();
        return RectTransformUtility.RectangleContainsScreenPoint(
        mapRegion, mousePos, null);
    }
    void OnZoom(InputValue input)
    {
        if (!IsPointerOverMapRegion())
            return;
        float scroll = input.Get<Vector2>().y;
        float candidateHalfSize = halfSize;
        if (scroll > 0)
        {
            candidateHalfSize *= zoomFactor;
            candidateHalfSize = Mathf.Max(candidateHalfSize, minHalfSize);
        }
        else if (scroll < 0)
        {
            candidateHalfSize /= zoomFactor;
            candidateHalfSize = Mathf.Min(candidateHalfSize, maxHalfSize);
        }

        Vector2 candidateCenter = ClampCenterToFitZoom(center, candidateHalfSize); 

        halfSize = candidateHalfSize;
        center = candidateCenter;
        
        viewportPending = true;
        lastReportedCenter = center;
        lastReportedHalfSize = halfSize;
        PynqParamController.NotifyViewportChanged();
        ScheduleZoomCommit();
    }

    Vector2 ClampCenterToFitZoom(Vector2 center, float halfSize)
    {
        float minCenterCoord = -simHalfSize + halfSize; // center.x and .y have the same min and max
        float maxCenterCoord = simHalfSize - halfSize;

        // If candidateHalfSize reaches the full simulation half-size,
        // minCenter and maxCenter both become 0, so center becomes (0, 0).
        float clamped_x = Mathf.Clamp(center.x, minCenterCoord, maxCenterCoord);
        float clamped_y = Mathf.Clamp(center.y, minCenterCoord, maxCenterCoord);

        return new Vector2(clamped_x, clamped_y);
    }

    void OnPan(InputValue input)
    {

        deltaMouse = input.Get<Vector2>();

        Vector2 candidateCenter = ApplyPan(center, deltaMouse);
        if (Violate(candidateCenter, halfSize))
            deltaMouse = Vector2.zero;
    }

    void ScheduleZoomCommit()
    {
        if (zoomCommitRoutine != null)
            StopCoroutine(zoomCommitRoutine);
        zoomCommitRoutine = StartCoroutine(CommitViewportAfterDelay());
    }

    IEnumerator CommitViewportAfterDelay()
    {
        yield return new WaitForSeconds(zoomCommitDelay);
        zoomCommitRoutine = null;
        CommitViewport();
    }

    void CommitViewport()
    {
        if (!viewportPending)
            return;

        viewportPending = false;
        PynqParamController.NotifyViewportReleased();
    }

    Vector2 ApplyPan(Vector2 currentCenter, Vector2 currentDeltaMouse)
    {
        return currentCenter - currentDeltaMouse * panningSensitivity;
    }

    bool Violate(Vector2 currentCenter, float currentHalfSize)
    {
        float x_min = currentCenter.x - currentHalfSize;
        float x_max = currentCenter.x + currentHalfSize;
        float y_min = currentCenter.y - currentHalfSize;
        float y_max = currentCenter.y + currentHalfSize;

        return x_min < -simHalfSize || x_max > simHalfSize || y_min < -simHalfSize || y_max > simHalfSize;
    }
}
