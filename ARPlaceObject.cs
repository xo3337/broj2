using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.XR.ARFoundation;
using UnityEngine.XR.ARSubsystems;
using UnityEngine.UI;

public class ARPlaceObject : MonoBehaviour
{
    [Header("AR Objects")]
    public GameObject carObject;              // Parent of all car parts
    public ARRaycastManager raycastManager;   // ARRaycastManager in the scene

    [Header("Logic Scripts")]
    public ProAssemblyManger assemblyManager; // Link the assembly script here (optional)
    public YoloInference yoloInference;       // Link the YOLO script in the scene here (optional)

    [Header("Placement & Drag Settings")]
    [Tooltip("This value is kept for compatibility but is no longer used.")]
    public float longPressDuration = 0.4f;

    [Header("Rotation Settings")]
    [Tooltip("This value is kept for compatibility but is no longer used.")]
    public float rotationSpeed = 0.3f; // degrees per pixel of swipe

    [Header("Scale Settings")]
    [Tooltip("Minimum scale factor for the car (not used anymore)")]
    public float minScale = 0.5f;
    [Tooltip("Maximum scale factor for the car (not used anymore)")]
    public float maxScale = 2.0f;

    [Header("Usage Instructions UI")]
    [Tooltip("Optional helper text that explains how to place the car")]
    public Text usageText;                    // Shown when the scene starts

    private List<ARRaycastHit> hits = new List<ARRaycastHit>();
    private bool isPlaced = false;            // Has the car been placed at least once?

    void Start()
    {
        // Hide the car at the beginning
        if (carObject != null)
            carObject.SetActive(false);

        // Show usage instructions when the scene starts (if any)
        if (usageText != null)
            usageText.gameObject.SetActive(true);
    }

    void Update()
    {
        // No touch input
        if (Input.touchCount == 0)
            return;

        // We only care about the first touch
        Touch touch = Input.GetTouch(0);

        // If the touch is on a UI element, ignore it
        if (IsPointerOverUI(touch))
            return;

        // If the car is already placed, we do NOT allow moving/rotating/scaling
        if (isPlaced)
            return;

        // We only react on touch begin to place the car once
        if (touch.phase != TouchPhase.Began)
            return;

        if (raycastManager == null)
            return;

        // Raycast against AR planes
        if (raycastManager.Raycast(touch.position, hits, TrackableType.PlaneWithinPolygon))
        {
            Pose hitPose = hits[0].pose;

            if (carObject != null)
            {
                // Activate and place the car at the hit position
                carObject.SetActive(true);
                carObject.transform.position = hitPose.position;

                // Make the car face the camera (only on first placement)
                Camera cam = Camera.main;
                if (cam != null)
                {
                    Vector3 cameraPos = cam.transform.position;
                    Vector3 lookDir = cameraPos - carObject.transform.position;
                    lookDir.y = 0f; // keep only horizontal rotation
                    if (lookDir.sqrMagnitude > 0.0001f)
                    {
                        carObject.transform.rotation =
                            Quaternion.LookRotation(-lookDir.normalized, Vector3.up);
                    }
                }
            }

            // Mark as placed so no more movement/rotation/scale is allowed
            isPlaced = true;

            // Hide usage instructions after placement
            if (usageText != null)
                usageText.gameObject.SetActive(false);
        }
    }

    // Check if the touch is over a UI element
    bool IsPointerOverUI(Touch touch)
    {
        if (EventSystem.current == null)
            return false;

        PointerEventData eventData = new PointerEventData(EventSystem.current);
        eventData.position = new Vector2(touch.position.x, touch.position.y);
        List<RaycastResult> results = new List<RaycastResult>();
        EventSystem.current.RaycastAll(eventData, results);
        return results.Count > 0;
    }
}
