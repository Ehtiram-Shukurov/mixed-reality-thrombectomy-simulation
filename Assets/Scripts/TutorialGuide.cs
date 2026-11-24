using UnityEngine;

// Guided first run. Each step waits until you have done it, then moves on.
// Only covers the guidewire part; the phase card already explains the later steps.
public class TutorialGuide : MonoBehaviour
{
    public SimulationManager simManager;
    public GuidewireSimulator guidewire;
    public FluoroController fluoro;
    public ClotTarget clotTarget;

    public bool Running => _step >= 0;

    private enum Step { Dye, Advance, PullBack, ToFork, Twist, XRay, Done }
    private int _step = -1;
    private int _runs, _depth, _maxDepth;
    private float _timer;
    private bool _branchChosen;

    void Start()
    {
        if (guidewire != null) guidewire.BranchChosen += _ => _branchChosen = true;
    }

    void OnDestroy() => GuidewireUI.Guide = null;

    public void Begin()
    {
        _step = (int)Step.Dye;
        _runs = fluoro != null ? fluoro.ContrastRuns : 0;
        _timer = 0f;
        Show();
    }

    public void Stop()
    {
        _step = -1;
        GuidewireUI.Guide = null;
    }

    void Update()
    {
        if (_step < 0) return;
        if (simManager == null || simManager.CurrentPhase != SimulationManager.SimPhase.Phase1_Navigation || !SimMode.HintsOn)
        {
            Stop();
            return;
        }

        int depth = guidewire != null ? guidewire.HistoryDepth : 0;
        _maxDepth = Mathf.Max(_maxDepth, depth);
        _timer += Time.deltaTime;

        bool done = (Step)_step switch
        {
            Step.Dye => fluoro != null && fluoro.ContrastRuns > _runs && !fluoro.ContrastActive,
            Step.Advance => depth >= _depth + 12 || (guidewire != null && guidewire.IsAtFork),
            Step.PullBack => depth <= _maxDepth - 4,
            Step.ToFork => guidewire != null && guidewire.IsAtFork,
            Step.Twist => _branchChosen,
            Step.XRay => SimInput.FluoroHeld && _timer > 0.8f,
            _ => _timer > 5f
        };
        // The A hold only counts while A is down.
        if ((Step)_step == Step.XRay && !SimInput.FluoroHeld) _timer = 0f;

        if (!done) return;
        if ((Step)_step == Step.Done) { Stop(); return; }
        _step++;
        Feedback.PhaseChanged();
        Show();
    }

    private void Show()
    {
        _timer = 0f;
        _depth = guidewire != null ? guidewire.HistoryDepth : 0;
        _maxDepth = _depth;
        _branchChosen = false;

        GuidewireUI.Guide = (Step)_step switch
        {
            Step.Dye => "Tutorial 1/6. Press B to inject dye. Watch the X-ray monitor: the dye stops at the clot.",
            Step.Advance => "Tutorial 2/6. The clot is marked in red. Push the right stick up to move the wire.",
            Step.PullBack => "Tutorial 3/6. Pull the stick down to back up a bit. You can always pull back.",
            Step.ToFork => "Tutorial 4/6. Push forward again until the wire stops at a fork.",
            Step.Twist => "Tutorial 5/6. Turn the stick left or right until the branch toward the clot is green, then push.",
            Step.XRay => "Tutorial 6/6. Hold A to take an X-ray. Let go when you are done.",
            _ => "Tutorial done. Steer to the clot. This card guides you from here."
        };
    }
}
