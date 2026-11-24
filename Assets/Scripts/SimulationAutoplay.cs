using System.Collections;
using System.IO;
using UnityEngine;

// Test driver. Plays a full run with scripted input so the logic can be checked without a headset.
// It is not in the scene. Add it to any object in Play mode, then read the Console.
public class SimulationAutoplay : MonoBehaviour
{
    public float timeScale = 5f;
    public bool takeWrongTurn = true;
    public bool testRestartHold = true;
    [Tooltip("Only walk through the guided tutorial on the first run, then stop.")]
    public bool tutorialOnly;

    public string Report = "";
    public bool Done;

    private GuidewireSimulator _gw;
    private SimulationManager _sm;
    private ClotTarget _clot;
    private ResultsPanel _rp;
    private CatheterSimulator _cath;
    private CenterlineNode _forceWrong;

    private void Log(string s)
    {
        Report += s + "\n";
        Debug.Log("[Autoplay] " + s);
    }

    IEnumerator Start()
    {
        _gw = FindFirstObjectByType<GuidewireSimulator>();
        _sm = FindFirstObjectByType<SimulationManager>();
        _clot = FindFirstObjectByType<ClotTarget>(FindObjectsInactive.Include);
        _rp = FindFirstObjectByType<ResultsPanel>(FindObjectsInactive.Include);
        _cath = FindFirstObjectByType<CatheterSimulator>();

        while (!_gw.IsReady || _clot.ClotNode == null) yield return null;

        _gw.BranchChosen += n =>
        {
            Log("branch chosen " + n.id + ", on path " + _clot.IsOnPath(n));
            _forceWrong = null;
        };
        _gw.DeadEndReached += () => Log("DeadEndReached event");

        SimInput.UseOverride = true;
        Time.timeScale = timeScale;
        Log("start: clot node " + _clot.ClotNode.id + ", case " + _clot.CaseLabel + ", phase " + _sm.CurrentPhase);
        yield return PassCard();

        if (tutorialOnly)
        {
            yield return WalkTutorial();
            SimInput.StickOverride = Vector2.zero;
            SimInput.FluoroOverride = false;
            SimInput.UseOverride = false;
            Time.timeScale = 1f;
            Done = true;
            Log("done");
            yield break;
        }

        yield return CheckDyeAndRoadmap();

        yield return RunOnce(takeWrongTurn);

        // Play Again
        Log("press A on the results screen (Play Again shortcut)");
        SimInput.ConfirmOverride = true;
        yield return null;
        yield return null;
        yield return PassCard();
        Log("after Play Again: phase " + _sm.CurrentPhase + ", depth " + _gw.HistoryDepth
            + ", trail " + _gw.TrailPoints.Count + ", atFork " + _gw.IsAtFork
            + ", panelActive " + _rp.gameObject.activeSelf + ", new clot node " + _clot.ClotNode.id);

        // Second run on the same case: a rough first pass that should leave a fragment,
        // then another pass that reopens the vessel.
        yield return new WaitForSecondsRealtime(0.5f);
        Log("second run, same case: clot node " + _clot.ClotNode.id + " (" + _clot.CaseLabel + ")");
        yield return RunOnce(false, true);

        if (testRestartHold)
        {
            // Move a bit, then hold left Y for two seconds.
            SimInput.StickOverride = new Vector2(0f, 1f);
            yield return new WaitForSecondsRealtime(0.6f);
            Log("before restart hold: depth " + _gw.HistoryDepth + ", tip moved " + (_gw.HistoryDepth > 1));
            SimInput.StickOverride = Vector2.zero;
            SimInput.RestartOverride = true;
            float t = 0f;
            float maxProgress = 0f;
            while (t < 2.6f)
            {
                maxProgress = Mathf.Max(maxProgress, _sm.RestartProgress);
                t += Time.unscaledDeltaTime;
                yield return null;
            }
            SimInput.RestartOverride = false;
            yield return null;
            Log("after restart hold: phase " + _sm.CurrentPhase + ", depth " + _gw.HistoryDepth
                + ", trail " + _gw.TrailPoints.Count + ", max progress " + maxProgress.ToString("F2"));
        }

        SimInput.StickOverride = Vector2.zero;
        SimInput.UseOverride = false;
        Time.timeScale = 1f;
        Done = true;
        Log("done");
    }

