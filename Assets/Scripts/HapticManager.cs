using UnityEngine;

// Centralized haptic controller. All scripts call this — nothing calls OVRInput directly.
public class HapticManager : MonoBehaviour
{
    public static HapticManager Instance { get; private set; }

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