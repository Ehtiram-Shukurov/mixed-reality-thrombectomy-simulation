using System.Collections.Generic;
using UnityEngine;

// At a fork, draws the start of every branch as a short line, so you can see where each one goes.
// The branch your twist selects is bright. It turns green once the tip is aligned and held.
// Each line is a few points, so this costs almost nothing.
public class BranchPreview : MonoBehaviour
{
    public GuidewireSimulator guidewire;
    [Tooltip("How many nodes of each branch to draw.")]
    public int nodesAhead = 14;
    public float width = 0.0035f;

    public Color selectedColor = new Color(0.98f, 0.75f, 0.14f, 1f);
    public Color alignedColor = new Color(0.30f, 0.85f, 0.50f, 1f);
    public Color otherColor = new Color(1f, 1f, 1f, 0.3f);

    [Header("Bent tip")]
    [Tooltip("Length of the bent wire tip shown at a fork, in meters.")]
    public float tipLength = 0.012f;
    public float tipWidth = 0.003f;
    public Color tipColor = new Color(0.85f, 0.97f, 1f, 1f);

    private readonly List<LineRenderer> _lines = new List<LineRenderer>();
    private LineRenderer _tip;
    private Material _mat;
    private readonly Vector3[] _tipPoints = new Vector3[8];

    private LineRenderer MakeLine(string name)
    {
        var go = new GameObject(name);
        go.transform.SetParent(transform, false);
        var lr = go.AddComponent<LineRenderer>();
        lr.sharedMaterial = _mat;
        lr.useWorldSpace = true;
        lr.alignment = LineAlignment.View;
        lr.numCapVertices = 4;
        lr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        lr.receiveShadows = false;
        lr.enabled = false;
        return lr;
    }

    void Start()
    {
        if (guidewire == null) guidewire = FindFirstObjectByType<GuidewireSimulator>();
        _mat = FxUtil.Overlay(null);
        for (int i = 0; i < GuidewireSimulator.MaxDial; i++) _lines.Add(MakeLine("Branch" + i));
        _tip = MakeLine("BentTip");
    }

    void LateUpdate()
    {
        if (guidewire == null || !guidewire.IsReady || !guidewire.IsAtFork || guidewire.CurrentNode == null)
        {
            foreach (var l in _lines) l.enabled = false;
            if (_tip != null) _tip.enabled = false;
            return;
        }

        DrawBentTip();

        var fork = guidewire.CurrentNode;
        var loader = guidewire.centerlineLoader;
        int count = Mathf.Min(fork.branches.Count, _lines.Count);
        bool aligned = guidewire.IsAligned && guidewire.HoldProgress >= 1f;

        for (int i = 0; i < _lines.Count; i++)
        {
            var lr = _lines[i];
            if (i >= count) { lr.enabled = false; continue; }

            var points = new List<Vector3> { loader.GetNodeWorldPosition(fork) };
            var n = fork.branches[i];
            for (int k = 0; k < nodesAhead && n != null; k++)
            {
                points.Add(loader.GetNodeWorldPosition(n));
                n = n.branches.Count > 0 ? n.branches[0] : null;
            }

            bool selected = i == guidewire.SelectedBranch;
            Color c = !selected ? otherColor : (aligned ? alignedColor : selectedColor);
            lr.startColor = lr.endColor = c;
            lr.widthMultiplier = selected ? width * 1.6f : width;
            lr.positionCount = points.Count;
            lr.SetPositions(points.ToArray());
            lr.enabled = true;
        }
    }

    // A short curved hook at the wire tip: it runs on along the vessel, then bends toward
    // where the twist points. Like the shaped tip of a real microwire.
    private void DrawBentTip()
    {
        Vector3 p0 = guidewire.wireTip.position;
        Vector3 axis = guidewire.IncomingDirection;
        Vector3 bend = guidewire.BendDirection;
        Vector3 p1 = p0 + axis * tipLength * 0.7f;
        Vector3 p2 = p0 + axis * tipLength * 0.55f + bend * tipLength;

        for (int k = 0; k < _tipPoints.Length; k++)
        {
            float t = k / (float)(_tipPoints.Length - 1);
            float u = 1f - t;
            _tipPoints[k] = u * u * p0 + 2f * u * t * p1 + t * t * p2;
        }

        bool aligned = guidewire.IsAligned && guidewire.HoldProgress >= 1f;
        _tip.startColor = _tip.endColor = aligned ? alignedColor : (guidewire.IsAligned ? selectedColor : tipColor);
        _tip.widthMultiplier = tipWidth;
        _tip.positionCount = _tipPoints.Length;
        _tip.SetPositions(_tipPoints);
        _tip.enabled = true;
    }
}
