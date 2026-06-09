using UnityEngine;
using TMPro;

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
    //public ResultsPanel resultsPanel;

    public SimPhase CurrentPhase { get; private set; } = SimPhase.Idle;

    private float _totalTime = 0f;
    private bool _timerRunning = false;
    private bool _stentDeployed = false;
    private int _extractionPenalty = 0;

    void Start()
    {
        //if (resultsPanel != null) resultsPanel.gameObject.SetActive(false);
        EnterPhase(SimPhase.Phase1_Navigation);
    }

    void Update()
    {
        if (_timerRunning) _totalTime += Time.deltaTime;

        switch (CurrentPhase)
        {
            case SimPhase.Phase2b_StentDeploy: CheckStentButton(); break;
        }
    }
    public void StartPhase1() => EnterPhase(SimPhase.Phase1_Navigation);

    private void EnterPhase(SimPhase phase)
    {
        CurrentPhase = phase;
        switch (phase)
        {
            case SimPhase.Phase1_Navigation:
                _totalTime = 0f;
                _timerRunning = true;
                _stentDeployed = false;
                _extractionPenalty = 0;
                fluoroController?.ResetTimer();
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
                catheterSimulator?.BeginRetraction();
                Debug.Log("[SimManager] Phase 2c — Extract clot.");
                break;

            case SimPhase.Phase3_Results:
                _timerRunning = false;
                SetGuidewire(false);
                HapticManager.Instance?.PulseOnce(1f, 0.6f);
                ShowResults();
                Debug.Log("[SimManager] Phase 3 — Results.");
                break;
        }
    }

    private void CheckStentButton()
    {
        if (_stentDeployed) return;
        if (OVRInput.GetDown(OVRInput.Button.Two, OVRInput.Controller.RTouch))
        {
            _stentDeployed = true;
            HapticManager.Instance?.DoublePulse(0.9f);
            Debug.Log("[SimManager] Stent deployed.");
            EnterPhase(SimPhase.Phase2c_Extraction);
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
    
    public void AddExtractionPenalty(int points) => _extractionPenalty += points;

    private void ShowResults()
    {
        float exposure = fluoroController != null ? fluoroController.TotalExposureTime : 0f;
        float safety = Mathf.Clamp(100f - _extractionPenalty, 0f, 100f);
        //resultsPanel?.ShowResults(_totalTime, exposure, safety);
    }

    private void SetGuidewire(bool active)
    {
        if (guidewireSimulator != null) guidewireSimulator.enabled = active;
    }
}