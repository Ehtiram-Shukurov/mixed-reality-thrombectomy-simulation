using UnityEngine;
using UnityEngine.UI;
using TMPro;

// Small dial that floats near the wire tip at a fork.
// The mark at the top is your twist. Each dot is a branch.
// Push the stick toward a dot to bring it onto the mark, then push forward.
public class ForkDial : MonoBehaviour
{
    public GuidewireSimulator guidewire;
    public CanvasGroup group;
    public Image ring;
    public Image mark;
    public Image[] dots;
    public TextMeshProUGUI label;

    [Tooltip("Dot distance from the center, in canvas units.")]
    public float radius = 70f;
    [Tooltip("How far toward the user the dial floats, in meters.")]
    public float towardUser = 0.12f;
    [Tooltip("How far above the wire tip, in meters.")]
    public float above = 0.05f;
    public float fadeSpeed = 10f;

    public Color idleColor = new Color(0.98f, 0.75f, 0.14f, 1f);
    public Color alignedColor = new Color(0.30f, 0.85f, 0.50f, 1f);

    private Transform _head;

    void Start()
    {
        if (guidewire == null) guidewire = FindFirstObjectByType<GuidewireSimulator>();
        var rig = FindFirstObjectByType<OVRCameraRig>();
        if (rig != null) _head = rig.centerEyeAnchor;
        if (group != null) group.alpha = 0f;
        // The dial floats inside the volume's depth range, so draw it without a depth test.
        UiDepth.MakeAlwaysVisible(gameObject);
    }

    void LateUpdate()
    {
        if (guidewire == null || group == null) return;

        bool show = guidewire.IsReady && guidewire.IsAtFork && SimMode.HintsOn;
        group.alpha = Mathf.MoveTowards(group.alpha, show ? 1f : 0f, Time.unscaledDeltaTime * fadeSpeed);
        if (group.alpha <= 0.01f) return;

        // Float in front of the tip, facing the user.
        Vector3 tip = guidewire.wireTip.position;
        Vector3 headPos = _head != null ? _head.position : tip + Vector3.back;
        Vector3 toHead = (headPos - tip).normalized;
        transform.position = tip + toHead * towardUser + Vector3.up * above;
        Vector3 away = transform.position - headPos;
        if (away.sqrMagnitude > 0.0001f)
            transform.rotation = Quaternion.LookRotation(away, Vector3.up);

        if (!show) return;

        bool anyAligned = false;
        for (int i = 0; i < dots.Length; i++)
        {
            bool used = i < guidewire.DialCount;
            dots[i].gameObject.SetActive(used);
            if (!used) continue;

            float a = guidewire.DialAngles[i];
            float rad = a * Mathf.Deg2Rad;
            ((RectTransform)dots[i].transform).anchoredPosition = new Vector2(Mathf.Sin(rad), Mathf.Cos(rad)) * radius;

            bool aligned = Mathf.Abs(Mathf.DeltaAngle(0f, a)) <= guidewire.AlignmentTolerance;
            anyAligned |= aligned;
            dots[i].color = aligned ? alignedColor : idleColor;
            dots[i].transform.localScale = Vector3.one * (aligned ? 1.3f : 1f);
        }

        Color c = anyAligned ? alignedColor : Color.white;
        if (mark != null) mark.color = c;
        if (ring != null) ring.color = new Color(c.r, c.g, c.b, 0.55f);

        if (label != null)
        {
            int dir = guidewire.TwistDirection;
            if (dir != 0) label.text = dir < 0 ? "<< Twist left" : "Twist right >>";
            else label.text = guidewire.HoldProgress >= 1f ? "Push forward" : "Hold steady";
            label.color = anyAligned ? alignedColor : Color.white;
        }
    }
}
