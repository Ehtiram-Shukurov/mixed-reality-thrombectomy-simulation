using UnityEngine;

// Keeps a world-space panel near the user. It keeps the panel's authored layout
// (left/right and distance from the rig origin) but follows head height and turn.
// It waits until the panel is far from where it should be, then glides back, so
// the panel stays put while you poke it.
public class UIFollower : MonoBehaviour
{
    [Tooltip("Eye height the panels were placed for in the scene, in meters.")]
    public float authoredEyeHeight = 1.6f;
    [Tooltip("Extra height added to the authored layout, in meters.")]
    public float raise = 0.25f;
    [Tooltip("How far the panel can be from its spot before it moves.")]
    public float distanceTolerance = 0.25f;
    public float smoothTime = 0.4f;
    public float turnSpeed = 4f;

    private Transform _head;
    private Vector3 _offset;   // x, z in the rig frame, y relative to head height
    private Vector3 _velocity;
    private bool _ready;
    private bool _moving;

    void Start()
    {
        var rig = FindFirstObjectByType<OVRCameraRig>();
        if (rig == null || rig.centerEyeAnchor == null)
        {
            enabled = false;
            return;
        }

        _head = rig.centerEyeAnchor;
        Vector3 local = rig.transform.InverseTransformPoint(transform.position);
        _offset = new Vector3(local.x, transform.position.y - authoredEyeHeight + raise, local.z);
    }

    private Vector3 TargetPosition()
    {
        var yaw = Quaternion.Euler(0f, _head.eulerAngles.y, 0f);
        Vector3 flat = yaw * new Vector3(_offset.x, 0f, _offset.z);
        return new Vector3(_head.position.x, _head.position.y + _offset.y, _head.position.z) + flat;
    }

    void LateUpdate()
    {
        if (_head == null) return;

        // No tracking yet (or no headset in the editor): leave the panel where it is.
        if (!_ready)
        {
            if (_head.position.y < 0.5f) return;
            transform.position = TargetPosition();
            _ready = true;
        }

        Vector3 target = TargetPosition();
        if (!_moving && Vector3.Distance(transform.position, target) > distanceTolerance)
            _moving = true;

        if (_moving)
        {
            transform.position = Vector3.SmoothDamp(transform.position, target, ref _velocity, smoothTime);
            if (Vector3.Distance(transform.position, target) < 0.02f) _moving = false;
        }

        // Face the user, turning only around the vertical axis.
        Vector3 away = transform.position - _head.position;
        away.y = 0f;
        if (away.sqrMagnitude > 0.0001f)
        {
            var look = Quaternion.LookRotation(away);
            transform.rotation = Quaternion.Slerp(transform.rotation, look, Time.deltaTime * turnSpeed);
        }
    }
}
