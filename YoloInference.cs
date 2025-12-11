using System;
using System.Collections;
using System.Text;
using UnityEngine;
using UnityEngine.Networking;
using UnityEngine.UI;

public class YoloInference : MonoBehaviour
{
    [Header("Server Settings")]
    public string checkPieceUrl = "http://192.168.100.15:5000/check_piece";

    [Header("UI Debug")]
    public Text debugText;            // Text in the Canvas used for messages
    public RawImage debugView;        // Shows annotated image returned from the server

    [Header("Assembly Manager (Optional)")]
    public ProAssemblyManger assemblyManager;   // Link ProAssemblyManger here

    [Header("AR Car Root (Optional)")]
    public GameObject arCarRoot;      // Parent object of all AR car parts (hidden during capture)

    [Header("Camera")]
    [Tooltip("AR camera used to project virtual parts into screen space")]
    public Camera arCamera;           // Assign the AR camera here (or it will fall back to Camera.main)

    // ------------------------ JSON MODELS ------------------------

    [Serializable]
    public class CheckPieceRequest
    {
        public string image;          // Base64-encoded JPG
        public string expected_class; // YOLO class for the current Unity step
        public int step_index;        // 0-based Unity step index
    }

    [Serializable]
    public class CheckPieceResponse
    {
        public bool success;
        public bool found;
        public bool matched;          // true if YOLO class == expected_class
        public string yolo_class;     // detected YOLO class
        public string expected_class; // expected class for this step
        public int step_index;        // same index we sent (0-based)
        public float confidence;
        public string annotated_image; // base64 image with bounding box + center
        public string error;           // error message when success == false

        // Physical center of the real piece in FULL image pixels
        public float center_x;        // -1 if unknown
        public float center_y;        // -1 if unknown

        // Note: yaw/pitch/roll/reproj_error are available in JSON but not mapped here
        // because we only care about the 2D center for alignment checks.
    }

    private bool isBusy = false;

    // ----------------------------------------------------------------
    //  HIDE DEBUG UI WHEN SCENE STARTS
    // ----------------------------------------------------------------
    void Start()
    {
        if (debugText != null)
            debugText.gameObject.SetActive(false);

        if (debugView != null)
            debugView.gameObject.SetActive(false);
    }

    // ----------------------------------------------------------------
    //  BUTTON EVENT
    // ----------------------------------------------------------------
    // Attach this function to the "On Click" of the Check button
    public void OnCheckButtonClicked()
    {
        if (!isBusy)
        {
            StartCoroutine(CheckPieceRoutine());
        }
    }

    // ----------------------------------------------------------------
    //  Helper: get current part's center in screen space
    // ----------------------------------------------------------------
    /// <summary>
    /// Computes the screen-space center of the current virtual part (ghost/solid)
    /// by using its renderers' bounds. Returns Vector3.zero if something is missing.
    /// </summary>
    private Vector3 GetVirtualPartScreenCenter()
    {
        if (assemblyManager == null)
            return Vector3.zero;

        Camera cam = arCamera != null ? arCamera : Camera.main;
        if (cam == null)
            return Vector3.zero;

        GameObject partObj = assemblyManager.GetCurrentPartObject();
        if (partObj == null)
            return Vector3.zero;

        Renderer[] renderers = partObj.GetComponentsInChildren<Renderer>(true);
        Vector3 worldCenter;

        if (renderers != null && renderers.Length > 0)
        {
            // Combine all renderer bounds into one and take its center
            Bounds combinedBounds = renderers[0].bounds;
            for (int i = 1; i < renderers.Length; i++)
            {
                combinedBounds.Encapsulate(renderers[i].bounds);
            }
            worldCenter = combinedBounds.center;
        }
        else
        {
            // Fallback: use the object's transform position
            worldCenter = partObj.transform.position;
        }

        return cam.WorldToScreenPoint(worldCenter);
    }