    // Press A through the start card until the run begins.
    private IEnumerator PassCard()
    {
        var card = FindFirstObjectByType<StartCard>(FindObjectsInactive.Include);
        int presses = 0;
        float t0 = Time.realtimeSinceStartup;
        while (_sm.CurrentPhase == SimulationManager.SimPhase.Idle && Time.realtimeSinceStartup - t0 < 10f)
        {
            if (card != null && card.Showing && card.body != null && presses < 6)
                Log("card: " + (card.title != null ? card.title.text : "") + " | footer " + (card.footer != null ? card.footer.text : ""));
            SimInput.ConfirmOverride = true;
            presses++;
            for (int i = 0; i < 4; i++) yield return null;
        }
        SimInput.ConfirmOverride = false;
        var guide = FindFirstObjectByType<TutorialGuide>();
        Log("card passed after " + presses + " presses, phase " + _sm.CurrentPhase + ", tutorial running " + (guide != null && guide.Running)
            + ", guide text: " + (GuidewireUI.Guide ?? "-"));
    }

    // Does what each tutorial step asks and logs when the step changes.
    private IEnumerator WalkTutorial()
    {
        string last = GuidewireUI.Guide;
        float t0 = Time.realtimeSinceStartup;
        SimInput.ContrastOverride = true;
        while (GuidewireUI.Guide != null && Time.realtimeSinceStartup - t0 < 90f)
        {
            string g = GuidewireUI.Guide;
            Vector2 s = Vector2.zero;
            bool a = false;
            if (g.Contains("2/6") || g.Contains("4/6")) s = new Vector2(0f, 1f);
            else if (g.Contains("3/6")) s = new Vector2(0f, -1f);
            else if (g.Contains("5/6") && _gw.IsAtFork && _gw.DialCount > 0)
            {
                int want = 0;
                for (int i = 0; i < _gw.CurrentNode.branches.Count && i < _gw.DialCount; i++)
                    if (_clot.IsOnPath(_gw.CurrentNode.branches[i])) want = i;
                float ang = _gw.DialAngles[want];
                s = Mathf.Abs(ang) <= _gw.AlignmentTolerance - 3f ? new Vector2(0f, 1f) : new Vector2(ang > 0f ? 1f : -1f, 0f);
            }
            else if (g.Contains("6/6")) a = true;
            SimInput.StickOverride = s;
            SimInput.FluoroOverride = a;
            yield return null;
            if (GuidewireUI.Guide != last)
            {
                last = GuidewireUI.Guide;
                Log("tutorial +" + (Time.realtimeSinceStartup - t0).ToString("F1") + " s: " + (last ?? "(ended)"));
            }
        }
        Log("tutorial walk finished, phase " + _sm.CurrentPhase + ", guide " + (GuidewireUI.Guide ?? "none"));
    }

    // Dye run, vessel mesh mapping, roadmap on the monitor.
    private IEnumerator CheckDyeAndRoadmap()
    {
        var fluoro = FindFirstObjectByType<FluoroController>();
        var mesh = FindFirstObjectByType<VesselMeshView>();
        var mon = FindFirstObjectByType<XRayMonitor>();
        var dye = FindFirstObjectByType<DyeFlow>();

        float t0 = Time.realtimeSinceStartup;
        while (mesh != null && !mesh.MappingReady && Time.realtimeSinceStartup - t0 < 20f) yield return null;
        Log("vessel mesh loaded " + (mesh != null && mesh.IsLoaded) + ", mapped " + (mesh != null && mesh.MappingReady)
            + ", graph nodes " + VesselGraph.Count + ", dye component " + (dye != null) + ", monitor " + (mon != null));

        SimInput.ContrastOverride = true;
        yield return null;
        yield return null;
        Log("after B: contrast active " + fluoro.ContrastActive + ", runs " + fluoro.ContrastRuns
            + ", imaging " + fluoro.IsActive + ", main view X-ray " + fluoro.MainViewXRay + " (should be false)");

        float front = 0f;
        var mat = mesh != null ? mesh.Material : null;
        t0 = Time.time;
        while (fluoro.ContrastActive && Time.time - t0 < 10f)
        {
            if (mat != null) front = Mathf.Max(front, mat.GetFloat("_DyeFront"));
            yield return null;
        }
        Log("dye run ended: max dye front " + front.ToString("F2") + " volume sizes, clot roadmapped " + _clot.IsRoadmapped);

        SimInput.FluoroOverride = true;
        yield return null;
        yield return null;
        Log("hold A: main view X-ray " + fluoro.MainViewXRay + ", monitor roadmap showing " + (mon != null && mon.RoadmapShowing));
        SimInput.FluoroOverride = false;
        yield return null;
        yield return null;
        Log("release A: main view X-ray " + fluoro.MainViewXRay);
    }

