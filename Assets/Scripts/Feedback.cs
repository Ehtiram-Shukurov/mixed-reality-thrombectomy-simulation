// One place that fires the haptic and the sound for an event.
public static class Feedback
{
    public static void ForkArrived()
    {
        HapticManager.Instance?.ForkArrived();
        AudioCues.Instance?.Play(AudioCues.Cue.Fork);
    }

    public static void BranchChosen()
    {
        AudioCues.Instance?.Play(AudioCues.Cue.Tick);
    }

    public static void ContrastInjected()
    {
        AudioCues.Instance?.Play(AudioCues.Cue.Tick);
    }

    public static void DeadEnd()
    {
        HapticManager.Instance?.DeadEnd();
        AudioCues.Instance?.Play(AudioCues.Cue.DeadEnd);
    }

    public static void ClotContact()
    {
        HapticManager.Instance?.ClotContact();
        AudioCues.Instance?.Play(AudioCues.Cue.Clot);
    }

    public static void SpeedWarning()
    {
        HapticManager.Instance?.SpeedWarning();
        AudioCues.Instance?.Play(AudioCues.Cue.Warning);
    }

    public static void StentDeployed()
    {
        HapticManager.Instance?.StentDeployed();
        AudioCues.Instance?.Play(AudioCues.Cue.Stent);
    }

    public static void Success()
    {
        HapticManager.Instance?.Success();
        AudioCues.Instance?.Play(AudioCues.Cue.Success);
    }

    public static void PhaseChanged()
    {
        AudioCues.Instance?.Play(AudioCues.Cue.Phase);
    }
}
