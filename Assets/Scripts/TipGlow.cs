using UnityEngine;

// A soft pulsing glow at the wire tip. The color shows the state:
// cyan while moving, amber at a fork, coral at a dead end, green once the clot is reached.
// It is one small quad, so it costs almost nothing.
public class TipGlow : MonoBehaviour
{
    public float size = 0.035f;
    public float pulseHz = 1.2f;

    public Color normalColor = new Color(0.30f, 0.90f, 1.00f, 0.9f);
    public Color forkColor = new Color(0.98f, 0.75f, 0.14f, 0.9f);
    public Color deadEndColor = new Color(0.97f, 0.50f, 0.42f, 0.9f);
    public Color goodColor = new Color(0.30f, 0.85f, 0.50f, 0.9f);

    private Transform _quad;
    private Renderer _renderer;
    private Material _mat;
    private Transform _head;
    private GuidewireSimulator _wire;
    private SimulationManager _sim;

    void Awake() { Build(); }

    public void Build()
    {
        if (_quad != null) return;
        var go = FxUtil.MakeQuad("Glow", transform);
        _quad = go.transform;
        _renderer = go.GetComponent<Renderer>();
        _mat = FxUtil.Overlay(FxUtil.GlowTexture());
        _renderer.sharedMaterial = _mat;
        _quad.localScale = Vector3.one * size;
    }

    void Start()
    {
        _head = FxUtil.FindHead();
        _wire = FindFirstObjectByType<GuidewireSimulator>();
        _sim = FindFirstObjectByType<SimulationManager>();
    }

    // Which color to show, or null to hide.
    public Color? PickColor(bool atFork, bool atDeadEnd, SimulationManager.SimPhase phase)
    {
        switch (phase)
        {
            case SimulationManager.SimPhase.Phase3_Results:
            case SimulationManager.SimPhase.Idle:
                return null;
            case SimulationManager.SimPhase.Phase2b_StentDeploy:
            case SimulationManager.SimPhase.Phase2c_Extraction:
            case SimulationManager.SimPhase.Phase2d_ControlAngio:
                return goodColor;
            default:
                return atFork ? forkColor : atDeadEnd ? deadEndColor : normalColor;
        }
    }

    void LateUpdate()
    {
        if (_quad == null) return;

        bool atFork = _wire != null && _wire.IsReady && _wire.IsAtFork;
        bool atDead = _wire != null && _wire.IsReady && _wire.IsAtDeadEnd;
        var phase = _sim != null ? _sim.CurrentPhase : SimulationManager.SimPhase.Phase1_Navigation;

        var color = PickColor(atFork, atDead, phase);
        _renderer.enabled = color.HasValue;
        if (!color.HasValue) return;

        float pulse = 0.85f + 0.15f * Mathf.Sin(Time.time * pulseHz * 2f * Mathf.PI);
        _quad.localScale = Vector3.one * size * pulse;
        _mat.color = color.Value;
        FxUtil.Billboard(_quad, _head);
    }
}
