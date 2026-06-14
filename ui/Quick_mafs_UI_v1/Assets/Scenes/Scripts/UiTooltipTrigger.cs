using System.Collections;
using UnityEngine;
using UnityEngine.EventSystems;

public sealed class UiTooltipTrigger : MonoBehaviour,
    IPointerEnterHandler,
    IPointerExitHandler,
    IPointerMoveHandler,
    IPointerDownHandler,
    IPointerUpHandler,
    IPointerClickHandler
{
    [TextArea]
    [SerializeField] private string tooltipText;
    [SerializeField] private float showDelaySeconds = 0.8f;

    private RectTransform rectTransform;
    private Coroutine showRoutine;
    private bool pointerInside;
    private bool pointerDown;
    private bool suppressUntilExit;
    private Vector2 lastScreenPosition;

    public string TooltipText => tooltipText;

    void Awake()
    {
        rectTransform = transform as RectTransform;
    }

    void OnDisable()
    {
        Hide();
    }

    public void SetTooltip(string text)
    {
        tooltipText = text;
    }

    public void OnPointerEnter(PointerEventData eventData)
    {
        pointerInside = true;
        suppressUntilExit = false;
        lastScreenPosition = eventData.position;
        QueueShow();
    }

    public void OnPointerMove(PointerEventData eventData)
    {
        lastScreenPosition = eventData.position;
        UiTooltipSystem.Instance.Move(lastScreenPosition);
    }

    public void OnPointerExit(PointerEventData eventData)
    {
        pointerInside = false;
        pointerDown = false;
        suppressUntilExit = false;
        Hide();
    }

    public void OnPointerDown(PointerEventData eventData)
    {
        pointerDown = true;
        suppressUntilExit = true;
        Hide();
    }

    public void OnPointerUp(PointerEventData eventData)
    {
        pointerDown = false;
        lastScreenPosition = eventData.position;
    }

    public void OnPointerClick(PointerEventData eventData)
    {
        suppressUntilExit = true;
        Hide();
    }

    private void QueueShow()
    {
        if (string.IsNullOrWhiteSpace(tooltipText) || pointerDown || suppressUntilExit)
            return;

        if (showRoutine != null)
            StopCoroutine(showRoutine);

        showRoutine = StartCoroutine(ShowAfterDelay());
    }

    private IEnumerator ShowAfterDelay()
    {
        yield return new WaitForSecondsRealtime(showDelaySeconds);
        showRoutine = null;

        if (rectTransform == null || !pointerInside || pointerDown || suppressUntilExit)
            yield break;

        UiTooltipSystem.Instance.Show(tooltipText, rectTransform, lastScreenPosition);
    }

    private void Hide()
    {
        if (showRoutine != null)
        {
            StopCoroutine(showRoutine);
            showRoutine = null;
        }

        if (UiTooltipSystem.HasInstance)
            UiTooltipSystem.Instance.Hide();
    }
}
