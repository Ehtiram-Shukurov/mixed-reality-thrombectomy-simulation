using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using TMPro;

// The app around the simulation. Two screens, never shown together:
//  Home: a title, a large menu and the model turning slowly on the right. Nothing else.
//  Session: the training layout (menu, density card, X-ray monitor, phase card).
// Pop-ups (case card, settings, about, results, case picker) open over either one through the
// modal layer. Switching screens fades through black, like loading a new screen.
public class AppShell : MonoBehaviour
{
    [Header("Simulation")]
    public SimulationManager simManager;
    public StartCard startCard;
    public ClotTarget clotTarget;
    public MenuToggle menuToggle;
    public VolumeViewPresets viewPresets;
    public GameObject phaseCard;
    public DicomVolumeLoader loader;
    public XRayMonitor monitor;

    [Header("Screens")]
    public GameObject homePanel;     // the home canvas
    public GameObject settingsPanel; // pop-up
    public GameObject casePicker;    // pop-up
    public TextMeshProUGUI homeBest;

    [Header("Settings labels")]
    public TextMeshProUGUI viewLabel;
    public TextMeshProUGUI passthroughLabel;
    public TextMeshProUGUI soundLabel;
    public GameObject restartButton; // hidden when opened from home

    [Header("Home layout")]
    public float homeDistance = 0.8f;
    public float homeMenuLeft = 0.16f;
    public float modelRight = 0.3f;
    public float modelDistance = 1.0f;
    public float modelBelowEyes = 0.12f;
    public float spinDegreesPerSecond = 12f;
    [Range(-1f, 0f)] public float homeRoomDim = -0.3f;

    [Header("Fade")]
    public float fadeSeconds = 0.3f;
    public float splashSeconds = 1.2f;

    public bool HomeShowing => homePanel != null && homePanel.activeSelf;
    // The session menu toggle (palm pinch) only works in a session with no pop-up open.
    public static bool AtHome { get; private set; }
    public bool SettingsShowing => settingsPanel != null && settingsPanel.activeSelf;

    private OVRPassthroughLayer _passthrough;
    private Camera _eye;
    private Color _eyeBackground;
    private bool _passthroughOn = true;
    private bool _soundOn = true;
    private bool _settingsFromHome;
    private bool _cardWasShowing;

    private bool _home;
    private float _spin;
    private Quaternion _modelBase;
    private int _sessionMode = 2;
    private Renderer _fade;
    private Material _fadeMat;
    private bool _fading;
    private readonly List<Renderer> _hidden = new List<Renderer>();

