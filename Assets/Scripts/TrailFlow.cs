using UnityEngine;

// Makes light pulses travel along the guidewire trail toward the tip.
// The trail mesh already has a coordinate along its length, so this only moves a texture.
// It uses an unlit material, which is cheaper than the lit one it replaces.
[RequireComponent(typeof(TubeRenderer))]
public class TrailFlow : MonoBehaviour
{
    [Tooltip("One pulse for this many points of the trail.")]
    public float pointsPerPulse = 70f;
    public float pulsesPerSecond = 0.6f;

    private Renderer _renderer;
    private MeshFilter _meshFilter;
    private TubeRenderer _tube;
    private Material _mat;

    void Start()
    {
        _renderer = GetComponent<Renderer>();
        _meshFilter = GetComponent<MeshFilter>();
        _tube = GetComponent<TubeRenderer>();
        _mat = _renderer.material; // own copy
    }

    void LateUpdate()
    {
        if (_mat == null || _meshFilter.sharedMesh == null) return;

        int points = _meshFilter.sharedMesh.vertexCount / Mathf.Max(1, _tube.sides);
        if (points < 2) return;

        // Tile the pulse along the trail so the spacing stays the same as it grows,
        // and slide it toward the tip.
        _mat.SetTextureScale("_BaseMap", new Vector2(1f, points / pointsPerPulse));
        _mat.SetTextureOffset("_BaseMap", new Vector2(0f, -Time.time * pulsesPerSecond));
    }
}
