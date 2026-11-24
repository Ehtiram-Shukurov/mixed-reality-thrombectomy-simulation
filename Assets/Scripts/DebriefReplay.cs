using System.Collections.Generic;
using UnityEngine;

// Records the wire's whole route during navigation (wrong branches and pull-backs too) and,
// on the results screen, draws it over the model: green on the route to the clot, amber off it,
// red dots where a dead end or a forced bend happened. A dot replays the route on a loop.
// One small ribbon mesh rebuilt on the CPU plus a few dots, so it costs very little.
public class DebriefReplay : MonoBehaviour
{
    public GuidewireSimulator guidewire;
    public ClotTarget clot;
    public SimulationManager sim;

    [Header("Look")]
    public float width = 0.004f;
    [Tooltip("Seconds for one replay of the route.")]
    public float replaySeconds = 6f;
    [Tooltip("Minimum tip movement (meters, in volume space) before a new point is recorded.")]
    public float minStep = 0.002f;
    public int maxMarkers = 24;

    public Color onTrack = new Color(0.30f, 0.85f, 0.50f, 0.95f);
    public Color offTrack = new Color(0.98f, 0.75f, 0.14f, 0.95f);
    public Color mistake = new Color(0.97f, 0.40f, 0.36f, 1f);

    private readonly List<Vector3> _points = new List<Vector3>();   // volume local space
    private readonly List<bool> _onPath = new List<bool>();
    private readonly List<Vector3> _markers = new List<Vector3>();  // volume local space
    private readonly List<Color> _markerColors = new List<Color>();

    private Transform _volume;
    private Transform _head;
    private Mesh _mesh;
    private MeshRenderer _ribbon;
    private Transform _cursor;
    private Material _cursorMat;
    private readonly List<Transform> _dots = new List<Transform>();
    private readonly List<Material> _dotMats = new List<Material>();
    private SimulationManager.SimPhase _lastPhase = SimulationManager.SimPhase.Idle;

    void Start()
    {
        if (guidewire == null) guidewire = FindFirstObjectByType<GuidewireSimulator>();
        if (clot == null) clot = FindFirstObjectByType<ClotTarget>();
        if (sim == null) sim = FindFirstObjectByType<SimulationManager>();
        _head = FxUtil.FindHead();
        if (guidewire != null)
        {
            _volume = guidewire.centerlineLoader != null ? guidewire.centerlineLoader.volumeTransform : null;
            guidewire.BranchChosen += OnBranch;
            guidewire.DeadEndReached += OnDeadEnd;
            guidewire.Forced += OnForced;
        }

        var go = new GameObject("DebriefRibbon");
        go.transform.SetParent(transform, false);
        _mesh = new Mesh { name = "DebriefRibbon" };
        _mesh.MarkDynamic();
        go.AddComponent<MeshFilter>().sharedMesh = _mesh;
        _ribbon = go.AddComponent<MeshRenderer>();
        _ribbon.sharedMaterial = FxUtil.Overlay(null);
        _ribbon.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        _ribbon.receiveShadows = false;
        _ribbon.enabled = false;

        var c = FxUtil.MakeQuad("ReplayCursor", transform);
        _cursor = c.transform;
        _cursorMat = FxUtil.Overlay(FxUtil.GlowTexture());
        _cursorMat.color = Color.white;
        c.GetComponent<Renderer>().sharedMaterial = _cursorMat;
        c.SetActive(false);
    }

    void OnDestroy()
    {
        if (guidewire == null) return;
        guidewire.BranchChosen -= OnBranch;
        guidewire.DeadEndReached -= OnDeadEnd;
        guidewire.Forced -= OnForced;
    }

    private bool Navigating => sim != null && sim.CurrentPhase == SimulationManager.SimPhase.Phase1_Navigation;

    private void AddMarker(Color color)
    {
        if (!Navigating || _volume == null || _markers.Count >= maxMarkers) return;
        _markers.Add(_volume.InverseTransformPoint(guidewire.wireTip.position));
        _markerColors.Add(color);
    }

    private void OnBranch(CenterlineNode branch)
    {
        if (clot != null && !clot.IsOnPath(branch)) AddMarker(offTrack);
    }
    private void OnDeadEnd() => AddMarker(mistake);
    private void OnForced() => AddMarker(mistake);

