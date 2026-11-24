using UnityEngine;
using UnityEngine.UI;
using TMPro;

public class GuidewireUI : MonoBehaviour
{
    [Header("References")]
    public GuidewireSimulator guidewireSimulator;
    public FluoroController fluoroController;
    public SimulationManager simManager;
    public ClotTarget clotTarget;

    public TextMeshProUGUI phaseText;
    public TextMeshProUGUI fluoroText;  
    public TextMeshProUGUI forkText;
    public TextMeshProUGUI instructionText; // one line of help for the current phase

    [Header("Badges (optional rounded backgrounds)")]
    public Image fluoroBadge;
    public Image forkBadge;

    private static readonly Color Dark = new Color(0.04f, 0.08f, 0.09f, 1f);
    private static readonly Color Amber = new Color(0.98f, 0.75f, 0.14f, 0.95f);
    private static readonly Color Green = new Color(0.30f, 0.85f, 0.50f, 0.95f);
    private static readonly Color Coral = new Color(0.97f, 0.50f, 0.42f, 0.95f);

    void Update()
    {
        UpdatePhase();
        UpdateInstruction();
        UpdateFluoro();
        UpdateFork();
    }

    private void UpdateInstruction()
    {
        if (instructionText == null || simManager == null) return;

        string phase1 = "Press B to inject dye and find the clot. Push to advance. Twist at forks.";
        if (clotTarget != null && clotTarget.IsTouched)
            phase1 = "Wire is at the clot. Keep pushing to cross it.";
        else if (clotTarget != null && clotTarget.IsRoadmapped)
            phase1 = "Clot is marked in red. Steer the wire to it. Twist at forks.";

        string stent = simManager.StentDeployed
            ? "Stent is gripping the clot. Wait."
            : "Catheter is past the clot. Press B to open the stent across it.";

        instructionText.text = simManager.CurrentPhase switch
        {
            SimulationManager.SimPhase.Phase1_Navigation => phase1,
            SimulationManager.SimPhase.Phase2a_CatheterAdvance => "Catheter follows the wire across the clot. Push forward.",
            SimulationManager.SimPhase.Phase2b_StentDeploy => stent,
            SimulationManager.SimPhase.Phase2c_Extraction => "Pull back slowly. Keep the pace bar green. It buzzes if you go too fast.",
            SimulationManager.SimPhase.Phase2d_ControlAngio => AngioText(),
            SimulationManager.SimPhase.Phase3_Results => "Your route: green on track, amber off track, red mistakes. A: same case. B: next case.",
            _ => ""
        };
    }

    // Control angiogram after a pass: watch the dye, then the result and the choice.
    private string AngioText()
    {
        if (!simManager.AngioDone) return "Control angiogram. Watch where the dye goes.";
        if (simManager.Reopened) return "Dye flows past. The vessel is open.";
        return simManager.CanTryAgain
            ? "Dye still stops at a piece of clot. B: another pass. A: finish."
            : "Dye still stops at a piece of clot. No passes left.";
    }

    private void UpdatePhase()
    {
        if (phaseText == null || simManager == null) return;

        phaseText.text = simManager.CurrentPhase switch
        {
            SimulationManager.SimPhase.Phase1_Navigation => "Guidewire",
            SimulationManager.SimPhase.Phase2a_CatheterAdvance => "Catheter",
            SimulationManager.SimPhase.Phase2b_StentDeploy => "Deploy Stent",
            SimulationManager.SimPhase.Phase2c_Extraction => "Extraction",
            SimulationManager.SimPhase.Phase2d_ControlAngio => "Check",
            SimulationManager.SimPhase.Phase3_Results => "Complete",
            _ => ""
        };
    }

    private void UpdateFluoro()
    {
        if (fluoroText == null || fluoroController == null) return;

        bool on = fluoroController.IsActive;
        fluoroText.gameObject.SetActive(on);
        if (fluoroBadge != null) fluoroBadge.gameObject.SetActive(on);
        if (!on) return;

        fluoroText.color = fluoroBadge != null ? Dark : Color.yellow;
        if (fluoroBadge != null) fluoroBadge.color = Amber;
        string mode = fluoroController.ContrastActive ? "CONTRAST" : "X-RAY ON";
        fluoroText.text = $"{mode}  {fluoroController.TotalExposureTime:F1} s";
    }

    private void UpdateFork()
    {
        if (forkText == null || guidewireSimulator == null) return;

        bool atFork = guidewireSimulator.IsReady && guidewireSimulator.IsAtFork;
        bool deadEnd = guidewireSimulator.IsReady && guidewireSimulator.IsAtDeadEnd;
        bool bend = guidewireSimulator.IsReady && guidewireSimulator.enabled && guidewireSimulator.BendAmount > 0.3f;
        bool show = atFork || deadEnd || bend;
        forkText.gameObject.SetActive(show);
        if (forkBadge != null) forkBadge.gameObject.SetActive(show);
        if (!show) return;

        if (bend && !atFork && !deadEnd)
        {
            forkText.color = forkBadge != null ? Dark : Color.yellow;
            if (forkBadge != null) forkBadge.color = Amber;
            forkText.text = "<size=70%>TIGHT BEND</size>\nEase off";
            return;
        }

        if (deadEnd)
        {
            forkText.color = forkBadge != null ? Dark : Color.red;
            if (forkBadge != null) forkBadge.color = Coral;
            forkText.text = "<size=70%>DEAD END</size>\nPull back";
            return;
        }

        int dir = guidewireSimulator.TwistDirection;
        bool hasBadge = forkBadge != null;
        if (dir == 0)
        {
            forkText.color = hasBadge ? Dark : Color.green;
            if (hasBadge) forkBadge.color = Green;
            forkText.text = "<size=70%>ALIGNED</size>\nPush forward";
        }
        else
        {
            forkText.color = hasBadge ? Dark : Color.yellow;
            if (hasBadge) forkBadge.color = Amber;
            forkText.text = dir < 0 ? "<size=70%>FORK REACHED</size>\n<< Twist left"
                                    : "<size=70%>FORK REACHED</size>\nTwist right >>";
        }
    }
}