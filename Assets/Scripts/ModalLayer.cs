using System.Collections.Generic;
using UnityEngine;

// The pop-up layer (case card, settings, about, results). When the first pop-up opens it is
// placed straight in front of you, the room dims, a dark backdrop goes behind the panel and
// the session panels are hidden. When the last one closes, everything comes back.
// The backdrop is one quad drawn only while a pop-up is open.
public class ModalLayer : MonoBehaviour
{
    public static ModalLayer Instance { get; private set; }

    public MenuToggle menuToggle;
    public float distance = 0.6f;
    public float belowEyes = 0.06f;
    [Range(0f, 1f)] public float backdropAlpha = 0.6f;
    [Range(-1f, 0f)] public float passthroughDim = -0.5f;

    public bool IsOpen => _open.Count > 0;
    // Fires when the first pop-up opens (true) and when the last one closes (false).
    public static event System.Action<bool> Changed;

    private readonly HashSet<ModalPanel> _open = new HashSet<ModalPanel>();
    private GameObject _backdrop;
    private OVRPassthroughLayer _passthrough;
    private bool _menuWasVisible;

    // Room brightness when no pop-up is open (the home screen dims the room a little).
    private float _baseBrightness;
    public void SetBaseBrightness(float b)
    {
        _baseBrightness = b;
        Dim(IsOpen);
    }

    void Awake()
    {
        Instance = this;
        _passthrough = FindFirstObjectByType<OVRPassthroughLayer>(FindObjectsInactive.Include);

        // A large dark quad behind the panels. Not part of the canvas, so the canvas stays small.
        // It ignores depth and draws after the scene, so the model and devices behind it dim too;
        // the pop-up canvas draws after it.
        _backdrop = FxUtil.MakeQuad("ModalBackdrop", transform);
        var mat = new Material(Shader.Find("UI/Default")) { color = new Color(0.02f, 0.025f, 0.03f, backdropAlpha) };
        mat.SetInt("unity_GUIZTestMode", (int)UnityEngine.Rendering.CompareFunction.Always);
        var br = _backdrop.GetComponent<Renderer>();
        br.sharedMaterial = mat;
        br.sortingOrder = 100;
        var canvas = GetComponent<Canvas>();
        if (canvas != null) canvas.sortingOrder = 101;
        _backdrop.transform.localScale = Vector3.one / Mathf.Max(1e-6f, transform.lossyScale.x) * 4f;
        _backdrop.transform.localPosition = new Vector3(0f, 0f, 0.12f / Mathf.Max(1e-6f, transform.lossyScale.z));
        _backdrop.SetActive(false);
    }

    void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }

    public void Opened(ModalPanel p)
    {
        bool first = _open.Count == 0;
        _open.Add(p);
        if (!first) return;

        PlaceInFront();
        if (menuToggle != null) { _menuWasVisible = menuToggle.Visible; menuToggle.Set(false); }
        _backdrop.SetActive(true);
        Dim(true);
        Changed?.Invoke(true);
    }

    public void Closed(ModalPanel p)
    {
        if (!_open.Remove(p) || _open.Count > 0) return;
        _backdrop.SetActive(false);
        Dim(false);
        if (menuToggle != null && _menuWasVisible) menuToggle.Set(true);
        Changed?.Invoke(false);
    }

    private void PlaceInFront()
    {
        var head = FxUtil.FindHead();
        if (head == null) return;
        Vector3 fwd = Vector3.ProjectOnPlane(head.forward, Vector3.up);
        if (fwd.sqrMagnitude < 1e-4f) fwd = Vector3.forward;
        fwd.Normalize();
        transform.position = head.position + fwd * distance + Vector3.down * belowEyes;
        transform.rotation = Quaternion.LookRotation(fwd, Vector3.up);
    }

    private void Dim(bool on)
    {
        if (_passthrough != null) _passthrough.SetBrightnessContrastSaturation(on ? passthroughDim : _baseBrightness, 0f, 0f);
    }
}
