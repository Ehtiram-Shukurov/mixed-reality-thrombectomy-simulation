using UnityEngine;

// Training or Assessment.
// Training shows the hints: branch lines, the fork dial, the next-fork ring, twist directions,
// the tight-bend text and the clot marker. Assessment hides them, so the run log measures the
// task itself. X-ray, dye, the monitor roadmap, haptics and the wire's own tip stay in both.
public static class SimMode
{
    public static bool Assessment { get; set; }
    public static bool HintsOn => !Assessment;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics() => Assessment = false;
}
