using System.Collections;
using System.Collections.Generic;
using UnityEngine;

// Blood flow and contrast dye, kept the same on the vessel mesh and on the X-ray.
// Blood enters at the heart end of the vessel tree and flows away from it. Dye is injected
// upstream, like a diagnostic run from a guide catheter, so it follows the blood and stops at
// the clot. Past the clot nothing flows and no dye arrives, until the clot is removed.
//
// The distances along the vessels are computed on the 917-node graph and given to:
//  - the mesh, per vertex (flow streaks, dim no-flow region, dye front)
//  - the X-ray shader, through a small 3D lookup map, so the dye stops at the clot there too.
// The map is built once at load and refilled only when the clot changes, not every frame.
public class DyeFlow : MonoBehaviour
{
    public VesselMeshView vesselMesh;
    public FluoroController fluoro;
    public ClotTarget clot;
    public GuidewireSimulator guidewire;
    public CatheterSimulator catheter;
    public CenterlineLoader centerlines;

    [Header("Dye")]
    [Tooltip("Seconds for the dye to reach the far ends of the open vessels.")]
    public float fillSeconds = 3f;
    [Tooltip("Softness of the dye front, in volume sizes.")]
    public float dyeFeather = 0.04f;
    [Tooltip("Seconds for the dye to wash out after the fill, nearest vessels first.")]
    public float washSeconds = 2f;

    // Behind this distance the dye has already washed out.
    public float DyeTail { get; private set; }

    [Header("Blood flow")]
    public bool showFlow = true;

    [Header("X-ray dye map")]
    [Tooltip("Cells of the lookup map across the volume. Small, so it is cheap to build and read.")]
    public Vector3Int mapSize = new Vector3Int(96, 96, 48);
    [Tooltip("A cell takes the distance of the nearest centerline node within this radius (volume sizes).")]
    public float mapRadius = 0.045f;
    [Tooltip("Map cells built per frame at load, so loading does not stall.")]
    public int cellsPerFrame = 40000;

    // True when the clot no longer blocks the vessel (set after a successful pass).
    public bool Reperfused { get; set; }
    // Current dye front, in volume sizes along the vessels from the heart end.
    public float DyeFront { get; private set; }
    public bool DyeRunning => _dyeStart >= 0f;
    // Distance to the farthest vessel the dye can reach right now.
    public float MaxReach { get; private set; }
    // The dye has reached every vessel it can.
    public bool FillComplete => DyeRunning && DyeFront >= MaxReach + dyeFeather;

    private float[] _dist;
    private CenterlineNode _lastClot;
    private bool _lastBlocked;
    private bool _computed;
    private bool _meshFed;
    private float _dyeStart = -1f;

    private Texture3D _map;
    private int[] _cellNode;
    private float[] _cellDist;
    private bool _mapReady;
    private bool _mapDirty;

    private static readonly int DyeFrontId = Shader.PropertyToID("_DyeFront");
    private static readonly int DyeAlphaId = Shader.PropertyToID("_DyeAlpha");
    private static readonly int DyeFeatherId = Shader.PropertyToID("_DyeFeather");
    private static readonly int FlowOnId = Shader.PropertyToID("_FlowOn");
    // Globals read by the volume shader (not in its Properties, so they are not overridden).
    private static readonly int VolDyeTexId = Shader.PropertyToID("_VolDyeTex");
    private static readonly int VolDyeOnId = Shader.PropertyToID("_VolDyeOn");
    private static readonly int VolDyeFrontId = Shader.PropertyToID("_VolDyeFront");
    private static readonly int VolDyeTailId = Shader.PropertyToID("_VolDyeTail");
    private static readonly int DyeTailId = Shader.PropertyToID("_DyeTail");
    private static readonly int VolDyeFeatherId = Shader.PropertyToID("_VolDyeFeather");
    private static readonly int VolDyePlainId = Shader.PropertyToID("_VolDyePlain");

    void Start()
    {
        if (vesselMesh == null) vesselMesh = FindFirstObjectByType<VesselMeshView>();
        if (fluoro == null) fluoro = FindFirstObjectByType<FluoroController>();
        if (clot == null) clot = FindFirstObjectByType<ClotTarget>();
        if (guidewire == null) guidewire = FindFirstObjectByType<GuidewireSimulator>();
        if (catheter == null) catheter = FindFirstObjectByType<CatheterSimulator>();
        if (centerlines == null) centerlines = FindFirstObjectByType<CenterlineLoader>();
        if (fluoro != null) fluoro.ContrastStarted += OnInject;
        Shader.SetGlobalFloat(VolDyeOnId, 0f);
        StartCoroutine(BuildMap());
    }

    void OnDestroy()
    {
        if (fluoro != null) fluoro.ContrastStarted -= OnInject;
        Shader.SetGlobalFloat(VolDyeOnId, 0f);
    }

    private CenterlineNode Block()
        => clot != null && clot.ClotNode != null && !Reperfused ? clot.ClotNode : null;

