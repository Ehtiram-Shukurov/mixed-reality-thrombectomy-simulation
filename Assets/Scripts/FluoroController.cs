using UnityEngine;

// Fluoroscopy controller — hold the A button to activate X-ray mode.
public class FluoroController : MonoBehaviour
{
    [Header("References")]
    public DicomVolumeLoader dicomLoader;

    [Header("Fluoro Shader Mode")]
    [Tooltip("The _RenderMode value that gives the X-ray / segmentation view. " +
             "Mode 6 was confirmed working in the old CatheterController.")]
    public int fluoroRenderMode = 6;

    // Public state — read by SimulationManager and GuidewireUI
    public bool IsActive { get; private set; } = false;
    public float TotalExposureTime { get; private set; } = 0f;

    // The render mode that was active before fluoro — restored on release
    private int _previousRenderMode = 0;

    void Update()
    {
        bool buttonHeld = OVRInput.Get(OVRInput.Button.One, OVRInput.Controller.RTouch);

        if (buttonHeld && !IsActive)
            ActivateFluoro();
        else if (!buttonHeld && IsActive)
            DeactivateFluoro();

        if (IsActive)
            TotalExposureTime += Time.deltaTime;
    }

    private void ActivateFluoro()
    {
        if (dicomLoader == null || dicomLoader.volumeMaterial == null) return;

        // Store current mode so we can restore it exactly on release
        _previousRenderMode = dicomLoader.volumeMaterial.GetInt("_RenderMode");
        dicomLoader.UpdateSegmentationMode(fluoroRenderMode);

        IsActive = true;
        Debug.Log("[Fluoro] Activated.");
    }

    private void DeactivateFluoro()
    {
        if (dicomLoader == null) return;

        dicomLoader.UpdateSegmentationMode(_previousRenderMode);

        IsActive = false;
        Debug.Log($"[Fluoro] Deactivated. Total exposure: {TotalExposureTime:F1}s");
    }

    // Call this at the start of a new simulation run to reset the radiation score
    public void ResetTimer()
    {
        TotalExposureTime = 0f;
        Debug.Log("[Fluoro] Timer reset.");
    }
}