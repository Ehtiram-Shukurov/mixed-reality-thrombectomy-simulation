using UnityEngine;

// All controller input for the simulation goes through here. The override lets a test
// script (SimulationAutoplay) drive a full run without a headset.
public static class SimInput
{
    public static bool UseOverride;
    public static Vector2 StickOverride;
    public static bool FluoroOverride;
    public static bool StentOverride;
    public static bool RestartOverride;
    public static bool ConfirmOverride;
    public static bool ContrastOverride;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics()
    {
        UseOverride = false;
        StickOverride = Vector2.zero;
        FluoroOverride = StentOverride = RestartOverride = ConfirmOverride = ContrastOverride = false;
    }

    // Inject contrast dye (true once per press): right B, or left X if the left controller is held.
    // B also deploys the stent, so FluoroController ignores this during the stent step.
    public static bool ContrastPressed
    {
        get
        {
            if (!UseOverride)
                return OVRInput.GetDown(OVRInput.Button.Two, OVRInput.Controller.RTouch)
                    || OVRInput.GetDown(OVRInput.Button.Three, OVRInput.Controller.LTouch);
            bool v = ContrastOverride;
            ContrastOverride = false;
            return v;
        }
    }

    // Right thumbstick: Y advances or retracts, X twists.
    public static Vector2 Stick => UseOverride
        ? StickOverride
        : OVRInput.Get(OVRInput.Axis2D.PrimaryThumbstick, OVRInput.Controller.RTouch);

    // Right controller orientation, for twisting the wire by rolling the hand.
    // Null when the controller is not tracked (or in the autoplay test).
    public static Quaternion? RightControllerRotation
    {
        get
        {
            if (UseOverride) return null;
            if (!OVRInput.GetControllerOrientationTracked(OVRInput.Controller.RTouch)) return null;
            return OVRInput.GetLocalControllerRotation(OVRInput.Controller.RTouch);
        }
    }

    // Right A: hold for fluoroscopy.
    public static bool FluoroHeld => UseOverride
        ? FluoroOverride
        : OVRInput.Get(OVRInput.Button.One, OVRInput.Controller.RTouch);

    // Right B: deploy the stent (true once per press).
    public static bool StentPressed
    {
        get
        {
            if (!UseOverride) return OVRInput.GetDown(OVRInput.Button.Two, OVRInput.Controller.RTouch);
            bool v = StentOverride;
            StentOverride = false;
            return v;
        }
    }

    // Right A on the results screen: play again (true once per press).
    public static bool ConfirmPressed
    {
        get
        {
            if (!UseOverride) return OVRInput.GetDown(OVRInput.Button.One, OVRInput.Controller.RTouch);
            bool v = ConfirmOverride;
            ConfirmOverride = false;
            return v;
        }
    }

    // Hold the right thumbstick click (or left Y) to restart the run.
    public static bool RestartHeld => UseOverride
        ? RestartOverride
        : OVRInput.Get(OVRInput.Button.PrimaryThumbstick, OVRInput.Controller.RTouch)
          || OVRInput.Get(OVRInput.Button.Four, OVRInput.Controller.LTouch);
}