    private IEnumerator RunOnce(bool wrongTurn, bool rough = false)
    {
        // ---- Phase 1: guidewire ----
        float t0 = Time.time;
        bool wrongTried = false, retrace = false;
        CenterlineNode wrongFork = null;
        int forkVisits = 0;
        float fluoroUntil = Time.time + 2f;
        SimInput.FluoroOverride = true;
        bool wasAtFork = false;

        while (_sm.CurrentPhase == SimulationManager.SimPhase.Phase1_Navigation && Time.time - t0 < 600f)
        {
            if (Time.time > fluoroUntil) SimInput.FluoroOverride = false;

            var node = _gw.CurrentNode;
            Vector2 s = new Vector2(0f, 1f);

            if (retrace)
            {
                if (_clot.IsOnPath(node)) retrace = false;
                else s = new Vector2(0f, -1f);
            }

            if (!retrace)
            {
                if (_gw.IsAtDeadEnd)
                {
                    retrace = true;
                    Log("dead end at node " + node.id + ", pulling back");
                    s = new Vector2(0f, -1f);
                }
                else if (_gw.IsAtFork)
                {
                    if (!wasAtFork) { forkVisits++; Log("fork at node " + node.id); }
                    if (_gw.DialCount == 0)
                    {
                        // The dial has not been evaluated yet this fork. Wait a frame.
                        s = Vector2.zero;
                    }
                    else
                    {
                        int want = ChooseBranch(node, wrongTurn, ref wrongTried, ref wrongFork);
                        float a = _gw.DialAngles[want];
                        int best = 0;
                        for (int i = 1; i < _gw.DialCount; i++)
                            if (Mathf.Abs(_gw.DialAngles[i]) < Mathf.Abs(_gw.DialAngles[best])) best = i;
                        s = (best == want && Mathf.Abs(a) <= _gw.AlignmentTolerance - 3f)
                            ? new Vector2(0f, 1f)
                            : new Vector2(a > 0f ? 1f : -1f, 0f);
                    }
                }
            }
            wasAtFork = _gw.IsAtFork;
            SimInput.StickOverride = s;
            yield return null;
        }
        SimInput.FluoroOverride = false;
        Log("phase 1 done in " + (Time.time - t0).ToString("F1") + " s, fork visits " + forkVisits
            + ", trail " + _gw.TrailPoints.Count + " depth " + _gw.HistoryDepth + ", phase now " + _sm.CurrentPhase);

        for (int pass = 1; pass <= 3; pass++)
        {
        // ---- Phase 2a: catheter advance ----
        t0 = Time.time;
        while (_sm.CurrentPhase == SimulationManager.SimPhase.Phase2a_CatheterAdvance && Time.time - t0 < 300f)
        {
            SimInput.StickOverride = new Vector2(0f, 1f);
            yield return null;
        }
        Log("pass " + pass + ": phase 2a done, phase now " + _sm.CurrentPhase);

        // ---- Phase 2b: stent ----
        t0 = Time.time;
        while (_sm.CurrentPhase == SimulationManager.SimPhase.Phase2b_StentDeploy && Time.time - t0 < 20f)
        {
            SimInput.StickOverride = Vector2.zero;
            // Press once; a held override would leak into later phases as a stale B press.
            if (!_sm.StentDeployed) SimInput.StentOverride = true;
            yield return null;
        }
        SimInput.StentOverride = false;
        var stent = FindFirstObjectByType<StentVisual>();
        Log("stent deployed, phase now " + _sm.CurrentPhase + ", clot rides tip " + (_clot != null)
            + ", stent object " + (stent != null));

        // ---- Phase 2c: extraction. Careful runs pull too fast once at the start (one warning);
        // the rough first pass pulls in bursts so several warnings leave a fragment. ----
        bool roughNow = rough && pass == 1;
        t0 = Time.time;
        bool clotVisibleSeen = false;
        bool sawTooFast = false, sawOk = false;
        float maxPace = 0f, minPaceOk = 9f;
        var pace = FindFirstObjectByType<PaceHint>();
        float paceAlphaFast = -1f, paceAlphaOk = -1f;
        string paceTextFast = "", paceTextOk = "";
        while (_sm.CurrentPhase == SimulationManager.SimPhase.Phase2c_Extraction && Time.time - t0 < 400f)
        {
            float push = roughNow
                ? (((Time.time - t0) % 1.0f) < 0.5f ? 1f : 0f)
                : ((Time.time - t0) < 1.5f ? 1f : 0.3f);
            SimInput.StickOverride = new Vector2(0f, -push);
            var r = _clot.GetComponent<Renderer>();
            if (r != null && r.enabled) clotVisibleSeen = true;
            if (_cath.IsTooFast) { sawTooFast = true; maxPace = Mathf.Max(maxPace, _cath.PaceRatio); if (pace != null) { paceAlphaFast = pace.group.alpha; paceTextFast = pace.label.text; } }
            else if (_cath.PaceRatio > 0.01f) { sawOk = true; minPaceOk = Mathf.Min(minPaceOk, _cath.PaceRatio); if (pace != null) { paceAlphaOk = pace.group.alpha; paceTextOk = pace.label.text; } }
            yield return null;
        }
        SimInput.StickOverride = Vector2.zero;
        Log("extraction done, phase now " + _sm.CurrentPhase + ", clot was visible on tip " + clotVisibleSeen);
        Log("pace: too fast seen " + sawTooFast + " (max ratio " + maxPace.ToString("F2") + ", meter alpha " + paceAlphaFast.ToString("F2") + ", text '" + paceTextFast + "'); "
            + "ok pace seen " + sawOk + " (ratio " + minPaceOk.ToString("F2") + ", meter alpha " + paceAlphaOk.ToString("F2") + ", text '" + paceTextOk + "')");

        // ---- Control angiogram: wait for it. If a fragment is left, take another pass (B). ----
        t0 = Time.time;
        bool another = false;
        while (_sm.CurrentPhase == SimulationManager.SimPhase.Phase2d_ControlAngio && Time.time - t0 < 30f)
        {
            if (_sm.AngioDone && _sm.CanTryAgain && !another)
            {
                Log("control angiogram after pass " + pass + ": " + _sm.OutcomeText
                    + ", fragment hidden until dye: " + !_clot.GetComponent<Renderer>().enabled);
                SimInput.StentOverride = true;
                another = true;
            }
            yield return null;
        }
        SimInput.StentOverride = false;
        if (another && _sm.CurrentPhase == SimulationManager.SimPhase.Phase2a_CatheterAdvance)
        {
            Log("pressed B: another pass, phase now " + _sm.CurrentPhase);
            continue;
        }
        Log("control angiogram done: " + _sm.OutcomeText + ", phase now " + _sm.CurrentPhase);
        break;
        }

        // ---- Phase 3 ----
        yield return new WaitForSecondsRealtime(0.5f);
        Log("results panel active " + _rp.gameObject.activeSelf + " | "
            + (_rp.totalTimeText != null ? _rp.totalTimeText.text.Replace("\n", " ") : "-") + " | "
            + (_rp.radiationText != null ? _rp.radiationText.text.Replace("\n", " ") : "-") + " | "
            + (_rp.safetyScoreText != null ? _rp.safetyScoreText.text : "-") + " | "
            + (_rp.routeText != null ? _rp.routeText.text : "-"));

        try
        {
            string path = Path.Combine(Application.persistentDataPath, "run_log.jsonl");
            if (File.Exists(path))
            {
                var lines = File.ReadAllLines(path);
                Log("last run_log line: " + lines[lines.Length - 1]);
            }
            else Log("run_log.jsonl not found at " + path);
        }
        catch (System.Exception e) { Log("run log read failed: " + e.Message); }
    }

    private int ChooseBranch(CenterlineNode node, bool wrongTurn, ref bool wrongTried, ref CenterlineNode wrongFork)
    {
        int pathIdx = -1, otherIdx = -1;
        for (int i = 0; i < node.branches.Count && i < GuidewireSimulator.MaxDial; i++)
        {
            if (_clot.IsOnPath(node.branches[i])) pathIdx = i;
            else if (otherIdx < 0) otherIdx = i;
        }

        // Keep aiming at the wrong branch until it has been committed.
        if (_forceWrong == node && otherIdx >= 0) return otherIdx;

        if (wrongTurn && !wrongTried && pathIdx >= 0 && otherIdx >= 0)
        {
            wrongTried = true;
            wrongFork = node;
            _forceWrong = node;
            Log("choosing the WRONG branch at node " + node.id + ": branch ids " + node.branches[0].id + ","
                + node.branches[1].id + " path idx " + pathIdx + " other idx " + otherIdx
                + ", dial angles " + _gw.DialAngles[0].ToString("F0") + "," + _gw.DialAngles[1].ToString("F0"));
            return otherIdx;
        }
        if (pathIdx >= 0) return pathIdx;
        return 0;
    }
}
