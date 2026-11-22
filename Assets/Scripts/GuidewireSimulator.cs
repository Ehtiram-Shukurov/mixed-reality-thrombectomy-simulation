using System;
using System.Collections.Generic;
using UnityEngine;

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

    // Navigation state. _path runs from the entry node to the current node.
    private CenterlineNode currentNode;
    private CenterlineNode nextNode;
    private CenterlineNode _retractTarget;
    private readonly List<CenterlineNode> _path = new List<CenterlineNode>();

    private bool isReady = false;
    private bool isAtFork = false;
    private Vector3 incomingDir;

    private float _bestBranchAngle = 180f;
    private Quaternion _startLocalRotation;

    // Public state for the UI, the fork dial and the other simulators
    public bool IsReady => isReady;
    public bool IsAtFork => isAtFork;
    public float BestBranchAngle => _bestBranchAngle;
    public CenterlineNode CurrentNode => currentNode;
    public int HistoryDepth => _path.Count;
    public List<Vector3> TrailPoints => _trailPoints;
    public float AlignmentTolerance => alignmentTolerance;

    // True when the wire sits at the end of a vessel and is not pulling back.
    public bool IsAtDeadEnd => isReady && !isAtFork && _retractTarget == null
                               && currentNode != null && currentNode.branches.Count == 0;

    // Signed angle from the tip to the closest branch, seen along the vessel.
    public float BranchSignedAngle { get; private set; }
    // Which way to push the stick to close the gap: -1 left, +1 right, 0 aligned.
    public int TwistDirection { get; private set; }

    // Fork dial: one angle per branch, in degrees clockwise from the top mark.
    // A branch on the right of the mark means push the stick right.
    public const int MaxDial = 4;
    public int DialCount { get; private set; }
    public float[] DialAngles { get; } = new float[MaxDial];

    // Raised when a branch is committed at a fork, and when the wire reaches a dead end.
    public event Action<CenterlineNode> BranchChosen;
    public event Action DeadEndReached;

    // Trail
    private readonly List<Vector3> _trailPoints = new List<Vector3>();

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
        _retractTarget = null;

        nextNode = currentNode.branches.Count > 0 ? currentNode.branches[0] : null;

        wireTip.position = centerlineLoader.GetNodeWorldPosition(currentNode);

        _path.Clear();
        _path.Add(currentNode);

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
        var stick = SimInput.Stick;
        if (Mathf.Abs(stick.x) > 0.1f)
            wireTip.Rotate(0, 0, -stick.x * twistSpeed * Time.deltaTime, Space.Self);
    }

    // -------------------------------------------------------------------------
    // Movement
    // -------------------------------------------------------------------------

    private void HandleMovement()
    {
        float moveInput = SimInput.Stick.y;

        if (moveInput > 0.1f && nextNode != null)
        {
            _retractTarget = null;
            HapticManager.Instance?.Moving();
            MoveToward(nextNode, moveInput);
        }
        else if (moveInput < -0.1f && _path.Count > 1)
        {
            HapticManager.Instance?.Moving();

            // Nothing is removed from the path until the previous node is reached, so
            // letting go half way keeps the state valid. Forward then returns to this node.
            _retractTarget = _path[_path.Count - 2];
            nextNode = currentNode;

            var targetPos = centerlineLoader.GetNodeWorldPosition(_retractTarget);
            wireTip.position = Vector3.MoveTowards(
                wireTip.position, targetPos, Mathf.Abs(moveInput) * moveSpeed * Time.deltaTime);

            if (Vector3.Distance(wireTip.position, targetPos) < 0.001f)
                FinishRetract();
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

    private void FinishRetract()
    {
        var left = currentNode;
        _path.RemoveAt(_path.Count - 1);
        currentNode = _path[_path.Count - 1];
        _retractTarget = null;

        while (_trailPoints.Count > _path.Count)
            _trailPoints.RemoveAt(_trailPoints.Count - 1);
        UpdateTrail();

        // Pulling back onto a fork lets you choose a branch again.
        if (currentNode.branches.Count > 1)
            EnterFork();
        else
            nextNode = left;
    }

    // -------------------------------------------------------------------------
    // Node arrival
    // -------------------------------------------------------------------------

    private void ArriveAtNode(CenterlineNode node)
    {
        // Coming back to a node we never left (after letting go half way) adds nothing.
        if (_path[_path.Count - 1] != node)
        {
            _path.Add(node);
            _trailPoints.Add(WorldToLocal(wireTip.position));
            UpdateTrail();
        }
        currentNode = node;

        if (node.branches.Count > 1)
        {
            EnterFork();
        }
        else if (node.branches.Count == 1)
        {
            nextNode = node.branches[0];
            isAtFork = false;
        }
        else
        {
            nextNode = null;
            isAtFork = false;
            HapticManager.Instance?.StopContinuous();
            Feedback.DeadEnd();
            DeadEndReached?.Invoke();
            Debug.Log("[Guidewire] End of vessel.");
        }
    }

    private void EnterFork()
    {
        isAtFork = true;
        nextNode = null;

        // Direction the wire came in from, along the path.
        incomingDir = _path.Count >= 2
            ? (centerlineLoader.GetNodeWorldPosition(currentNode) -
               centerlineLoader.GetNodeWorldPosition(_path[_path.Count - 2])).normalized
            : wireTip.forward;

        Feedback.ForkArrived();
        Debug.Log("[Guidewire] Fork reached.");
    }

    private void LeaveFork()
    {
        isAtFork = false;
        _bestBranchAngle = 180f;
        TwistDirection = 0;
        DialCount = 0;
        HapticManager.Instance?.StopContinuous();
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
        var stick = SimInput.Stick;

        // Pulling back at a fork goes back down the vessel.
        if (stick.y < -0.1f && _path.Count > 1)
        {
            LeaveFork();
            return;
        }

        var tipProjected = Vector3.ProjectOnPlane(wireTip.up, incomingDir).normalized;

        int count = Mathf.Min(currentNode.branches.Count, MaxDial);
        var gaps = new float[count];
        CenterlineNode bestBranch = null;
        Vector3 bestProj = Vector3.zero;
        float bestAngle = float.MaxValue;
        int bestIndex = 0;

        for (int i = 0; i < count; i++)
        {
            var branch = currentNode.branches[i];
            var branchWorld = centerlineLoader.GetNodeWorldPosition(branch);
            var branchDir = (branchWorld - wireTip.position).normalized;
            var branchProj = Vector3.ProjectOnPlane(branchDir, incomingDir).normalized;
            gaps[i] = Vector3.SignedAngle(tipProjected, branchProj, incomingDir);
            float angle = Mathf.Abs(gaps[i]);

            if (angle < bestAngle) { bestAngle = angle; bestBranch = branch; bestProj = branchProj; bestIndex = i; }
        }

        _bestBranchAngle = bestAngle;

        // The twist axis is not the vessel axis, so which way the gap moves depends on the
        // fork. Try a small twist to find the sign, then draw the dial so that pushing the
        // stick right always brings a dot on the right of the mark onto the mark.
        float sign = 1f;
        if (bestBranch != null)
        {
            var upPlus = Quaternion.AngleAxis(5f, wireTip.forward) * wireTip.up;
            var plusProjected = Vector3.ProjectOnPlane(upPlus, incomingDir).normalized;
            float gapPlus = Vector3.SignedAngle(plusProjected, bestProj, incomingDir);
            // A positive twist is stick left (HandleTwist uses -stick.x).
            float k = Mathf.DeltaAngle(gaps[bestIndex], gapPlus);
            sign = k >= 0f ? 1f : -1f;
        }

        DialCount = count;
        for (int i = 0; i < count; i++) DialAngles[i] = sign * gaps[i];

        BranchSignedAngle = bestBranch != null ? gaps[bestIndex] : 0f;
        if (bestAngle <= alignmentTolerance) TwistDirection = 0;
        else TwistDirection = DialAngles[bestIndex] > 0f ? 1 : -1;

        float signal = Mathf.Clamp01(1f - (bestAngle / 180f));
        HapticManager.Instance?.ForkAlignment(signal);

        if (stick.y < 0.1f) return;

        if (bestAngle <= alignmentTolerance && bestBranch != null)
        {
            nextNode = bestBranch;
            isAtFork = false;
            _bestBranchAngle = 180f;
            TwistDirection = 0;
            DialCount = 0;
            HapticManager.Instance?.StopContinuous();
            Feedback.BranchChosen();
            BranchChosen?.Invoke(bestBranch);
            Debug.Log($"[Guidewire] Branch unlocked at {bestAngle:F1}°.");
        }
    }

    // Used by Play Again and restart. Clears fork state and tip twist, then goes back to the entry.
    public void ResetGuidewire()
    {
        isAtFork = false;
        _bestBranchAngle = 180f;
        TwistDirection = 0;
        BranchSignedAngle = 0f;
        DialCount = 0;
        wireTip.localRotation = _startLocalRotation;
        HapticManager.Instance?.StopAll();
        InitializeGuidewire();
    }
}
