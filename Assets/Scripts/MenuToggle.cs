using System.Collections.Generic;
using UnityEngine;

// Shows or hides the menu and the density card, to keep the space clear during a run.
// Left palm-up pinch (hand tracking) or the menu button on the left controller.
// The poke surfaces of the hidden canvases are switched off too, so nothing invisible
// is left in front of the screens behind them.
public class MenuToggle : MonoBehaviour
{
    public GameObject[] targets;

    public bool Visible { get; private set; } = true;

    private readonly List<GameObject> _interactions = new List<GameObject>();

    void Awake()
    {
        // A target that sits inside a canvas shares that canvas's poke surfaces.
        foreach (var t in targets)
        {
            if (t == null) continue;
            var parent = t.transform.parent;
            if (parent == null || parent.GetComponent<Canvas>() == null) continue;
            foreach (Transform c in parent)
                if (c.name.StartsWith("ISDK_") && !_interactions.Contains(c.gameObject)) _interactions.Add(c.gameObject);
        }
    }

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
        foreach (var g in _interactions) if (g != null) g.SetActive(visible);
    }
}