    // For each map cell, the nearest centerline node within mapRadius (or -1).
    private IEnumerator BuildMap()
    {
        while (!VesselGraph.Ensure(centerlines)) yield return new WaitForSeconds(0.2f);

        int nx = mapSize.x, ny = mapSize.y, nz = mapSize.z;
        int n = VesselGraph.Count;
        float cell = mapRadius;
        var grid = new Dictionary<Vector3Int, List<int>>();
        for (int i = 0; i < n; i++)
        {
            var key = Vector3Int.FloorToInt(VesselGraph.LocalPosition(i) / cell);
            if (!grid.TryGetValue(key, out var list)) grid[key] = list = new List<int>();
            list.Add(i);
        }

        int total = nx * ny * nz;
        _cellNode = new int[total];
        _cellDist = new float[total];
        float r2 = mapRadius * mapRadius;
        int done = 0;
        for (int z = 0; z < nz; z++)
        for (int y = 0; y < ny; y++)
        for (int x = 0; x < nx; x++)
        {
            var p = new Vector3((x + 0.5f) / nx - 0.5f, (y + 0.5f) / ny - 0.5f, (z + 0.5f) / nz - 0.5f);
            var c = Vector3Int.FloorToInt(p / cell);
            int best = -1;
            float bestD = r2;
            for (int dx = -1; dx <= 1; dx++)
            for (int dy = -1; dy <= 1; dy++)
            for (int dz = -1; dz <= 1; dz++)
            {
                if (!grid.TryGetValue(new Vector3Int(c.x + dx, c.y + dy, c.z + dz), out var list)) continue;
                foreach (int i in list)
                {
                    float d = (VesselGraph.LocalPosition(i) - p).sqrMagnitude;
                    if (d < bestD) { bestD = d; best = i; }
                }
            }
            _cellNode[x + y * nx + z * nx * ny] = best;
            if (++done % cellsPerFrame == 0) yield return null;
        }

        _map = new Texture3D(nx, ny, nz, TextureFormat.RFloat, false)
        {
            name = "VolumeDyeMap",
            filterMode = FilterMode.Point,
            wrapMode = TextureWrapMode.Clamp
        };
        Shader.SetGlobalTexture(VolDyeTexId, _map);
        _mapReady = true;
        _mapDirty = true;
        Debug.Log($"[DyeFlow] Dye map built: {nx}x{ny}x{nz} cells.");
    }

    // Distances from the heart end along the vessels, stopping at the clot.
    private void Recompute()
    {
        _dist = VesselGraph.DistancesFrom(VesselGraph.TopNode(centerlines), Block());
        float max = 0f;
        foreach (float d in _dist) if (!float.IsInfinity(d) && d > max) max = d;
        MaxReach = max;
        _meshFed = false;
        _mapDirty = true;
        _computed = true;
    }

    private void FillMap()
    {
        for (int i = 0; i < _cellNode.Length; i++)
        {
            int k = _cellNode[i];
            _cellDist[i] = k >= 0 && !float.IsInfinity(_dist[k]) ? _dist[k] : VesselMeshView.Unreached;
        }
        _map.SetPixelData(_cellDist, 0);
        _map.Apply(false, false);
        _mapDirty = false;
    }

    void LateUpdate()
    {
        if (!VesselGraph.Ensure(centerlines)) return;

        // Recompute only when the clot or its state changes.
        var block = Block();
        bool blocked = block != null;
        var clotNode = clot != null ? clot.ClotNode : null;
        if (!_computed || clotNode != _lastClot || blocked != _lastBlocked)
        {
            Recompute();
            _lastClot = clotNode;
            _lastBlocked = blocked;
        }
        if (!_meshFed && vesselMesh != null && vesselMesh.MappingReady)
        {
            vesselMesh.SetDistances(_dist, _dist);
            _meshFed = true;
        }
        if (_mapReady && _mapDirty) FillMap();

        // Dye animation: the front moves from the heart end to the farthest open vessel in fillSeconds.
        float alpha = 0f;
        if (_dyeStart >= 0f && fluoro != null && fluoro.ContrastActive)
        {
            float elapsed = Time.time - _dyeStart;
            float span = MaxReach + dyeFeather;
            DyeFront = elapsed / Mathf.Max(0.1f, fillSeconds) * span;
            // Once the vessels are full, the dye passes on: the nearest vessels clear first.
            DyeTail = Mathf.Clamp01((elapsed - fillSeconds) / Mathf.Max(0.1f, washSeconds)) * span;
            alpha = Mathf.Clamp01(fluoro.ContrastRemaining01 * 3f);
        }
        else
        {
            _dyeStart = -1f;
            DyeFront = 0f;
            DyeTail = 0f;
        }

        var mat = vesselMesh != null ? vesselMesh.Material : null;
        if (mat != null)
        {
            mat.SetFloat(DyeFrontId, DyeFront);
            mat.SetFloat(DyeTailId, DyeTail);
            mat.SetFloat(DyeAlphaId, alpha);
            mat.SetFloat(DyeFeatherId, dyeFeather);
            mat.SetFloat(FlowOnId, showFlow ? 1f : 0f);
        }

        Shader.SetGlobalFloat(VolDyeOnId, _mapReady && DyeRunning ? 1f : 0f);
        Shader.SetGlobalFloat(VolDyeFrontId, DyeFront);
        Shader.SetGlobalFloat(VolDyeTailId, DyeTail);
        Shader.SetGlobalFloat(VolDyeFeatherId, dyeFeather);
        Shader.SetGlobalFloat(VolDyePlainId, fluoro != null ? fluoro.plainVesselVisibility : 0.05f);
    }

    private void OnInject()
    {
        if (!VesselGraph.Ensure(centerlines)) return;
        // The state may have changed this frame (for example right after a pass), so refresh first.
        Recompute();
        _lastClot = clot != null ? clot.ClotNode : null;
        _lastBlocked = Block() != null;
        if (vesselMesh != null && vesselMesh.MappingReady) { vesselMesh.SetDistances(_dist, _dist); _meshFed = true; }
        if (_mapReady) FillMap();
        _dyeStart = Time.time;
    }
}
