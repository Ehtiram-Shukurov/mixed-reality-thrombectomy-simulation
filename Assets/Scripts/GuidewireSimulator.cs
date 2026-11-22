using UnityEngine;
using System.Collections.Generic;

// Guidewire navigation — graph-based, right stick only.
// Stick Y = advance/retract. Stick X = twist at forks.
public class GuidewireSimulator : MonoBehaviour
{
    [Header("References")]
    public CenterlineLoader centerlineLoader;
    public Transform wireTip;
    public TubeRenderer catheterTrail;

    [Header("Settings")]
    public float moveSpeed = 0.05f;
    public float twistSpeed = 60f;
    public float alignmentTolerance = 35f;

    // Navigation state
    private CenterlineNode previousNode;
    private CenterlineNode currentNode;
    private CenterlineNode nextNode;
    private CenterlineNode _retractTarget;

    private bool isReady = false;
    private bool isAtFork = false;
    private Vector3 incomingDir;

    private float _bestBranchAngle = 180f;
    private Quaternion _startLocalRotation;

    // Public properties for GuidewireUI and CatheterSimulator
    public bool IsReady => isReady;
    public bool IsAtFork => isAtFork;
    public float BestBranchAngle => _bestBranchAngle;

    // Signed angle from the tip to the closest branch, seen along the vessel.
    public float BranchSignedAngle { get; private set; }
    // Which way to push the stick to close the gap: -1 left, +1 right, 0 aligned.
    public int TwistDirection { get; private set; }
    public int HistoryDepth => history.Count;
    public List<Vector3> TrailPoints => _trailPoints;

    // Trail
    private List<Vector3> _trailPoints = new List<Vector3>();
    private Stack<CenterlineNode> history = new Stack<CenterlineNode>();

    // -------------------------------------------------------------------------
    // Init
    // -------------------------------------------------------------------------

    void Start()
    {
        _startLocalRotation = wireTip.localRotation;
        InvokeRepeating(nameof(CheckLoader), 0.5f, 0.5f);
    }

    private void CheckLoader()
    {
        if (centerlineLoader != null && centerlineLoader.IsLoaded)
        {
            InitializeGuidewire();
            CancelInvoke(nameof(CheckLoader));
        }
    }

    private void InitializeGuidewire()
    {
        currentNode = centerlineLoader.EntryNode;
        previousNode = null;
        _retractTarget = null;

        if (currentNode.branches.Count > 0)
            nextNode = currentNode.branches[0];

        wireTip.position = centerlineLoader.GetNodeWorldPosition(currentNode);

        history.Clear();
        history.Push(currentNode);

        _trailPoints.Clear();
        _trailPoints.Add(WorldToLocal(wireTip.position));
        UpdateTrail();

        isReady = true;
    }

    // -------------------------------------------------------------------------
    // Update
    // -------------------------------------------------------------------------

    void Update()
    {
        if (!isReady) return;

        HandleTwist();

        if (isAtFork)
            EvaluateForkChoice();
        else
            HandleMovement();
    }

    // -------------------------------------------------------------------------
    // Twist
    // -------------------------------------------------------------------------

    private void HandleTwist()
    {
        var stick = OVRInput.Get(OVRInput.Axis2D.PrimaryThumbstick, OVRInput.Controller.RTouch);
        if (Mathf.Abs(stick.x) > 0.1f)
            wireTip.Rotate(0, 0, -stick.x * twistSpeed * Time.deltaTime, Space.Self);
    }

    // -------------------------------------------------------------------------
    // Movement
    // -------------------------------------------------------------------------

    private void HandleMovement()
    {
        var stick = OVRInput.Get(OVRInput.Axis2D.PrimaryThumbstick, OVRInput.Controller.RTouch);
        float moveInput = stick.y;

        if (moveInput > 0.1f && nextNode != null)
        {
            _retractTarget = null;
            HapticManager.Instance?.StartContinuous(0.1f);
            MoveToward(nextNode, moveInput);
        }
        else if (moveInput < -0.1f && history.Count > 1)
        {
            HapticManager.Instance?.StartContinuous(0.1f);

            if (_retractTarget == null)
            {
                history.Pop();
                _retractTarget = history.Peek();
            }

            wireTip.position = Vector3.MoveTowards(
                wireTip.position,
                centerlineLoader.GetNodeWorldPosition(_retractTarget),
                Mathf.Abs(moveInput) * moveSpeed * Time.deltaTime);

            if (Vector3.Distance(wireTip.position,
                centerlineLoader.GetNodeWorldPosition(_retractTarget)) < 0.001f)
            {
                nextNode = currentNode;
                previousNode = null;
                currentNode = _retractTarget;
                _retractTarget = null;

                while (_trailPoints.Count > history.Count)
                    _trailPoints.RemoveAt(_trailPoints.Count - 1);
                UpdateTrail();
            }
        }
        else
        {
            _retractTarget = null;
            HapticManager.Instance?.StopContinuous();
        }
    }

