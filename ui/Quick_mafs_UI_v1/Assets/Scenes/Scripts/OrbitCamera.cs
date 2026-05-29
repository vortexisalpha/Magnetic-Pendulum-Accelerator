using UnityEngine;

//referance: https://discussions.unity.com/t/mouseorbitimproved-camera-distance-seemingly-inverted/181603
//refreance: https://catlikecoding.com/unity/tutorials/movement/orbit-camera/

public class OrbitCamera : MonoBehaviour
{
    [SerializeField] private Vector3 target = new Vector3(40, 50, 30);
    [SerializeField] private float distance = 200f;
    [SerializeField] private float rotateSpeed = 4f;
    [SerializeField] private float panSpeed = 0.3f;
    [SerializeField] private float zoomSpeed = 30f;
    [SerializeField] private float minDistance = 10f;
    [SerializeField] private float maxDistance = 600f;

    private float yaw = 0f;
    private float pitch = 25f;

    void OnEnable()
    {
        ApplyTransform();
    }

    void Update()
    {
        //left drag = orbit
        if (Input.GetMouseButton(0))
        {
            yaw += Input.GetAxis("Mouse X") * rotateSpeed;
            pitch -= Input.GetAxis("Mouse Y") * rotateSpeed;
            pitch = Mathf.Clamp(pitch, -89f, 89f);
        }

        //right drag = pan
        if (Input.GetMouseButton(1))
        {
            Vector3 right = transform.right;
            Vector3 up = transform.up;
            target -= right * Input.GetAxis("Mouse X") * panSpeed * (distance * 0.05f);
            target -= up * Input.GetAxis("Mouse Y") * panSpeed * (distance * 0.05f);
        }

        //scroll = zoom
        float scroll = Input.GetAxis("Mouse ScrollWheel");
        distance -= scroll * zoomSpeed;
        distance = Mathf.Clamp(distance, minDistance, maxDistance);

        ApplyTransform();
    }

    void ApplyTransform()
    {
        Quaternion rot = Quaternion.Euler(pitch, yaw, 0);
        transform.position = target + rot * Vector3.back * distance;
        transform.LookAt(target);
    }
}
