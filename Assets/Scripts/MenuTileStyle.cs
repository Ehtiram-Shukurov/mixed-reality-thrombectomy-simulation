using UnityEngine;
using UnityEngine.UI;
using TMPro;

// Recolors a menu button after its animator runs. The animator still decides hover and press
// (it makes the gray lighter or darker); this keeps that change but applies it to our colors.
public class MenuTileStyle : MonoBehaviour
{
    public Color tile = new Color(0.118f, 0.13f, 0.15f, 1f);
    public Color selectedIcon = new Color(0.17f, 0.77f, 0.71f, 1f);

    // Gray the button prefab uses when idle.
    private const float AnimatorIdle = 0.294f;

    private Image _background, _icon;
    private Toggle _toggle;

    void Start()
    {
        var bg = transform.Find("Content/Background");
        if (bg != null) _background = bg.GetComponent<Image>();
        var icon = transform.Find("Content/Background/Elements/Icon");
        if (icon != null) _icon = icon.GetComponent<Image>();
        _toggle = GetComponent<Toggle>();
    }

    // How much lighter or darker than idle the animator made the button (1 = idle).
    public static float StateFactor(Color animated)
        => Mathf.Clamp(animated.r / AnimatorIdle, 0.6f, 1.6f);

    // Applies a base color with the animator's shading. If the animator did not write this
    // frame, the color is still ours, so the last factor is reused instead of read back.
    public static void Apply(Image img, Color baseColor, ref Color lastWritten, ref float lastFactor)
    {
        if (img.color != lastWritten) lastFactor = StateFactor(img.color);
        lastWritten = new Color(baseColor.r * lastFactor, baseColor.g * lastFactor, baseColor.b * lastFactor, 1f);
        img.color = lastWritten;
    }

    private Color _written = Color.clear;
    private float _factor = 1f;

    void LateUpdate()
    {
        if (_background != null) Apply(_background, tile, ref _written, ref _factor);
        if (_icon != null && _toggle != null)
        {
            if (_toggle.isOn) _icon.color = selectedIcon;
            else if (_icon.color == selectedIcon) _icon.color = new Color(1f, 1f, 1f, 0.9f);
        }
    }
}
