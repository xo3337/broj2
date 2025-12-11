using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

public class ProAssemblyManger : MonoBehaviour
{
    [Header("Ghost Material & Instruction Text")]
    public Material ghostMaterial;
    public Text instructionText;

    [Header("Current Part Preview")]
    public RawImage partPreview;   // RawImage in the Canvas that shows the current part image

    [System.Serializable]
    public class AssemblyStep
    {
        [Header("Step Info")]
        public string stepName;            // Step name or instruction
        public GameObject partObject;      // Actual part in the scene

        [Header("Reference Image for the Part")]
        public Texture partTexture;        // Example: PNG / JPEG image

        [Header("YOLO Class Name")]
        public string yoloClassName;       // Class name used in the YOLO model

        [HideInInspector]
        public Material originalMaterial;      // First original material (fallback)
        [HideInInspector]
        public Material[] originalMaterials;   // All original materials for renderers
    }

    [Header("Steps List")]
    public List<AssemblyStep> steps = new List<AssemblyStep>();

    private int currentStepIndex = 0;

    // --------------------------------------------------------------------
    //  INITIALIZATION
    // --------------------------------------------------------------------
    void Start()
    {
        // Cache original materials and hide all parts at the beginning
        foreach (var step in steps)
        {
            if (step.partObject != null)
            {
                Renderer[] renderers = step.partObject.GetComponentsInChildren<Renderer>(true);
                if (renderers != null && renderers.Length > 0)
                {
                    step.originalMaterials = new Material[renderers.Length];
                    for (int i = 0; i < renderers.Length; i++)
                    {
                        step.originalMaterials[i] = renderers[i].material;
                    }

                    // Optional: keep first material for backward compatibility
                    step.originalMaterial = renderers[0].material;
                }

                // Hide all parts at the beginning
                step.partObject.SetActive(false);
            }
        }

        // Activate first step as ghost if available
        if (steps.Count > 0)
        {
            currentStepIndex = 0;
            ActivateGhostStep(currentStepIndex);
        }
        else
        {
            Debug.LogWarning("ProAssemblyManger: No steps assigned.");
        }
    }

    // --------------------------------------------------------------------
    //  NEXT STEP
    // --------------------------------------------------------------------
    public void ConfirmAndNext()
    {
        if (steps.Count == 0)
            return;

        if (currentStepIndex < 0 || currentStepIndex >= steps.Count)
            return;

        // Make current part solid (real material instead of ghost)
        MakePartSolid(steps[currentStepIndex]);

        // Move to next step
        currentStepIndex++;

        if (currentStepIndex < steps.Count)
        {
            ActivateGhostStep(currentStepIndex);
        }
        else
        {
            // All steps are completed
            if (instructionText != null)
                instructionText.text = "Assembly Complete!";

            if (partPreview != null)
            {
                partPreview.texture = null;
                partPreview.gameObject.SetActive(false);   // Hide RawImage at the end
            }
        }
    }

    // --------------------------------------------------------------------
    //  GO BACK
    // --------------------------------------------------------------------
    public void GoBack()
    {
        if (steps.Count == 0)
            return;

        // Hide current ghost if in range
        if (currentStepIndex >= 0 && currentStepIndex < steps.Count)
        {
            if (steps[currentStepIndex].partObject != null)
                steps[currentStepIndex].partObject.SetActive(false);
        }

        // Move back one step
        currentStepIndex--;

        if (currentStepIndex < 0)
            currentStepIndex = 0;

        ActivateGhostStep(currentStepIndex);
    }

    // --------------------------------------------------------------------
    //  INTERNAL HELPERS
    // --------------------------------------------------------------------
    void ActivateGhostStep(int index)
    {
        if (index < 0 || index >= steps.Count)
            return;

        AssemblyStep step = steps[index];
        GameObject part = step.partObject;

        if (part != null)
        {
            part.SetActive(true);

            // Apply ghost material to all renderers
            Renderer[] renderers = part.GetComponentsInChildren<Renderer>(true);
            if (renderers != null && renderers.Length > 0 && ghostMaterial != null)
            {
                foreach (Renderer rend in renderers)
                {
                    rend.material = ghostMaterial;
                }
            }
        }

        // Instruction text
        if (instructionText != null)
        {
            instructionText.text = step.stepName;
        }

        // UI image for the part
        if (partPreview != null)
        {
            partPreview.gameObject.SetActive(true);
            partPreview.texture = step.partTexture;
        }
    }

    void MakePartSolid(AssemblyStep step)
    {
        if (step == null || step.partObject == null)
            return;

        Renderer[] renderers = step.partObject.GetComponentsInChildren<Renderer>(true);
        if (renderers == null || renderers.Length == 0)
            return;

        // Restore all original materials if we cached them
        if (step.originalMaterials != null && step.originalMaterials.Length == renderers.Length)
        {
            for (int i = 0; i < renderers.Length; i++)
            {
                if (step.originalMaterials[i] != null)
                    renderers[i].material = step.originalMaterials[i];
            }
        }
        else if (step.originalMaterial != null)
        {
            // Fallback: use only the first original material
            foreach (Renderer rend in renderers)
            {
                rend.material = step.originalMaterial;
            }
        }
    }

    // --------------------------------------------------------------------
    //  PUBLIC HELPERS USED BY YoloInference
    // --------------------------------------------------------------------

    /// <summary>
    /// Returns the YOLO class name for the current step.
    /// </summary>
    public string GetCurrentYOLOClass()
    {
        if (currentStepIndex >= 0 && currentStepIndex < steps.Count)
        {
            return steps[currentStepIndex].yoloClassName;
        }

        return "";
    }

    /// <summary>
    /// Returns the current step index (0-based).
    /// </summary>
    public int GetCurrentStepIndex()
    {
        return currentStepIndex;
    }

    /// <summary>
    /// Returns the current step name (instruction text).
    /// </summary>
    public string GetCurrentStepName()
    {
        if (currentStepIndex >= 0 && currentStepIndex < steps.Count)
        {
            return steps[currentStepIndex].stepName;
        }

        return "";
    }

    /// <summary>
    /// Optional: returns the total number of steps.
    /// </summary>
    public int GetTotalSteps()
    {
        return (steps != null) ? steps.Count : 0;
    }

    /// <summary>
    /// Returns the step index (0-based) for a given YOLO class name.
    /// If no step uses this class, returns -1.
    /// </summary>
    public int GetStepIndexForYOLOClass(string yoloClass)
    {
        if (string.IsNullOrEmpty(yoloClass))
            return -1;

        for (int i = 0; i < steps.Count; i++)
        {
            if (steps[i].yoloClassName == yoloClass)
                return i;
        }

        return -1;
    }

    /// <summary>
    /// Returns the GameObject of the current step part (ghost or solid).
    /// </summary>
    public GameObject GetCurrentPartObject()
    {
        if (currentStepIndex >= 0 && currentStepIndex < steps.Count)
        {
            return steps[currentStepIndex].partObject;
        }

        return null;
    }
}
