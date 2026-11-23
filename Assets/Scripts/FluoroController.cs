using UnityEngine;

// X-ray imaging. Hold A for plain fluoroscopy: bones and soft tissue, vessels barely show.
// Press left X to inject contrast dye: the vessels light up for a few seconds and the clot
// shows as a gap in the dye. Both count as imaging time.
public class FluoroController : MonoBehaviour
{
    [Header("References")]
    public DicomVolumeLoader dicomLoader;

    [Header("Fluoro Shader Mode")]
    [Tooltip("The _RenderMode value that gives the X-ray / segmentation view. " +
             "Mode 6 was confirmed working in the old CatheterController.")]
    public int fluoroRenderMode = 6;

    [Header("Contrast")]
    [Tooltip("How long one injection lights up the vessels, in seconds.")]
    public float contrastSeconds = 5f;
    [Tooltip("How faintly the vessels show on plain fluoroscopy without dye (0 to 1).")]
    [Range(0f, 1f)] public float plainVesselVisibility = 0.05f;
    [Tooltip("Dye fades in and out over this many seconds.")]
    public float contrastFade = 0.4f;

    // Public state, read by SimulationManager, GuidewireUI and ClotTarget
    public bool IsActive { get; private set; } = false;
    public float TotalExposureTime { get; private set; } = 0f;
    public bool ContrastActive => _contrastLeft > 0f;
    public int ContrastRuns { get; private set; } = 0;
    // 1 right after an injection, falling to 0 as the dye washes out.
    public float ContrastRemaining01 => contrastSeconds <= 0f ? 0f : Mathf.Clamp01(_contrastLeft / contrastSeconds);

    private const string ContrastProperty = "_Contrast";

    // The render mode that was active before fluoro. Restored on release.
    private int _previousRenderMode = 0;
    private float _contrastLeft;
    private float _contrastLevel;

    private SimulationManager _sim;

    void Start()
    {
        _sim = FindFirstObjectByType<SimulationManager>();
    }

    // B is the stent button once the catheter is at the clot, so no dye then.
    private bool ContrastAllowed()
    {
        if (_sim == null) return true;
        var p = _sim.CurrentPhase;
        return p == SimulationManager.SimPhase.Phase1_Navigation
            || p == SimulationManager.SimPhase.Phase2a_CatheterAdvance
            || p == SimulationManager.SimPhase.Phase2c_Extraction;
    }

    void Update()
    {
        bool pressed = SimInput.ContrastPressed;
        if (pressed && ContrastAllowed() && !ContrastActive)
        {
            _contrastLeft = contrastSeconds;
            ContrastRuns++;
            Feedback.ContrastInjected();
        }
        if (_contrastLeft > 0f) _contrastLeft = Mathf.Max(0f, _contrastLeft - Time.deltaTime);

        bool wantImaging = SimInput.FluoroHeld || ContrastActive;

        if (wantImaging && !IsActive)
            ActivateFluoro();
        else if (!wantImaging && IsActive)
            DeactivateFluoro();

        if (IsActive)
        {
            TotalExposureTime += Time.deltaTime;
            UpdateContrastLevel();
        }
    }

    // Fade the dye in at the start and out at the end of a run.
    private void UpdateContrastLevel()
    {
        float target = 0f;
        if (ContrastActive)
        {
            float elapsed = contrastSeconds - _contrastLeft;
            float fadeIn = contrastFade <= 0f ? 1f : Mathf.Clamp01(elapsed / contrastFade);
            float fadeOut = contrastFade <= 0f ? 1f : Mathf.Clamp01(_contrastLeft / contrastFade);
            target = Mathf.Min(fadeIn, fadeOut);
        }
        _contrastLevel = Mathf.Lerp(plainVesselVisibility, 1f, target);
        SetContrast(_contrastLevel);
    }

    private void SetContrast(float value)
    {
        if (dicomLoader != null && dicomLoader.volumeMaterial != null && dicomLoader.volumeMaterial.HasProperty(ContrastProperty))
            dicomLoader.volumeMaterial.SetFloat(ContrastProperty, value);
    }

    private void ActivateFluoro()
    {
        if (dicomLoader == null || dicomLoader.volumeMaterial == null) return;

        // Store current mode so we can restore it exactly on release
        _previousRenderMode = dicomLoader.volumeMaterial.GetInt("_RenderMode");
        dicomLoader.UpdateSegmentationMode(fluoroRenderMode);
        SetContrast(plainVesselVisibility);

        IsActive = true;
        Debug.Log("[Fluoro] Activated.");
    }

    private void DeactivateFluoro()
    {
        if (dicomLoader == null) return;

        dicomLoader.UpdateSegmentationMode(_previousRenderMode);
        SetContrast(1f);

        IsActive = false;
        Debug.Log($"[Fluoro] Deactivated. Total exposure: {TotalExposureTime:F1}s");
    }

    void OnDisable()
    {
        // Leave the shared material the way the shader expects it.
        SetContrast(1f);
    }

    // Call this at the start of a new simulation run.
    public void ResetTimer()
    {
        TotalExposureTime = 0f;
        ContrastRuns = 0;
        _contrastLeft = 0f;
        Debug.Log("[Fluoro] Timer reset.");
    }
}
