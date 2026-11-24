using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.Networking;

// Smooth vessel surface for the "Vascular" render mode.
// The mesh was built offline from the same segmentation (labels 22 to 29), smoothed,
// and stored in the volume's own unit-cube space, so it lines up with the volume exactly.
// In that mode the raymarched volume is switched off and this mesh is drawn instead,
// which looks smoother and is much cheaper. X-ray and dye still use the volume.
public class VesselMeshView : MonoBehaviour
{
    public DicomVolumeLoader loader;
    public FluoroController fluoro;
    [Tooltip("Custom/VesselGlass. Set here so the shader is included in the build.")]
    public Shader glassShader;
    [Tooltip("Render mode that shows the mesh instead of the volume (2 = vascular).")]
    public int meshRenderMode = 2;
    public string fileName = "vessel_mesh.bytes";

    public bool IsLoaded { get; private set; }
    // True once every vertex knows its nearest centerline node.
    public bool MappingReady { get; private set; }
    public MeshRenderer MeshRenderer => _meshRenderer;
    public Material Material => _meshRenderer != null ? _meshRenderer.sharedMaterial : null;

    private Renderer _volumeRenderer;
    private MeshRenderer _meshRenderer;
    private Mesh _mesh;
    private int[] _vertexNode;
    private readonly List<Vector2> _uv2 = new List<Vector2>();
    private CenterlineLoader _centerlines;

    // Stand-in for "never reached" in the shader.
    public const float Unreached = 10000f;

    IEnumerator Start()
    {
        if (loader == null) loader = FindFirstObjectByType<DicomVolumeLoader>();
        if (fluoro == null) fluoro = FindFirstObjectByType<FluoroController>();
        if (loader == null) yield break;
        _volumeRenderer = loader.GetComponent<Renderer>();

        byte[] data = null;
        string path = Path.Combine(Application.streamingAssetsPath, fileName);
        if (path.Contains("://") || path.Contains(":///"))
        {
            using (var req = UnityWebRequest.Get(path))
            {
                yield return req.SendWebRequest();
                if (req.result == UnityWebRequest.Result.Success) data = req.downloadHandler.data;
                else Debug.LogWarning("[VesselMesh] Could not load " + path + ": " + req.error);
            }
        }
        else if (File.Exists(path))
        {
            data = File.ReadAllBytes(path);
        }
        else
        {
            Debug.LogWarning("[VesselMesh] Missing " + path + ". The vascular mode will use the volume.");
        }

        if (data == null) yield break;
        Build(data);

        // Link vertices to the centerline graph once it has loaded.
        _centerlines = FindFirstObjectByType<CenterlineLoader>();
        while (!VesselGraph.Ensure(_centerlines)) yield return new WaitForSeconds(0.2f);
        MapVertices();
    }

    // Nearest centerline node for every vertex, using a coarse grid so it stays quick.
    private void MapVertices()
    {
        var verts = _mesh.vertices;
        int n = VesselGraph.Count;
        const float cell = 0.03f;
        var grid = new Dictionary<Vector3Int, List<int>>();
        for (int i = 0; i < n; i++)
        {
            var key = Vector3Int.FloorToInt(VesselGraph.LocalPosition(i) / cell);
            if (!grid.TryGetValue(key, out var list)) grid[key] = list = new List<int>();
            list.Add(i);
        }

        _vertexNode = new int[verts.Length];
        for (int v = 0; v < verts.Length; v++)
        {
            var c = Vector3Int.FloorToInt(verts[v] / cell);
            int best = -1;
            float bestD = float.MaxValue;
            for (int r = 1; r <= 4 && best < 0; r++)
            {
                for (int x = -r; x <= r; x++)
                for (int y = -r; y <= r; y++)
                for (int z = -r; z <= r; z++)
                {
                    if (!grid.TryGetValue(new Vector3Int(c.x + x, c.y + y, c.z + z), out var list)) continue;
                    foreach (int i in list)
                    {
                        float d = (VesselGraph.LocalPosition(i) - verts[v]).sqrMagnitude;
                        if (d < bestD) { bestD = d; best = i; }
                    }
                }
            }
            if (best < 0)
            {
                for (int i = 0; i < n; i++)
                {
                    float d = (VesselGraph.LocalPosition(i) - verts[v]).sqrMagnitude;
                    if (d < bestD) { bestD = d; best = i; }
                }
            }
            _vertexNode[v] = best;
        }

        for (int v = 0; v < verts.Length; v++) _uv2.Add(new Vector2(Unreached, Unreached));
        _mesh.SetUVs(1, _uv2);
        MappingReady = true;
        Debug.Log($"[VesselMesh] Mapped {verts.Length} vertices to {n} centerline nodes.");
    }

