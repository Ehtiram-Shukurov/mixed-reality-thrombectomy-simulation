using System.Collections;
using System.Collections.Generic;
using UnityEngine;

// Places the clot randomly in the upper cerebral vessel region.
// Invisible normally — visible as a dark filling defect in fluoro mode.
// During extraction it rides on the catheter tip so you can see it come out.
public class ClotTarget : MonoBehaviour
{
    [Header("References")]
    public CenterlineLoader centerlineLoader;
    public SimulationManager simManager;
    public FluoroController fluoroController;
    public Transform wireTip;

    [Header("Placement")]
    [Range(0.1f, 0.5f)]
    [Tooltip("Top fraction of Z range used as candidate region (cerebral side).")]
    public float upperFraction = 0.3f;

    [Header("Crossing")]
    [Tooltip("The wire has to go this many nodes past the clot (or to the next fork or the vessel end) to cross it.")]
    public int crossNodes = 8;
    [Tooltip("The clot is only placed where the vessel runs on at least this many nodes past it.")]
    public int minCrossNodes = 3;

    [Header("Roadmap marker")]
    [Tooltip("After dye has shown the clot once, a soft marker stays on it, like a saved roadmap image.")]
    public bool keepRoadmapMarker = true;
    public float markerSize = 0.03f;
    public Color markerColor = new Color(0.97f, 0.45f, 0.38f, 0.85f);

    public bool IsReached { get; private set; } = false;
    // Dye has shown the clot at least once this run.
    public bool IsRoadmapped { get; private set; } = false;
    // The wire tip is at the clot but has not crossed it yet.
    public bool IsTouched { get; private set; } = false;
    public CenterlineNode ClotNode { get; private set; }
    // The node the wire has to reach to count as past the clot.
    public CenterlineNode CrossNode { get; private set; }

    private Renderer _renderer;
    private Material _clotMaterial;
    private Material _capturedMaterial;
    private bool _isPlaced = false;
    private bool _fluoroWasActive = false;
    private bool _captured = false;
    private CatheterSimulator _follow;
    private float _behindTip;
    private GuidewireSimulator _guidewire;
    private readonly HashSet<CenterlineNode> _path = new HashSet<CenterlineNode>();
    private Transform _marker;
    private Renderer _markerRenderer;
    private Material _markerMat;
    private Transform _head;

    void Start()
    {
        _renderer = GetComponent<Renderer>();
        _clotMaterial = new Material(Shader.Find("Unlit/Color"));
        _clotMaterial.color = new Color(0.08f, 0.08f, 0.08f);
        _capturedMaterial = new Material(_clotMaterial);
        _capturedMaterial.color = new Color(0.55f, 0.10f, 0.10f);
        if (_renderer != null) _renderer.enabled = false;

        _guidewire = FindFirstObjectByType<GuidewireSimulator>();
        _head = FxUtil.FindHead();

        // Marker lives at scene root so the clot's own scale does not shrink it.
        var go = FxUtil.MakeQuad("ClotRoadmapMarker", null);
        _marker = go.transform;
        _markerRenderer = go.GetComponent<Renderer>();
        _markerMat = FxUtil.Overlay(FxUtil.GlowTexture());
        _markerRenderer.sharedMaterial = _markerMat;
        _markerRenderer.enabled = false;

        StartCoroutine(WaitAndPlace());
    }

    private IEnumerator WaitAndPlace()
    {
        while (centerlineLoader == null || !centerlineLoader.IsLoaded)
            yield return new WaitForSeconds(0.2f);
        PlaceClot();
    }

    private void PlaceClot()
    {
        var allNodes = centerlineLoader.AllNodes;

        float minZ = float.MaxValue, maxZ = float.MinValue;
        foreach (var n in allNodes)
        {
            if (n.normalizedPosition.z < minZ) minZ = n.normalizedPosition.z;
            if (n.normalizedPosition.z > maxZ) maxZ = n.normalizedPosition.z;
        }

        float threshold = Mathf.Lerp(minZ, maxZ, 1f - upperFraction);

        var candidates = new List<CenterlineNode>();
        foreach (var n in allNodes)
        {
            if (n == centerlineLoader.EntryNode) continue;
            if (n.branches.Count != 1) continue;
            if (n.normalizedPosition.z < threshold) continue;
            if (CountAhead(n, crossNodes, out _) < minCrossNodes) continue;
            candidates.Add(n);
        }

        if (candidates.Count == 0)
        {
            Debug.LogWarning("[ClotTarget] No candidates found. Try lowering upperFraction.");
            return;
        }

        ClotNode = candidates[Random.Range(0, candidates.Count)];
        CountAhead(ClotNode, crossNodes, out var cross);
        CrossNode = cross;
        IsTouched = false;
        transform.position = centerlineLoader.GetNodeWorldPosition(ClotNode);
        BuildPath();
        _isPlaced = true;

        Debug.Log($"[ClotTarget] Placed at node {ClotNode.id} (Z={ClotNode.normalizedPosition.z:F3}), cross at node {CrossNode.id}");
    }

