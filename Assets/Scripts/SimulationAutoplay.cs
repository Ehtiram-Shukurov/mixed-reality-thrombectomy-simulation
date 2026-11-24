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
        Log("start: clot node " + _clot.ClotNode.id + ", phase " + _sm.CurrentPhase);

        yield return RunOnce(takeWrongTurn);

        // Play Again
        Log("press A on the results screen (Play Again shortcut)");
        SimInput.ConfirmOverride = true;
        yield return null;
        yield return null;
        Log("after Play Again: phase " + _sm.CurrentPhase + ", depth " + _gw.HistoryDepth
            + ", trail " + _gw.TrailPoints.Count + ", atFork " + _gw.IsAtFork
            + ", panelActive " + _rp.gameObject.activeSelf + ", new clot node " + _clot.ClotNode.id);

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

    private IEnumerator RunOnce(bool wrongTurn)
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

        // ---- Phase 2a: catheter advance ----
        t0 = Time.time;
        while (_sm.CurrentPhase == SimulationManager.SimPhase.Phase2a_CatheterAdvance && Time.time - t0 < 300f)
        {
            SimInput.StickOverride = new Vector2(0f, 1f);
            yield return null;
        }
        Log("phase 2a done, phase now " + _sm.CurrentPhase);

        // ---- Phase 2b: stent ----
        t0 = Time.time;
        while (_sm.CurrentPhase == SimulationManager.SimPhase.Phase2b_StentDeploy && Time.time - t0 < 20f)
        {
            SimInput.StickOverride = Vector2.zero;
            SimInput.StentOverride = true;
            yield return null;
        }
        var stent = FindFirstObjectByType<StentVisual>();
        Log("stent deployed, phase now " + _sm.CurrentPhase + ", clot rides tip " + (_clot != null)
            + ", stent object " + (stent != null));

        // ---- Phase 2c: extraction. Pull too fast at first to trigger a warning. ----
        t0 = Time.time;
        bool clotVisibleSeen = false;
        bool sawTooFast = false, sawOk = false;
        float maxPace = 0f, minPaceOk = 9f;
        var pace = FindFirstObjectByType<PaceHint>();
        float paceAlphaFast = -1f, paceAlphaOk = -1f;
        string paceTextFast = "", paceTextOk = "";
        while (_sm.CurrentPhase == SimulationManager.SimPhase.Phase2c_Extraction && Time.time - t0 < 400f)
        {
            float push = (Time.time - t0) < 1.5f ? 1f : 0.3f;
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