    // Per-node distances along the vessels: x = blood flow from the heart end,
    // y = dye from the injection point. Infinity means never reached (beyond the clot).
    public void SetDistances(float[] flowDist, float[] dyeDist)
    {
        if (!MappingReady) return;
        for (int v = 0; v < _vertexNode.Length; v++)
        {
            int k = _vertexNode[v];
            float f = flowDist != null && k >= 0 && !float.IsInfinity(flowDist[k]) ? flowDist[k] : Unreached;
            float d = dyeDist != null && k >= 0 && !float.IsInfinity(dyeDist[k]) ? dyeDist[k] : Unreached;
            _uv2[v] = new Vector2(f, d);
        }
        _mesh.SetUVs(1, _uv2);
    }

    private void Build(byte[] data)
    {
        int vertCount = System.BitConverter.ToInt32(data, 0);
        int indexCount = System.BitConverter.ToInt32(data, 4);
        var verts = new Vector3[vertCount];
        int o = 8;
        for (int i = 0; i < vertCount; i++, o += 12)
            verts[i] = new Vector3(System.BitConverter.ToSingle(data, o),
                                   System.BitConverter.ToSingle(data, o + 4),
                                   System.BitConverter.ToSingle(data, o + 8));
        var indices = new int[indexCount];
        for (int i = 0; i < indexCount; i++, o += 4)
            indices[i] = System.BitConverter.ToInt32(data, o);

        var mesh = new Mesh { name = "VesselMesh" };
        if (vertCount > 65000) mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
        mesh.vertices = verts;
        mesh.triangles = indices;
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();
        _mesh = mesh;

        // Child of the volume with no offset: the mesh is already in the volume's local space.
        // No collider, so the grab on the volume is not affected.
        var go = new GameObject("VesselMesh");
        go.transform.SetParent(loader.transform, false);
        go.AddComponent<MeshFilter>().sharedMesh = mesh;
        _meshRenderer = go.AddComponent<MeshRenderer>();
        var shader = glassShader != null ? glassShader : Shader.Find("Custom/VesselGlass");
        _meshRenderer.sharedMaterial = new Material(shader);
        _meshRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        _meshRenderer.receiveShadows = false;
        _meshRenderer.enabled = false;

        IsLoaded = true;
        Debug.Log($"[VesselMesh] Loaded {vertCount} vertices, {indexCount / 3} triangles.");
    }

    void LateUpdate()
    {
        if (!IsLoaded || loader.volumeMaterial == null) return;

        // Only holding A turns the main view into X-ray. Dye runs show on the mesh and the monitor.
        int mode = Mathf.RoundToInt(loader.volumeMaterial.GetFloat("_RenderMode"));
        bool xrayView = fluoro != null && fluoro.MainViewXRay;
        bool meshOn = !xrayView && mode == meshRenderMode;

        _meshRenderer.enabled = meshOn;
        if (_volumeRenderer != null) _volumeRenderer.enabled = !meshOn;
    }

    void OnDisable()
    {
        if (_volumeRenderer != null) _volumeRenderer.enabled = true;
    }
}
