using UnityEngine;
using UnityEngine.UI;

public class ViewManager : MonoBehaviour
{
    [SerializeField] private GameObject mainCanvas;
    [SerializeField] private GameObject canvas3DOverlay;
    [SerializeField] private OrbitCamera orbitCamera;
    [SerializeField] private GameObject panZoomController;

    [Header("Potential view")]
    [Tooltip("Canvas that hosts the potential surface RawImage.")]
    [SerializeField] private GameObject potentialCanvas;
    [Tooltip("Canvas the runtime 'Potential' button is parented to (e.g. canvas3DOverlay).")]
    [SerializeField] private RectTransform potentialButtonParent;
    [SerializeField] private Vector2 potentialButtonSize = new Vector2(120f, 36f);
    [SerializeField] private Vector2 potentialButtonMargin = new Vector2(6f, 48f);

    void Start()
    {
        Show2D();
        panZoomController.SetActive(true);
    }

    public void Show2D()
    {
        mainCanvas.SetActive(true);
        canvas3DOverlay.SetActive(false);
        if (potentialCanvas != null) potentialCanvas.SetActive(false);
        panZoomController.SetActive(true);
        orbitCamera.enabled = false;
    }

    public void Show3D()
    {
        mainCanvas.SetActive(false);
        canvas3DOverlay.SetActive(true);
        if (potentialCanvas != null) potentialCanvas.SetActive(false);
        panZoomController.SetActive(false);
        orbitCamera.enabled = true;
    }

    public void ShowPotential()
    {
        mainCanvas.SetActive(false);
        canvas3DOverlay.SetActive(false);
        if (potentialCanvas != null) potentialCanvas.SetActive(true);
        panZoomController.SetActive(false);
        orbitCamera.enabled = false;
    }

    //build the 'Potential' button at runtime so no scene wiring is needed beyond
    //the parent canvas; mirrors the code-built button pattern in TrajectoryRenderer
    private void BuildPotentialButton()
    {
        if (potentialButtonParent == null)
        {
            Debug.LogWarning("[ViewManager] potentialButtonParent not assigned; Potential button not built.");
            return;
        }

        var go = new GameObject("PotentialButton", typeof(Image), typeof(Button));
        var rt = go.GetComponent<RectTransform>();
        rt.SetParent(potentialButtonParent, false);
        rt.sizeDelta = potentialButtonSize;
        rt.anchorMin = rt.anchorMax = new Vector2(1f, 0f);
        rt.pivot = new Vector2(1f, 0f);
        rt.anchoredPosition = new Vector2(-potentialButtonMargin.x, potentialButtonMargin.y);

        go.GetComponent<Image>().color = new Color(0.1f, 0.1f, 0.1f, 0.85f);
        go.GetComponent<Button>().onClick.AddListener(ShowPotential);

        var labelGo = new GameObject("Label", typeof(Text));
        var label = labelGo.GetComponent<Text>();
        label.rectTransform.SetParent(rt, false);
        label.rectTransform.anchorMin = Vector2.zero;
        label.rectTransform.anchorMax = Vector2.one;
        label.rectTransform.offsetMin = Vector2.zero;
        label.rectTransform.offsetMax = Vector2.zero;
        label.text = "Potential";
        label.alignment = TextAnchor.MiddleCenter;
        label.color = Color.white;
        label.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        label.fontSize = Mathf.RoundToInt(potentialButtonSize.y * 0.45f);
    }
}
