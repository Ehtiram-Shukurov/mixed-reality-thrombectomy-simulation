using System.Collections;
using UnityEngine;

// Two ways to place the patient model in front of you:
//  Upright: standing, as in the anatomical position, good for an overview.
//  Supine: lying on the back at table height across in front of you, as on a cath-lab table,
//          head end to your left.
// The model can still be grabbed and moved by hand afterwards.
public class VolumeViewPresets : MonoBehaviour
{
    public enum Pose { Upright, Supine }

    public Transform volume;
    [Header("Upright")]
    public float uprightDistance = 0.55f;
    [Tooltip("How far below eye level the model's center sits.")]
    public float uprightBelowEyes = 0.35f;
    [Header("Supine")]
    public float supineDistance = 0.5f;
    [Tooltip("How far below eye level the table sits.")]
    public float supineBelowEyes = 0.65f;
    public float moveSeconds = 0.6f;

    public Pose Current { get; private set; } = Pose.Upright;

    private Quaternion _uprightRelative;
    private bool _ready;
    private Coroutine _move;

    void Start()
    {
        if (volume == null)
        {
            var go = GameObject.Find("dicomBody");
            if (go != null) volume = go.transform;
        }
        if (volume == null) return;
        // The scene places the model upright, facing a user who looks along +Z.
        _uprightRelative = volume.rotation;
        _ready = true;
    }

    // Hooked to the View button on the menu (On Value Changed, dynamic bool).
    public void Toggle(bool _) => Set(Current == Pose.Upright ? Pose.Supine : Pose.Upright);

    public void Set(Pose pose)
    {
        if (!_ready) return;
        var head = FxUtil.FindHead();
        if (head == null) return;

        float yaw = head.eulerAngles.y;
        var facing = Quaternion.Euler(0f, yaw, 0f);
        Vector3 fwd = facing * Vector3.forward;

        Vector3 pos;
        Quaternion rot;
        if (pose == Pose.Upright)
        {
            pos = head.position + fwd * uprightDistance + Vector3.down * uprightBelowEyes;
            rot = facing * _uprightRelative;
        }
        else
        {
            // Tip the model back so its front faces up, then turn it so the head end is to the left.
            pos = head.position + fwd * supineDistance + Vector3.down * supineBelowEyes;
            rot = facing * Quaternion.Euler(0f, -90f, 0f) * Quaternion.Euler(90f, 0f, 0f) * _uprightRelative;
        }

        Current = pose;
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
