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
    public GameObject phaseCard; // hidden while the results show

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

        const string label = "<size=60%><color=#8B96A5>{0}</color></size>\n";
        if (totalTimeText != null) totalTimeText.text = string.Format(label, "Total time") + $"{min:00}:{sec:00}";
        if (radiationText != null) radiationText.text = string.Format(label, "Fluoroscopy") + $"{fluoroSeconds:F1} s";
        if (safetyScoreText != null)
        {
            safetyScoreText.text = $"<color=#8B96A5>Extraction</color>   {rating}";
            safetyScoreText.color = ratingColor;
        }

        // The phase card sits in the same spot, so hide it while results show.
        if (phaseCard != null) phaseCard.SetActive(false);

        // Toggle stays on after a click. Reset it so Play Again works every time.
        var toggle = GetComponentInChildren<Toggle>(true);
        if (toggle != null) toggle.SetIsOnWithoutNotify(false);

        gameObject.SetActive(true);
    }

    void OnDisable()
    {
        if (phaseCard != null) phaseCard.SetActive(true);
    }

    private void OnPlayAgain()
    {
        gameObject.SetActive(false);
        clotTarget?.ResetClot();
        guidewireSimulator?.ResetGuidewire();
        simManager?.StartPhase1();
    }
}
