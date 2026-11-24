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
    public TextMeshProUGUI routeText;       // wrong turns and dead ends

    [Header("References")]
    public SimulationManager simManager;
    public GuidewireSimulator guidewireSimulator; // kept so the scene wiring stays valid
    public ClotTarget clotTarget;                 // kept so the scene wiring stays valid
    public GameObject phaseCard; // hidden while the results show

    private float _shownAt;

    void OnEnable()
    {
        _shownAt = Time.unscaledTime;
    }

    void Update()
    {
        // Small pop and fade in.
        float t = Mathf.Clamp01((Time.unscaledTime - _shownAt) / 0.25f);
        float s = Mathf.SmoothStep(0f, 1f, t);
        transform.localScale = Vector3.one * Mathf.Lerp(0.9f, 1f, s);
        if (_group == null) _group = GetComponent<CanvasGroup>();
        if (_group == null) _group = gameObject.AddComponent<CanvasGroup>();
        _group.alpha = s;
    }
    private CanvasGroup _group;

    // Hooked to the Play Again toggle (On Value Changed, dynamic bool)
    public void OnPlayAgainToggle(bool isOn)
    {
        if (!isOn) return;
        OnPlayAgain();
    }

    public void ShowResults(float totalTime, float fluoroSeconds, string rating, Color ratingColor,
                            int wrongTurns, int deadEnds, int contrastRuns = 0, int forcedBends = 0)
    {
        int min = Mathf.FloorToInt(totalTime / 60f);
        int sec = Mathf.FloorToInt(totalTime % 60f);

        const string label = "<size=60%><color=#9AA1A9>{0}</color></size>\n";
        if (totalTimeText != null) totalTimeText.text = string.Format(label, "Total time") + $"{min:00}:{sec:00}";
        if (radiationText != null) radiationText.text = string.Format(label, "Fluoroscopy") + $"{fluoroSeconds:F1} s";
        if (safetyScoreText != null)
        {
            safetyScoreText.text = $"<color=#9AA1A9>Extraction</color>   {rating}";
            safetyScoreText.color = ratingColor;
        }
        if (routeText != null)
        {
            routeText.text = $"<size=80%><color=#9AA1A9>Wrong turns</color> {wrongTurns}  <color=#9AA1A9>Dead ends</color> {deadEnds}  <color=#9AA1A9>Forced</color> {forcedBends}  <color=#9AA1A9>Dye</color> {contrastRuns}</size>";
        }

        // The phase card sits in the same spot, so hide it while results show.
        if (phaseCard != null) phaseCard.SetActive(false);

        // Toggle stays on after a click. Reset it so Play Again works every time.
        var toggle = GetComponentInChildren<Toggle>(true);
        if (toggle != null) toggle.SetIsOnWithoutNotify(false);

        gameObject.SetActive(true);
    }

    // Outcome line in the panel's title: reopened or still blocked, and how many passes.
    public void ShowOutcome(string text, bool good)
    {
        var title = transform.Find("Title");
        var tmp = title != null ? title.GetComponent<TextMeshProUGUI>() : null;
        if (tmp == null) return;
        tmp.text = text;
        tmp.color = good ? new Color(0.30f, 0.85f, 0.50f) : new Color(0.97f, 0.50f, 0.42f);
    }

    void OnDisable()
    {
        transform.localScale = Vector3.one;
        if (phaseCard != null) phaseCard.SetActive(true);
    }

    private void OnPlayAgain()
    {
        gameObject.SetActive(false);
        simManager?.RestartRun();
    }
}
