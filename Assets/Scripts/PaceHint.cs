using UnityEngine;
using UnityEngine.UI;
using TMPro;

// Small pace meter near the catheter tip while pulling back (extraction).
// The bar fills as you pull the stick harder. Past the mark means too fast.
public class PaceHint : MonoBehaviour
{
    public CatheterSimulator catheter;
    public CanvasGroup group;
    public Image fill;
    public TextMeshProUGUI label;

    [Tooltip("How far toward the user the meter floats, in meters.")]
    public float towardUser = 0.12f;
    [Tooltip("How far above the catheter tip, in meters.")]
    public float above = 0.05f;
    public float fadeSpeed = 8f;
    [Tooltip("Bar full length is this many times the allowed pace. The mark sits at 1 / this.")]
    public float fullScale = 1.5f;

    public Color okColor = new Color(0.30f, 0.85f, 0.50f, 1f);
    public Color fastColor = new Color(0.97f, 0.50f, 0.42f, 1f);

    private Transform _head;

    void Start()
    {
        if (catheter == null) catheter = FindFirstObjectByType<CatheterSimulator>();
        var rig = FindFirstObjectByType<OVRCameraRig>();
        if (rig != null) _head = rig.centerEyeAnchor;
        if (group != null) group.alpha = 0f;
        UiDepth.MakeAlwaysVisible(gameObject);
    }

    void LateUpdate()
    {
        if (catheter == null || group == null || catheter.catheterTipTransform == null) return;

        bool show = catheter.IsRetracting;
        group.alpha = Mathf.MoveTowards(group.alpha, show ? 1f : 0f, Time.unscaledDeltaTime * fadeSpeed);
        if (group.alpha <= 0.01f) return;

        Vector3 tip = catheter.catheterTipTransform.position;
        Vector3 headPos = _head != null ? _head.position : tip + Vector3.back;
        Vector3 toHead = (headPos - tip).normalized;
        transform.position = tip + toHead * towardUser + Vector3.up * above;
        Vector3 away = transform.position - headPos;
        if (away.sqrMagnitude > 0.0001f)
            transform.rotation = Quaternion.LookRotation(away, Vector3.up);

        bool fast = catheter.IsTooFast;
        Color c = fast ? fastColor : okColor;
        if (fill != null)
        {
            fill.fillAmount = Mathf.Clamp01(catheter.PaceRatio / fullScale);
            fill.color = c;
        }
        if (label != null)
        {
            label.text = fast ? "Slow down" : catheter.PaceRatio < 0.05f ? "Pull back slowly" : "Good pace";
            label.color = fast ? fastColor : Color.white;
        }
    }
}
