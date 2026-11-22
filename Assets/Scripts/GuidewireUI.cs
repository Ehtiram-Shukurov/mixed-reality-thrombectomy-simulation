using UnityEngine;
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

        if (fluoroController.IsActive)
        {
            fluoroText.gameObject.SetActive(true);
            fluoroText.color = Color.yellow;
            fluoroText.text = $"☢ FLUORO {fluoroController.TotalExposureTime:F1}s";
        }
        else
        {
            fluoroText.gameObject.SetActive(false);
        }
    }

    private void UpdateFork()
    {
        if (forkText == null || guidewireSimulator == null) return;

        if (!guidewireSimulator.IsReady || !guidewireSimulator.IsAtFork)
        {
            forkText.gameObject.SetActive(false);
            return;
        }

        forkText.gameObject.SetActive(true);

        int dir = guidewireSimulator.TwistDirection;
        if (dir == 0)
        {
            forkText.color = Color.green;
            forkText.text = "Aligned. Push forward.";
        }
        else
        {
            forkText.color = Color.yellow;
            forkText.text = dir < 0 ? "Fork reached. Twist left <<" : "Fork reached. Twist right >>";
        }
    }
}