    private void MoveToward(CenterlineNode target, float input)
    {
        float step = Mathf.Abs(input) * moveSpeed * Time.deltaTime;
        var targetPos = centerlineLoader.GetNodeWorldPosition(target);
        wireTip.position = Vector3.MoveTowards(wireTip.position, targetPos, step);

        if (Vector3.Distance(wireTip.position, targetPos) < 0.001f)
            ArriveAtNode(target);
    }

    // -------------------------------------------------------------------------
    // Node arrival
    // -------------------------------------------------------------------------

    private void ArriveAtNode(CenterlineNode node)
    {
        previousNode = currentNode;
        currentNode = node;
        history.Push(currentNode);

        _trailPoints.Add(WorldToLocal(wireTip.position));
        UpdateTrail();

        if (currentNode.branches.Count > 1)
        {
            isAtFork = true;
            nextNode = null;

            incomingDir = previousNode != null
                ? (centerlineLoader.GetNodeWorldPosition(currentNode) -
                   centerlineLoader.GetNodeWorldPosition(previousNode)).normalized
                : wireTip.forward;

            HapticManager.Instance?.StopContinuous();
            HapticManager.Instance?.PulseOnce(0.4f, 0.15f);

            Debug.Log("[Guidewire] Fork reached.");
        }
        else if (currentNode.branches.Count == 1)
        {
            nextNode = currentNode.branches[0];
            isAtFork = false;
        }
        else
        {
            nextNode = null;
            isAtFork = false;
            HapticManager.Instance?.StopContinuous();
            Debug.Log("[Guidewire] End of vessel.");
        }
    }

    // -------------------------------------------------------------------------
    // Trail
    // -------------------------------------------------------------------------

    private Vector3 WorldToLocal(Vector3 worldPos)
        => centerlineLoader.volumeTransform.InverseTransformPoint(worldPos);

    private void UpdateTrail()
    {
        if (catheterTrail == null) return;
        if (_trailPoints.Count < 2) { catheterTrail.Clear(); return; }
        catheterTrail.SetPoints(_trailPoints, centerlineLoader.volumeTransform);
    }

    // -------------------------------------------------------------------------
    // Fork choice
    // -------------------------------------------------------------------------

    private void EvaluateForkChoice()
    {
        var stick = OVRInput.Get(OVRInput.Axis2D.PrimaryThumbstick, OVRInput.Controller.RTouch);
        var tipProjected = Vector3.ProjectOnPlane(wireTip.up, incomingDir).normalized;

        CenterlineNode bestBranch = null;
        Vector3 bestProj = Vector3.zero;
        float bestAngle = float.MaxValue;

        foreach (var branch in currentNode.branches)
        {
            var branchWorld = centerlineLoader.GetNodeWorldPosition(branch);
            var branchDir = (branchWorld - wireTip.position).normalized;
            var branchProj = Vector3.ProjectOnPlane(branchDir, incomingDir).normalized;
            float angle = Vector3.Angle(tipProjected, branchProj);

            if (angle < bestAngle) { bestAngle = angle; bestBranch = branch; bestProj = branchProj; }
        }

        _bestBranchAngle = bestAngle;
        if (bestBranch != null) UpdateTwistHint(tipProjected, bestProj, bestAngle);

        float signal = Mathf.Clamp01(1f - (bestAngle / 180f));
        HapticManager.Instance?.StartContinuous(signal * 0.5f);

        if (stick.y < 0.1f) return;

        if (bestAngle <= alignmentTolerance && bestBranch != null)
        {
            nextNode = bestBranch;
            isAtFork = false;
            _bestBranchAngle = 180f;
            TwistDirection = 0;
            HapticManager.Instance?.StopContinuous();
            Debug.Log($"[Guidewire] Branch unlocked at {bestAngle:F1}°.");
        }
    }

    // The twist axis is not the vessel axis, so the sign of BranchSignedAngle does not
    // say which way to twist. Try a small twist and see if the gap shrinks.
    private void UpdateTwistHint(Vector3 tipProjected, Vector3 branchProj, float angle)
    {
        BranchSignedAngle = Vector3.SignedAngle(tipProjected, branchProj, incomingDir);

        if (angle <= alignmentTolerance) { TwistDirection = 0; return; }

        var upPlus = Quaternion.AngleAxis(5f, wireTip.forward) * wireTip.up;
        var plusProjected = Vector3.ProjectOnPlane(upPlus, incomingDir).normalized;
        float anglePlus = Vector3.Angle(plusProjected, branchProj);

        // HandleTwist rotates by -stick.x, so a positive twist is stick left.
        TwistDirection = anglePlus < angle ? -1 : 1;
    }

    // Used by Play Again. Clears fork state and tip twist, then goes back to the entry.
    public void ResetGuidewire()
    {
        isAtFork = false;
        _bestBranchAngle = 180f;
        TwistDirection = 0;
        BranchSignedAngle = 0f;
        wireTip.localRotation = _startLocalRotation;
        HapticManager.Instance?.StopAll();
        InitializeGuidewire();
    }
}