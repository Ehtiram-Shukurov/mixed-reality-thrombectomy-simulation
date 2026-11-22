using UnityEngine;
using UnityEngine.UI;
using TMPro;

// Results screen and the Play Again reset.
// SimulationManager hides this panel at start and shows it in Phase 3.
public class ResultsPanel : MonoBehaviour
{
    [Header("Score Fields")]
    public TextMeshProUGUI totalTimeText;
    public TextMeshProUGUI radiationText;   // shows fluoroscopy time
    public TextMeshProUGUI safetyScoreText; // shows the extraction rating

    [Header("References")]
    public SimulationManager simManager;
    public GuidewireSimulator guidewireSimulator;
    public ClotTarget clotTarget;

    // Hooked to the Play Again toggle (On Value Changed, dynamic bool)
    public void OnPlayAgainToggle(bool isOn)
    {
        if (!isOn) return;
        OnPlayAgain();
    }

    public void ShowResults(float totalTime, float fluoroSeconds, string rating, Color ratingColor)
    {
        int min = Mathf.FloorToInt(totalTime / 60f);
        int sec = Mathf.FloorToInt(totalTime % 60f);

        if (totalTimeText != null) totalTimeText.text = $"Total time: {min:00}:{sec:00}";
        if (radiationText != null) radiationText.text = $"Fluoroscopy: {fluoroSeconds:F1} s";
        if (safetyScoreText != null)
        {
            safetyScoreText.text = $"Extraction: {rating}";
            safetyScoreText.color = ratingColor;
        }

        // Toggle stays on after a click. Reset it so Play Again works every time.
        var toggle = GetComponentInChildren<Toggle>(true);
        if (toggle != null) toggle.SetIsOnWithoutNotify(false);

        gameObject.SetActive(true);
    }

    private void OnPlayAgain()
    {
        gameObject.SetActive(false);
        clotTarget?.ResetClot();
        guidewireSimulator?.ResetGuidewire();
        simManager?.StartPhase1();
    }
}
