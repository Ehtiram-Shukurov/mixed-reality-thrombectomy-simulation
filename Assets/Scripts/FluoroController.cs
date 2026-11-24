using UnityEngine;

// X-ray imaging. Hold A (the fluoroscopy pedal) for live X-ray on the monitor; in Training
// mode the 3D model also turns into X-ray while A is held. Press B (or left X) to inject
// contrast dye: it flows through the vessels for a few seconds and the clot shows as the
// place where it stops. Both count as imaging time.
public class FluoroController : MonoBehaviour
{
    [Header("References")]
    public DicomVolumeLoader dicomLoader;

    [Header("Fluoro Shader Mode")]
    [Tooltip("The _RenderMode value that gives the X-ray view.")]
    public int fluoroRenderMode = 6;

    [Tooltip("In Training mode, holding A also turns the 3D model into X-ray. In Assessment mode X-ray is only on the monitor.")]
    public bool showXRayInSceneInTraining = true;

    [Header("Contrast")]
    [Tooltip("How long one injection lasts, in seconds.")]
    public float contrastSeconds = 5f;
    [Tooltip("How faintly the vessels show on plain fluoroscopy without dye (0 to 1).")]
    [Range(0f, 1f)] public float plainVesselVisibility = 0.05f;
    [Tooltip("Dye fades in and out over this many seconds.")]
    public float contrastFade = 0.4f;

    // Imaging is on: A held or a dye run going. Counts toward X-ray time.
    public bool IsActive { get; private set; } = false;
    // The main view is showing X-ray (only while A is held).
    public bool MainViewXRay { get; private set; } = false;
    public float TotalExposureTime { get; private set; } = 0f;
    public bool ContrastActive => _contrastLeft > 0f;
    public int ContrastRuns { get; private set; } = 0;
    // Seconds since the current injection started.
    public float ContrastElapsed => ContrastActive ? contrastSeconds - _contrastLeft : 0f;
    // 1 right after an injection, falling to 0 as the dye washes out.
    public float ContrastRemaining01 => contrastSeconds <= 0f ? 0f : Mathf.Clamp01(_contrastLeft / contrastSeconds);
    // Vessel visibility for the X-ray rendering right now (plain level up to 1 with dye).
    public float ContrastLevel { get; private set; }

    // Raised when an injection starts. Listeners start the dye animation.
    public event System.Action ContrastStarted;

    private const string ContrastProperty = "_Contrast";

    private int _previousRenderMode = 0;
    private float _contrastLeft;
    private SimulationManager _sim;

    private bool _waitForRelease;

    void Start()
    {
        _sim = FindFirstObjectByType<SimulationManager>();
        ContrastLevel = plainVesselVisibility;
    }

    // B is the stent button once the catheter is at the clot, so no dye then.
    private bool ContrastAllowed()
    {
        if (_sim == null) return true;
        // The B press that just started a pass or a new case is not a dye injection.
        if (_sim.PhaseChangedFrame == Time.frameCount) return false;
        var p = _sim.CurrentPhase;
        return p == SimulationManager.SimPhase.Phase1_Navigation
            || p == SimulationManager.SimPhase.Phase2a_CatheterAdvance
            || p == SimulationManager.SimPhase.Phase2c_Extraction;
    }

    void Update()
    {
        if (SimInput.ContrastPressed && ContrastAllowed() && !ContrastActive) Inject();
        if (_contrastLeft > 0f) _contrastLeft = Mathf.Max(0f, _contrastLeft - Time.deltaTime);

        // A is the fluoroscopy pedal: live X-ray on the monitor while held, as in a cath lab.
        // The 3D model only turns into X-ray in Training mode, as a learning aid.
        // A is the start card's button while it shows, not the pedal. The press that starts the
        // run (or any press that changed the phase) only counts as the pedal once A is let go.
        bool rawA = SimInput.FluoroHeld;
        if (_sim != null && (_sim.CurrentPhase == SimulationManager.SimPhase.Idle || _sim.PhaseChangedFrame == Time.frameCount) && rawA)
            _waitForRelease = true;
        if (!rawA) _waitForRelease = false;
        bool aHeld = rawA && !_waitForRelease && (_sim == null || _sim.CurrentPhase != SimulationManager.SimPhase.Idle);
        bool xrayInScene = aHeld && showXRayInSceneInTraining && SimMode.HintsOn;
        if (xrayInScene && !MainViewXRay) SwitchMainToXRay();
        else if (!xrayInScene && MainViewXRay) RestoreMainView();

        IsActive = aHeld || ContrastActive;
        if (IsActive) TotalExposureTime += Time.deltaTime;

        UpdateContrastLevel();
        if (MainViewXRay) SetMainContrast(ContrastLevel);
    }

    // Starts a dye run. Also used by the control angiogram after extraction.
    public void Inject()
    {
        _contrastLeft = contrastSeconds;
        ContrastRuns++;
        Feedback.ContrastInjected();
        ContrastStarted?.Invoke();
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
        ContrastLevel = Mathf.Lerp(plainVesselVisibility, 1f, target);
    }

    private void SetMainContrast(float value)
    {
        if (dicomLoader != null && dicomLoader.volumeMaterial != null && dicomLoader.volumeMaterial.HasProperty(ContrastProperty))
            dicomLoader.volumeMaterial.SetFloat(ContrastProperty, value);
    }

    private void SwitchMainToXRay()
    {
        if (dicomLoader == null || dicomLoader.volumeMaterial == null) return;
        _previousRenderMode = dicomLoader.volumeMaterial.GetInt("_RenderMode");
        dicomLoader.UpdateSegmentationMode(fluoroRenderMode);
        MainViewXRay = true;
    }

    private void RestoreMainView()
    {
        if (dicomLoader == null) return;
        dicomLoader.UpdateSegmentationMode(_previousRenderMode);
        SetMainContrast(1f);
        MainViewXRay = false;
    }

    void OnDisable()
    {
        // Leave the shared material the way the shader expects it.
        SetMainContrast(1f);
    }

    // Call this at the start of a new simulation run.
    public void ResetTimer()
    {
        TotalExposureTime = 0f;
        ContrastRuns = 0;
        _contrastLeft = 0f;
    }
}
