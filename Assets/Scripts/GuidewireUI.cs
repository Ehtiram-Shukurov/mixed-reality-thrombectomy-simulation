using UnityEngine;
using UnityEngine.UI;
using TMPro;

public class GuidewireUI : MonoBehaviour
{
    [Header("References")]
    public GuidewireSimulator guidewireSimulator;
    public FluoroController fluoroController;
    public SimulationManager simManager;

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

        instructionText.text = simManager.CurrentPhase switch
        {
            SimulationManager.SimPhase.Phase1_Navigation => "Advance guidewire through vessel. Twist at forks to choose branch. Hold A for X-ray.",
            SimulationManager.SimPhase.Phase2a_CatheterAdvance => "Catheter following guidewire path. Push forward to advance.",
            SimulationManager.SimPhase.Phase2b_StentDeploy => "Clot reached. Press B to deploy stent.",
            SimulationManager.SimPhase.Phase2c_Extraction => "Pull back to extract. Slow and steady.",
            SimulationManager.SimPhase.Phase3_Results => "Procedure complete.",
            _ => ""
        };
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
        fluoroText.text = $"X-RAY ON  {fluoroController.TotalExposureTime:F1} s";
    }

    private void UpdateFork()
    {
        if (forkText == null || guidewireSimulator == null) return;

        bool atFork = guidewireSimulator.IsReady && guidewireSimulator.IsAtFork;
        bool deadEnd = guidewireSimulator.IsReady && guidewireSimulator.IsAtDeadEnd;
        bool show = atFork || deadEnd;
        forkText.gameObject.SetActive(show);
        if (forkBadge != null) forkBadge.gameObject.SetActive(show);
        if (!show) return;

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