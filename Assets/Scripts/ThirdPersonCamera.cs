using UnityEngine;

public class ThirdPersonCamera : MonoBehaviour
{
    public Transform target;
    public float distance = 4f;          // how far behind
    public float height = 1.7f;          // pivot height above his feet
    public float shoulderOffset = 0.5f;  // slight shift to the right
    public float sensitivity = 3f;
    public float minPitch = -20f;        // look up limit
    public float maxPitch = 60f;         // look down limit
    public float smooth = 12f;
    public float collisionRadius = 0.25f;
    public LayerMask collisionMask = ~0;

    float yaw;
    float pitch = 15f;

    void Start()
    {
        if (target != null) yaw = target.eulerAngles.y;
        Cursor.lockState = CursorLockMode.Locked;
        Cursor.visible = false;
    }

    void LateUpdate()
    {
        if (target == null) return;

        if (Input.GetKeyDown(KeyCode.Escape))
        {
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
        }
        if (Input.GetMouseButtonDown(0) && Cursor.lockState != CursorLockMode.Locked)
        {
            Cursor.lockState = CursorLockMode.Locked;
            Cursor.visible = false;
        }

        yaw += Input.GetAxis("Mouse X") * sensitivity;
        pitch -= Input.GetAxis("Mouse Y") * sensitivity;
        pitch = Mathf.Clamp(pitch, minPitch, maxPitch);

        Quaternion rot = Quaternion.Euler(pitch, yaw, 0f);
        Vector3 pivot = target.position + Vector3.up * height;
        Vector3 desired = pivot + rot * new Vector3(shoulderOffset, 0f, -distance);

        // Pull the camera in if a wall is between it and the player
        Vector3 toCam = desired - pivot;
        if (Physics.SphereCast(pivot, collisionRadius, toCam.normalized, out RaycastHit hit,
                               toCam.magnitude, collisionMask, QueryTriggerInteraction.Ignore))
        {
            desired = pivot + toCam.normalized * Mathf.Max(0.5f, hit.distance);
        }

        transform.position = Vector3.Lerp(transform.position, desired, smooth * Time.deltaTime);
        transform.rotation = rot;
    }
}
