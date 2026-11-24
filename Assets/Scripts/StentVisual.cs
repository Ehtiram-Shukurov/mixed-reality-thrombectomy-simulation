using UnityEngine;

// A simple mesh-style stent opened across the clot, then carried back with the catheter.
public class StentVisual : MonoBehaviour
{
    public float length = 0.03f;
    public float diameter = 0.012f;
    public float growSeconds = 0.4f;
    public Color color = new Color(0.18f, 0.83f, 0.75f, 0.9f);

    [Tooltip("Size of the metal end markers that show on the X-ray monitor, in meters.")]
    public float markerSize = 0.006f;

    private CatheterSimulator _catheter;
    private GameObject _mesh;
    private float _t = -1f;
    private float _behind;
    // The stent's struts barely show on X-ray; its metal end markers do. These two dots
    // only appear on the X-ray monitor.
    private readonly XRayOnly[] _markers = new XRayOnly[2];
    private XRayMonitor _monitor;

    void Awake()
    {
        _mesh = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        _mesh.name = "StentMesh";
        Destroy(_mesh.GetComponent<Collider>());
        _mesh.transform.SetParent(transform, false);

        var mat = new Material(Shader.Find("Sprites/Default"));
        mat.color = color;
        mat.mainTexture = MakeLattice();
        mat.mainTextureScale = new Vector2(4f, 3f);
        _mesh.GetComponent<Renderer>().sharedMaterial = mat;
        _mesh.SetActive(false);

        var markerMat = FxUtil.Overlay(FxUtil.GlowTexture());
        markerMat.color = Color.white; // fully absorbing: dark on the monitor
        for (int i = 0; i < 2; i++)
        {
            var q = FxUtil.MakeQuad("StentMarker" + i, null);
            q.GetComponent<Renderer>().sharedMaterial = markerMat;
            q.transform.localScale = Vector3.one * markerSize;
            _markers[i] = q.AddComponent<XRayOnly>();
        }
    }

    // A diamond grid: thin bright lines on a nearly clear background, so it reads like a stent mesh.
    private static Texture2D MakeLattice()
    {
        const int size = 64;
        var tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                bool line = ((x + y) % 16) < 2 || ((x - y + size * 4) % 16) < 2;
                tex.SetPixel(x, y, new Color(1f, 1f, 1f, line ? 1f : 0.08f));
            }
        }
        tex.wrapMode = TextureWrapMode.Repeat;
        tex.filterMode = FilterMode.Bilinear;
        tex.Apply();
        return tex;
    }

    void Start()
    {
        _catheter = FindFirstObjectByType<CatheterSimulator>();
    }

    // behind: how far the middle of the stent sits behind the catheter tip, along the path.
    // The stent is opened across the clot, so this is where the clot is.
    public void Deploy(float behind)
    {
        _behind = behind;
        _t = 0f;
        _mesh.SetActive(true);
        Place(0f);
    }

    public void Hide()
    {
        _t = -1f;
        _mesh.SetActive(false);
        foreach (var m in _markers) if (m != null) m.wanted = false;
    }

    void LateUpdate()
    {
        if (_t < 0f) return;
        _t += Time.deltaTime;
        Place(Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(_t / growSeconds)));
    }

    private void Place(float grow)
    {
        if (_catheter == null || _catheter.catheterTipTransform == null) return;
        transform.position = _catheter.ArcPointBehindTip(_behind, out Vector3 dir);
        transform.rotation = Quaternion.FromToRotation(Vector3.up, dir);
        // The Unity cylinder is 2 units tall and 1 unit wide.
        float d = diameter * grow;
        _mesh.transform.localScale = new Vector3(d, length * 0.5f, d);

        // End markers at both ends of the stent, facing the X-ray camera.
        if (_monitor == null) _monitor = FindFirstObjectByType<XRayMonitor>();
        var cam = _monitor != null ? _monitor.MonitorCamera : null;
        for (int i = 0; i < 2; i++)
        {
            if (_markers[i] == null) continue;
            var mt = _markers[i].transform;
            mt.position = transform.position + dir * (i == 0 ? 0.5f : -0.5f) * length;
            if (cam != null) mt.rotation = Quaternion.LookRotation(mt.position - cam.transform.position, cam.transform.up);
            _markers[i].wanted = true;
        }
    }
}
