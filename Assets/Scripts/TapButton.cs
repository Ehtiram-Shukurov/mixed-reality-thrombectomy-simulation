using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

// Turns a poke toggle (the only button type that works with hand poke here) into a plain
// button: each press fires onTap once and the toggle goes back to off.
[RequireComponent(typeof(Toggle))]
public class TapButton : MonoBehaviour
{
    public UnityEvent onTap = new UnityEvent();

    private Toggle _toggle;

    void Awake()
    {
        _toggle = GetComponent<Toggle>();
        _toggle.onValueChanged.AddListener(OnChanged);
    }

    void OnDestroy()
    {
        if (_toggle != null) _toggle.onValueChanged.RemoveListener(OnChanged);
    }

    private void OnChanged(bool on)
    {
        if (!on) return;
        _toggle.SetIsOnWithoutNotify(false);
        Debug.Log("[Tap] " + name);
        onTap.Invoke();
    }
}
