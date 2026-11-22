using UnityEngine;

// A soft ring on the next fork ahead of the wire. It does not say which way to go,
// it only shows where the next choice is. It fades out as the wire gets close.
public class ForkMarker : MonoBehaviour
{
    public Sprite ringSprite;
    public float size = 0.04f;
    public float pulseHz = 0.8f;

    private Transform _quad;
    private Renderer _renderer;
    private Material _mat;
    private Transform _head;
    private GuidewireSimulator _wire;
    private SimulationManager _sim;
    private CenterlineNode _lastNode;
    private CenterlineNode _fork;

    void Awake() { Build(); }

    public void Build()
    {
        if (_quad != null || ringSprite == null) return;
        var go = FxUtil.MakeQuad("Ring", transform);
        _quad = go.transform;
        _renderer = go.GetComponent<Renderer>();
        _mat = FxUtil.Overlay(ringSprite.texture);
        _renderer.sharedMaterial = _mat;
        _quad.localScale = Vector3.one * size;
        _renderer.enabled = false;
    }

    void Start()
    {
        _head = FxUtil.FindHead();
        _wire = FindFirstObjectByType<GuidewireSimulator>();
        _sim = FindFirstObjectByType<SimulationManager>();
    }

    // The first fork found by walking forward along single-branch nodes.
    // A node that is itself a fork gives nothing: the choice there has already been made.
    public static CenterlineNode NextForkAhead(CenterlineNode start)
    {
        if (start == null || start.branches.Count != 1) return null;
        var n = start;
        for (int i = 0; i < 5000 && n != null && n.branches.Count == 1; i++) n = n.branches[0];
        return n != null && n.branches.Count > 1 ? n : null;
    }

    void LateUpdate()
    {
        if (_quad == null || _wire == null || _sim == null) return;

        bool show = _wire.IsReady && _sim.CurrentPhase == SimulationManager.SimPhase.Phase1_Navigation && !_wire.IsAtFork;

        if (_wire.CurrentNode != _lastNode)
        {
            _lastNode = _wire.CurrentNode;
            _fork = NextForkAhead(_lastNode);
        }
        show &= _fork != null;

        _renderer.enabled = show;
        if (!show) return;

        Vector3 pos = _wire.centerlineLoader.GetNodeWorldPosition(_fork);
        float dist = Vector3.Distance(_wire.wireTip.position, pos);
        float fade = Mathf.Clamp01((dist - 0.012f) / 0.03f);
        float pulse = 0.9f + 0.1f * Mathf.Sin(Time.time * pulseHz * 2f * Mathf.PI);

        _quad.position = pos;
        _quad.localScale = Vector3.one * size * pulse;
        _mat.color = new Color(1f, 1f, 1f, 0.8f * fade);
        FxUtil.Billboard(_quad, _head);
    }
}
