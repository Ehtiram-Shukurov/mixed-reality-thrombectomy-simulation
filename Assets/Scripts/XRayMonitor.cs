using UnityEngine;
using UnityEngine.Rendering;
using TMPro;

// A cath-lab style monitor. A camera looks at the volume from one fixed side, like a C-arm,
// and its image shows on a floating screen. It only renders while X-ray or dye is on.
// Between runs the screen keeps the last image, as real systems do ("last image hold").
public class XRayMonitor : MonoBehaviour
{
    [Header("References")]
    public FluoroController fluoro;
    public CenterlineLoader centerlineLoader;

    [Header("Screen")]
    [Tooltip("Custom/XRayMonitor. Set here so the shader is included in the build.")]
    public Shader monitorShader;
    public float screenSize = 0.22f;
    public int resolution = 384;
    [Tooltip("Field of view of the C-arm camera, in degrees.")]
    public float fieldOfView = 48f;
    [Tooltip("Camera distance from the volume center, in volume sizes.")]
    public float distance = 1.25f;

    private Camera _cam;
    private RenderTexture _rt;
    private Material _screenMat;
    private TextMeshPro _title;
    private TextMeshPro _status;
    private bool _hasImage;
    private bool _axesReady;
    private Vector3 _viewLocal = Vector3.back;
    private Vector3 _upLocal = Vector3.up;
    private Transform _volume;

    // The monitor draws its own X-ray copy of the volume, so dye can show here while the
    // main view keeps its own render mode.
    private DicomVolumeLoader _loader;
    private Renderer _volumeRenderer;
    private MeshRenderer _proxy;
    private Material _proxyMat;
    private VesselMeshView _vesselMesh;
    private bool _volumeWasOn, _meshWasOn;
    public Camera MonitorCamera => _cam;
    public RenderTexture Image => _rt;

    void Awake()
    {
        int uiLayer = LayerMask.NameToLayer("UI");
        if (uiLayer < 0) uiLayer = 0;

        // Dark bezel behind the screen.
        var bezel = FxUtil.MakeQuad("Bezel", transform);
        bezel.layer = uiLayer;
        bezel.transform.localPosition = new Vector3(0f, 0.012f, 0.002f);
        bezel.transform.localScale = new Vector3(screenSize + 0.016f, screenSize + 0.05f, 1f);
        var bezelMat = new Material(Shader.Find("Unlit/Color")) { color = new Color(0.05f, 0.07f, 0.09f, 1f) };
        bezel.GetComponent<Renderer>().sharedMaterial = bezelMat;

        // The image.
        _rt = new RenderTexture(resolution, resolution, 16, RenderTextureFormat.ARGB32) { name = "XRayMonitorRT" };
        _rt.Create();
        var screen = FxUtil.MakeQuad("Screen", transform);
        screen.layer = uiLayer;
        screen.transform.localScale = new Vector3(screenSize, screenSize, 1f);
        var shader = monitorShader != null ? monitorShader : Shader.Find("Custom/XRayMonitor");
        _screenMat = new Material(shader) { mainTexture = _rt };
        screen.GetComponent<Renderer>().sharedMaterial = _screenMat;

        _title = MakeText("Title", new Vector3(-screenSize * 0.5f, screenSize * 0.5f + 0.02f, -0.001f),
                          TextAlignmentOptions.Left, uiLayer);
        _title.text = "X-RAY";
        _title.color = new Color(0.55f, 0.62f, 0.70f, 1f);
        _status = MakeText("Status", new Vector3(screenSize * 0.5f, screenSize * 0.5f + 0.02f, -0.001f),
                           TextAlignmentOptions.Right, uiLayer);

        // The C-arm camera. Not an XR camera: it renders to the texture only.
        var camGo = new GameObject("CArmCamera");
        camGo.transform.SetParent(transform, false);
        _cam = camGo.AddComponent<Camera>();
        _cam.targetTexture = _rt;
        _cam.stereoTargetEye = StereoTargetEyeMask.None;
        _cam.clearFlags = CameraClearFlags.SolidColor;
        _cam.backgroundColor = Color.black;
        int mask = ~(1 << uiLayer);
        int overlayUi = LayerMask.NameToLayer("Overlay UI");
        if (overlayUi >= 0) mask &= ~(1 << overlayUi);
        _cam.cullingMask = mask;
        _cam.fieldOfView = fieldOfView;
        _cam.nearClipPlane = 0.01f;
        _cam.farClipPlane = 5f;
        _cam.allowHDR = false;
        _cam.allowMSAA = false;
        _cam.depth = -10;
        _cam.enabled = false;
    }

