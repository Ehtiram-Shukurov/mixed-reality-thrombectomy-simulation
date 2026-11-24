using System.Collections;
using UnityEngine;

// Two ways to place the patient model in front of you:
//  Upright: standing, chest end up, front facing you. Good for an overview.
//  Supine: in the same spot, lying on the back as if it fell backward: front up,
//          chest end away from you.
// The body axes come from the vessel tree itself (long axis = where it spreads most,
// front-back = where it is thinnest), so this works whatever way the model was turned before.
// The model can still be grabbed and moved by hand afterwards.
public class VolumeViewPresets : MonoBehaviour
{
    public enum Pose { Upright, Supine }

    public Transform volume;
    public CenterlineLoader centerlines;
    [Tooltip("How far in front of you the model's center sits.")]
    public float uprightDistance = 0.7f;
    [Tooltip("How far below eye level the model's center sits.")]
    public float uprightBelowEyes = 0.2f;
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

    // Upright rotation with the front of the body facing 'toward' (a flat direction).
    public bool UprightRotation(Vector3 toward, out Quaternion rot)
    {
        rot = Quaternion.identity;
        if (!EnsureAxes()) return false;
        toward = Vector3.ProjectOnPlane(toward, Vector3.up).normalized;
        rot = Quaternion.LookRotation(toward, Vector3.up) * Quaternion.Inverse(Quaternion.LookRotation(_frontLocal, _longLocal));
        return true;
    }

    public void Set(Pose pose) => Set(pose, true);

    public void Set(Pose pose, bool notify)
    {
        if (!EnsureAxes()) return;
        var head = FxUtil.FindHead();
        if (head == null) return;

        Vector3 fwd = Vector3.ProjectOnPlane(head.forward, Vector3.up);
        if (fwd.sqrMagnitude < 1e-4f) fwd = Vector3.ProjectOnPlane(head.up, Vector3.up);
        fwd.Normalize();
        // Both views sit in the same spot in front of you.
        Vector3 pos = head.position + fwd * uprightDistance + Vector3.down * uprightBelowEyes;
        Vector3 wantLong, wantFront;
        if (pose == Pose.Upright)
        {
            wantLong = Vector3.up;
            wantFront = -fwd;
        }
        else
        {
            // As if the upright model fell backward: front up, chest end away from you.
            wantLong = fwd;
            wantFront = Vector3.up;
        }

        // Turn the body frame (front, long) onto the wanted world directions.
        Quaternion body = Quaternion.LookRotation(_frontLocal, _longLocal);
        Quaternion world = Quaternion.LookRotation(wantFront, wantLong);
        Quaternion rot = world * Quaternion.Inverse(body);

        Current = pose;
        if (notify) GuidewireUI.Notify(pose == Pose.Upright
            ? "Upright view: the patient stands in front of you. Grab the model to move it."
            : "Supine view: the patient lies on their back, chest away from you. Grab the model to move it.");
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
