using System.Collections;
using UnityEngine;
using UnityEngine.EventSystems;

// Implements only hover/move events and polls for right click while hovered. This
// avoids consuming clicks before they reach Selectables such as dropdowns and sliders.
public sealed class UiTooltipTrigger : MonoBehaviour,
    IPointerEnterHandler,
    IPointerExitHandler,
    IPointerMoveHandler
{
    [TextArea]
    [SerializeField] private string tooltipText;
    [SerializeField] private float showDelaySeconds = 0.8f;
    [SerializeField] private bool showOnHover;

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

    public void SetTooltip(string text, bool hoverToShow = false)
    {
        tooltipText = text;
        showOnHover = hoverToShow;
    }

    public void OnPointerEnter(PointerEventData eventData)
    {
        pointerInside = true;
        lastScreenPosition = eventData.position;
        if (showOnHover)
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

    void Update()
    {
        if (!showOnHover && pointerInside && Input.GetMouseButtonDown(1))
            ShowNow();
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

        ShowNow();
    }

    private void ShowNow()
    {
        if (rectTransform == null || !pointerInside)
            return;

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
