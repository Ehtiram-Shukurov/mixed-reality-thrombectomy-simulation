using UnityEngine;
using UnityEngine.UI;
using TMPro;

// Four-segment progress bar and "Step n of 4" label for the phase card.
// The card gives a small pop when the phase changes. A bar shows the restart hold.
public class PhaseStepper : MonoBehaviour
{
    public SimulationManager simManager;
    public Image[] segments;          // guidewire, catheter, stent, extraction
    public TextMeshProUGUI stepLabel;

    [Header("Restart hold (optional)")]
    public GameObject restartRoot;
    public Image restartFill;

    public Color doneColor = new Color(0.18f, 0.62f, 0.56f, 1f);
    public Color currentColor = new Color(0.45f, 0.95f, 0.88f, 1f);
    public Color todoColor = new Color(0.17f, 0.20f, 0.25f, 1f);
    public float fadeSpeed = 8f;

    private int _lastStep = -1;
    private float _punch;

    void Update()
    {
        if (simManager == null || segments == null) return;

        int step = StepIndex(simManager.CurrentPhase);
        if (_lastStep >= 0 && step != _lastStep) _punch = 1f;
        _lastStep = step;

        for (int i = 0; i < segments.Length; i++)
        {
            if (segments[i] == null) continue;
            Color target = i < step ? doneColor : (i == step ? currentColor : todoColor);
            segments[i].color = Color.Lerp(segments[i].color, target, Time.unscaledDeltaTime * fadeSpeed);
        }

        if (stepLabel != null)
            stepLabel.text = step < segments.Length ? $"Step {step + 1} of {segments.Length}" : "Complete";

        // Pop on phase change.
        _punch = Mathf.MoveTowards(_punch, 0f, Time.unscaledDeltaTime * 4f);
        transform.localScale = Vector3.one * (1f + 0.05f * Mathf.Sin(_punch * Mathf.PI));

        float held = simManager.RestartProgress;
        if (restartRoot != null) restartRoot.SetActive(held > 0.02f);
        if (restartFill != null) restartFill.fillAmount = held;
    }

    void OnDisable()
    {
        transform.localScale = Vector3.one;
    }

    private static int StepIndex(SimulationManager.SimPhase phase)
    {
        switch (phase)
        {
            case SimulationManager.SimPhase.Phase2a_CatheterAdvance: return 1;
            case SimulationManager.SimPhase.Phase2b_StentDeploy: return 2;
            case SimulationManager.SimPhase.Phase2c_Extraction: return 3;
            case SimulationManager.SimPhase.Phase3_Results: return 4;
            default: return 0;
        }
    }
}
