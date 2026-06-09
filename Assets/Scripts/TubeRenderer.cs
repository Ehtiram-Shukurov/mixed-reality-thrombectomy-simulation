using System.Collections.Generic;
using UnityEngine;

// Generates a 3D tube mesh along a path of points.
// Used instead of LineRenderer
[RequireComponent(typeof(MeshFilter))]
[RequireComponent(typeof(MeshRenderer))]
public class TubeRenderer : MonoBehaviour
{
    public float radius = 0.003f;
    [Range(3, 12)]
    public int sides = 6;

    private MeshFilter _meshFilter;
    private Mesh _mesh;
    private List<Vector3> _points = new List<Vector3>();

    void Awake()
    {
        _meshFilter = GetComponent<MeshFilter>();
        _mesh = new Mesh();
        _mesh.name = "CatheterTube";
        _meshFilter.mesh = _mesh;
    }

    // takes local volume positions and converts them to world space before building the mesh
    public void SetPoints(List<Vector3> localPoints, Transform volumeTransform)
    {
        _points.Clear();
        foreach (var p in localPoints)
            _points.Add(volumeTransform.TransformPoint(p));
        RebuildMesh();
    }

    public void Clear()
    {
        _points.Clear();
        _mesh.Clear();
    }

    void RebuildMesh()
    {
        if (_points == null || _points.Count < 2)
        {
            _mesh.Clear();
            return;
        }

        int pointCount = _points.Count;
        int vertCount = pointCount * sides;
        int triCount = (pointCount - 1) * sides * 2;

        Vector3[] vertices = new Vector3[vertCount];
        int[] triangles = new int[triCount * 3];
        Vector2[] uvs = new Vector2[vertCount];

        for (int i = 0; i < pointCount; i++)
        {
            Vector3 point = _points[i];
            Vector3 forward = GetForward(i);
            Vector3 right = GetPerpendicular(forward);
            Vector3 up = Vector3.Cross(forward, right).normalized;

            for (int s = 0; s < sides; s++)
            {
                float angle = (float)s / sides * Mathf.PI * 2f;
                Vector3 offset = (Mathf.Cos(angle) * right + Mathf.Sin(angle) * up) * radius;
                vertices[i * sides + s] = transform.InverseTransformPoint(point + offset);
                uvs[i * sides + s] = new Vector2((float)s / sides, (float)i / pointCount);
            }
        }

        int triIndex = 0;
        for (int i = 0; i < pointCount - 1; i++)
        {
            for (int s = 0; s < sides; s++)
            {
                int current = i * sides + s;
                int next = i * sides + (s + 1) % sides;
                int currentNext = (i + 1) * sides + s;
                int nextNext = (i + 1) * sides + (s + 1) % sides;

                triangles[triIndex++] = current;
                triangles[triIndex++] = currentNext;
                triangles[triIndex++] = next;

                triangles[triIndex++] = next;
                triangles[triIndex++] = currentNext;
                triangles[triIndex++] = nextNext;
            }
        }

        _mesh.Clear();
        _mesh.vertices = vertices;
        _mesh.triangles = triangles;
        _mesh.uv = uvs;
        _mesh.RecalculateNormals();
        _mesh.RecalculateBounds();
    }

    Vector3 GetForward(int index)
    {
        if (_points.Count < 2) return Vector3.forward;

        if (index == 0)
            return (_points[1] - _points[0]).normalized;
        else if (index == _points.Count - 1)
            return (_points[index] - _points[index - 1]).normalized;
        else
            return (_points[index + 1] - _points[index - 1]).normalized;
    }

    Vector3 GetPerpendicular(Vector3 forward)
    {
        Vector3 up = Vector3.up;
        if (Mathf.Abs(Vector3.Dot(forward, up)) > 0.99f)
            up = Vector3.right;
        return Vector3.Cross(forward, up).normalized;
    }
}