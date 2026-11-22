using UnityEngine;
using UnityEngine.UI;

// Keeps a world-space panel in front of the user, facing them.
// The offset is for the panel's visual center, measured from the head: x right, y up, z forward.
// The panel waits until it is far from that spot, then glides back. It does not move while
// a hand is close to it, so you can reach it.
public class UIFollower : MonoBehaviour
{
    [Header("Placement (meters, relative to the head)")]
    [Tooltip("Where the center of the panel sits: x right, y up, z forward.")]
    public Vector3 viewOffset = new Vector3(-0.28f, -0.2f, 0.55f);

    [Header("Follow")]
    [Tooltip("How far the panel can be from its spot before it moves.")]
    public float distanceTolerance = 0.4f;
    public float smoothTime = 0.9f;
    public float turnSpeed = 2f;
    [Tooltip("The panel stays put while a hand is this close to it.")]
    public float freezeHandDistance = 0.25f;

    private Transform _head, _leftHand, _rightHand;
    private Vector3 _centerInRootFrame; // panel center, as an offset from this object in its own rotation
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
        _leftHand = rig.leftHandAnchor;
        _rightHand = rig.rightHandAnchor;
        _centerInRootFrame = MeasureCenter();
    }

    // The root object is not where the panel looks centered (the menu root sits to one side),
    // so find the middle of everything that is drawn.
    private Vector3 MeasureCenter()
    {
        var corners = new Vector3[4];
        bool any = false;
        var bounds = new Bounds(transform.position, Vector3.zero);
        foreach (var g in GetComponentsInChildren<Graphic>(false))
        {
            if (!g.isActiveAndEnabled) continue;
            g.rectTransform.GetWorldCorners(corners);
            foreach (var c in corners)
            {
                if (!any) { bounds = new Bounds(c, Vector3.zero); any = true; }
                else bounds.Encapsulate(c);
            }
        }
        if (!any) return Vector3.zero;
        return Quaternion.Inverse(transform.rotation) * (bounds.center - transform.position);
    }

    private Vector3 DesiredCenter()
    {
        var yaw = Quaternion.Euler(0f, _head.eulerAngles.y, 0f);
        return _head.position + yaw * viewOffset;
    }

    private Quaternion Facing(Vector3 center)
    {
        Vector3 away = center - _head.position;
        away.y = 0f;
        return away.sqrMagnitude > 0.0001f ? Quaternion.LookRotation(away) : transform.rotation;
    }

    private bool HandNear()
    {
        Vector3 center = transform.position + transform.rotation * _centerInRootFrame;
        if (_leftHand != null && Vector3.Distance(_leftHand.position, center) < freezeHandDistance) return true;
        if (_rightHand != null && Vector3.Distance(_rightHand.position, center) < freezeHandDistance) return true;
        return false;
    }

    void LateUpdate()
    {
        if (_head == null) return;

        Vector3 center = DesiredCenter();
        Quaternion face = Facing(center);
        Vector3 targetRoot = center - face * _centerInRootFrame;

        // No tracking yet (or no headset in the editor): leave the panel where it is.
        if (!_ready)
        {
            if (_head.position.y < 0.5f) return;
            // Measure now, not in Start: the grid layout has settled by this point.
            _centerInRootFrame = MeasureCenter();
            targetRoot = center - face * _centerInRootFrame;
            transform.SetPositionAndRotation(targetRoot, face);
            _ready = true;
            return;
        }

        if (HandNear())
        {
            _moving = false;
            _velocity = Vector3.zero;
            return;
        }

        if (!_moving && Vector3.Distance(transform.position, targetRoot) > distanceTolerance)
            _moving = true;

        if (_moving)
        {
            transform.position = Vector3.SmoothDamp(transform.position, targetRoot, ref _velocity, smoothTime);
            if (Vector3.Distance(transform.position, targetRoot) < 0.02f) _moving = false;
        }

        transform.rotation = Quaternion.Slerp(transform.rotation, face, Time.unscaledDeltaTime * turnSpeed);
    }
}
