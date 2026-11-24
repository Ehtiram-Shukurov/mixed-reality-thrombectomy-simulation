using System.Collections.Generic;
using UnityEngine;

// The centerline tree as an undirected graph, with distances measured along the vessels.
// Used to let dye and flow travel through the real vessel tree and stop at the clot.
// Distances are in the volume's local units (the unit cube), so grabbing and scaling
// the model does not change them.
public static class VesselGraph
{
    private static CenterlineLoader _loader;
    private static readonly Dictionary<CenterlineNode, int> _index = new Dictionary<CenterlineNode, int>();
    private static List<int>[] _adj;
    private static List<float>[] _len;
    private static Vector3[] _local;

    public static int Count => _local != null ? _local.Length : 0;

    // Builds the graph once per loader. Returns false until the centerlines are loaded.
    public static bool Ensure(CenterlineLoader loader)
    {
        if (loader == null || !loader.IsLoaded || loader.volumeTransform == null) return false;
        if (_loader == loader && _local != null && _local.Length == loader.AllNodes.Count) return true;

        _loader = loader;
        var nodes = loader.AllNodes;
        int n = nodes.Count;
        _index.Clear();
        for (int i = 0; i < n; i++) _index[nodes[i]] = i;

        _local = new Vector3[n];
        for (int i = 0; i < n; i++)
            _local[i] = loader.volumeTransform.InverseTransformPoint(loader.GetNodeWorldPosition(nodes[i]));

        _adj = new List<int>[n];
        _len = new List<float>[n];
        for (int i = 0; i < n; i++) { _adj[i] = new List<int>(3); _len[i] = new List<float>(3); }
        for (int i = 0; i < n; i++)
        {
            foreach (var b in nodes[i].branches)
            {
                if (!_index.TryGetValue(b, out int j)) continue;
                float d = Vector3.Distance(_local[i], _local[j]);
                _adj[i].Add(j); _len[i].Add(d);
                _adj[j].Add(i); _len[j].Add(d);
            }
        }
        return true;
    }

    public static int IndexOf(CenterlineNode node)
        => node != null && _index.TryGetValue(node, out int i) ? i : -1;

    public static Vector3 LocalPosition(int i) => _local[i];

    // Distance along the vessels from start to every node. The walk does not go past
    // `block` (the clot): nodes only reachable through it stay at +infinity.
    public static float[] DistancesFrom(CenterlineNode start, CenterlineNode block)
    {
        var dist = new float[Count];
        for (int i = 0; i < dist.Length; i++) dist[i] = float.PositiveInfinity;
        int s = IndexOf(start);
        if (s < 0) return dist;
        int b = IndexOf(block);

        // The graph is a tree, so a plain walk gives the path distance.
        var stack = new Stack<int>();
        dist[s] = 0f;
        stack.Push(s);
        while (stack.Count > 0)
        {
            int u = stack.Pop();
            if (u == b && u != s) continue; // dye reaches the clot but does not pass it
            for (int k = 0; k < _adj[u].Count; k++)
            {
                int v = _adj[u][k];
                float nd = dist[u] + _len[u][k];
                if (nd < dist[v]) { dist[v] = nd; stack.Push(v); }
            }
        }
        return dist;
    }

    // The node nearest to the top of the scan (the heart end), used as the source of blood flow.
    public static CenterlineNode TopNode(CenterlineLoader loader)
    {
        CenterlineNode best = null;
        foreach (var n in loader.AllNodes)
            if (best == null || n.normalizedPosition.z > best.normalizedPosition.z) best = n;
        return best;
    }
}
