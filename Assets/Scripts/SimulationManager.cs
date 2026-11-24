using System;
using System.Globalization;
using System.IO;
using UnityEngine;

// Master state machine. Controls phase transitions and system activation.
public class SimulationManager : MonoBehaviour
{
    public enum SimPhase
    {
        Idle,
        Phase1_Navigation,
        Phase2a_CatheterAdvance,
        Phase2b_StentDeploy,
        Phase2c_Extraction,
        Phase2d_ControlAngio,
        Phase3_Results
    }

    [Header("Systems")]
    public GuidewireSimulator guidewireSimulator;
    public FluoroController fluoroController;
    public GuidewireUI guidewireUI;
    public CatheterSimulator catheterSimulator;

    [Header("Clot")]
    public ClotTarget clotTarget;

    [Header("UI")]
    public ResultsPanel resultsPanel;

    [Header("Stent")]
    [Tooltip("Seconds the stent stays open on the clot before you can pull back. Real procedures wait a few minutes; this is shortened.")]
    public float stentDwellSeconds = 3f;

    [Header("Case difficulty (twist window in degrees, hold in seconds)")]
    public float easyTolerance = 25f;
    public float easyHold = 0.25f;
    public float mediumTolerance = 20f;
    public float mediumHold = 0.4f;
    public float hardTolerance = 12f;
    public float hardHold = 0.7f;
    [Tooltip("Bend angle (degrees) where pushing hard counts as forcing, per level.")]
    public float easyForceAngle = 55f, mediumForceAngle = 45f, hardForceAngle = 38f;

    [Header("Outcome")]
    [Tooltip("Seconds of pulling too fast in one pass before a clot fragment stays behind. A rule for the simulation, not clinical data.")]
    public float fragmentTooFastSeconds = 2f;
    // Seconds pulled too fast in the current pass. Shown in the result message.
    public float PassTooFastSeconds => _passTooFast;
    private float _passTooFast;
    public int maxPasses = 3;
    [Tooltip("Seconds the control angiogram runs before the result shows.")]
    public float angioSeconds = 4.5f;

    [Header("Restart")]
    [Tooltip("Hold left Y this long to restart the run.")]
    public float restartHoldSeconds = 2f;

    public SimPhase CurrentPhase { get; private set; } = SimPhase.Idle;
    // 0 to 1 while left Y is held. Shown on the phase card.
    public float RestartProgress { get; private set; }
    public bool StentDeployed => _stentDeployed;
    // 0 to 1 while the stent settles on the clot.
    public float StentDwell01 => _stentDeployed ? Mathf.Clamp01(_dwell / Mathf.Max(0.1f, stentDwellSeconds)) : 0f;

    // Outcome of the passes so far.
    public int Passes => _passes;
    public bool Reopened => _reopened;
    public bool CanTryAgain => !_reopened && _passes < maxPasses;
    // The control angiogram has run long enough for the choice to be shown.
    public bool AngioDone => CurrentPhase == SimPhase.Phase2d_ControlAngio && _angioTimer >= angioSeconds;
    public string OutcomeText => _reopened
        ? $"Reopened, {_passes} pass{(_passes == 1 ? "" : "es")}"
        : $"Still blocked, {_passes} pass{(_passes == 1 ? "" : "es")}";

    private int _passes;
    private int _forcedBends;
    private int _passPenalty;
    private bool _reopened;
    private float _angioTimer;

    private float _totalTime = 0f;
    private bool _timerRunning = false;
    private bool _stentDeployed = false;
    private float _dwell = 0f;
    private float _clotBehindTip = 0f;
    private int _extractionPenalty = 0;
    private int _speedWarnings = 0;
    private int _wrongTurns = 0;
    private int _deadEnds = 0;
    private float _restartHold = 0f;
    private bool _started = false;
    private StentVisual _stent;
    private DyeFlow _dye;

    void Start()
    {
        if (resultsPanel != null) resultsPanel.gameObject.SetActive(false);
        _stent = FindFirstObjectByType<StentVisual>();
        _dye = FindFirstObjectByType<DyeFlow>();

        if (guidewireSimulator != null)
        {
            guidewireSimulator.BranchChosen += OnBranchChosen;
            guidewireSimulator.DeadEndReached += OnDeadEnd;
            guidewireSimulator.Forced += OnForced;
        }

        EnterPhase(SimPhase.Phase1_Navigation);
    }

