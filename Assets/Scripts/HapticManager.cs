using UnityEngine;

// Centralized haptic controller. All scripts call this — nothing calls OVRInput directly.
// Strengths and times are public so they can be tuned in the Inspector on the Quest.
public class HapticManager : MonoBehaviour
{
    public static HapticManager Instance { get; private set; }

    [Header("Continuous hum (0 to 1)")]
    public float movingHum = 0.1f;
    public float advanceHum = 0.15f;
    public float retractHum = 0.1f;

    [Header("Fork")]
    public float forkArrival = 0.4f;
    public float forkArrivalTime = 0.15f;
    [Tooltip("Hum strength at a perfect alignment. It scales up as the tip lines up.")]
    public float forkAlignMax = 0.5f;

    [Header("Events (strength, seconds)")]
    public float clotContact = 0.8f;
    public float clotContactTime = 0.3f;
    public float speedWarning = 0.8f;
    public float speedWarningTime = 0.2f;
    public float deadEnd = 0.5f;
    public float deadEndTime = 0.2f;
    public float stentDeploy = 0.9f;
    public float success = 1f;
    public float successTime = 0.6f;

    private const OVRInput.Controller CTRL = OVRInput.Controller.RTouch;

    private float _continuousStrength = 0f;
    private float _pulseTimer = 0f;
    private bool _pulsing = false;

    void Awake()
    {
        if (Instance != null) { Destroy(gameObject); return; }
        Instance = this;
    }

    void Update()
    {
        if (_pulsing)
        {
            _pulseTimer -= Time.deltaTime;
            if (_pulseTimer <= 0f)
            {
                _pulsing = false;
                // Resume continuous if one was running underneath
                OVRInput.SetControllerVibration(_continuousStrength, _continuousStrength, CTRL);
            }
        }
    }

    // ---- Named events used by the simulation ----
    public void Moving() => StartContinuous(movingHum);
    public void Advancing() => StartContinuous(advanceHum);
    public void Retracting() => StartContinuous(retractHum);
    public void ForkAlignment(float signal) => StartContinuous(signal * forkAlignMax);
    public void ForkArrived() { StopContinuous(); PulseOnce(forkArrival, forkArrivalTime); }
    public void ClotContact() => PulseOnce(clotContact, clotContactTime);
    public void SpeedWarning() => PulseOnce(speedWarning, speedWarningTime);
    public void DeadEnd() => PulseOnce(deadEnd, deadEndTime);
    public void StentDeployed() => DoublePulse(stentDeploy);
    public void Success() => PulseOnce(success, successTime);

    // ---- Building blocks ----

    // Single vibration burst
    public void PulseOnce(float strength, float duration)
    {
        OVRInput.SetControllerVibration(strength, strength, CTRL);
        _pulseTimer = duration;
        _pulsing = true;
    }

    // Two quick pulses — stent deploy
    public void DoublePulse(float strength)
    {
        StartCoroutine(DoublePulseRoutine(strength));
    }

    private System.Collections.IEnumerator DoublePulseRoutine(float strength)
    {
        OVRInput.SetControllerVibration(strength, strength, CTRL);
        yield return new WaitForSeconds(0.12f);
        OVRInput.SetControllerVibration(0f, 0f, CTRL);
        yield return new WaitForSeconds(0.1f);
        OVRInput.SetControllerVibration(strength, strength, CTRL);
        yield return new WaitForSeconds(0.12f);
        OVRInput.SetControllerVibration(_continuousStrength, _continuousStrength, CTRL);
    }

    // Start a continuous background vibration
    public void StartContinuous(float strength)
    {
        _continuousStrength = strength;
        if (!_pulsing)
            OVRInput.SetControllerVibration(strength, strength, CTRL);
    }

    // Stop continuous vibration
    public void StopContinuous()
    {
        _continuousStrength = 0f;
        if (!_pulsing)
            OVRInput.SetControllerVibration(0f, 0f, CTRL);
    }

    // Stop everything
    public void StopAll()
    {
        _pulsing = false;
        _continuousStrength = 0f;
        _pulseTimer = 0f;
        OVRInput.SetControllerVibration(0f, 0f, CTRL);
    }
}
