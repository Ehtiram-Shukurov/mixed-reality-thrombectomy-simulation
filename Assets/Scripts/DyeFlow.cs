using UnityEngine;

// Drives two effects on the smooth vessel mesh:
//  - Blood flow: faint streaks moving away from the heart end. Nothing flows past the clot,
//    so the vessel there looks dim, until the clot is removed.
//  - Dye: on each injection a bright front spreads from the device tip along the vessel tree
//    and stops at the clot. After a successful extraction it fills the vessels past it.
// Distances are computed on the 917-node graph (cheap) and passed per vertex, and the
// animation itself is a few maths operations in the vessel shader.
public class DyeFlow : MonoBehaviour
{
    public VesselMeshView vesselMesh;
    public FluoroController fluoro;
    public ClotTarget clot;
    public GuidewireSimulator guidewire;
    public CatheterSimulator catheter;
    public CenterlineLoader centerlines;

    [Header("Dye")]
    [Tooltip("How fast the dye front travels, in volume sizes per second.")]
    public float dyeSpeed = 0.35f;
    [Tooltip("Softness of the dye front, in volume sizes.")]
    public float dyeFeather = 0.04f;

    [Header("Blood flow")]
    public bool showFlow = true;

    // True when the clot no longer blocks the vessel (set after a successful extraction).
    public bool Reperfused { get; set; }

    private float[] _flow;
    private float[] _dye;
    private CenterlineNode _lastClot;
    private bool _lastBlocked;
    private bool _lastMapped;
    private float _dyeStart = -1f;

    private static readonly int DyeFrontId = Shader.PropertyToID("_DyeFront");
    private static readonly int DyeAlphaId = Shader.PropertyToID("_DyeAlpha");
    private static readonly int DyeFeatherId = Shader.PropertyToID("_DyeFeather");
    private static readonly int FlowOnId = Shader.PropertyToID("_FlowOn");

    void Start()
    {
        if (vesselMesh == null) vesselMesh = FindFirstObjectByType<VesselMeshView>();
        if (fluoro == null) fluoro = FindFirstObjectByType<FluoroController>();
        if (clot == null) clot = FindFirstObjectByType<ClotTarget>();
        if (guidewire == null) guidewire = FindFirstObjectByType<GuidewireSimulator>();
        if (catheter == null) catheter = FindFirstObjectByType<CatheterSimulator>();
        if (centerlines == null) centerlines = FindFirstObjectByType<CenterlineLoader>();
        if (fluoro != null) fluoro.ContrastStarted += OnInject;
    }

    void OnDestroy()
    {
        if (fluoro != null) fluoro.ContrastStarted -= OnInject;
    }

    private CenterlineNode Block()
        => clot != null && clot.ClotNode != null && !Reperfused ? clot.ClotNode : null;

    void LateUpdate()
    {
        if (vesselMesh == null || !vesselMesh.MappingReady || !VesselGraph.Ensure(centerlines)) return;

        // Recompute blood flow only when the clot or its state changes.
        var block = Block();
        bool blocked = block != null;
        var clotNode = clot != null ? clot.ClotNode : null;
        if (!_lastMapped || clotNode != _lastClot || blocked != _lastBlocked)
        {
            _flow = VesselGraph.DistancesFrom(VesselGraph.TopNode(centerlines), block);
            // A dye run in progress has to respect the new state too.
            if (_dyeStart >= 0f) _dye = VesselGraph.DistancesFrom(InjectionNode(), block);
            vesselMesh.SetDistances(_flow, _dye);
            _lastClot = clotNode;
            _lastBlocked = blocked;
            _lastMapped = true;
        }

        var mat = vesselMesh.Material;
        if (mat == null) return;

        float front = 0f, alpha = 0f;
        if (_dyeStart >= 0f && fluoro != null && fluoro.ContrastActive)
        {
            front = (Time.time - _dyeStart) * dyeSpeed;
            alpha = Mathf.Clamp01(fluoro.ContrastRemaining01 * 3f); // wash out over the last third
        }
        else _dyeStart = -1f;

        mat.SetFloat(DyeFrontId, front);
        mat.SetFloat(DyeAlphaId, alpha);
        mat.SetFloat(DyeFeatherId, dyeFeather);
        mat.SetFloat(FlowOnId, showFlow ? 1f : 0f);
    }

    private void OnInject()
    {
        if (vesselMesh == null || !vesselMesh.MappingReady || !VesselGraph.Ensure(centerlines)) return;
        _dye = VesselGraph.DistancesFrom(InjectionNode(), Block());
        if (_flow == null) _flow = VesselGraph.DistancesFrom(VesselGraph.TopNode(centerlines), Block());
        vesselMesh.SetDistances(_flow, _dye);
        _dyeStart = Time.time;
    }

    // Dye comes out of whatever device is furthest in: the catheter once it moves, else the wire.
    private CenterlineNode InjectionNode()
    {
        Transform tip = null;
        if (catheter != null && (catheter.IsAdvancing || catheter.IsRetracting) && catheter.catheterTipTransform != null)
            tip = catheter.catheterTipTransform;
        else if (guidewire != null && guidewire.CurrentNode != null && guidewire.IsReady)
            return guidewire.CurrentNode;
        else if (guidewire != null)
            tip = guidewire.wireTip;
        if (tip == null) return centerlines.EntryNode;

        Vector3 local = centerlines.volumeTransform.InverseTransformPoint(tip.position);
        int best = 0;
        float bestD = float.MaxValue;
        for (int i = 0; i < VesselGraph.Count; i++)
        {
            float d = (VesselGraph.LocalPosition(i) - local).sqrMagnitude;
            if (d < bestD) { bestD = d; best = i; }
        }
        return centerlines.AllNodes[best];
    }
}