    void OnDestroy()
    {
        if (guidewireSimulator != null)
        {
            guidewireSimulator.BranchChosen -= OnBranchChosen;
            guidewireSimulator.DeadEndReached -= OnDeadEnd;
            guidewireSimulator.Forced -= OnForced;
        }
    }

    // Wire pushed hard through a sharp bend: a handling error.
    private void OnForced()
    {
        if (CurrentPhase == SimPhase.Phase1_Navigation) _forcedBends++;
    }

    void Update()
    {
        if (_timerRunning) _totalTime += Time.deltaTime;
        if (CurrentPhase == SimPhase.Phase2c_Extraction && catheterSimulator != null && catheterSimulator.IsTooFast)
            _passTooFast += Time.deltaTime;

        switch (CurrentPhase)
        {
            case SimPhase.Phase2b_StentDeploy: CheckStentButton(); break;
            case SimPhase.Phase2d_ControlAngio: HandleControlAngio(); break;
        }

        // On the results: A plays the same case again, B moves to the next case.
        if (CurrentPhase == SimPhase.Phase3_Results)
        {
            if (SimInput.ConfirmPressed) RestartRun();
            else if (SimInput.StentPressed) { clotTarget?.NextCase(); RestartRun(); }
        }

        HandleRestartHold();
    }

    // Hold left Y to start over at any time.
    private void HandleRestartHold()
    {
        if (CurrentPhase == SimPhase.Idle) return;

        if (SimInput.RestartHeld) _restartHold += Time.unscaledDeltaTime;
        else _restartHold = Mathf.Max(0f, _restartHold - Time.unscaledDeltaTime * 2f);

        RestartProgress = Mathf.Clamp01(_restartHold / Mathf.Max(0.1f, restartHoldSeconds));
        if (_restartHold >= restartHoldSeconds)
        {
            _restartHold = 0f;
            RestartProgress = 0f;
            RestartRun();
        }
    }

    public void StartPhase1() => EnterPhase(SimPhase.Phase1_Navigation);

    // Used by Play Again and by the left Y hold. Puts everything back at the start.
    public void RestartRun()
    {
        if (resultsPanel != null) resultsPanel.gameObject.SetActive(false);
        HapticManager.Instance?.StopAll();
        catheterSimulator?.ResetCatheter();
        clotTarget?.ResetClot();
        guidewireSimulator?.ResetGuidewire();
        _stent?.Hide();
        FindFirstObjectByType<XRayMonitor>()?.ClearRoadmap();
        EnterPhase(SimPhase.Phase1_Navigation);
    }

    // Harder cases need a more precise twist at each fork and a longer steady hold.
    private void ApplyCaseDifficulty()
    {
        if (guidewireSimulator == null || clotTarget == null) return;
        switch (clotTarget.caseLevel)
        {
            case ClotTarget.CaseLevel.Easy:
                guidewireSimulator.alignmentTolerance = easyTolerance;
                guidewireSimulator.alignHoldSeconds = easyHold;
                guidewireSimulator.forceAngle = easyForceAngle;
                break;
            case ClotTarget.CaseLevel.Hard:
                guidewireSimulator.alignmentTolerance = hardTolerance;
                guidewireSimulator.alignHoldSeconds = hardHold;
                guidewireSimulator.forceAngle = hardForceAngle;
                break;
            default:
                guidewireSimulator.alignmentTolerance = mediumTolerance;
                guidewireSimulator.alignHoldSeconds = mediumHold;
                guidewireSimulator.forceAngle = mediumForceAngle;
                break;
        }
    }

    // Hooked to the Case button on the menu: next level and a fresh run.
    public void CycleCase(bool _)
    {
        if (clotTarget == null) return;
        clotTarget.NextCase();
        RestartRun();
        GuidewireUI.Notify(clotTarget.caseLevel switch
        {
            ClotTarget.CaseLevel.Easy => "Easy case: short route, forks accept a wide twist. Run restarted.",
            ClotTarget.CaseLevel.Hard => "Hard case: most forks on the route, forks need a tight twist. Run restarted.",
            _ => "Medium case: longer route, normal twist at forks. Run restarted."
        }, 4f);
    }

    // Seconds since the current phase started. Used to show the case at the start of a run.
    public float TimeInPhase => Time.time - _phaseStartTime;
    private float _phaseStartTime;

    // Hooked to the Assessment toggle on the menu (On Value Changed, dynamic bool).
    // Takes effect right away; the run log records the mode at the end of the run.
    public void SetAssessmentMode(bool on)
    {
        bool changed = SimMode.Assessment != on;
        SimMode.Assessment = on;
        if (changed)
            GuidewireUI.Notify(on
                ? "Assessment mode: no fork hints, no clot marker, no X-ray on the model. Results are scored."
                : "Training mode: fork hints, clot marker and X-ray on the model are back.", 4f);
        Debug.Log("[SimManager] " + (on ? "Assessment mode: hints off." : "Training mode: hints on."));
    }

