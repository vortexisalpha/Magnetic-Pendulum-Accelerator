using System.Collections;
using UnityEngine;
using UnityEngine.EventSystems;

// Implements only the hover events (enter/exit/move). These are dispatched to the
// whole hovered hierarchy, so attaching this to child graphics of a control does not
// consume pointer clicks. Do NOT add IPointerClick/Down/UpHandler here: those are
// delivered to the first ancestor that handles them and would swallow the click before
// it reaches the underlying Selectable (e.g. TMP_Dropdown, Button, Slider).
public sealed class UiTooltipTrigger : MonoBehaviour,
    IPointerEnterHandler,
    IPointerExitHandler,
    IPointerMoveHandler
{
    [TextArea]
    [SerializeField] private string tooltipText;
    [SerializeField] private float showDelaySeconds = 0.8f;

    private RectTransform rectTransform;
    private Coroutine showRoutine;
    private bool pointerInside;
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
        Hide();
    }

    private void QueueShow()
    {
        if (string.IsNullOrWhiteSpace(tooltipText))
            return;

        if (showRoutine != null)
            StopCoroutine(showRoutine);

        showRoutine = StartCoroutine(ShowAfterDelay());
    }

    private IEnumerator ShowAfterDelay()
    {
        yield return new WaitForSecondsRealtime(showDelaySeconds);
        showRoutine = null;

        if (rectTransform == null || !pointerInside)
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
