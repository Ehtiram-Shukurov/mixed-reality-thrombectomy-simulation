using UnityEngine;
using System.Collections.Generic;

// Drives the catheter in Phase 2.
// 2a: advances along the guidewire trail. 2c: retracts. Speed is capped during retraction.
public class CatheterSimulator : MonoBehaviour
{
    [Header("References")]
    public GuidewireSimulator guidewireSimulator;
    public SimulationManager simManager;
    public Transform catheterTipTransform;
    public TubeRenderer catheterBodyRenderer;

    [Header("Speed")]
    public float maxAdvanceSpeed = 0.03f;
    public float maxRetractSpeed = 0.03f;

    [Header("Extraction Penalty")]
    public int speedPenaltyPoints = 5;

    private List<Vector3> _trail;
    private int _trailIndex;
    private bool _advancing = false;
    private bool _retracting = false;
    private bool _speedWarningActive = false;

    public bool IsAdvancing => _advancing;
    public bool IsRetracting => _retracting;

    public void BeginAdvance()
    {
        _trail = new List<Vector3>(guidewireSimulator.TrailPoints);
        if (_trail == null || _trail.Count < 2)
        {
            Debug.LogWarning("[CatheterSim] Trail is empty.");
            return;
        }

        _trailIndex = 0;
        _advancing = true;
        _retracting = false;
        SnapTipToIndex(0);
        UpdateCatheterBody();
        Debug.Log($"[CatheterSim] Advancing — {_trail.Count} points.");
    }

    public void BeginRetraction()
    {
        if (_trail == null || _trail.Count < 2)
        {
            Debug.LogWarning("[CatheterSim] No trail to retract along.");
            return;
        }
        _advancing = false;
        _retracting = true;
        Debug.Log("[CatheterSim] Retracting.");
    }

    void Update()
    {
        if (_advancing) HandleAdvance();
        if (_retracting) HandleRetraction();
    }

    private void HandleAdvance()
    {
        Vector2 stick = OVRInput.Get(OVRInput.Axis2D.PrimaryThumbstick, OVRInput.Controller.RTouch);
        if (stick.y < 0.1f)
        {
            HapticManager.Instance?.StopContinuous();
            return;
        }

        HapticManager.Instance?.StartContinuous(0.15f);
        float step = Mathf.Min(stick.y * maxAdvanceSpeed, maxAdvanceSpeed) * Time.deltaTime;
        MoveTipForward(step);
    }

    private void MoveTipForward(float step)
    {
        if (_trailIndex >= _trail.Count - 1)
        {
            _advancing = false;
            SnapTipToIndex(_trail.Count - 1);
            UpdateCatheterBody();
            HapticManager.Instance?.StopContinuous();
            Debug.Log("[CatheterSim] Catheter reached clot.");
            simManager?.OnCatheterArrivedAtClot();
            return;
        }

        catheterTipTransform.position = Vector3.MoveTowards(
            catheterTipTransform.position, TrailWorldPos(_trailIndex + 1), step);

        if (Vector3.Distance(catheterTipTransform.position, TrailWorldPos(_trailIndex + 1)) < 0.001f)
        {
            _trailIndex++;
            UpdateCatheterBody();
        }
    }

    private void HandleRetraction()
    {
        Vector2 stick = OVRInput.Get(OVRInput.Axis2D.PrimaryThumbstick, OVRInput.Controller.RTouch);
        float input = -stick.y;

        if (input < 0.1f)
        {
            HapticManager.Instance?.StopContinuous();
            _speedWarningActive = false;
            return;
        }

        float requested = input * maxRetractSpeed * 3f;
        float actual = Mathf.Min(requested, maxRetractSpeed);

        if (requested > maxRetractSpeed * 1.1f)
        {
            if (!_speedWarningActive)
            {
                _speedWarningActive = true;
                HapticManager.Instance?.PulseOnce(0.8f, 0.2f);
                simManager?.AddExtractionPenalty(speedPenaltyPoints);
                Debug.Log("[CatheterSim] Speed warning.");
            }
        }
        else
        {
            _speedWarningActive = false;
            HapticManager.Instance?.StartContinuous(0.1f);
        }

        MoveTipBackward(actual * Time.deltaTime);
    }

    private void MoveTipBackward(float step)
    {
        if (_trailIndex <= 0)
        {
            _retracting = false;
            SnapTipToIndex(0);
            UpdateCatheterBody();
            HapticManager.Instance?.StopContinuous();
            Debug.Log("[CatheterSim] Fully retracted.");
            simManager?.OnCatheterFullyRetracted();
            return;
        }

        catheterTipTransform.position = Vector3.MoveTowards(
            catheterTipTransform.position, TrailWorldPos(_trailIndex - 1), step);

        if (Vector3.Distance(catheterTipTransform.position, TrailWorldPos(_trailIndex - 1)) < 0.001f)
        {
            _trailIndex--;
            UpdateCatheterBody();
        }
    }

    private Vector3 TrailWorldPos(int index)
    {
        return guidewireSimulator.centerlineLoader.volumeTransform.TransformPoint(_trail[index]);
    }

    private void SnapTipToIndex(int index)
    {
        if (catheterTipTransform != null)
            catheterTipTransform.position = TrailWorldPos(index);
    }

    private void UpdateCatheterBody()
    {
        if (catheterBodyRenderer == null) return;
        if (_trailIndex < 1) { catheterBodyRenderer.Clear(); return; }
        catheterBodyRenderer.SetPoints(
            _trail.GetRange(0, _trailIndex + 1),
            guidewireSimulator.centerlineLoader.volumeTransform);
    }
}