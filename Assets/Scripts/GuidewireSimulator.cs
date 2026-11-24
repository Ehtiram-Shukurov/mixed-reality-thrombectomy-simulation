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
    [Tooltip("How close the tip must point to a branch, in degrees.")]
    public float alignmentTolerance = 20f;
    [Tooltip("Seconds the tip must stay aligned before pushing forward commits the branch.")]
    public float alignHoldSeconds = 0.4f;
    [Tooltip("Branches closer than this (degrees, seen along the vessel) are spread apart so there is a real choice.")]
    public float minBranchSeparation = 40f;
    [Tooltip("How many nodes down a branch to look when measuring its direction.")]
    public int branchLookAhead = 4;

    [Header("Hand roll")]
    [Tooltip("Roll the right controller to twist the wire at forks, as well as the stick.")]
    public bool useControllerRoll = true;
    [Tooltip("Tip degrees per degree of hand roll.")]
    public float rollGain = 1f;
    private Quaternion _prevRoll;
    private bool _hasPrevRoll;

    [Header("Bends")]
    [Tooltip("Nodes on each side used to measure how sharply the vessel turns.")]
    public int bendWindow = 4;
    [Tooltip("Bend angle (degrees) where resistance starts.")]
    public float bendStart = 25f;
    [Tooltip("Bend angle (degrees) of full resistance.")]
    public float bendFull = 70f;
    [Tooltip("Speed left at full resistance (0 to 1).")]
    public float bendMinSpeed = 0.35f;
    [Tooltip("Pushing harder than this through a bend above forceAngle counts as forcing.")]
    public float forceStick = 0.9f;
    public float forceAngle = 45f;
    public float forceSeconds = 0.35f;

    // 0 (straight) to 1 (very tight) for the bend just ahead. Read by the UI.
    public float BendAmount { get; private set; }
    public float BendAngle { get; private set; }
    // Raised once per bend when the wire is forced through it.
    public event Action Forced;
    private float _forceTime;
    private CenterlineNode _forcedAt;

    // The bent tip's direction at a fork, in the plane across the vessel.
    private Vector3 _bendDir = Vector3.up;
    private float _viewSign = 1f;
    private Transform _head;
    public Vector3 BendDirection => _bendDir;
    public Vector3 IncomingDirection => incomingDir;

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

    // Which branch (index in CurrentNode.branches) the twist selects, and how long it has been aligned.
    public int SelectedBranch { get; private set; } = -1;
    public bool IsAligned => isAtFork && _bestBranchAngle <= alignmentTolerance;
    public float HoldProgress => alignHoldSeconds <= 0f ? 1f : Mathf.Clamp01(_alignedTime / alignHoldSeconds);
    private float _alignedTime;
    private int _alignedBranch = -1;

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

    // At a fork the bent tip turns around the vessel axis. Stick right turns it clockwise
    // as you see it, so the dot you want on the right means push right.
    private void HandleTwist()
    {
        HandleControllerRoll();

        var stick = SimInput.Stick;
        if (Mathf.Abs(stick.x) <= 0.1f) return;

        if (isAtFork)
        {
            UpdateViewSign();
            _bendDir = Quaternion.AngleAxis(stick.x * twistSpeed * Time.deltaTime * _viewSign, incomingDir) * _bendDir;
            _bendDir = Vector3.ProjectOnPlane(_bendDir, incomingDir).normalized;
        }
        else
        {
            wireTip.Rotate(0, 0, -stick.x * twistSpeed * Time.deltaTime, Space.Self);
        }
    }

    // Rolling the right controller turns the tip, like torquing a real wire by hand.
    // A clockwise roll (as you look along your hand) turns the tip clockwise as you see it,
    // the same as pushing the stick right. Tracked every frame so there is no jump.
    private void HandleControllerRoll()
    {
        var rot = SimInput.RightControllerRotation;
        if (!useControllerRoll || rot == null) { _hasPrevRoll = false; return; }

        if (_hasPrevRoll && isAtFork)
        {
            var delta = Quaternion.Inverse(_prevRoll) * rot.Value;
            float roll = Mathf.DeltaAngle(0f, delta.eulerAngles.z);
            // Ignore tiny tracking jitter and big jumps (tracking glitches).
            if (Mathf.Abs(roll) > 0.05f && Mathf.Abs(roll) < 45f)
            {
                UpdateViewSign();
                // Positive local-Z rotation looks counter-clockwise from behind, so flip it.
                _bendDir = Quaternion.AngleAxis(-roll * rollGain * _viewSign, incomingDir) * _bendDir;
                _bendDir = Vector3.ProjectOnPlane(_bendDir, incomingDir).normalized;
            }
        }
        _prevRoll = rot.Value;
        _hasPrevRoll = true;
    }

    // Positive AngleAxis turns clockwise for a viewer looking against the axis.
    // Flip it when the vessel points away from you. Keep the old sign when the vessel is
    // seen nearly side on, so it does not flicker.
    private void UpdateViewSign()
    {
        if (_head == null) _head = FxUtil.FindHead();
        if (_head == null) return;
        float d = Vector3.Dot(incomingDir, _head.forward);
        if (d < -0.15f) _viewSign = 1f;
        else if (d > 0.15f) _viewSign = -1f;
    }

    // Direction from the fork into a branch, measured a few nodes down it so a very
    // short first step does not throw the angle off.
    private Vector3 BranchDirection(CenterlineNode branch)
    {
        var n = branch;
        for (int k = 0; k < branchLookAhead && n.branches.Count == 1; k++) n = n.branches[0];
        var forkPos = centerlineLoader.GetNodeWorldPosition(currentNode);
        return (centerlineLoader.GetNodeWorldPosition(n) - forkPos).normalized;
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
            UpdateBend(nextNode);

            // Tight bends push back: slower, a stronger hum, and forcing it counts as an error.
            float speedFactor = Mathf.Lerp(1f, bendMinSpeed, BendAmount);
            if (BendAmount > 0.01f) HapticManager.Instance?.Resistance(BendAmount);
            else HapticManager.Instance?.Moving();
            CheckForcing(moveInput);

            MoveToward(nextNode, moveInput * speedFactor);
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
            _forceTime = 0f;
            BendAmount = 0f;
            HapticManager.Instance?.StopContinuous();
        }
    }

    // Angle between the vessel direction behind the wire and the direction just ahead of it.
    private void UpdateBend(CenterlineNode target)
    {
        var loader = centerlineLoader;
        int back = Mathf.Max(0, _path.Count - 1 - bendWindow);
        Vector3 pBack = loader.GetNodeWorldPosition(_path[back]);
        Vector3 pHere = loader.GetNodeWorldPosition(target);
        var n = target;
        for (int k = 0; k < bendWindow && n.branches.Count == 1; k++) n = n.branches[0];
        Vector3 pAhead = loader.GetNodeWorldPosition(n);

        Vector3 a = pHere - pBack, b = pAhead - pHere;
        BendAngle = a.sqrMagnitude > 1e-10f && b.sqrMagnitude > 1e-10f ? Vector3.Angle(a, b) : 0f;
        BendAmount = Mathf.Clamp01((BendAngle - bendStart) / Mathf.Max(1f, bendFull - bendStart));
    }

    // Holding the stick fully forward through a sharp bend for a moment counts once per bend.
    private void CheckForcing(float moveInput)
    {
        bool forcing = moveInput >= forceStick && BendAngle >= forceAngle;
        _forceTime = forcing ? _forceTime + Time.deltaTime : 0f;
        if (_forceTime >= forceSeconds && _forcedAt != nextNode && !NearForced(nextNode))
        {
            _forcedAt = nextNode;
            _forceTime = 0f;
            Feedback.SpeedWarning();
            Forced?.Invoke();
            Debug.Log($"[Guidewire] Forced through a {BendAngle:F0} degree bend.");
        }
    }

    // One error per bend: ignore the next few nodes after a forced one.
    private bool NearForced(CenterlineNode node)
    {
        if (_forcedAt == null) return false;
        var n = _forcedAt;
        for (int k = 0; k < bendWindow * 2 && n != null; k++)
        {
            if (n == node) return true;
            n = n.branches.Count > 0 ? n.branches[0] : null;
        }
        return false;
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
        _alignedTime = 0f;
        _alignedBranch = -1;

        // Direction the wire came in from, along the path.
        incomingDir = _path.Count >= 2
            ? (centerlineLoader.GetNodeWorldPosition(currentNode) -
               centerlineLoader.GetNodeWorldPosition(_path[_path.Count - 2])).normalized
            : wireTip.forward;

        // Start the bent tip pointing at a random spot that is not already on a branch,
        // so every fork needs a real twist.
        var basis = Vector3.ProjectOnPlane(wireTip.up, incomingDir);
        if (basis.sqrMagnitude < 1e-4f) basis = Vector3.ProjectOnPlane(Vector3.up, incomingDir);
        if (basis.sqrMagnitude < 1e-4f) basis = Vector3.ProjectOnPlane(Vector3.right, incomingDir);
        basis.Normalize();
        _bendDir = basis;
        for (int attempt = 0; attempt < 12; attempt++)
        {
            var candidate = Quaternion.AngleAxis(UnityEngine.Random.Range(0f, 360f), incomingDir) * basis;
            float nearest = 180f;
            foreach (var b in currentNode.branches)
            {
                var proj = Vector3.ProjectOnPlane(BranchDirection(b), incomingDir).normalized;
                nearest = Mathf.Min(nearest, Vector3.Angle(candidate, proj));
            }
            _bendDir = candidate;
            if (nearest > alignmentTolerance * 2.5f) break;
        }
        UpdateViewSign();

        Feedback.ForkArrived();
        Debug.Log("[Guidewire] Fork reached.");
    }

    private void LeaveFork()
    {
        isAtFork = false;
        _bestBranchAngle = 180f;
        TwistDirection = 0;
        DialCount = 0;
        SelectedBranch = -1;
        _alignedTime = 0f;
        _alignedBranch = -1;
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

        UpdateViewSign();

        int count = Mathf.Min(currentNode.branches.Count, MaxDial);
        var gaps = new float[count];
        var projs = new Vector3[count];

        // Gap from the bent tip to each branch, seen down the vessel, positive = clockwise
        // as you see it = push the stick right.
        for (int i = 0; i < count; i++)
        {
            projs[i] = Vector3.ProjectOnPlane(BranchDirection(currentNode.branches[i]), incomingDir).normalized;
            gaps[i] = Vector3.SignedAngle(_bendDir, projs[i], incomingDir) * _viewSign;
        }

        // Two branches that leave almost in the same direction cannot be told apart by twisting.
        // Spread them to at least minBranchSeparation around their middle so the choice is real.
        if (count == 2)
        {
            float d = Mathf.DeltaAngle(gaps[0], gaps[1]);
            if (Mathf.Abs(d) < minBranchSeparation)
            {
                float s = d >= 0f ? 1f : -1f;
                float mid = gaps[0] + d * 0.5f;
                gaps[0] = mid - s * minBranchSeparation * 0.5f;
                gaps[1] = mid + s * minBranchSeparation * 0.5f;
            }
        }

        CenterlineNode bestBranch = null;
        float bestAngle = float.MaxValue;
        int bestIndex = 0;
        for (int i = 0; i < count; i++)
        {
            gaps[i] = Mathf.DeltaAngle(0f, gaps[i]);
            float angle = Mathf.Abs(gaps[i]);
            if (angle < bestAngle)
            {
                bestAngle = angle; bestBranch = currentNode.branches[i]; bestIndex = i;
            }
        }

        _bestBranchAngle = bestAngle;
        SelectedBranch = bestBranch != null ? bestIndex : -1;

        DialCount = count;
        for (int i = 0; i < count; i++) DialAngles[i] = gaps[i];

        BranchSignedAngle = bestBranch != null ? gaps[bestIndex] : 0f;
        if (bestAngle <= alignmentTolerance) TwistDirection = 0;
        else TwistDirection = DialAngles[bestIndex] > 0f ? 1 : -1;

        float signal = Mathf.Clamp01(1f - (bestAngle / 180f));
        HapticManager.Instance?.ForkAlignment(signal);

        // The tip has to stay on the same branch, inside the zone, for alignHoldSeconds.
        bool aligned = bestAngle <= alignmentTolerance && bestBranch != null;
        if (aligned && _alignedBranch == bestIndex) _alignedTime += Time.deltaTime;
        else _alignedTime = 0f;
        _alignedBranch = aligned ? bestIndex : -1;

        if (stick.y < 0.1f) return;

        if (aligned && _alignedTime >= alignHoldSeconds)
        {
            nextNode = bestBranch;
            isAtFork = false;
            _bestBranchAngle = 180f;
            TwistDirection = 0;
            DialCount = 0;
            SelectedBranch = -1;
            _alignedTime = 0f;
            _alignedBranch = -1;
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
        BendAmount = 0f;
        _forcedAt = null;
        _forceTime = 0f;
        DialCount = 0;
        wireTip.localRotation = _startLocalRotation;
        HapticManager.Instance?.StopAll();
        InitializeGuidewire();
    }
}
