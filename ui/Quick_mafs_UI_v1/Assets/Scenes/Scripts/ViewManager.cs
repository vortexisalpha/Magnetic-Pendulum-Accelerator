using UnityEngine;

public class ViewManager : MonoBehaviour
{
    [SerializeField] private GameObject mainCanvas;
    [SerializeField] private GameObject canvas3DOverlay;
    [SerializeField] private OrbitCamera orbitCamera;

    void Start()
    {
        Show2D();
    }

    public void Show2D()
    {
        mainCanvas.SetActive(true);
        canvas3DOverlay.SetActive(false);
        orbitCamera.enabled = false;
    }

    public void Show3D()
    {
        mainCanvas.SetActive(false);
        canvas3DOverlay.SetActive(true);
        orbitCamera.enabled = true;
    }
}
