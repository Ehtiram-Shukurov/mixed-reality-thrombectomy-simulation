//using UnityEngine;
//using UnityEngine.UI;
//using TMPro;

//// Handles the results screen display and Play Again reset.
//public class ResultsPanel : MonoBehaviour
//{
//    [Header("Score Fields")]
//    public TextMeshProUGUI totalTimeText;
//    public TextMeshProUGUI radiationText;
//    public TextMeshProUGUI safetyScoreText;

//    [Header("References")]
//    public SimulationManager simManager;
//    public GuidewireSimulator guidewireSimulator;
//    public ClotTarget clotTarget;

//    void Start()
//    {
//        gameObject.SetActive(false);
//    }
//    public void OnPlayAgainToggle(bool isOn)
//    {
//        if (!isOn) return;
//        OnPlayAgain();
//    }

//    // Called by SimulationManager.ShowResults() — colors scores by performance
//    public void ShowResults(float totalTime, float radiation, float safety)
//    {
//        int min = Mathf.FloorToInt(totalTime / 60f);
//        int sec = Mathf.FloorToInt(totalTime % 60f);

//        if (totalTimeText != null) totalTimeText.text = $"Total Time\n{min:00}:{sec:00}";
//        if (radiationText != null) radiationText.text = $"Radiation\n{radiation:F1}s";
//        if (safetyScoreText != null)
//        {
//            safetyScoreText.text = $"Extraction Safety\n{safety:F0}/100";
//            safetyScoreText.color = safety >= 80f ? Color.green
//                                  : safety >= 50f ? Color.yellow
//                                  : Color.red;
//        }

//        gameObject.SetActive(true);
//    }

//    private void OnPlayAgain()
//    {
//        gameObject.SetActive(false);
//        clotTarget?.ResetClot();
//        guidewireSimulator?.ResetGuidewire();
//        simManager?.StartPhase1();
//    }
//}