using UnityEngine;

public class ViewManager : MonoBehaviour
{
    [SerializeField] private GameObject mainCanvas;
    [SerializeField] private GameObject canvas3DOverlay;
    [SerializeField] private OrbitCamera orbitCamera;
    [SerializeField] private GameObject panZoomController;

    void Start()
    {
        Show2D();
        panZoomController.SetActive(true);
    }

    public void Show2D()
    {
        mainCanvas.SetActive(true);
        canvas3DOverlay.SetActive(false);
        panZoomController.SetActive(true);
        orbitCamera.enabled = false;
    }

    public void Show3D()
    {
        mainCanvas.SetActive(false);
        canvas3DOverlay.SetActive(true);
        panZoomController.SetActive(false);
        orbitCamera.enabled = true;
    }
}
