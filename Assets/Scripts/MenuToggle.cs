using UnityEngine;

// Shows or hides the menu and the brightness card, to keep the space clear during a run.
// Left palm-up pinch (hand tracking) or the menu button on the left controller.
public class MenuToggle : MonoBehaviour
{
    public GameObject[] targets;

    public bool Visible { get; private set; } = true;

    void Update()
    {
        if (!SimInput.MenuTogglePressed) return;
        if (AppShell.AtHome || (ModalLayer.Instance != null && ModalLayer.Instance.IsOpen)) return;
        Set(!Visible);
    }

    public void Set(bool visible)
    {
        Visible = visible;
        foreach (var t in targets) if (t != null) t.SetActive(visible);
    }
}
