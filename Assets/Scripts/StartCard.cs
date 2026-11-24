using UnityEngine;
using TMPro;

// Card shown before each run. The first time it has three pages: what the procedure is,
// the controls, then the case. After that only the case page. A moves on, and on the case
// page A starts the run. B turns the guided tutorial on or off (Training mode only).
public class StartCard : MonoBehaviour
{
    public SimulationManager simManager;
    public ClotTarget clotTarget;
    public TutorialGuide tutorial;
    public GameObject phaseCard; // hidden while the card shows

    public TextMeshProUGUI title;
    public TextMeshProUGUI body;
    public TextMeshProUGUI footer;

    // The first run gets the tutorial unless it is turned off.
    public bool TutorialOn { get; private set; } = true;
    public bool Showing => gameObject.activeSelf;

    private enum Page { Intro, Controls, Case }
    private Page _page;
    private int _shownFrame;
    private float _shownAt;
    private CanvasGroup _group;

    private const string Grey = "#9AA1A9";

    public void Show(bool firstTime)
    {
        _page = firstTime ? Page.Intro : Page.Case;
        _shownFrame = Time.frameCount;
        _shownAt = Time.unscaledTime;
        if (phaseCard != null) phaseCard.SetActive(false);
        gameObject.SetActive(true);
        Refresh();
    }

    // showPhaseCard: false when the card closes without starting a run (about, settings, home).
    public void Hide(bool showPhaseCard = true)
    {
        gameObject.SetActive(false);
        _aboutDone = null;
        if (showPhaseCard && phaseCard != null) phaseCard.SetActive(true);
    }

    public void SetTutorial(bool on) => TutorialOn = on;

    // About from the home screen: what the procedure is and the controls, then back.
    private System.Action _aboutDone;
    public void ShowAbout(System.Action done)
    {
        Show(true);
        _aboutDone = done;
    }

    void Update()
    {
        // Small pop and fade in, like the results panel.
        float t = Mathf.Clamp01((Time.unscaledTime - _shownAt) / 0.25f);
        float s = Mathf.SmoothStep(0f, 1f, t);
        transform.localScale = Vector3.one * Mathf.Lerp(0.9f, 1f, s);
        if (_group == null) _group = GetComponent<CanvasGroup>();
        if (_group == null) _group = gameObject.AddComponent<CanvasGroup>();
        _group.alpha = s;

        // The press that opened the card does not also move it on.
        if (Time.frameCount <= _shownFrame + 1) return;

        if (SimInput.ConfirmPressed)
        {
            Advance();
            return;
        }
        else if (_page == Page.Case && SimInput.StentPressed && SimMode.HintsOn)
        {
            TutorialOn = !TutorialOn;
            Refresh();
        }

        // Mode or case can change from the menu while the card is up.
        if (_page == Page.Case) Refresh();
    }

    // The footer button (poke) and the A button do the same: next page, start, or back.
    public void Advance()
    {
        if (!Showing || Time.frameCount <= _shownFrame + 1) return;
        Debug.Log("[StartCard] advance from page " + _page);
        if (_aboutDone != null && _page == Page.Controls)
        {
            var done = _aboutDone;
            Hide(false);
            done();
            return;
        }
        if (_page == Page.Case) { StartRun(); return; }
        _page++;
        _shownAt = Time.unscaledTime;
        Refresh();
    }

    private void StartRun()
    {
        Hide();
        simManager?.BeginRun();
        if (tutorial != null && TutorialOn && SimMode.HintsOn) tutorial.Begin();
        // The tutorial is offered once; later runs start without it unless turned on again.
        TutorialOn = false;
    }

    private void Refresh()
    {
        string head, text, foot;
        switch (_page)
        {
            case Page.Intro:
                head = "Thrombectomy in mixed reality";
                text = "A stroke is often caused by a clot in an artery. In a thrombectomy the doctor pushes "
                     + "a thin wire to the clot, slides a catheter over it, opens a mesh stent in the clot and "
                     + "pulls it out.\n\n"
                     + "You do these steps in a real CT scan of the chest and belly, so the clot is in a body "
                     + "artery, not the brain.\n\n"
                     + $"<color={Grey}>Simplified training, not a medical device.</color>";
                foot = Footer("continue");
                break;

            case Page.Controls:
                head = "Controls";
                // The five you use all the time, then the rest in one quiet line.
                text = "<line-height=150%>"
                     + Row("Push / pull", "stick up/down")
                     + Row("Turn at a fork", "stick left/right")
                     + Row("Inject dye", "B")
                     + Row("X-ray", "hold A")
                     + Row("Restart", "hold stick")
                     + "</line-height>\n"
                     + $"<size=85%><color={Grey}>Pinch to grab the model and poke the menu. "
                     + "Left palm pinch hides the menu. Tap the stick to turn the C-arm.</color></size>";
                foot = Footer(_aboutDone != null ? "go back" : "continue");
                break;

            default:
                head = "Case: " + (clotTarget != null ? clotTarget.caseLevel.ToString() : "");
                int forks = clotTarget != null ? clotTarget.ForksOnPath : 0;
                string twist = clotTarget == null ? "" : clotTarget.caseLevel switch
                {
                    ClotTarget.CaseLevel.Easy => "Forks accept a wide twist.",
                    ClotTarget.CaseLevel.Hard => "Forks need a tight, steady twist.",
                    _ => "Forks need a normal twist."
                };
                string mode = SimMode.HintsOn
                    ? "Training: fork hints and a clot marker help you."
                    : "Assessment: no hints. Your run is scored.";
                string tut = SimMode.HintsOn
                    ? $"\n\nGuided tutorial: <b>{(TutorialOn ? "on" : "off")}</b>  <color={Grey}>(B to switch)</color>"
                    : "";
                text = $"{forks} fork{(forks == 1 ? "" : "s")} on the way to the clot. {twist}\n\n"
                     + "Find the clot with dye, cross it with the wire, open the stent and pull the clot out slowly.\n\n"
                     + $"<color={Grey}>{mode}</color>" + tut;
                foot = Footer("start");
                break;
        }

        if (title != null && title.text != head) title.text = head;
        if (body != null && body.text != text) body.text = text;
        if (footer != null && footer.text != foot) footer.text = foot;
    }

    private static string Row(string action, string key) => $"{action}<pos=50%><color={Grey}>{key}</color>\n";

    // The footer is a poke button; A on the controller does the same.
    private static string Footer(string what)
    {
        string label = what == "start" ? "Start" : what == "go back" ? "Back" : "Continue";
        return $"<b>{label}</b>  <size=80%><color={Grey}>or press</color> <color={Teal}><b>A</b></color></size>";
    }
    private const string Teal = "#2BC4B4";

    void OnDisable()
    {
        transform.localScale = Vector3.one;
    }
}