    // Frame of the last phase change, so a button press that changed the phase is not reused.
    public int PhaseChangedFrame { get; private set; } = -1;

    private void EnterPhase(SimPhase phase)
    {
        CurrentPhase = phase;
        PhaseChangedFrame = Time.frameCount;
        _phaseStartTime = Time.time;
        if (_started) Feedback.PhaseChanged();
        _started = true;

        switch (phase)
        {
            case SimPhase.Phase1_Navigation:
                _totalTime = 0f;
                _timerRunning = true;
                _stentDeployed = false;
                _dwell = 0f;
                _extractionPenalty = 0;
                _speedWarnings = 0;
                _wrongTurns = 0;
                _deadEnds = 0;
                _passes = 0;
                _forcedBends = 0;
                _reopened = false;
                fluoroController?.ResetTimer();
                if (_dye != null) _dye.Reperfused = false;
                if (resultsPanel != null) resultsPanel.gameObject.SetActive(false);
                ApplyCaseDifficulty();
                SetGuidewire(true);
                Debug.Log("[SimManager] Phase 1 — Navigate guidewire to clot.");
                break;

            case SimPhase.Phase2a_CatheterAdvance:
                // Also the start of a second or third pass: the wire stays across the clot.
                SetGuidewire(false);
                _stentDeployed = false;
                _dwell = 0f;
                catheterSimulator?.ResetCatheter();
                catheterSimulator?.BeginAdvance();
                Debug.Log("[SimManager] Phase 2a — Advance catheter to clot.");
                break;

            case SimPhase.Phase2b_StentDeploy:
                Debug.Log("[SimManager] Phase 2b — Deploy stent.");
                break;

            case SimPhase.Phase2c_Extraction:
                // The clot comes back with the stent, which sits on it behind the catheter tip.
                if (clotTarget != null && catheterSimulator != null)
                    clotTarget.Capture(catheterSimulator, _clotBehindTip);
                _passPenalty = 0;
                _passTooFast = 0f;
                catheterSimulator?.BeginRetraction();
                Debug.Log("[SimManager] Phase 2c — Extract clot.");
                break;

            case SimPhase.Phase2d_ControlAngio:
                // Pulling too fast in this pass leaves a fragment behind. Then dye shows the result.
                _passes++;
                // Counting how long the pull was too fast, not how many times it crossed the limit:
                // a hand on a stick drifts over and back many times without being rough.
                _reopened = _passTooFast <= fragmentTooFastSeconds;
                _stent?.Hide();
                if (_reopened) clotTarget?.Release();
                else clotTarget?.LeaveFragment();
                if (_dye != null) _dye.Reperfused = _reopened;
                _angioTimer = 0f;
                fluoroController?.Inject();
                Debug.Log($"[SimManager] Control angiogram after pass {_passes}: {(_reopened ? "reopened" : "fragment left")}.");
                break;

            case SimPhase.Phase3_Results:
                _timerRunning = false;
                SetGuidewire(false);
                if (_reopened) clotTarget?.Release();
                _stent?.Hide();
                if (_reopened) Feedback.Success();
                ShowResults();
                Debug.Log("[SimManager] Phase 3 — Results.");
                break;
        }
    }

    private void CheckStentButton()
    {
        if (_stentDeployed)
        {
            // The stent has to settle into the clot before you can pull it back.
            _dwell += Time.deltaTime;
            if (_dwell >= stentDwellSeconds) EnterPhase(SimPhase.Phase2c_Extraction);
            return;
        }

        if (SimInput.StentPressed)
        {
            _stentDeployed = true;
            _dwell = 0f;
            // The catheter tip is past the clot. The stent opens across it.
            _clotBehindTip = (clotTarget != null && catheterSimulator != null)
                ? clotTarget.DistanceBehindTip(catheterSimulator) : 0f;
            Feedback.StentDeployed();
            _stent?.Deploy(_clotBehindTip);
            Debug.Log($"[SimManager] Stent deployed across the clot ({_clotBehindTip:F3} m behind the tip).");
        }
    }

    // Called by ClotTarget
    public void OnGuidewireReachedClot()
    {
        if (CurrentPhase == SimPhase.Phase1_Navigation)
            EnterPhase(SimPhase.Phase2a_CatheterAdvance);
    }