    // Walks forward along the vessel from a node. Stops at a fork, a dead end or after max nodes.
    // Returns how many nodes it moved and the node it stopped on.
    private static int CountAhead(CenterlineNode start, int max, out CenterlineNode last)
    {
        int count = 0;
        var n = start;
        while (count < max && n.branches.Count >= 1)
        {
            n = n.branches[0];
            count++;
            if (n.branches.Count != 1) break;
        }
        last = n;
        return count;
    }

    // Every node from the entry to the clot. A branch that is not in here is a wrong turn.
    private void BuildPath()
    {
        _path.Clear();
        var parent = new Dictionary<CenterlineNode, CenterlineNode>();
        var queue = new Queue<CenterlineNode>();
        queue.Enqueue(centerlineLoader.EntryNode);
        parent[centerlineLoader.EntryNode] = null;
        while (queue.Count > 0)
        {
            var n = queue.Dequeue();
            foreach (var b in n.branches)
                if (!parent.ContainsKey(b)) { parent[b] = n; queue.Enqueue(b); }
        }

        for (var n = ClotNode; n != null; n = parent.ContainsKey(n) ? parent[n] : null)
            _path.Add(n);
    }

    public bool IsOnPath(CenterlineNode node) => node != null && _path.Contains(node);

    void Update()
    {
        if (_captured && _follow != null)
        {
            transform.position = _follow.ArcPointBehindTip(_behindTip, out _);
            UpdateMarker(false);
            return;
        }

        if (!_isPlaced || IsReached) { UpdateMarker(false); return; }
        UpdateFluoroVisual();
        CheckDetection();

        bool dyeShowing = fluoroController != null && fluoroController.IsActive && fluoroController.ContrastActive;
        if (dyeShowing) IsRoadmapped = true;
        UpdateMarker(keepRoadmapMarker && IsRoadmapped && !dyeShowing && !IsReached);
    }

    private void UpdateMarker(bool show)
    {
        if (_markerRenderer == null) return;
        _markerRenderer.enabled = show;
        if (!show) return;
        float pulse = 0.85f + 0.15f * Mathf.Sin(Time.time * 2f * Mathf.PI * 0.7f);
        _marker.position = transform.position;
        _marker.localScale = Vector3.one * markerSize * pulse;
        _markerMat.color = markerColor;
        FxUtil.Billboard(_marker, _head);
    }

    private void UpdateFluoroVisual()
    {
        if (fluoroController == null || _renderer == null) return;

        // The clot only shows as a gap in the dye, never on plain fluoroscopy.
        bool active = fluoroController.IsActive && fluoroController.ContrastActive;
        if (active && !_fluoroWasActive)
        {
            _renderer.material = _clotMaterial;
            _renderer.enabled = true;
        }
        else if (!active && _fluoroWasActive)
        {
            _renderer.enabled = false;
        }
        _fluoroWasActive = active;
    }

    private void CheckDetection()
    {
        if (wireTip == null) return;

        if (_guidewire == null) return;

        // The wire touches the clot at its own node. That is not enough: it has to cross.
        if (!IsTouched && _guidewire.CurrentNode == ClotNode)
        {
            IsTouched = true;
            Feedback.ClotContact();
            Debug.Log("[ClotTarget] Wire at the clot. Cross it.");
        }

        // Crossed once the wire is on the node past the clot. Using nodes, not distance,
        // so a nearby parallel vessel cannot trigger it.
        if (IsTouched && _guidewire.CurrentNode == CrossNode)
        {
            IsReached = true;
            if (_renderer != null) _renderer.enabled = false;

            Debug.Log("[ClotTarget] Clot crossed.");
            simManager?.OnGuidewireReachedClot();
        }
    }

    // How far the clot sits behind the catheter tip along the path.
    public float DistanceBehindTip(CatheterSimulator catheter)
        => catheter.DistanceBehindTipTo(transform.position);

    // Shows the clot behind the catheter tip and moves it with the tip.
    public void Capture(CatheterSimulator catheter, float behind)
    {
        _captured = true;
        _follow = catheter;
        _behindTip = behind;
        if (_renderer != null)
        {
            _renderer.material = _capturedMaterial;
            _renderer.enabled = true;
        }
    }

    public void Release()
    {
        _captured = false;
        _follow = null;
        if (_renderer != null) _renderer.enabled = false;
    }

    public void ResetClot()
    {
        IsReached = false;
        IsTouched = false;
        IsRoadmapped = false;
        _isPlaced = false;
        _fluoroWasActive = false;
        _captured = false;
        _follow = null;
        if (_renderer != null) _renderer.enabled = false;
        StartCoroutine(WaitAndPlace());
    }
}
