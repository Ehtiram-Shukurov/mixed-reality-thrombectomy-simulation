using UnityEngine;
using UnityEngine.UI;
using TMPro;

// Keeps the label of a menu button that cycles a setting up to date (Case, View),
// and keeps the toggle un-highlighted, since these buttons act on each press.
public class MenuStateLabel : MonoBehaviour
{
    public enum Kind { Case, View }
    public Kind kind;

    private TextMeshProUGUI _label;
    private Toggle _toggle;
    private ClotTarget _clot;
    private VolumeViewPresets _view;

    void Start()
    {
        _label = GetComponentInChildren<TextMeshProUGUI>(true);
        _toggle = GetComponent<Toggle>();
        _clot = FindFirstObjectByType<ClotTarget>();
        _view = FindFirstObjectByType<VolumeViewPresets>();
    }

    void LateUpdate()
    {
        if (_label != null)
        {
            if (kind == Kind.Case && _clot != null) _label.text = "Case: " + _clot.caseLevel;
            else if (kind == Kind.View && _view != null) _label.text = "View: " + _view.Current;
        }
        if (_toggle != null && _toggle.isOn) _toggle.SetIsOnWithoutNotify(false);
    }
}
