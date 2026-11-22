using UnityEngine;
using UnityEngine.UI;
using TMPro;

// Teal outline and brighter label on the menu button whose render mode is active.
// It follows the real _RenderMode, so it stays right even though the toggles flip on and off.
public class SelectedAccent : MonoBehaviour
{
    public Toggle toggle;           // the button (kept for the scene wiring)
    public int modeIndex = -1;      // the mode this button sets
    public Image accent;
    public TextMeshProUGUI label;
    public Color labelOn = Color.white;
    public Color labelOff = new Color(0.78f, 0.82f, 0.88f, 1f);

    private DicomVolumeLoader _loader;

    void Update()
    {
        if (_loader == null) _loader = FindFirstObjectByType<DicomVolumeLoader>();

        bool on = _loader != null && _loader.volumeMaterial != null && modeIndex >= 0
                  && Mathf.RoundToInt(_loader.volumeMaterial.GetFloat("_RenderMode")) == modeIndex;

        if (accent != null) accent.enabled = on;
        if (label != null) label.color = on ? labelOn : labelOff;
    }
}