    private TextMeshPro MakeText(string name, Vector3 localPos, TextAlignmentOptions align, int layer)
    {
        var go = new GameObject(name);
        go.transform.SetParent(transform, false);
        go.layer = layer;
        var t = go.AddComponent<TextMeshPro>();
        var rt = t.rectTransform;
        rt.sizeDelta = new Vector2(screenSize * 0.5f, 0.02f);
        rt.pivot = new Vector2(align == TextAlignmentOptions.Right ? 1f : 0f, 0.5f);
        rt.localPosition = localPos;
        t.alignment = align;
        t.enableAutoSizing = true;
        t.fontSizeMin = 0.01f;
        t.fontSizeMax = 0.14f;
        t.textWrappingMode = TextWrappingModes.NoWrap;
        return t;
    }

    void Start()
    {
        if (fluoro == null) fluoro = FindFirstObjectByType<FluoroController>();
        if (centerlineLoader == null) centerlineLoader = FindFirstObjectByType<CenterlineLoader>();
        if (centerlineLoader != null) _volume = centerlineLoader.volumeTransform;
        _loader = FindFirstObjectByType<DicomVolumeLoader>();
        _vesselMesh = FindFirstObjectByType<VesselMeshView>();
        BuildProxy();
        RenderPipelineManager.beginCameraRendering += OnBeginCamera;
        RenderPipelineManager.endCameraRendering += OnEndCamera;
        SetStatus();
    }

    private void BuildProxy()
    {
        if (_loader == null) return;
        _volumeRenderer = _loader.GetComponent<Renderer>();
        var mf = _loader.GetComponent<MeshFilter>();
        if (_volumeRenderer == null || mf == null) return;

        // Same cube, same place, no collider. Only switched on while the monitor camera draws.
        var go = new GameObject("XRayProxy");
        go.transform.SetParent(_loader.transform, false);
        go.AddComponent<MeshFilter>().sharedMesh = mf.sharedMesh;
        _proxy = go.AddComponent<MeshRenderer>();
        _proxyMat = new Material(_loader.volumeMaterial);
        _proxy.sharedMaterial = _proxyMat;
        _proxy.shadowCastingMode = ShadowCastingMode.Off;
        _proxy.receiveShadows = false;
        _proxy.enabled = false;
    }

    // Swap what is drawn for the monitor camera only. Culling happens after this call.
    private void OnBeginCamera(ScriptableRenderContext ctx, Camera cam)
    {
        if (cam != _cam || _proxy == null) return;

        // Copy the current volume settings (textures, thresholds, hand clipping), then force X-ray.
        _proxyMat.CopyPropertiesFromMaterial(_loader.volumeMaterial);
        _proxyMat.SetFloat("_RenderMode", fluoro != null ? fluoro.fluoroRenderMode : 6);
        _proxyMat.SetFloat("_Contrast", fluoro != null ? fluoro.ContrastLevel : 1f);
        _proxyMat.SetFloat("_Steps", Mathf.Max(48f, _loader.volumeMaterial.GetFloat("_Steps")));

        _volumeWasOn = _volumeRenderer.enabled;
        _volumeRenderer.enabled = false;
        var mr = _vesselMesh != null ? _vesselMesh.MeshRenderer : null;
        _meshWasOn = mr != null && mr.enabled;
        if (mr != null) mr.enabled = false;
        _proxy.enabled = true;
    }

    private void OnEndCamera(ScriptableRenderContext ctx, Camera cam)
    {
        if (cam != _cam || _proxy == null) return;
        _proxy.enabled = false;
        _volumeRenderer.enabled = _volumeWasOn;
        var mr = _vesselMesh != null ? _vesselMesh.MeshRenderer : null;
        if (mr != null) mr.enabled = _meshWasOn;
    }

