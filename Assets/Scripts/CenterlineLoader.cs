using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using System.IO;
using UnityEngine.Networking;
using System.Text.RegularExpressions;

public class CenterlineNode
{
    public int id;
    public Vector3 rawPosition;
    public Vector3 normalizedPosition;
    public float radius;
    public List<CenterlineNode> branches = new List<CenterlineNode>();

    public CenterlineNode(int id, Vector3 rawPos, float r)
    {
        this.id = id;
        this.rawPosition = rawPos;
        this.radius = r;
    }
}

public class CenterlineLoader : MonoBehaviour
{
    [Header("Core References")]
    public Transform volumeTransform;

    [Header("Live Alignment Tweaks")]
    public Vector3 alignmentOffset = Vector3.zero;
    public float scaleMultiplier = 1f;

    [Header("Graph Construction")]
    [Tooltip("Two raw points within this distance get merged into one node. " +
             "Smaller = more nodes, more accurate forks. Larger = fewer nodes. " +
             "Tune this if forks are missing (try 0.0005 - 0.0015).")]
    public float mergeTolerance = 0.0008f;

    [Tooltip("Distance threshold that separates one VMTK route from the next. " +
             "VMTK exports each route as outlet -> common confluence; consecutive " +
             "routes are separated by huge spatial jumps in the file.")]
    public float routeBoundaryThreshold = 0.1f;

    public List<CenterlineNode> AllNodes { get; private set; } = new List<CenterlineNode>();
    public CenterlineNode EntryNode { get; private set; }
    public bool IsLoaded { get; private set; } = false;

    private void Start()
    {
        StartCoroutine(LoadCenterlinesRoutine());
    }

    private IEnumerator LoadCenterlinesRoutine()
    {
        string path = Path.Combine(Application.streamingAssetsPath, "centerlines_vmtk.json");
        string json = "";

        if (Application.platform == RuntimePlatform.Android)
        {
            using (UnityWebRequest www = UnityWebRequest.Get(path))
            {
                yield return www.SendWebRequest();
                if (www.result != UnityWebRequest.Result.Success)
                {
                    Debug.LogError("[CenterlineLoader] Failed to load: " + www.error);
                    yield break;
                }
                json = www.downloadHandler.text;
            }
        }
        else
        {
            if (!File.Exists(path))
            {
                Debug.LogError("[CenterlineLoader] File not found: " + path);
                yield break;
            }
            json = File.ReadAllText(path);
        }

        ProcessJsonData(json);
    }

