using UnityEngine;
using UnityEngine.UI;
using TMPro;

// The scan brightness card: two sliders (low and high) with their values shown as percents.
// Colors are set after the sliders' animators run, so the track, fill and handle stay visible.
public class ThresholdCard : MonoBehaviour
{
    public Slider low, high;
    public TextMeshProUGUI lowValue, highValue;

    public Color track = new Color(0.16f, 0.175f, 0.195f, 1f);
    public Color fill = new Color(0.17f, 0.77f, 0.71f, 1f);
    public Color handle = Color.white;

    private float _lastLow = -1f, _lastHigh = -1f;
    private bool _synced;

    // Start the handles where the scan's material already is, so moving one does not jump.
    private void SyncFromMaterial()
    {
        var loader = FindFirstObjectByType<DicomVolumeLoader>();
        var mat = loader != null ? loader.volumeMaterial : null;
        if (mat == null) return;
        if (low != null && mat.HasProperty("_Threshold")) low.SetValueWithoutNotify(mat.GetFloat("_Threshold"));
        if (high != null && mat.HasProperty("_UpperThreshold")) high.SetValueWithoutNotify(mat.GetFloat("_UpperThreshold"));
        _synced = true;
    }

    void LateUpdate()
    {
        if (!_synced) SyncFromMaterial();
        Style(low);
        Style(high);
        Show(low, lowValue, ref _lastLow);
        Show(high, highValue, ref _lastHigh);
    }

    private void Style(Slider s)
    {
        if (s == null) return;
        var bg = s.transform.Find("Background");
        if (bg != null) { var i = bg.GetComponent<Image>(); if (i != null) i.color = track; }
        if (s.fillRect != null) { var i = s.fillRect.GetComponent<Image>(); if (i != null) i.color = fill; }
        if (s.handleRect != null) { var i = s.handleRect.GetComponent<Image>(); if (i != null) i.color = handle; }
    }

    private static void Show(Slider s, TextMeshProUGUI text, ref float last)
    {
        if (s == null || text == null || Mathf.Approximately(s.value, last)) return;
        last = s.value;
        float t = Mathf.InverseLerp(s.minValue, s.maxValue, s.value);
        text.text = Mathf.RoundToInt(t * 100f) + "%";
    }
}