    // Called by CatheterSimulator
    public void OnCatheterArrivedAtClot()
    {
        if (CurrentPhase == SimPhase.Phase2a_CatheterAdvance)
            EnterPhase(SimPhase.Phase2b_StentDeploy);
    }

    // Called by CatheterSimulator
    public void OnCatheterFullyRetracted()
    {
        if (CurrentPhase == SimPhase.Phase2c_Extraction)
            EnterPhase(SimPhase.Phase2d_ControlAngio);
    }

    // After the control angiogram: reopened goes to the results; a fragment lets you
    // choose another pass (B) or finish (A).
    private void HandleControlAngio()
    {
        _angioTimer += Time.deltaTime;
        if (_angioTimer < angioSeconds) return;

        if (!CanTryAgain) { EnterPhase(SimPhase.Phase3_Results); return; }
        if (SimInput.StentPressed) EnterPhase(SimPhase.Phase2a_CatheterAdvance);
        else if (SimInput.ConfirmPressed) EnterPhase(SimPhase.Phase3_Results);
    }

    // Called by CatheterSimulator on each speed warning
    public void AddExtractionPenalty(int points)
    {
        _extractionPenalty += points;
        _passPenalty += points;
        _speedWarnings++;
    }

    // A wrong turn is a branch that does not lead to the clot.
    private void OnBranchChosen(CenterlineNode branch)
    {
        if (CurrentPhase != SimPhase.Phase1_Navigation || clotTarget == null) return;
        if (!clotTarget.IsOnPath(branch)) _wrongTurns++;
    }

    private void OnDeadEnd()
    {
        if (CurrentPhase == SimPhase.Phase1_Navigation) _deadEnds++;
    }

    private void ShowResults()
    {
        float exposure = fluoroController != null ? fluoroController.TotalExposureTime : 0f;
        string rating = ExtractionRating(out Color color);
        int contrast = fluoroController != null ? fluoroController.ContrastRuns : 0;
        resultsPanel?.ShowResults(_totalTime, exposure, rating, color, _wrongTurns, _deadEnds, contrast, _forcedBends);
        resultsPanel?.ShowOutcome(OutcomeText, _reopened);
        WriteRunLog(exposure, rating);
    }

    // Each speed warning adds 5 points. Cutoffs are a guess, not validated.
    private string ExtractionRating(out Color color)
    {
        if (_extractionPenalty == 0) { color = Color.green; return "Careful"; }
        if (_extractionPenalty <= 10) { color = Color.yellow; return "Good"; }
        color = Color.red;
        return "Too Fast";
    }

    // One JSON line per finished run, in the app's data folder on the headset.
    private void WriteRunLog(float exposure, string rating)
    {
        try
        {
            var inv = CultureInfo.InvariantCulture;
            int clotId = clotTarget != null && clotTarget.ClotNode != null ? clotTarget.ClotNode.id : -1;
            string line = "{"
                + "\"utc\":\"" + DateTime.UtcNow.ToString("o", inv) + "\","
                + "\"totalSeconds\":" + _totalTime.ToString("F1", inv) + ","
                + "\"fluoroSeconds\":" + exposure.ToString("F1", inv) + ","
                + "\"wrongTurns\":" + _wrongTurns + ","
                + "\"deadEnds\":" + _deadEnds + ","
                + "\"forcedBends\":" + _forcedBends + ","
                + "\"contrastRuns\":" + (fluoroController != null ? fluoroController.ContrastRuns : 0) + ","
                + "\"speedWarnings\":" + _speedWarnings + ","
                + "\"mode\":\"" + (SimMode.Assessment ? "assessment" : "training") + "\","
                + "\"case\":\"" + (clotTarget != null ? clotTarget.caseLevel.ToString() : "") + "\","
                + "\"forksOnPath\":" + (clotTarget != null ? clotTarget.ForksOnPath : 0) + ","
                + "\"passes\":" + _passes + ","
                + "\"reopened\":" + (_reopened ? "true" : "false") + ","
                + "\"rating\":\"" + rating + "\","
                + "\"clotNode\":" + clotId
                + "}";
            File.AppendAllText(Path.Combine(Application.persistentDataPath, "run_log.jsonl"), line + "\n");
        }
        catch (Exception e)
        {
            Debug.LogWarning("[SimManager] Could not write run log: " + e.Message);
        }
    }

    private void SetGuidewire(bool active)
    {
        if (guidewireSimulator != null) guidewireSimulator.enabled = active;
    }
}
