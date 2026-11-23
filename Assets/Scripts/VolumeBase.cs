using UnityEngine;

// A soft disc with a thin ring under the volume, so the hologram sits in the room
// instead of floating. It follows the volume's position but stays flat.
// One quad drawn before the volume, so it costs almost nothing.
public class VolumeBase : MonoBehaviour
{
    public Transform volume;
    [Tooltip("Ring diameter as a multiple of the volume's size.")]
    public float sizeFactor = 1.25f;
    [Tooltip("Gap between the bottom of the volume and the ring, in meters.")]
    public float gap = 0.02f;
    public Color color = new Color(0.45f, 0.95f, 0.88f, 0.55f);

    private Transform _quad;
    private Material _mat;

    void Start()
    {
        if (volume == null)
        {
            var go = GameObject.Find("dicomBody");
            if (go != null) volume = go.transform;
        }

        var q = FxUtil.MakeQuad("VolumeBaseRing", transform);
        // UI layer so the X-ray monitor camera does not see it.
        int ui = LayerMask.NameToLayer("UI");
        if (ui >= 0) q.layer = ui;
        _quad = q.transform;
        _mat = new Material(Shader.Find("Sprites/Default")) { mainTexture = RingTexture() };
        _mat.color = color;
        // Drawn just before the transparent volume, so the volume blends over it.
        _mat.renderQueue = 2990;
        q.GetComponent<Renderer>().sharedMaterial = _mat;
    }

    void LateUpdate()
    {
        if (volume == null || _quad == null) return;

        // The volume is a unit cube scaled by its transform. Use the world bounds of that cube.
        float size = volume.lossyScale.x;
        Vector3 c = volume.position;
        float halfHeight = 0.5f * size * 0.866f; // a rotated cube can reach a bit lower
        _quad.position = new Vector3(c.x, c.y - halfHeight - gap, c.z);
        _quad.rotation = Quaternion.Euler(90f, 0f, 0f);
        _quad.localScale = Vector3.one * size * sizeFactor;
    }

    // Soft shadow-like disc plus a thin bright ring at the edge.
    private static Texture2D RingTexture()
    {
        const int n = 128;
        var tex = new Texture2D(n, n, TextureFormat.RGBA32, false);
        float c = (n - 1) * 0.5f;
        for (int y = 0; y < n; y++)
        {
            for (int x = 0; x < n; x++)
            {
                float r = Vector2.Distance(new Vector2(x, y), new Vector2(c, c)) / c;
                float disc = Mathf.Clamp01(1f - r) * 0.22f;
                float ring = Mathf.Clamp01(1f - Mathf.Abs(r - 0.93f) / 0.025f);
                float a = Mathf.Clamp01(disc + ring * 0.9f) * (r <= 1f ? 1f : 0f);
                tex.SetPixel(x, y, new Color(1f, 1f, 1f, a));
            }
        }
        tex.wrapMode = TextureWrapMode.Clamp;
        tex.filterMode = FilterMode.Bilinear;
        tex.Apply();
        return tex;
    }
}
