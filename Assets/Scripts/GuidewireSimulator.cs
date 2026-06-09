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

    [Header("Fork Visualizer")]
    public float arrowLength = 0.01f;
    public float arrowWidth = 0.002f;

    // Navigation state
    private CenterlineNode previousNode;
    private CenterlineNode currentNode;
    private CenterlineNode nextNode;
    private CenterlineNode _retractTarget;

    private bool isReady = false;
    private bool isAtFork = false;
    private Vector3 incomingDir;

    private float _bestBranchAngle = 180f;

    // Public properties for GuidewireUI and CatheterSimulator
    public bool IsReady => isReady;
    public bool IsAtFork => isAtFork;
    public float BestBranchAngle => _bestBranchAngle;
    public int HistoryDepth => history.Count;
    public List<Vector3> TrailPoints => _trailPoints;

    // Trail
    private List<Vector3> _trailPoints = new List<Vector3>();
    private Stack<CenterlineNode> history = new Stack<CenterlineNode>();

    // Fork visualizer
    private LineRenderer tipArrow;
    private List<LineRenderer> branchArrows = new List<LineRenderer>();

    // -------------------------------------------------------------------------
    // Init
    // -------------------------------------------------------------------------

    void Start()
    {
        CreateArrows();
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
    // Arrows
    // -------------------------------------------------------------------------

    private void CreateArrows()
    {
        tipArrow = CreateArrow("TipArrow", Color.white);
        for (int i = 0; i < 4; i++)
            branchArrows.Add(CreateArrow("BranchArrow_" + i, Color.yellow));
        HideAllArrows();
    }

    private LineRenderer CreateArrow(string name, Color color)
    {
        var go = new GameObject(name);
        go.transform.SetParent(wireTip);
        var lr = go.AddComponent<LineRenderer>();
        lr.positionCount = 2;
        lr.startWidth = arrowWidth;
        lr.endWidth = arrowWidth * 0.1f;
        lr.useWorldSpace = true;
        lr.material = new Material(Shader.Find("Sprites/Default"));
        lr.startColor = color;
        lr.endColor = color;
        lr.enabled = false;
        return lr;
    }

    private void HideAllArrows()
    {
        if (tipArrow != null) tipArrow.enabled = false;
        foreach (var lr in branchArrows) lr.enabled = false;
    }

    // -------------------------------------------------------------------------
    // Update
    // -------------------------------------------------------------------------

    void Update()
    {
        if (!isReady) return;

        HandleTwist();

        if (isAtFork)
        {
            UpdateForkVisualizer();
            EvaluateForkChoice();
        }
        else
        {
            HideAllArrows();
            HandleMovement();
        }
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
    // Fork visualizer
    // -------------------------------------------------------------------------

    private void UpdateForkVisualizer()
    {
        var tipBase = wireTip.position;
        var tipProjected = Vector3.ProjectOnPlane(wireTip.up, incomingDir).normalized;

        tipArrow.enabled = true;
        tipArrow.SetPosition(0, tipBase);
        tipArrow.SetPosition(1, tipBase + tipProjected * arrowLength);

        for (int i = 0; i < branchArrows.Count; i++)
        {
            if (i < currentNode.branches.Count)
            {
                var branch = currentNode.branches[i];
                var branchWorld = centerlineLoader.GetNodeWorldPosition(branch);
                var branchDir = (branchWorld - tipBase).normalized;
                var branchProj = Vector3.ProjectOnPlane(branchDir, incomingDir).normalized;
                float angle = Vector3.Angle(tipProjected, branchProj);
                Color c = angle <= alignmentTolerance ? Color.green : Color.yellow;

                branchArrows[i].startColor = c;
                branchArrows[i].endColor = c;
                branchArrows[i].enabled = true;
                branchArrows[i].SetPosition(0, tipBase);
                branchArrows[i].SetPosition(1, tipBase + branchProj * arrowLength);
            }
            else
            {
                branchArrows[i].enabled = false;
            }
        }
    }

    // -------------------------------------------------------------------------
    // Fork choice
    // -------------------------------------------------------------------------

    private void EvaluateForkChoice()
    {
        var stick = OVRInput.Get(OVRInput.Axis2D.PrimaryThumbstick, OVRInput.Controller.RTouch);
        var tipProjected = Vector3.ProjectOnPlane(wireTip.up, incomingDir).normalized;

        CenterlineNode bestBranch = null;
        float bestAngle = float.MaxValue;

        foreach (var branch in currentNode.branches)
        {
            var branchWorld = centerlineLoader.GetNodeWorldPosition(branch);
            var branchDir = (branchWorld - wireTip.position).normalized;
            var branchProj = Vector3.ProjectOnPlane(branchDir, incomingDir).normalized;
            float angle = Vector3.Angle(tipProjected, branchProj);

            if (angle < bestAngle) { bestAngle = angle; bestBranch = branch; }
        }

        _bestBranchAngle = bestAngle;

        float signal = Mathf.Clamp01(1f - (bestAngle / 180f));
        HapticManager.Instance?.StartContinuous(signal * 0.5f);

        if (stick.y < 0.1f) return;

        if (bestAngle <= alignmentTolerance && bestBranch != null)
        {
            nextNode = bestBranch;
            isAtFork = false;
            _bestBranchAngle = 180f;
            HideAllArrows();
            HapticManager.Instance?.StopContinuous();
            Debug.Log($"[Guidewire] Branch unlocked at {bestAngle:F1}°.");
        }
    }
    public void ResetGuidewire() => InitializeGuidewire();
}