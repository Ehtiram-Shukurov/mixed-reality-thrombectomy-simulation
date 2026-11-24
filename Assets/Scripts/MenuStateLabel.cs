using UnityEngine;
using UnityEngine.UI;
using TMPro;

// The session buttons on the menu (Mode, Case, View). They get an amber tint so they read
// apart from the render mode buttons, and their label always says what is set right now.
// Case and View act on each press, so their toggle never stays on. Mode stays lit while
// Assessment is on.
public class MenuStateLabel : MonoBehaviour
{
    public enum Kind { Case, View, Mode }
    public Kind kind;

    public Color tint = new Color(0.36f, 0.25f, 0.05f, 1f);
    public Color activeTint = new Color(0.85f, 0.52f, 0.08f, 1f);

    private TextMeshProUGUI _label;
    private Toggle _toggle;
    private Image _background;
    private ClotTarget _clot;
    private VolumeViewPresets _view;

    void Start()
    {
        _label = GetComponentInChildren<TextMeshProUGUI>(true);
        _toggle = GetComponent<Toggle>();
        var bg = transform.Find("Content/Background");
        if (bg != null) _background = bg.GetComponent<Image>();
        _clot = FindFirstObjectByType<ClotTarget>();
        _view = FindFirstObjectByType<VolumeViewPresets>();
        if (_label != null) _label.richText = true;
    }

    private static string Text(string title, string value, string note)
        => $"<size=60%><color=#FFD27A>{title}</color></size>\n<b>{value}</b>\n<size=60%>{note}</size>";

    void LateUpdate()
    {
        bool active = false;
        string text = null;
        switch (kind)
        {
            case Kind.Mode:
                active = SimMode.Assessment;
                text = active ? Text("MODE", "Assessment", "no hints, scored") : Text("MODE", "Training", "hints on");
                if (_toggle != null && _toggle.isOn != active) _toggle.SetIsOnWithoutNotify(active);
                break;
            case Kind.Case:
                if (_clot != null)
                {
                    string twist = _clot.caseLevel switch
                    {
                        ClotTarget.CaseLevel.Easy => "wide twist",
                        ClotTarget.CaseLevel.Hard => "tight twist",
                        _ => "normal twist"
                    };
                    text = Text("CASE", _clot.caseLevel.ToString(), $"{_clot.ForksOnPath} forks, {twist}");
                }
                break;
            case Kind.View:
                if (_view != null)
                    text = _view.Current == VolumeViewPresets.Pose.Upright
                        ? Text("VIEW", "Upright", "standing")
                        : Text("VIEW", "Supine", "lying on table");
                break;
        }

        if (_label != null && text != null && _label.text != text) _label.text = text;
        if (_background != null) _background.color = active ? activeTint : tint;
        if (kind != Kind.Mode && _toggle != null && _toggle.isOn) _toggle.SetIsOnWithoutNotify(false);
    }
}
