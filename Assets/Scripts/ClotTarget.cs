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

    [Header("Detection")]
    public float detectionRadius = 0.015f;

    public bool IsReached { get; private set; } = false;
    public CenterlineNode ClotNode { get; private set; }

    private Renderer _renderer;
    private Material _clotMaterial;
    private Material _capturedMaterial;
    private bool _isPlaced = false;
    private bool _fluoroWasActive = false;
    private bool _captured = false;
    private Transform _follow;
    private GuidewireSimulator _guidewire;
    private readonly HashSet<CenterlineNode> _path = new HashSet<CenterlineNode>();

    void Start()
    {
        _renderer = GetComponent<Renderer>();
        _clotMaterial = new Material(Shader.Find("Unlit/Color"));
        _clotMaterial.color = new Color(0.08f, 0.08f, 0.08f);
        _capturedMaterial = new Material(_clotMaterial);
        _capturedMaterial.color = new Color(0.55f, 0.10f, 0.10f);
        if (_renderer != null) _renderer.enabled = false;

        _guidewire = FindFirstObjectByType<GuidewireSimulator>();
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
            if (n.normalizedPosition.z >= threshold) candidates.Add(n);
        }

        if (candidates.Count == 0)
        {
            Debug.LogWarning("[ClotTarget] No candidates found. Try lowering upperFraction.");
            return;
        }

        ClotNode = candidates[Random.Range(0, candidates.Count)];
        transform.position = centerlineLoader.GetNodeWorldPosition(ClotNode);
        BuildPath();
        _isPlaced = true;

        Debug.Log($"[ClotTarget] Placed at node {ClotNode.id} (Z={ClotNode.normalizedPosition.z:F3})");
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
            transform.position = _follow.position;
            return;
        }

        if (!_isPlaced || IsReached) return;
        UpdateFluoroVisual();
        CheckDetection();
    }

    private void UpdateFluoroVisual()
    {
        if (fluoroController == null || _renderer == null) return;

        bool active = fluoroController.IsActive;
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

        // The wire has to arrive at the clot's own node, so a nearby parallel vessel
        // cannot trigger it.
        bool atNode = _guidewire == null || (_guidewire.CurrentNode == ClotNode && !_guidewire.IsAtFork);
        if (atNode && Vector3.Distance(wireTip.position, transform.position) < detectionRadius)
        {
            IsReached = true;
            if (_renderer != null) _renderer.enabled = false;

            Feedback.ClotContact();

            Debug.Log("[ClotTarget] Clot reached.");
            simManager?.OnGuidewireReachedClot();
        }
    }

    // Shows the clot on the catheter tip and moves it with the tip.
    public void Capture(Transform tip)
    {
        _captured = true;
        _follow = tip;
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
        _isPlaced = false;
        _fluoroWasActive = false;
        _captured = false;
        _follow = null;
        if (_renderer != null) _renderer.enabled = false;
        StartCoroutine(WaitAndPlace());
    }
}
