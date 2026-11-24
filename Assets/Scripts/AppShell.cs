using UnityEngine;
using TMPro;

// The app around the simulation: the home screen at launch, settings, the about pages,
// and the best time per case. Buttons are poke tiles (TapButton) wired to the methods below.
public class AppShell : MonoBehaviour
{
    [Header("Simulation")]
    public SimulationManager simManager;
    public StartCard startCard;
    public ClotTarget clotTarget;
    public MenuToggle menuToggle;
    public VolumeViewPresets viewPresets;
    public GameObject phaseCard;

    [Header("Panels")]
    public GameObject homePanel;
    public GameObject settingsPanel;
    public TextMeshProUGUI homeBest;

    [Header("Settings labels")]
    public TextMeshProUGUI viewLabel;
    public TextMeshProUGUI passthroughLabel;
    public TextMeshProUGUI soundLabel;
    public GameObject restartButton; // hidden when opened from home

    public bool HomeShowing => homePanel != null && homePanel.activeSelf;
    public bool SettingsShowing => settingsPanel != null && settingsPanel.activeSelf;

    private OVRPassthroughLayer _passthrough;
    private Camera _eye;
    private Color _eyeBackground;
    private bool _passthroughOn = true;
    private bool _soundOn = true;
    private bool _settingsFromHome;
    private bool _cardWasShowing;

    private const string Grey = "#9AA1A9";

    void Awake()
    {
        _passthrough = FindFirstObjectByType<OVRPassthroughLayer>(FindObjectsInactive.Include);
        _eye = Camera.main;
        if (_eye != null) _eyeBackground = _eye.backgroundColor;
        _passthroughOn = PlayerPrefs.GetInt("setting_passthrough", 1) == 1;
        _soundOn = PlayerPrefs.GetInt("setting_sound", 1) == 1;
        ApplyPassthrough();
        ApplySound();
        if (settingsPanel != null) settingsPanel.SetActive(false);
    }

    void Update()
    {
        if (SettingsShowing) RefreshSettingsLabels();
    }

    // ---------- Home ----------

    public void ShowHome()
    {
        Time.timeScale = 1f;
        if (settingsPanel != null) settingsPanel.SetActive(false);
        if (startCard != null && startCard.Showing) startCard.Hide();
        if (phaseCard != null) phaseCard.SetActive(false);
        menuToggle?.Set(false);
        if (homePanel != null) homePanel.SetActive(true);
        RefreshBest();
    }

    private void LeaveHome()
    {
        if (homePanel != null) homePanel.SetActive(false);
        menuToggle?.Set(true);
    }

    // Easy case, hints on, the guided tutorial.
    public void StartGuided() => Begin(ClotTarget.CaseLevel.Easy, assessment: false, tutorial: true);

    // Free practice: hints on, the case picked on the menu.
    public void StartPractice() => Begin(clotTarget != null ? clotTarget.caseLevel : ClotTarget.CaseLevel.Easy, false, false);

    // Scored: no hints, from the easy case up.
    public void StartAssessment() => Begin(ClotTarget.CaseLevel.Easy, assessment: true, tutorial: false);

    private void Begin(ClotTarget.CaseLevel level, bool assessment, bool tutorial)
    {
        SimMode.Assessment = assessment;
        if (clotTarget != null && clotTarget.caseLevel != level)
        {
            clotTarget.caseLevel = level;
            clotTarget.ResetClot();
        }
        LeaveHome();
        startCard?.SetTutorial(tutorial);
        startCard?.Show(false); // the case card; A starts the run
    }

    public void ShowAbout()
    {
        LeaveHome();
        menuToggle?.Set(false);
        startCard?.ShowAbout(ShowHome);
    }

    // Best time per case, kept on the headset. Only runs that reopened the vessel count.
    public void RecordRun(ClotTarget.CaseLevel level, float seconds, bool reopened)
    {
        if (!reopened) return;
        string key = "best_" + level;
        float best = PlayerPrefs.GetFloat(key, -1f);
        if (best < 0f || seconds < best)
        {
            PlayerPrefs.SetFloat(key, seconds);
            PlayerPrefs.Save();
        }
    }

    private void RefreshBest()
    {
        if (homeBest == null) return;
        string Line(ClotTarget.CaseLevel l)
        {
            float b = PlayerPrefs.GetFloat("best_" + l, -1f);
            string t = b < 0f ? "–" : $"{Mathf.FloorToInt(b / 60f)}:{Mathf.FloorToInt(b % 60f):00}";
            return $"{l} <b>{t}</b>";
        }
        homeBest.text = $"<color={Grey}>Best times</color>   {Line(ClotTarget.CaseLevel.Easy)}   {Line(ClotTarget.CaseLevel.Medium)}   {Line(ClotTarget.CaseLevel.Hard)}";
    }

    // ---------- Settings ----------

    public void OpenSettings()
    {
        if (settingsPanel == null || SettingsShowing) return;
        _settingsFromHome = HomeShowing;
        _cardWasShowing = startCard != null && startCard.Showing;
        if (_settingsFromHome) homePanel.SetActive(false);
        if (_cardWasShowing) startCard.Hide();
        if (phaseCard != null) phaseCard.SetActive(false);
        if (restartButton != null) restartButton.SetActive(!_settingsFromHome);
        // The run waits while settings are open.
        if (!_settingsFromHome) Time.timeScale = 0f;
        settingsPanel.SetActive(true);
        RefreshSettingsLabels();
    }

    public void CloseSettings()
    {
        if (!SettingsShowing) return;
        settingsPanel.SetActive(false);
        Time.timeScale = 1f;
        if (_settingsFromHome) { ShowHome(); return; }
        if (_cardWasShowing) startCard.Show(false);
        else if (phaseCard != null) phaseCard.SetActive(true);
    }

    public void ToggleView() => viewPresets?.Toggle(true);

    public void TogglePassthrough()
    {
        _passthroughOn = !_passthroughOn;
        PlayerPrefs.SetInt("setting_passthrough", _passthroughOn ? 1 : 0);
        ApplyPassthrough();
    }

    public void ToggleSound()
    {
        _soundOn = !_soundOn;
        PlayerPrefs.SetInt("setting_sound", _soundOn ? 1 : 0);
        ApplySound();
    }

    public void RestartRun()
    {
        settingsPanel.SetActive(false);
        Time.timeScale = 1f;
        simManager?.RestartRun();
    }

    public void MainMenu()
    {
        settingsPanel.SetActive(false);
        Time.timeScale = 1f;
        simManager?.ReturnHome();
    }

    // Without passthrough the room goes dark gray, so the model still stands out.
    private void ApplyPassthrough()
    {
        if (_passthrough != null) _passthrough.enabled = _passthroughOn;
        if (_eye != null)
            _eye.backgroundColor = _passthroughOn ? _eyeBackground : new Color(0.05f, 0.055f, 0.06f, 1f);
    }

    private void ApplySound() => AudioListener.volume = _soundOn ? 1f : 0f;

    private void RefreshSettingsLabels()
    {
        Set(viewLabel, "View", viewPresets != null && viewPresets.Current == VolumeViewPresets.Pose.Supine ? "Supine" : "Upright");
        Set(passthroughLabel, "Passthrough", _passthroughOn ? "On" : "Off");
        Set(soundLabel, "Sound", _soundOn ? "On" : "Off");
    }

    private static void Set(TextMeshProUGUI t, string name, string value)
    {
        if (t == null) return;
        string s = $"{name}   <color=#2BC4B4><b>{value}</b></color>";
        if (t.text != s) t.text = s;
    }
}
