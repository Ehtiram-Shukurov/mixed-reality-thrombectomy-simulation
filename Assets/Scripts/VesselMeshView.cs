using System.Collections;
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

    private Renderer _volumeRenderer;
    private MeshRenderer _meshRenderer;

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

        int mode = Mathf.RoundToInt(loader.volumeMaterial.GetFloat("_RenderMode"));
        bool imaging = fluoro != null && fluoro.IsActive;
        bool meshOn = mode == meshRenderMode && !imaging;

        _meshRenderer.enabled = meshOn;
        if (_volumeRenderer != null) _volumeRenderer.enabled = !meshOn;
    }

    void OnDisable()
    {
        if (_volumeRenderer != null) _volumeRenderer.enabled = true;
    }
}
