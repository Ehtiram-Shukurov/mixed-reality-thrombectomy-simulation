using UnityEngine;

// Grounds the hologram: a soft dark shadow under the volume and a fine instrument-style
// ring with tick marks that turns slowly. It follows the volume's position but stays flat.
// Two quads drawn before the volume, so it costs almost nothing.
public class VolumeBase : MonoBehaviour
{
    public Transform volume;
    [Tooltip("Ring diameter as a multiple of the volume's size.")]
    public float sizeFactor = 1.3f;
    [Tooltip("Gap between the bottom of the volume and the ring, in meters.")]
    public float gap = 0.015f;
    public Color ringColor = new Color(0.78f, 0.95f, 1f, 0.55f);
    [Range(0f, 1f)] public float shadowStrength = 0.45f;
    [Tooltip("Degrees per second.")]
    public float spinSpeed = 3f;

    private Transform _ring, _shadow;
    private float _angle;

    void Start()
    {
        if (volume == null)
        {
            var go = GameObject.Find("dicomBody");
            if (go != null) volume = go.transform;
        }

        int ui = LayerMask.NameToLayer("UI"); // keeps it out of the X-ray monitor camera

        _shadow = MakeLayer("VolumeShadow", ShadowTexture(), new Color(0f, 0f, 0f, shadowStrength), 2989, ui);
        _ring = MakeLayer("VolumeRing", RingTexture(), ringColor, 2990, ui);
    }

    private Transform MakeLayer(string name, Texture2D tex, Color color, int queue, int layer)
    {
        var q = FxUtil.MakeQuad(name, transform);
        if (layer >= 0) q.layer = layer;
        var mat = new Material(Shader.Find("Sprites/Default")) { mainTexture = tex, color = color };
        // Drawn just before the transparent volume, so the volume blends over it.
        mat.renderQueue = queue;
        q.GetComponent<Renderer>().sharedMaterial = mat;
        return q.transform;
    }

    void LateUpdate()
    {
        if (volume == null || _ring == null) return;

        float size = volume.lossyScale.x;
        Vector3 c = volume.position;
        float y = c.y - 0.5f * size * 0.866f - gap; // a rotated cube can reach a bit lower
        _angle = (_angle + spinSpeed * Time.deltaTime) % 360f;

        _shadow.position = new Vector3(c.x, y - 0.001f, c.z);
        _shadow.rotation = Quaternion.Euler(90f, 0f, 0f);
        _shadow.localScale = Vector3.one * size * sizeFactor * 0.95f;

        _ring.position = new Vector3(c.x, y, c.z);
        _ring.rotation = Quaternion.Euler(90f, _angle, 0f);
        _ring.localScale = Vector3.one * size * sizeFactor;
    }

    // Dark in the middle, fading out, like a contact shadow.
    private static Texture2D ShadowTexture()
    {
        const int n = 128;
        var tex = new Texture2D(n, n, TextureFormat.RGBA32, false);
        float c = (n - 1) * 0.5f;
        for (int y = 0; y < n; y++)
            for (int x = 0; x < n; x++)
            {
                float r = Vector2.Distance(new Vector2(x, y), new Vector2(c, c)) / c;
                float a = Mathf.Clamp01(1f - r);
                a = a * a * (3f - 2f * a);
                tex.SetPixel(x, y, new Color(1f, 1f, 1f, a));
            }
        tex.wrapMode = TextureWrapMode.Clamp;
        tex.filterMode = FilterMode.Bilinear;
        tex.Apply();
        return tex;
    }

    // A thin outer ring, a fainter inner ring, and tick marks between them.
    private static Texture2D RingTexture()
    {
        const int n = 512;
        var tex = new Texture2D(n, n, TextureFormat.RGBA32, true);
        float c = (n - 1) * 0.5f;
        for (int y = 0; y < n; y++)
        {
            for (int x = 0; x < n; x++)
            {
                Vector2 d = new Vector2(x - c, y - c);
                float r = d.magnitude / c;
                float px = 1f / c; // one pixel in radius units

                float outer = Band(r, 0.975f, 1.5f * px);
                float inner = Band(r, 0.86f, 1.0f * px) * 0.45f;

                // 72 ticks, every 6th one longer.
                float ang = Mathf.Atan2(d.y, d.x) * Mathf.Rad2Deg + 180f;
                float slot = ang / 5f;
                int idx = Mathf.RoundToInt(slot);
                float angDist = Mathf.Abs(slot - idx) * 5f * Mathf.Deg2Rad * r; // arc distance
                bool major = idx % 6 == 0;
                float tickStart = major ? 0.90f : 0.93f;
                float tick = (r > tickStart && r < 0.965f) ? Mathf.Clamp01(1f - angDist / (1.2f * px)) : 0f;
                tick *= major ? 0.9f : 0.5f;

                float a = Mathf.Clamp01(outer + inner + tick);
                tex.SetPixel(x, y, new Color(1f, 1f, 1f, a));
            }
        }
        tex.wrapMode = TextureWrapMode.Clamp;
        tex.filterMode = FilterMode.Trilinear;
        tex.Apply(true);
        return tex;
    }

    private static float Band(float r, float center, float halfWidth)
        => Mathf.Clamp01(1f - Mathf.Abs(r - center) / halfWidth);
}
