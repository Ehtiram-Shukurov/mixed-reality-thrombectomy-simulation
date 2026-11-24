using System.Collections;
using UnityEngine;

// Two ways to place the patient model in front of you:
//  Upright: standing, chest end up, front facing you. Good for an overview.
//  Supine: lying on the back at table height across in front of you, as on a cath-lab table,
//          chest end to your left (you stand at the patient's right side).
// The body axes come from the vessel tree itself (long axis = where it spreads most,
// front-back = where it is thinnest), so this works whatever way the model was turned before.
// The model can still be grabbed and moved by hand afterwards.
public class VolumeViewPresets : MonoBehaviour
{
    public enum Pose { Upright, Supine }

    public Transform volume;
    public CenterlineLoader centerlines;
    [Header("Upright")]
    public float uprightDistance = 0.55f;
    [Tooltip("How far below eye level the model's center sits.")]
    public float uprightBelowEyes = 0.35f;
    [Header("Supine")]
    public float supineDistance = 0.5f;
    [Tooltip("How far below eye level the table sits.")]
    public float supineBelowEyes = 0.65f;
    public float moveSeconds = 0.6f;
    [Tooltip("Turn on if the model shows its back instead of its front.")]
    public bool flipFront;

    // The scene starts with the model lying down.
    public Pose Current { get; private set; } = Pose.Supine;

    private Vector3 _longLocal;   // toward the chest end
    private Vector3 _frontLocal;  // toward the front of the body
    private bool _axesReady;
    private Coroutine _move;

    void Start()
    {
        if (volume == null)
        {
            var go = GameObject.Find("dicomBody");
            if (go != null) volume = go.transform;
        }
        if (centerlines == null) centerlines = FindFirstObjectByType<CenterlineLoader>();
    }

    private bool EnsureAxes()
    {
        if (_axesReady) return true;
        if (volume == null || centerlines == null || !centerlines.IsLoaded) return false;

        var nodes = centerlines.AllNodes;
        var local = new Vector3[nodes.Count];
        Vector3 mean = Vector3.zero;
        for (int i = 0; i < nodes.Count; i++)
        {
            local[i] = volume.InverseTransformPoint(centerlines.GetNodeWorldPosition(nodes[i]));
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

        // Long axis points to the chest end (highest normalized Z, where the tree meets the heart).
        int top = 0;
        for (int i = 1; i < nodes.Count; i++)
            if (nodes[i].normalizedPosition.z > nodes[top].normalizedPosition.z) top = i;
        _longLocal = Vector3.zero;
        _longLocal[maxAxis] = local[top][maxAxis] >= mean[maxAxis] ? 1f : -1f;

        // The big vessels run in front of the spine, a little behind the middle of the body,
        // so the front is the side away from the vessels' average. Not checked on the scan itself.
        _frontLocal = Vector3.zero;
        _frontLocal[minAxis] = mean[minAxis] <= 0f ? 1f : -1f;
        if (flipFront) _frontLocal = -_frontLocal;

        _axesReady = true;
        return true;
    }

    // Hooked to the View button on the menu (On Value Changed, dynamic bool).
    public void Toggle(bool _) => Set(Current == Pose.Upright ? Pose.Supine : Pose.Upright);

    public void Set(Pose pose)
    {
        if (!EnsureAxes()) return;
        var head = FxUtil.FindHead();
        if (head == null) return;

        Vector3 fwd = Vector3.ProjectOnPlane(head.forward, Vector3.up);
        if (fwd.sqrMagnitude < 1e-4f) fwd = Vector3.ProjectOnPlane(head.up, Vector3.up);
        fwd.Normalize();
        Vector3 right = Vector3.Cross(Vector3.up, fwd);

        Vector3 pos, wantLong, wantFront;
        if (pose == Pose.Upright)
        {
            pos = head.position + fwd * uprightDistance + Vector3.down * uprightBelowEyes;
            wantLong = Vector3.up;
            wantFront = -fwd;
        }
        else
        {
            pos = head.position + fwd * supineDistance + Vector3.down * supineBelowEyes;
            wantLong = -right;
            wantFront = Vector3.up;
        }

        // Turn the body frame (front, long) onto the wanted world directions.
        Quaternion body = Quaternion.LookRotation(_frontLocal, _longLocal);
        Quaternion world = Quaternion.LookRotation(wantFront, wantLong);
        Quaternion rot = world * Quaternion.Inverse(body);

        Current = pose;
        GuidewireUI.Notify(pose == Pose.Upright
            ? "Upright view: the patient stands in front of you. Grab the model to move it."
            : "Supine view: the patient lies on a table, chest to your left, like in the cath lab.");
        if (_move != null) StopCoroutine(_move);
        _move = StartCoroutine(MoveTo(pos, rot));
    }

    private IEnumerator MoveTo(Vector3 pos, Quaternion rot)
    {
        Vector3 p0 = volume.position;
        Quaternion r0 = volume.rotation;
        for (float t = 0f; t < 1f; t += Time.deltaTime / Mathf.Max(0.05f, moveSeconds))
        {
            float s = Mathf.SmoothStep(0f, 1f, t);
            volume.SetPositionAndRotation(Vector3.Lerp(p0, pos, s), Quaternion.Slerp(r0, rot, s));
            yield return null;
        }
        volume.SetPositionAndRotation(pos, rot);
        _move = null;
    }
}
