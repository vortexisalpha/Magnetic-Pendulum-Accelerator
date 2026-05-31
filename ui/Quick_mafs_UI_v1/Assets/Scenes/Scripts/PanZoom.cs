using UnityEngine;
using System.Collections;
using UnityEngine.InputSystem;
using Mono.Cecil.Cil;
using UnityEditor;
using UnityEngine.SocialPlatforms.GameCenter;


public class PanZoom : MonoBehaviour
{
    private Vector2 center = new Vector2(0, 0);
    private Vector2 mouseDelta = new Vector2(0, 0);
    void Start()
    {
        Debug.Log(center);
        Debug.Log(mouseDelta);
    }

    void FixedUpdate()
    {
        center -= mouseDelta; // mouse dragging left moves the fov to the right and vice versa
        mouseDelta = new Vector2(0, 0);
        Debug.Log(center);
    }
    void OnPan(InputValue input)
    {
        mouseDelta = input.Get<Vector2>();
    }
    void OnZoomIn()
    {
        Debug.Log("Zooming in");
    }
    void OnZoomOut()
    {
        Debug.Log("Zooming out");
    }
}
