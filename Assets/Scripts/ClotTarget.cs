using System.Collections;
using System.Collections.Generic;
using UnityEngine;

// Places the clot in the upper part of the vessel tree. In this scan the top is the heart end
// (the scan runs from the chest to the upper thighs), not the brain.
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
    [Tooltip("Top fraction of the Z range used for clot positions (the heart end of this scan).")]
    public float upperFraction = 0.3f;

    public enum CaseLevel { Easy, Medium, Hard, Random }

    [Header("Case")]
    [Tooltip("Fixed clot positions ranked by forks on the way and distance, so runs are comparable. Random picks any position.")]
    public CaseLevel caseLevel = CaseLevel.Easy;
    // Shown on the phase card and written to the run log.
    public string CaseLabel { get; private set; } = "";
    public int ForksOnPath { get; private set; }

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
    private Vector3 _startScale;
    private bool _fragment;

    void Start()
    {
        _renderer = GetComponent<Renderer>();
        _startScale = transform.localScale;
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

        ClotNode = PickForCase(candidates);
        CountAhead(ClotNode, crossNodes, out var cross);
        CrossNode = cross;
        IsTouched = false;
        transform.position = centerlineLoader.GetNodeWorldPosition(ClotNode);
        BuildPath();
        _isPlaced = true;

        Debug.Log($"[ClotTarget] Placed at node {ClotNode.id} (Z={ClotNode.normalizedPosition.z:F3}), cross at node {CrossNode.id}");
    }

    // Ranks the candidates by forks on the way from the entry, then by path length, and takes
    // the one at the low, middle or high end for Easy, Medium or Hard. Always the same node per level.
    private CenterlineNode PickForCase(List<CenterlineNode> candidates)
    {
        // Parent of every node, walking from the entry.
        var parent = new Dictionary<CenterlineNode, CenterlineNode>();
        var queue = new Queue<CenterlineNode>();
        parent[centerlineLoader.EntryNode] = null;
        queue.Enqueue(centerlineLoader.EntryNode);
        while (queue.Count > 0)
        {
            var n = queue.Dequeue();
            foreach (var b in n.branches)
                if (!parent.ContainsKey(b)) { parent[b] = n; queue.Enqueue(b); }
        }

        int Forks(CenterlineNode node)
        {
            int f = 0;
            for (var p = parent.ContainsKey(node) ? parent[node] : null; p != null; p = parent.ContainsKey(p) ? parent[p] : null)
                if (p.branches.Count > 1) f++;
            return f;
        }
        int Depth(CenterlineNode node)
        {
            int d = 0;
            for (var p = node; p != null && parent.ContainsKey(p); p = parent[p]) d++;
            return d;
        }

        CenterlineNode chosen;
        if (caseLevel == CaseLevel.Random)
        {
            chosen = candidates[Random.Range(0, candidates.Count)];
        }
        else
        {
            var ranked = new List<CenterlineNode>(candidates);
            ranked.Sort((a, b) =>
            {
                int c = Forks(a).CompareTo(Forks(b));
                if (c != 0) return c;
                c = Depth(a).CompareTo(Depth(b));
                return c != 0 ? c : a.id.CompareTo(b.id);
            });
            float q = caseLevel == CaseLevel.Easy ? 0.1f : caseLevel == CaseLevel.Medium ? 0.5f : 0.9f;
            chosen = ranked[Mathf.Clamp(Mathf.RoundToInt(q * (ranked.Count - 1)), 0, ranked.Count - 1)];
        }

        ForksOnPath = Forks(chosen);
        CaseLabel = caseLevel == CaseLevel.Random
            ? $"Random case, {ForksOnPath} fork{(ForksOnPath == 1 ? "" : "s")}"
            : $"{caseLevel} case, {ForksOnPath} fork{(ForksOnPath == 1 ? "" : "s")}";
        return chosen;
    }

    // Moves to the next case level (Easy, Medium, Hard, then Easy again).
    public void NextCase()
    {
        caseLevel = caseLevel switch
        {
            CaseLevel.Easy => CaseLevel.Medium,
            CaseLevel.Medium => CaseLevel.Hard,
            _ => CaseLevel.Easy
        };
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

        // A fragment left after a pass shows in the dye like the clot did.
        if (_fragment) { UpdateFluoroVisual(); UpdateMarker(false); return; }

        if (!_isPlaced || IsReached) { UpdateMarker(false); return; }
        UpdateFluoroVisual();
        CheckDetection();

        bool dyeShowing = fluoroController != null && fluoroController.IsActive && fluoroController.ContrastActive;
        if (dyeShowing) IsRoadmapped = true;
        UpdateMarker(keepRoadmapMarker && SimMode.HintsOn && IsRoadmapped && !dyeShowing && !IsReached);
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
        if (_fragment) { transform.localScale = _startScale; _fragment = false; }
        _captured = false;
        _follow = null;
        if (_renderer != null) _renderer.enabled = false;
    }

    // After a pass that was too rough: a smaller piece stays where the clot was.
    // It is hidden until dye shows it, like the original clot.
    public void LeaveFragment(float shrink = 0.65f)
    {
        _captured = false;
        _follow = null;
        if (ClotNode != null) transform.position = centerlineLoader.GetNodeWorldPosition(ClotNode);
        transform.localScale *= shrink;
        if (_renderer != null)
        {
            _renderer.material = _clotMaterial;
            _renderer.enabled = false;
        }
        _fluoroWasActive = false;
        _fragment = true;
    }

    public void ResetClot()
    {
        if (_fragment) { transform.localScale = _startScale; _fragment = false; }
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
