using UnityEngine;

public class ViewManager : MonoBehaviour
{
    [SerializeField] private GameObject mainCanvas;
    [SerializeField] private GameObject canvas3DOverlay;
    [SerializeField] private OrbitCamera orbitCamera;
    [SerializeField] private GameObject panZoomController;

    [Header("Potential view")]
    [SerializeField] private GameObject potentialCanvas;

    [Header("Uncertainty view")]
    [SerializeField] private GameObject uncertaintyCanvas;
    [SerializeField] private DimensionSweep dimensionSweep;
    [SerializeField] private GameObject latencyStatsUI;

    [Header("Map dropdown routing")]
    [SerializeField] private PendulumRenderer pendulumRenderer;

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
        if (uncertaintyCanvas != null) uncertaintyCanvas.SetActive(false);
        if (latencyStatsUI != null) latencyStatsUI.SetActive(true);
        panZoomController.SetActive(true);
        orbitCamera.enabled = false;
    }

    public void Show3D()
    {
        mainCanvas.SetActive(false);
        canvas3DOverlay.SetActive(true);
        if (potentialCanvas != null) potentialCanvas.SetActive(false);
        if (uncertaintyCanvas != null) uncertaintyCanvas.SetActive(false);
        panZoomController.SetActive(false);
        orbitCamera.enabled = true;
    }

    public void ShowPotential()
    {
        mainCanvas.SetActive(false);
        canvas3DOverlay.SetActive(false);
        if (potentialCanvas != null) potentialCanvas.SetActive(true);
        if (uncertaintyCanvas != null) uncertaintyCanvas.SetActive(false);
        panZoomController.SetActive(false);
        orbitCamera.enabled = false;
    }

    public void OnMapDropdownChanged(int index)
    {
        switch (index)
        {
            case 3: ShowPotential(); break;
            case 4: Show3D(); break;
            case 5: ShowUncertainty(); break;
            default:
                Show2D();
                if (pendulumRenderer != null) pendulumRenderer.Set2DMap(index);
                break;
        }
    }

    public void ShowUncertainty()
    {
        mainCanvas.SetActive(false);
        canvas3DOverlay.SetActive(false);
        if (potentialCanvas != null) potentialCanvas.SetActive(false);
        if (uncertaintyCanvas != null) uncertaintyCanvas.SetActive(true);
        if (latencyStatsUI != null) latencyStatsUI.SetActive(false);
        panZoomController.SetActive(false);
        orbitCamera.enabled = false;

        if (dimensionSweep != null) dimensionSweep.StartSweep();
    }
}