    void LateUpdate()
    {
        if (sim == null || guidewire == null || _volume == null) return;
        var phase = sim.CurrentPhase;

        // A new run starts a new recording.
        if (phase == SimulationManager.SimPhase.Phase1_Navigation && _lastPhase != phase) Clear();
        _lastPhase = phase;

        if (phase == SimulationManager.SimPhase.Phase1_Navigation && guidewire.IsReady) Record();

        bool show = phase == SimulationManager.SimPhase.Phase3_Results && _points.Count > 1;
        _ribbon.enabled = show;
        _cursor.gameObject.SetActive(show);
        for (int i = 0; i < _dots.Count; i++) _dots[i].gameObject.SetActive(show && i < _markers.Count);
        if (!show) return;

        BuildRibbon();
        PlaceDots();
        MoveCursor();
    }

    private void Record()
    {
        Vector3 local = _volume.InverseTransformPoint(guidewire.wireTip.position);
        float step = minStep / Mathf.Max(1e-4f, _volume.lossyScale.x);
        if (_points.Count > 0 && (local - _points[_points.Count - 1]).magnitude < step) return;

        bool onPath = clot == null || clot.IsTouched || clot.IsOnPath(guidewire.CurrentNode);
        _points.Add(local);
        _onPath.Add(onPath);
    }

    private void Clear()
    {
        _points.Clear();
        _onPath.Clear();
        _markers.Clear();
        _markerColors.Clear();
    }

    // A flat strip that always faces the viewer, coloured per point.
    private readonly List<Vector3> _v = new List<Vector3>();
    private readonly List<Color> _c = new List<Color>();
    private readonly List<int> _t = new List<int>();
    private void BuildRibbon()
    {
        _v.Clear(); _c.Clear(); _t.Clear();
        Vector3 eye = _head != null ? _head.position : Vector3.zero;
        int n = _points.Count;
        for (int i = 0; i < n; i++)
        {
            Vector3 p = _volume.TransformPoint(_points[i]);
            Vector3 prev = _volume.TransformPoint(_points[Mathf.Max(0, i - 1)]);
            Vector3 next = _volume.TransformPoint(_points[Mathf.Min(n - 1, i + 1)]);
            Vector3 along = next - prev;
            Vector3 side = Vector3.Cross(along, p - eye);
            side = side.sqrMagnitude > 1e-12f ? side.normalized * (width * 0.5f) : Vector3.right * (width * 0.5f);
            _v.Add(p - side); _v.Add(p + side);
            Color col = _onPath[i] ? onTrack : offTrack;
            _c.Add(col); _c.Add(col);
            if (i > 0)
            {
                int a = (i - 1) * 2;
                _t.Add(a); _t.Add(a + 2); _t.Add(a + 1);
                _t.Add(a + 1); _t.Add(a + 2); _t.Add(a + 3);
            }
        }
        _mesh.Clear();
        _mesh.SetVertices(_v);
        _mesh.SetColors(_c);
        _mesh.SetTriangles(_t, 0);
        _mesh.RecalculateBounds();
    }

    private void PlaceDots()
    {
        while (_dots.Count < _markers.Count)
        {
            var q = FxUtil.MakeQuad("DebriefMarker", transform);
            var m = FxUtil.Overlay(FxUtil.GlowTexture());
            q.GetComponent<Renderer>().sharedMaterial = m;
            _dots.Add(q.transform);
            _dotMats.Add(m);
        }
        for (int i = 0; i < _markers.Count; i++)
        {
            _dots[i].position = _volume.TransformPoint(_markers[i]);
            _dots[i].localScale = Vector3.one * width * 5f;
            _dotMats[i].color = _markerColors[i];
            FxUtil.Billboard(_dots[i], _head);
        }
    }

    // A dot that runs along the recorded route, so the order of events is easy to follow.
    private void MoveCursor()
    {
        float t = (Time.time % Mathf.Max(0.5f, replaySeconds)) / Mathf.Max(0.5f, replaySeconds);
        float f = t * (_points.Count - 1);
        int i = Mathf.Clamp(Mathf.FloorToInt(f), 0, _points.Count - 2);
        Vector3 local = Vector3.Lerp(_points[i], _points[i + 1], f - i);
        _cursor.position = _volume.TransformPoint(local);
        _cursor.localScale = Vector3.one * width * 6f;
        FxUtil.Billboard(_cursor, _head);
    }
}
