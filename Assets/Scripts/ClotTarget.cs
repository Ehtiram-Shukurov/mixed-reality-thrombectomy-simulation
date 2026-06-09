using System.Collections;
using System.Collections.Generic;
using UnityEngine;

// Places the clot randomly in the upper cerebral vessel region.
// Invisible normally — visible as a dark filling defect in fluoro mode.
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
    private bool _isPlaced = false;
    private bool _fluoroWasActive = false;

    void Start()
    {
        _renderer = GetComponent<Renderer>();
        _clotMaterial = new Material(Shader.Find("Unlit/Color"));
        _clotMaterial.color = new Color(0.08f, 0.08f, 0.08f);
        if (_renderer != null) _renderer.enabled = false;

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
        _isPlaced = true;

        Debug.Log($"[ClotTarget] Placed at node {ClotNode.id} (Z={ClotNode.normalizedPosition.z:F3})");
    }

    void Update()
    {
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
        if (Vector3.Distance(wireTip.position, transform.position) < detectionRadius)
        {
            IsReached = true;
            _renderer.enabled = false;

            HapticManager.Instance?.PulseOnce(0.8f, 0.3f);

            Debug.Log("[ClotTarget] Clot reached.");
            simManager?.OnGuidewireReachedClot();
        }
    }

    //void OnDrawGizmos()
    //{
    //    if (!_isPlaced) return;
    //    Gizmos.color = Color.red;
    //    Gizmos.DrawWireSphere(transform.position, detectionRadius);
    //}
    public void ResetClot()
    {
        IsReached = false;
        _isPlaced = false;
        _fluoroWasActive = false;
        if (_renderer != null) _renderer.enabled = false;
        StartCoroutine(WaitAndPlace());
    }
}