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
    [SerializeField] private float zoomCommitDelay = 0.35f;

    private float zoomFactor = 0.9f;
    private float maxHalfSize = 1.8f;
    [SerializeField] private float minHalfSize = 0.01f;

    private bool viewportPending;
    private Coroutine zoomCommitRoutine;

    void Update()
    {
        center = ApplyPan(center, deltaMouse);
        deltaMouse = Vector2.zero;

        bool centerChanged = Vector2.Distance(center, lastReportedCenter) > viewportSendEpsilon;
        bool sizeChanged = Mathf.Abs(halfSize - lastReportedHalfSize) > viewportSendEpsilon;

        if (centerChanged || sizeChanged)
        {
            viewportPending = true;
            lastReportedCenter = center;
            lastReportedHalfSize = halfSize;
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

    void OnZoom(InputValue input)
    {
        if (MagnetPendulumPreview.IsPointerOverPreview)
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

        if (!Violate(center, candidateHalfSize))
        {
            halfSize = candidateHalfSize;
            viewportPending = true;
            lastReportedCenter = center;
            lastReportedHalfSize = halfSize;
            ScheduleZoomCommit();
        }
    }

    void OnPan(InputValue input)
    {
        if (MagnetPendulumPreview.IsPointerOverPreview || MagnetPendulumPreview.IsPointerControllingPreview)
            return;

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
