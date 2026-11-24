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

    private const string Grey = "#8B96A5";

    public void Show(bool firstTime)
    {
        _page = firstTime ? Page.Intro : Page.Case;
        _shownFrame = Time.frameCount;
        _shownAt = Time.unscaledTime;
        if (phaseCard != null) phaseCard.SetActive(false);
        gameObject.SetActive(true);
        Refresh();
    }

    public void Hide()
    {
        gameObject.SetActive(false);
        if (phaseCard != null) phaseCard.SetActive(true);
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
            if (_page == Page.Case) { StartRun(); return; }
            _page++;
            _shownAt = Time.unscaledTime;
            Refresh();
        }
        else if (_page == Page.Case && SimInput.StentPressed && SimMode.HintsOn)
        {
            TutorialOn = !TutorialOn;
            Refresh();
        }

        // Mode or case can change from the menu while the card is up.
        if (_page == Page.Case) Refresh();
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
                foot = "A  next";
                break;

            case Page.Controls:
                head = "Controls";
                text = $"<color={Grey}>Right controller</color>\n"
                     + Row("Stick up / down", "push / pull")
                     + Row("Stick left / right", "turn at a fork")
                     + Row("B", "inject dye")
                     + Row("Hold A", "X-ray")
                     + Row("Tap stick", "turn C-arm")
                     + Row("Hold stick", "restart")
                     + $"\n<color={Grey}>Hands</color>\n"
                     + Row("Pinch", "grab model")
                     + Row("Poke", "menu buttons");
                foot = "A  next";
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
                foot = "A  start";
                break;
        }

        if (title != null && title.text != head) title.text = head;
        if (body != null && body.text != text) body.text = text;
        if (footer != null && footer.text != foot) footer.text = foot;
    }

    private static string Row(string key, string what) => $"<b>{key}</b><pos=56%>{what}\n";

    void OnDisable()
    {
        transform.localScale = Vector3.one;
    }
}
