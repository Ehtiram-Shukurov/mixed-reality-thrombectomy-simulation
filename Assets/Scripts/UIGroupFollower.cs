using System;
using UnityEngine;
using UnityEngine.UI;

// Keeps all the control panels together in front of the user.
// The panels keep fixed spots relative to each other and move as one group.
// The group stays still while you look around, and glides after you only when
// you turn more than yawTolerance or walk more than distanceTolerance.
// It does not move while a hand is close, so you can reach it.
public class UIGroupFollower : MonoBehaviour
{
    [Serializable]
    public class Panel
    {
        public Transform root;
        [Tooltip("Where the center of the panel sits: x right, y up, z forward of the group.")]
        public Vector3 offset;
        [NonSerialized] public Vector3 centerInRootFrame;
    }

    public Panel[] panels;

    [Header("When the group moves")]
    [Tooltip("Degrees you can turn your head before the group follows.")]
    public float yawTolerance = 30f;
    [Tooltip("Meters you can walk before the group follows.")]
    public float distanceTolerance = 0.35f;
    public float yawSmoothTime = 0.8f;
    public float positionSmoothTime = 0.8f;
    [Tooltip("The group stays put while a hand is this close to a panel.")]
    public float freezeHandDistance = 0.25f;

    private Transform _head, _leftHand, _rightHand;
    private bool _ready;
    private bool _moving;
    private Vector3 _pos, _posVelocity;
    private float _yaw, _yawVelocity;

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
    }

    void LateUpdate()
    {
        if (_head == null) return;

        // No tracking yet (or no headset in the editor): leave the panels where they are.
        if (!_ready && _head.position.y < 0.5f) return;

        Step(_head.position, _head.eulerAngles.y, Time.unscaledDeltaTime, HandNear());
    }

    // One update. Public so it can be tested without a headset.
    public void Step(Vector3 headPos, float headYaw, float dt, bool handNear)
    {
        if (!_ready)
        {
            foreach (var p in panels) p.centerInRootFrame = MeasureCenter(p.root);
            _pos = headPos;
            _yaw = headYaw;
            _ready = true;
            Apply();
            return;
        }

        if (handNear)
        {
            _moving = false;
            _posVelocity = Vector3.zero;
            _yawVelocity = 0f;
            return;
        }

        float yawGap = Mathf.DeltaAngle(_yaw, headYaw);
        float posGap = Vector3.Distance(_pos, headPos);
        if (!_moving && (Mathf.Abs(yawGap) > yawTolerance || posGap > distanceTolerance))
            _moving = true;

        if (!_moving) return;

        _yaw = Mathf.SmoothDampAngle(_yaw, headYaw, ref _yawVelocity, yawSmoothTime, Mathf.Infinity, dt);
        _pos = Vector3.SmoothDamp(_pos, headPos, ref _posVelocity, positionSmoothTime, Mathf.Infinity, dt);
        if (Mathf.Abs(Mathf.DeltaAngle(_yaw, headYaw)) < 2f && Vector3.Distance(_pos, headPos) < 0.03f)
            _moving = false;

        Apply();
    }

    private void Apply()
    {
        var yaw = Quaternion.Euler(0f, _yaw, 0f);
        foreach (var p in panels)
        {
            if (p.root == null) continue;
            Vector3 center = _pos + yaw * p.offset;

            // Face the spot the group was built around, so the group stays rigid.
            Vector3 away = center - _pos;
            away.y = 0f;
            Quaternion face = away.sqrMagnitude > 0.0001f ? Quaternion.LookRotation(away) : p.root.rotation;

            p.root.SetPositionAndRotation(center - face * p.centerInRootFrame, face);
        }
    }

    // The root object is not where the panel looks centered, so find the middle of everything drawn.
    private static Vector3 MeasureCenter(Transform root)
    {
        if (root == null) return Vector3.zero;
        var corners = new Vector3[4];
        bool any = false;
        var bounds = new Bounds(root.position, Vector3.zero);
        foreach (var g in root.GetComponentsInChildren<Graphic>(false))
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
        return Quaternion.Inverse(root.rotation) * (bounds.center - root.position);
    }

    private bool HandNear()
    {
        foreach (var p in panels)
        {
            if (p.root == null) continue;
            Vector3 center = p.root.position + p.root.rotation * p.centerInRootFrame;
            if (_leftHand != null && Vector3.Distance(_leftHand.position, center) < freezeHandDistance) return true;
            if (_rightHand != null && Vector3.Distance(_rightHand.position, center) < freezeHandDistance) return true;
        }
        return false;
    }
}
