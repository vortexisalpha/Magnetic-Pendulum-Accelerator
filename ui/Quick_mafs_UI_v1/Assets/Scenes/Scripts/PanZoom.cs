using UnityEngine;
using UnityEngine.InputSystem;

public class PanZoom : MonoBehaviour
{
    // Simulation window coord center and size, used to check if user window is still within Simulation Window
    // Simulation window is centered at 0, so implicitly simCenter = 0f
    private const float simHalfSize = 1.8f;

    // Center and halfSize of the window the user is setting
    private Vector2 center = Vector2.zero;
    private float halfSize = 1.8f;

    // Panning variables
    private Vector2 deltaMouse = Vector2.zero;
    // deltaMouse returns the number of pixels the cursor has moved across, scale it down
    private float panningSensitivity = 0.001f;

    // Zooming variables
    private float zoomFactor = 0.9f;
    private float maxHalfSize = 1.8f;
    [SerializeField] private float minHalfSize = 0.2f; // Trial and error to determine when zooming in becomes too blocky

    void Update()
    {
        center = ApplyPan(center, deltaMouse);
        deltaMouse = Vector2.zero;
        Debug.Log($"Center:{center}, HalfSize: {halfSize}");
    }


    void OnZoom(InputValue input)
    {
        float scroll = input.Get<Vector2>().y;
        float candidateHalfSize = halfSize;
        // We ignore when scroll == 0, halfSize doesn't change
        if (scroll > 0)
        {
            Debug.Log("New Input: Zooming in...");
            candidateHalfSize *= zoomFactor;
            candidateHalfSize = Mathf.Max(candidateHalfSize, minHalfSize);
        }
        else if (scroll < 0)
        {
            Debug.Log("New Input: Zooming out...");
            candidateHalfSize /= zoomFactor;
            candidateHalfSize = Mathf.Min(candidateHalfSize, maxHalfSize);
        }

        if (!Violate(center, candidateHalfSize))
        {
            halfSize = candidateHalfSize;
        }
    }


    void OnPan(InputValue input)
    {
        Debug.Log("Panning detected...");
        deltaMouse = input.Get<Vector2>();

        Vector2 candidateCenter = ApplyPan(center, deltaMouse);
        if (Violate(candidateCenter, halfSize))
        {
            deltaMouse = Vector2.zero;
        }
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