    // ----------------------------------------------------------------
    //  MAIN COROUTINE
    // ----------------------------------------------------------------
    private IEnumerator CheckPieceRoutine()
    {
        isBusy = true;

        // 1) Hide AR car so it does not appear in the captured image
        if (arCarRoot != null)
            arCarRoot.SetActive(false);

        // Wait until end of frame so the camera image is updated
        yield return new WaitForEndOfFrame();

        // 2) Capture the screen
        Texture2D tex = ScreenCapture.CaptureScreenshotAsTexture();
        byte[] imgBytes = tex.EncodeToJPG();
        string base64Image = Convert.ToBase64String(imgBytes);

        int screenWidth = tex.width;
        int screenHeight = tex.height;

        // 3) Get expected YOLO class, step index, step name from Unity
        string expectedClass = "";
        int stepIndex = -1;
        string stepName = "";

        if (assemblyManager != null)
        {
            expectedClass = assemblyManager.GetCurrentYOLOClass();
            stepIndex = assemblyManager.GetCurrentStepIndex();
            stepName = assemblyManager.GetCurrentStepName();
        }

        // 4) Build JSON payload
        CheckPieceRequest payload = new CheckPieceRequest
        {
            image = base64Image,
            expected_class = expectedClass,
            step_index = stepIndex
        };

        string json = JsonUtility.ToJson(payload);

        if (debugText != null)
        {
            debugText.gameObject.SetActive(true);
            debugText.text = "Scanning...";
            debugText.color = Color.yellow;
        }

        // 5) Send to server
        using (UnityWebRequest req = new UnityWebRequest(checkPieceUrl, "POST"))
        {
            byte[] bodyRaw = Encoding.UTF8.GetBytes(json);
            req.uploadHandler = new UploadHandlerRaw(bodyRaw);
            req.downloadHandler = new DownloadHandlerBuffer();
            req.SetRequestHeader("Content-Type", "application/json");

            yield return req.SendWebRequest();

            if (req.result != UnityWebRequest.Result.Success)
            {
                Debug.LogError("Network error: " + req.error);
                if (debugText != null)
                {
                    debugText.text = "Error: " + req.error;
                    debugText.color = Color.red;
                }
            }
            else
            {
                string responseText = req.downloadHandler.text;
                Debug.Log("Server response: " + responseText);

                CheckPieceResponse resp = null;
                try
                {
                    resp = JsonUtility.FromJson<CheckPieceResponse>(responseText);
                }
                catch (Exception e)
                {
                    Debug.LogError("JSON parse error: " + e.Message);
                    if (debugText != null)
                    {
                        debugText.text = "JSON Error";
                        debugText.color = Color.red;
                    }
                }

                if (resp == null)
                {
                    if (debugText != null)
                    {
                        debugText.text = "Invalid Response";
                        debugText.color = Color.red;
                    }
                }
                else if (!resp.success)
                {
                    if (debugText != null)
                    {
                        string msg = string.IsNullOrEmpty(resp.error)
                            ? "Unknown error"
                            : resp.error;

                        debugText.text = "Error: " + msg;
                        debugText.color = Color.red;
                    }
                }
                else
                {
                    // ----------------- Step label (starts at 1) -----------------
                    int stepNumber = resp.step_index + 1;
                    string niceStepName = string.IsNullOrEmpty(stepName)
                        ? $"Step {stepNumber}"
                        : $"Step {stepNumber}: {stepName}";

                    string mainLine = "";

                    // Variables for position comparison
                    bool hasPosition = false;
                    bool wellAligned = false;
                    float pixelError = 0f;
                    float errorPercent = 0f;

                    // Only compute alignment if:
                    //  - a piece is found
                    //  - YOLO class matches the expected class
                    //  - the server returned a valid physical center
                    if (resp.found && resp.matched &&
                        resp.center_x >= 0f && resp.center_y >= 0f &&
                        assemblyManager != null &&
                        (arCamera != null || Camera.main != null))
                    {
                        Camera cam = arCamera != null ? arCamera : Camera.main;

                        if (cam != null)
                        {
                            Vector3 virtualScreenCenter = GetVirtualPartScreenCenter();

                            if (virtualScreenCenter.z > 0f)
                            {
                                // Unity screen coordinates: (0,0) at bottom-left
                                float vx_screen = virtualScreenCenter.x;
                                float vy_screen = virtualScreenCenter.y;

                                // Convert Unity screen Y to image-style Y (0 at top)
                                float vx_image = vx_screen;
                                float vy_image = screenHeight - vy_screen;

                                Vector2 virtualPt = new Vector2(vx_image, vy_image);
                                Vector2 physicalPt = new Vector2(resp.center_x, resp.center_y);

                                pixelError = Vector2.Distance(virtualPt, physicalPt);

                                float maxDim = Mathf.Max(screenWidth, screenHeight);
                                if (maxDim > 0f)
                                {
                                    errorPercent = (pixelError / maxDim) * 100f;
                                }
                                else
                                {
                                    errorPercent = 0f;
                                }

                                // For example: consider <= 5% of the screen as "well aligned"
                                float thresholdPercent = 5f;
                                wellAligned = (errorPercent <= thresholdPercent);
                                hasPosition = true;
                            }
                        }
                    }

                    // Build the main message
                    if (!resp.found)
                    {
                        // Expected piece for this step is not visible in the camera
                        mainLine =
                            "The piece for this step is not in the camera ✗\n" +
                            $"Step {stepNumber}";
                    }
                    else if (resp.matched)
                    {
                        // Correct piece class
                        if (hasPosition)
                        {
                            if (wellAligned)
                            {
                                // Correct piece and well aligned
                                mainLine =
                                    "Correct piece ✓\n" +
                                    $"Step {stepNumber}";
                            }
                            else
                            {
                                // Correct piece but position is not well aligned
                                mainLine =
                                    "Correct piece but misaligned ✗\n" +
                                    $"Step {stepNumber}";
                            }
                        }
                        else
                        {
                            // Fallback if we do not have center information
                            mainLine =
                                "Correct piece ✓\n" +
                                $"Step {stepNumber}";
                        }
                    }
                    else
                    {
                        // ---------- WRONG PIECE BRANCH ----------
                        int currentStepNumber = stepNumber;
                        string belongsLine = "";

                        if (assemblyManager != null)
                        {
                            int belongsIndex = assemblyManager.GetStepIndexForYOLOClass(resp.yolo_class);
                            if (belongsIndex >= 0)
                            {
                                int belongsStepNum = belongsIndex + 1;
                                belongsLine = $"\nThis piece belongs to Step {belongsStepNum}";
                            }
                            else
                            {
                                belongsLine = "\nThis piece does not belong to any step";
                            }
                        }

                        mainLine =
                            "Wrong piece ✗\n" +
                            $"Step {currentStepNumber}";
                    }

                    if (debugText != null)
                    {
                        debugText.text = mainLine;

                        // Choose color based on classification + alignment
                        if (!resp.found)
                        {
                            debugText.color = Color.red;
                        }
                        else if (!resp.matched)
                        {
                            debugText.color = Color.red;
                        }
                        else if (resp.matched && hasPosition && !wellAligned)
                        {
                            // Orange color for "correct piece but misaligned"
                            debugText.color = new Color(1f, 0.65f, 0f);
                        }
                        else
                        {
                            // Correct and well aligned (or no position info)
                            debugText.color = Color.green;
                        }
                    }

                    // Show annotated image (bounding box + center) if available
                    if (!string.IsNullOrEmpty(resp.annotated_image) && debugView != null)
                    {
                        try
                        {
                            byte[] annBytes = Convert.FromBase64String(resp.annotated_image);
                            Texture2D annTex = new Texture2D(2, 2, TextureFormat.RGB24, false);
                            annTex.LoadImage(annBytes);
                            debugView.gameObject.SetActive(true);
                            debugView.texture = annTex;
                        }
                        catch (Exception e)
                        {
                            Debug.LogError("Error decoding annotated image: " + e.Message);
                        }
                    }
                }
            }
        }

        // 6) Wait a bit so the user can read, then hide the text
        if (debugText != null)
        {
            yield return new WaitForSeconds(10f);
            debugText.gameObject.SetActive(false);
        }

        // Re-enable AR car
        if (arCarRoot != null)
            arCarRoot.SetActive(true);

        // Cleanup
        UnityEngine.Object.Destroy(tex);
        isBusy = false;
    }
}