    private void ProcessJsonData(string rawJson)
    {
        //Extract all 3D points from the JSON.
        List<float> nums = new List<float>();
        foreach (Match m in Regex.Matches(rawJson, @"[-+]?[0-9]*\.?[0-9]+(?:[eE][-+]?[0-9]+)?"))
        {
            if (float.TryParse(m.Value,
                               System.Globalization.NumberStyles.Any,
                               System.Globalization.CultureInfo.InvariantCulture,
                               out float v))
                nums.Add(v);
        }

        List<Vector3> allPoints = new List<Vector3>();
        for (int i = 0; i + 2 < nums.Count; i += 3)
            allPoints.Add(new Vector3(nums[i], nums[i + 1], nums[i + 2]));

        if (allPoints.Count < 2)
        {
            Debug.LogError("[CenterlineLoader] Not enough points parsed from JSON.");
            return;
        }

        List<List<Vector3>> routes = new List<List<Vector3>>();
        List<Vector3> current = new List<Vector3> { allPoints[0] };
        for (int i = 1; i < allPoints.Count; i++)
        {
            if (Vector3.Distance(allPoints[i - 1], allPoints[i]) > routeBoundaryThreshold)
            {
                routes.Add(current);
                current = new List<Vector3>();
            }
            current.Add(allPoints[i]);
        }
        if (current.Count > 0) routes.Add(current);

        for (int i = 0; i < routes.Count; i++) routes[i].Reverse();

        int nextId = 0;
        foreach (var route in routes)
        {
            CenterlineNode previousNode = null;
            foreach (var pt in route)
            {
                CenterlineNode currentNode = GetOrCreateNode(pt, 0.01f, ref nextId);
                if (previousNode != null && currentNode != previousNode)
                {
                    if (!previousNode.branches.Contains(currentNode))
                        previousNode.branches.Add(currentNode);
                }
                previousNode = currentNode;
            }
        }

        ApplyVolumeTransform();
        List<CenterlineNode> originalEndpoints = AllNodes.FindAll(n => n.branches.Count == 0);

        CenterlineNode newEntry = null;
        float lowestX = float.MaxValue;
        foreach (var ep in originalEndpoints)
        {
            if (ep.normalizedPosition.x < lowestX)
            {
                lowestX = ep.normalizedPosition.x;
                newEntry = ep;
            }
        }

        if (newEntry != null)
        {
            RerootTree(newEntry);
            EntryNode = newEntry;
        }
        else
        {
            // Fallback - shouldn't happen with a valid graph
            EntryNode = AllNodes.Count > 0 ? AllNodes[0] : null;
        }

        IsLoaded = true;

    }
    private void RerootTree(CenterlineNode newRoot)
    {
        Dictionary<CenterlineNode, List<CenterlineNode>> undirected =
            new Dictionary<CenterlineNode, List<CenterlineNode>>();

        foreach (var n in AllNodes)
            undirected[n] = new List<CenterlineNode>();

        foreach (var n in AllNodes)
        {
            foreach (var b in n.branches)
            {
                undirected[n].Add(b);
                if (!undirected[b].Contains(n))
                    undirected[b].Add(n);
            }
        }

        foreach (var n in AllNodes)
            n.branches.Clear();

        HashSet<CenterlineNode> visited = new HashSet<CenterlineNode>();
        Queue<CenterlineNode> queue = new Queue<CenterlineNode>();
        queue.Enqueue(newRoot);
        visited.Add(newRoot);

        while (queue.Count > 0)
        {
            var node = queue.Dequeue();
            foreach (var neighbor in undirected[node])
            {
                if (!visited.Contains(neighbor))
                {
                    visited.Add(neighbor);
                    node.branches.Add(neighbor);
                    queue.Enqueue(neighbor);
                }
            }
        }
    }


    private CenterlineNode GetOrCreateNode(Vector3 rawPos, float radius, ref int nextId)
    {
        foreach (var node in AllNodes)
        {
            if (Vector3.Distance(node.rawPosition, rawPos) < mergeTolerance)
                return node;
        }
        CenterlineNode newNode = new CenterlineNode(nextId++, rawPos, radius);
        AllNodes.Add(newNode);
        return newNode;
    }

    private void ApplyVolumeTransform()
    {
        Vector3 minBounds = AllNodes[0].rawPosition;
        Vector3 maxBounds = AllNodes[0].rawPosition;

        foreach (var node in AllNodes)
        {
            minBounds = Vector3.Min(minBounds, node.rawPosition);
            maxBounds = Vector3.Max(maxBounds, node.rawPosition);
        }

        Vector3 center = (minBounds + maxBounds) / 2f;
        Vector3 size = maxBounds - minBounds;
        float maxDim = Mathf.Max(size.x, size.y, size.z);

        foreach (var node in AllNodes)
        {
            node.normalizedPosition = (node.rawPosition - center) / maxDim;
        }
    }

    public Vector3 GetNodeWorldPosition(CenterlineNode node)
    {
        Vector3 tweakedLocalPos = (node.normalizedPosition * scaleMultiplier) + alignmentOffset;
        return volumeTransform.TransformPoint(tweakedLocalPos);
    }

    //private void OnDrawGizmos()
    //{
    //    if (!IsLoaded || volumeTransform == null) return;

    //    foreach (var node in AllNodes)
    //    {
    //        float size = 0.001f;

    //        if (node == EntryNode)
    //        {
    //            Gizmos.color = Color.green;
    //            size = 0.004f;
    //        }
    //        else if (node.branches.Count > 1)
    //        {
    //            Gizmos.color = Color.cyan;
    //            size = 0.003f;
    //        }
    //        else if (node.branches.Count == 0)
    //        {
    //            Gizmos.color = Color.magenta; 
    //            size = 0.003f;
    //        }
    //        else
    //        {
    //            Gizmos.color = Color.yellow;
    //        }

    //        Vector3 nodeWorldPos = GetNodeWorldPosition(node);
    //        Gizmos.DrawSphere(nodeWorldPos, size);

    //        Gizmos.color = Color.red;
    //        foreach (var branch in node.branches)
    //            Gizmos.DrawLine(nodeWorldPos, GetNodeWorldPosition(branch));
    //    }
    //}
}