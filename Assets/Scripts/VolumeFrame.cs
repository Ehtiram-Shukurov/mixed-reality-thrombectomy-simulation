using System.Collections.Generic;
using UnityEngine;

// Thin corner brackets around the volume, so its bounds are easy to see.
// One small mesh, one draw call. It is a child of the volume, so it moves with it.
public class VolumeFrame : MonoBehaviour
{
    [Tooltip("Length of each bracket arm, as a fraction of the volume size.")]
    public float arm = 0.14f;
    [Tooltip("Thickness of the lines in meters.")]
    public float thickness = 0.0025f;
    public Color color = new Color(1f, 1f, 1f, 0.35f);

    private GameObject _frame;

    void Awake() { Build(); }

    public void Build()
    {
        if (_frame != null) return;

        _frame = new GameObject("FrameBrackets");
        _frame.transform.SetParent(transform, false);

        var verts = new List<Vector3>();
        var tris = new List<int>();

        // The volume cube runs from -0.5 to 0.5 in its own space.
        float half = 0.5f;
        float w = thickness / Mathf.Max(0.001f, transform.lossyScale.x) * 0.5f;
        for (int sx = -1; sx <= 1; sx += 2)
        for (int sy = -1; sy <= 1; sy += 2)
        for (int sz = -1; sz <= 1; sz += 2)
        {
            var corner = new Vector3(sx * half, sy * half, sz * half);
            AddBar(verts, tris, corner, corner + new Vector3(-sx * arm, 0f, 0f), w);
            AddBar(verts, tris, corner, corner + new Vector3(0f, -sy * arm, 0f), w);
            AddBar(verts, tris, corner, corner + new Vector3(0f, 0f, -sz * arm), w);
        }

        var mesh = new Mesh { name = "FrameBrackets" };
        mesh.SetVertices(verts);
        mesh.SetTriangles(tris, 0);
        var colors = new Color[verts.Count];
        for (int i = 0; i < colors.Length; i++) colors[i] = Color.white;
        mesh.colors = colors;
        mesh.RecalculateBounds();

        _frame.AddComponent<MeshFilter>().sharedMesh = mesh;
        var mr = _frame.AddComponent<MeshRenderer>();
        var mat = FxUtil.Overlay(null);
        mat.color = color;
        mr.sharedMaterial = mat;
        mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        mr.receiveShadows = false;
    }

    // A thin square bar from a to b (4 sides, no end caps).
    private static void AddBar(List<Vector3> v, List<int> t, Vector3 a, Vector3 b, float w)
    {
        Vector3 dir = (b - a).normalized;
        Vector3 up = Mathf.Abs(Vector3.Dot(dir, Vector3.up)) > 0.9f ? Vector3.right : Vector3.up;
        Vector3 s1 = Vector3.Cross(dir, up).normalized * w;
        Vector3 s2 = Vector3.Cross(dir, s1).normalized * w;

        int i = v.Count;
        v.Add(a + s1 + s2); v.Add(a - s1 + s2); v.Add(a - s1 - s2); v.Add(a + s1 - s2);
        v.Add(b + s1 + s2); v.Add(b - s1 + s2); v.Add(b - s1 - s2); v.Add(b + s1 - s2);
        for (int k = 0; k < 4; k++)
        {
            int n = (k + 1) % 4;
            t.Add(i + k); t.Add(i + 4 + k); t.Add(i + n);
            t.Add(i + n); t.Add(i + 4 + k); t.Add(i + 4 + n);
        }
    }
}