    // Look along the axis where the vessel tree is thinnest, so the projection shows it spread out,
    // with its long axis up and the head-end (high Z, where the clot is placed) at the top.
    private void PickAxes()
    {
        if (centerlineLoader == null || !centerlineLoader.IsLoaded || _volume == null) return;

        var nodes = centerlineLoader.AllNodes;
        Vector3 mean = Vector3.zero;
        var local = new Vector3[nodes.Count];
        for (int i = 0; i < nodes.Count; i++)
        {
            local[i] = _volume.InverseTransformPoint(centerlineLoader.GetNodeWorldPosition(nodes[i]));
            mean += local[i];
        }
        mean /= Mathf.Max(1, nodes.Count);

        Vector3 spread = Vector3.zero;
        foreach (var p in local) { Vector3 d = p - mean; spread += Vector3.Scale(d, d); }

        int minAxis = 0, maxAxis = 0;
        for (int k = 1; k < 3; k++)
        {
            if (spread[k] < spread[minAxis]) minAxis = k;
            if (spread[k] > spread[maxAxis]) maxAxis = k;
        }
        if (minAxis == maxAxis) maxAxis = (minAxis + 1) % 3;

        _viewLocal = Vector3.zero; _viewLocal[minAxis] = -1f;
        _upLocal = Vector3.zero; _upLocal[maxAxis] = 1f;

        // Put the head-end at the top of the screen.
        CenterlineNode hi = null, lo = null;
        foreach (var n in nodes)
        {
            if (hi == null || n.normalizedPosition.z > hi.normalizedPosition.z) hi = n;
            if (lo == null || n.normalizedPosition.z < lo.normalizedPosition.z) lo = n;
        }
        if (hi != null && lo != null)
        {
            float a = _volume.InverseTransformPoint(centerlineLoader.GetNodeWorldPosition(hi))[maxAxis];
            float b = _volume.InverseTransformPoint(centerlineLoader.GetNodeWorldPosition(lo))[maxAxis];
            if (a < b) _upLocal = -_upLocal;
        }
        _axesReady = true;
    }

    void LateUpdate()
    {
        if (!_axesReady) PickAxes();

        bool imaging = fluoro != null && fluoro.IsActive;
        if (imaging && _volume != null)
        {
            Vector3 center = _volume.position;
            float size = _volume.lossyScale.x;
            Vector3 view = _volume.TransformDirection(_viewLocal).normalized;
            Vector3 up = _volume.TransformDirection(_upLocal).normalized;
            _cam.transform.position = center - view * size * distance;
            _cam.transform.rotation = Quaternion.LookRotation(view, up);
            // Only see a slab around the volume, so panels or hands behind it stay out of the image.
            _cam.nearClipPlane = Mathf.Max(0.01f, size * (distance - 0.8f));
            _cam.farClipPlane = size * (distance + 0.8f);
            _hasImage = true;
        }
        _cam.enabled = imaging;
        _screenMat.SetFloat("_Dim", imaging ? 0f : 1f);
        SetStatus();
    }

    private void SetStatus()
    {
        if (_status == null) return;
        bool imaging = fluoro != null && fluoro.IsActive;
        if (imaging && fluoro.ContrastActive) { _status.text = "● DYE"; _status.color = new Color(0.98f, 0.75f, 0.14f); }
        else if (imaging) { _status.text = "● LIVE"; _status.color = new Color(0.97f, 0.40f, 0.36f); }
        else if (_hasImage) { _status.text = "LAST IMAGE HOLD"; _status.color = new Color(0.55f, 0.62f, 0.70f); }
        else { _status.text = "NO IMAGE"; _status.color = new Color(0.40f, 0.45f, 0.50f); }
    }

    void OnDestroy()
    {
        RenderPipelineManager.beginCameraRendering -= OnBeginCamera;
        RenderPipelineManager.endCameraRendering -= OnEndCamera;
        if (_rt != null) _rt.Release();
    }
}