    private const string Grey = "#9AA1A9";
    private static readonly int RenderModeId = Shader.PropertyToID("_RenderMode");

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
        if (casePicker != null) casePicker.SetActive(false);
        BuildFade();
        ModalLayer.Changed += OnModalChanged;
    }

    void OnDestroy() => ModalLayer.Changed -= OnModalChanged;

    // In a session, a pop-up hides the devices and hint glows (some draw over everything).
    private void OnModalChanged(bool open)
    {
        if (!_home) SetSessionVisuals(!open);
    }

    void Update()
    {
        if (SettingsShowing) RefreshSettingsLabels();

        if (!_home) return;
        FollowHead();

        // The showcase model turns slowly while the home screen is up.
        if (viewPresets != null && viewPresets.volume != null)
        {
            _spin += spinDegreesPerSecond * Time.unscaledDeltaTime;
            viewPresets.volume.rotation = Quaternion.AngleAxis(_spin, Vector3.up) * _modelBase;
        }
    }

    // ---------- Home placement ----------
    // The home screen stays put while you look around and glides back in front of you when
    // you turn more than 30 degrees, walk away or stand up / sit down. This also fixes a
    // placement made before head tracking settled.
    private Vector3 _anchorEye, _anchorFwd = Vector3.forward;
    private bool _following;

    private static bool HeadPose(out Vector3 eye, out Vector3 fwd)
    {
        eye = new Vector3(0f, 1.5f, 0f); fwd = Vector3.forward;
        var head = FxUtil.FindHead();
        if (head == null) return false;
        eye = head.position;
        fwd = Vector3.ProjectOnPlane(head.forward, Vector3.up);
        if (fwd.sqrMagnitude < 1e-4f) fwd = Vector3.forward;
        fwd.Normalize();
        return true;
    }

    private void SnapHomeToHead()
    {
        HeadPose(out _anchorEye, out _anchorFwd);
        _following = false;
        PlaceHome();
    }

    private void FollowHead()
    {
        if (!HeadPose(out var eye, out var fwd)) return;
        float yaw = Mathf.Abs(Vector3.SignedAngle(_anchorFwd, fwd, Vector3.up));
        float walk = Vector3.ProjectOnPlane(eye - _anchorEye, Vector3.up).magnitude;
        float rise = Mathf.Abs(eye.y - _anchorEye.y);
        if (!_following && (yaw > 30f || walk > 0.35f || rise > 0.2f)) _following = true;
        if (!_following) return;

        float k = 1f - Mathf.Exp(-4f * Time.unscaledDeltaTime);
        _anchorEye = Vector3.Lerp(_anchorEye, eye, k);
        _anchorFwd = Vector3.Slerp(_anchorFwd, fwd, k).normalized;
        if (yaw < 2f && walk < 0.02f && rise < 0.02f) _following = false;
        PlaceHome();
    }

    // Menu on the left, model on the right, both relative to the anchor.
    private void PlaceHome()
    {
        Vector3 right = Vector3.Cross(Vector3.up, _anchorFwd);
        if (homePanel != null)
        {
            homePanel.transform.position = _anchorEye + _anchorFwd * homeDistance - right * homeMenuLeft;
            homePanel.transform.rotation = Quaternion.LookRotation(homePanel.transform.position - _anchorEye, Vector3.up);
        }
        if (viewPresets != null && viewPresets.volume != null)
        {
            Vector3 pos = _anchorEye + _anchorFwd * modelDistance + right * modelRight + Vector3.down * modelBelowEyes;
            if (viewPresets.UprightRotation(_anchorEye - pos, out _modelBase))
                viewPresets.volume.SetPositionAndRotation(pos, Quaternion.AngleAxis(_spin, Vector3.up) * _modelBase);
        }
    }

    // ---------- Fade ----------

    // A black quad just in front of the eyes, drawn only while fading.
    private void BuildFade()
    {
        if (_eye == null) return;
        var q = FxUtil.MakeQuad("ScreenFade", _eye.transform);
        q.transform.localPosition = new Vector3(0f, 0f, 0.15f);
        q.transform.localRotation = Quaternion.identity;
        q.transform.localScale = Vector3.one;
        _fadeMat = new Material(Shader.Find("Sprites/Default")) { color = new Color(0f, 0f, 0f, 0f), renderQueue = 4500 };
        _fade = q.GetComponent<Renderer>();
        _fade.sharedMaterial = _fadeMat;
        _fade.enabled = false;
    }

    private void SetFade(float a)
    {
        if (_fade == null) return;
        _fadeMat.color = new Color(0f, 0f, 0f, a);
        _fade.enabled = a > 0.001f;
        if (_passthrough != null && _passthroughOn) _passthrough.textureOpacity = 1f - a;
    }

    private IEnumerator FadeThrough(Action swap, float outSeconds, float inSeconds)
    {
        _fading = true;
        for (float t = 0f; t < outSeconds; t += Time.unscaledDeltaTime) { SetFade(t / outSeconds); yield return null; }
        SetFade(1f);
        swap?.Invoke();
        yield return null;
        for (float t = 0f; t < inSeconds; t += Time.unscaledDeltaTime) { SetFade(1f - t / inSeconds); yield return null; }
        SetFade(0f);
        _fading = false;
    }

    // ---------- Home ----------

    // First launch: from black straight onto the home screen (a short splash).
    public void Launch() => StartCoroutine(LaunchRoutine());

    private IEnumerator LaunchRoutine()
    {
        SetFade(1f);
        _fading = true;
        // Wait for head tracking so the home screen is placed in front of you, not at the floor.
        var head = FxUtil.FindHead();
        for (float t = 0f; t < 2f && (head == null || head.position.y < 0.5f); t += Time.unscaledDeltaTime)
        {
            yield return null;
            if (head == null) head = FxUtil.FindHead();
        }
        EnterHome();
        yield return FadeThrough(null, 0.01f, splashSeconds);
    }

    public void ShowHome()
    {
        if (_fading) return;
        StartCoroutine(FadeThrough(EnterHome, fadeSeconds, fadeSeconds));
    }

    private void EnterHome()
    {
        Debug.Log("[AppShell] home screen");
        Time.timeScale = 1f;
        if (settingsPanel != null) settingsPanel.SetActive(false);
        if (casePicker != null) casePicker.SetActive(false);
        if (startCard != null && startCard.Showing) startCard.Hide(false);
        menuToggle?.Set(false);
        if (phaseCard != null) phaseCard.SetActive(false);
        SetSessionVisuals(false);

        _spin = 0f;
        if (homePanel != null) homePanel.SetActive(true);
        SnapHomeToHead();

        // The showcase uses the smooth vessel mesh, the cheapest view.
        if (loader != null && loader.volumeMaterial != null)
        {
            int m = loader.volumeMaterial.GetInt(RenderModeId);
            if (!_home) _sessionMode = (m == 6 || m == 7) ? 2 : m;
            loader.UpdateSegmentationMode(2);
        }

        _home = true;
        AtHome = true;
        ModalLayer.Instance?.SetBaseBrightness(homeRoomDim);
        RefreshBest();
    }

    private void EnterSession(ClotTarget.CaseLevel level, bool assessment, bool tutorial)
    {
        Debug.Log($"[AppShell] session: {level}, assessment {assessment}, tutorial {tutorial}");
        _home = false;
        AtHome = false;
        if (homePanel != null) homePanel.SetActive(false);
        if (casePicker != null) casePicker.SetActive(false);
        ModalLayer.Instance?.SetBaseBrightness(0f);
        SetSessionVisuals(true);
        if (loader != null) loader.UpdateSegmentationMode(_sessionMode);
        if (viewPresets != null) viewPresets.Set(VolumeViewPresets.Pose.Upright, false);

        SimMode.Assessment = assessment;
        if (clotTarget != null) clotTarget.caseLevel = level;
        simManager?.PrepareRun(); // resets the wire, catheter and clot for this case
        menuToggle?.Set(true);
        // Measure the left panels while they are showing, as they were before Home hid them.
        if (phaseCard != null) phaseCard.SetActive(true);
        FindFirstObjectByType<UIGroupFollower>()?.Remeasure();
        startCard?.SetTutorial(tutorial);
        startCard?.Show(false); // the case card pop-up; A starts the run
    }

    // Devices, hint graphics and the X-ray monitor belong to the session.
    private void SetSessionVisuals(bool on)
    {
        if (!on)
        {
            _hidden.Clear();
            var keep = new HashSet<Renderer>();
            if (loader != null) foreach (var r in loader.GetComponents<Renderer>()) keep.Add(r);
            var mesh = FindFirstObjectByType<VesselMeshView>();
            if (mesh != null && mesh.MeshRenderer != null) keep.Add(mesh.MeshRenderer);
            if (viewPresets != null && viewPresets.volume != null)
                foreach (var r in viewPresets.volume.GetComponentsInChildren<Renderer>(true))
                    if (!keep.Contains(r) && r.GetComponentInParent<VolumeBase>() == null) _hidden.Add(r);
            if (monitor != null) _hidden.AddRange(monitor.GetComponentsInChildren<Renderer>(true));
            foreach (var r in _hidden) r.forceRenderingOff = true;
        }
        else
        {
            foreach (var r in _hidden) if (r != null) r.forceRenderingOff = false;
            _hidden.Clear();
        }
    }

    // Guided: easy case with the tutorial. Assessment: no hints, from the easy case.
    public void StartGuided() => Go(ClotTarget.CaseLevel.Easy, false, true);
    public void StartAssessment() => Go(ClotTarget.CaseLevel.Easy, true, false);

    // Practice: pick a case first.
    public void StartPractice() { if (casePicker != null) casePicker.SetActive(true); }
    public void PracticeEasy() => Go(ClotTarget.CaseLevel.Easy, false, false);
    public void PracticeMedium() => Go(ClotTarget.CaseLevel.Medium, false, false);
    public void PracticeHard() => Go(ClotTarget.CaseLevel.Hard, false, false);
    public void ClosePicker() { if (casePicker != null) casePicker.SetActive(false); }

    private void Go(ClotTarget.CaseLevel level, bool assessment, bool tutorial)
    {
        if (_fading) { Debug.Log("[AppShell] Start ignored: a fade is still running."); return; }
        if (casePicker != null) casePicker.SetActive(false);
        StartCoroutine(FadeThrough(() => EnterSession(level, assessment, tutorial), fadeSeconds, fadeSeconds));
    }

    public void ShowAbout() => startCard?.ShowAbout(() => { });

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

    // ---------- Settings (a pop-up) ----------

    public void OpenSettings()
    {
        if (settingsPanel == null || SettingsShowing) return;
        _settingsFromHome = _home;
        _cardWasShowing = startCard != null && startCard.Showing;
        if (_cardWasShowing) startCard.Hide(false);
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
        if (_settingsFromHome) return;
        if (_cardWasShowing) startCard.Show(false);
        else if (phaseCard != null) phaseCard.SetActive(true);
    }

    public void ToggleView() { if (!_home) viewPresets?.Toggle(true); }

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
        Set(viewLabel, "View", _home ? "Upright" : (viewPresets != null && viewPresets.Current == VolumeViewPresets.Pose.Supine ? "Supine" : "Upright"));
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
