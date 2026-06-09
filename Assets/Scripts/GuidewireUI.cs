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

    void Update()
    {
        UpdatePhase();
        UpdateFluoro();
        UpdateFork();
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

        float signal = Mathf.Clamp01(1f - (guidewireSimulator.BestBranchAngle / 180f)) * 100f;
        forkText.color = signal >= 80f ? Color.green
                       : signal >= 50f ? Color.yellow
                       : Color.red;
        forkText.text = $"FORK  {signal:F0}%";
    }
}