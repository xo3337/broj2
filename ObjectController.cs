using UnityEngine;

public class FreeObjectController : MonoBehaviour
{
    [Header("Free Rotation Settings")]
    public float rotationSpeed = 0.1f; // Rotation speed

    [Header("Zoom Settings")]
    public float scaleSpeed = 0.001f;
    public float minScale = 0.001f;
    public float maxScale = 0.1f;

    void Update()
    {
        // 1. Free rotation (one finger)
        if (Input.touchCount == 1)
        {
            Touch touch = Input.GetTouch(0);

            if (touch.phase == TouchPhase.Moved)
            {
                // Rotation based on the camera direction, not world axes.
                // This makes the control feel natural no matter where the camera is.

                // Horizontal rotation (left/right) around camera Y axis
                transform.Rotate(Camera.main.transform.up, -touch.deltaPosition.x * rotationSpeed, Space.World);

                // Vertical rotation (up/down) around camera X axis
                transform.Rotate(Camera.main.transform.right, touch.deltaPosition.y * rotationSpeed, Space.World);
            }
        }

        // 2. Zoom in/out (two fingers)
        else if (Input.touchCount == 2)
        {
            Touch touch0 = Input.GetTouch(0);
            Touch touch1 = Input.GetTouch(1);

            Vector2 touch0PrevPos = touch0.position - touch0.deltaPosition;
            Vector2 touch1PrevPos = touch1.position - touch1.deltaPosition;

            float prevTouchDeltaMag = (touch0PrevPos - touch1PrevPos).magnitude;
            float touchDeltaMag = (touch0.position - touch1.position).magnitude;

            float deltaMagnitudeDiff = prevTouchDeltaMag - touchDeltaMag;

            float scaleFactor = -deltaMagnitudeDiff * scaleSpeed;
            Vector3 newScale = transform.localScale + Vector3.one * scaleFactor;

            newScale.x = Mathf.Clamp(newScale.x, minScale, maxScale);
            newScale.y = Mathf.Clamp(newScale.y, minScale, maxScale);
            newScale.z = Mathf.Clamp(newScale.z, minScale, maxScale);

            transform.localScale = newScale;
        }
    }
}
