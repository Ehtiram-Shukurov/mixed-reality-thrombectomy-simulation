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

    [Header("Restart")]
    [Tooltip("Hold left Y this long to restart the run.")]
    public float restartHoldSeconds = 2f;

    public SimPhase CurrentPhase { get; private set; } = SimPhase.Idle;
    // 0 to 1 while left Y is held. Shown on the phase card.
    public float RestartProgress { get; private set; }
    public bool StentDeployed => _stentDeployed;
    // 0 to 1 while the stent settles on the clot.
    public float StentDwell01 => _stentDeployed ? Mathf.Clamp01(_dwell / Mathf.Max(0.1f, stentDwellSeconds)) : 0f;

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
        }

        EnterPhase(SimPhase.Phase1_Navigation);
    }

    void OnDestroy()
    {
        if (guidewireSimulator != null)
        {
            guidewireSimulator.BranchChosen -= OnBranchChosen;
            guidewireSimulator.DeadEndReached -= OnDeadEnd;
        }
    }

    void Update()
    {
        if (_timerRunning) _totalTime += Time.deltaTime;

        switch (CurrentPhase)
        {
            case SimPhase.Phase2b_StentDeploy: CheckStentButton(); break;
        }

        // Controller shortcut for Play Again, in case the poke button is hard to reach.
        if (CurrentPhase == SimPhase.Phase3_Results && SimInput.ConfirmPressed) RestartRun();

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

    private void EnterPhase(SimPhase phase)
    {
        CurrentPhase = phase;
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
                fluoroController?.ResetTimer();
                if (_dye != null) _dye.Reperfused = false;
                if (resultsPanel != null) resultsPanel.gameObject.SetActive(false);
                SetGuidewire(true);
                Debug.Log("[SimManager] Phase 1 — Navigate guidewire to clot.");
                break;

            case SimPhase.Phase2a_CatheterAdvance:
                SetGuidewire(false);
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
                catheterSimulator?.BeginRetraction();
                Debug.Log("[SimManager] Phase 2c — Extract clot.");
                break;

            case SimPhase.Phase3_Results:
                _timerRunning = false;
                SetGuidewire(false);
                clotTarget?.Release();
                _stent?.Hide();
                if (_dye != null) _dye.Reperfused = true;
                Feedback.Success();
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
            EnterPhase(SimPhase.Phase3_Results);
    }

    // Called by CatheterSimulator on each speed warning
    public void AddExtractionPenalty(int points)
    {
        _extractionPenalty += points;
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
        resultsPanel?.ShowResults(_totalTime, exposure, rating, color, _wrongTurns, _deadEnds, contrast);
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
                + "\"contrastRuns\":" + (fluoroController != null ? fluoroController.ContrastRuns : 0) + ","
                + "\"speedWarnings\":" + _speedWarnings + ","
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
