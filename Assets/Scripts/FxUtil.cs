using UnityEngine;

// Small helpers for the cheap visual effects (tip glow, fork ring, volume frame).
public static class FxUtil
{
    private static Texture2D _glow;

    // A soft round glow, white with a fading alpha. Tinted by the material color.
    public static Texture2D GlowTexture()
    {
        if (_glow != null) return _glow;
        const int size = 64;
        _glow = new Texture2D(size, size, TextureFormat.RGBA32, false);
        float c = (size - 1) * 0.5f;
        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                float d = Vector2.Distance(new Vector2(x, y), new Vector2(c, c)) / c;
                float a = Mathf.Clamp01(1f - d);
                a = a * a * (3f - 2f * a); // smooth falloff
                _glow.SetPixel(x, y, new Color(1f, 1f, 1f, a));
            }
        }
        _glow.wrapMode = TextureWrapMode.Clamp;
        _glow.filterMode = FilterMode.Bilinear;
        _glow.Apply();
        return _glow;
    }

    // A material that is drawn on top of the volume (no depth test, after the volume).
    public static Material Overlay(Texture tex)
    {
        var m = new Material(Shader.Find("UI/Default"));
        m.SetInt("unity_GUIZTestMode", (int)UnityEngine.Rendering.CompareFunction.Always);
        m.renderQueue = 3500;
        if (tex != null) m.mainTexture = tex;
        return m;
    }

    // Makes a quad (no collider) under a parent. Works in the editor and in play mode.
    public static GameObject MakeQuad(string name, Transform parent)
    {
        var quad = GameObject.CreatePrimitive(PrimitiveType.Quad);
        quad.name = name;
        var col = quad.GetComponent<Collider>();
        if (col != null)
        {
            if (Application.isPlaying) Object.Destroy(col); else Object.DestroyImmediate(col);
        }
        quad.transform.SetParent(parent, false);
        var r = quad.GetComponent<Renderer>();
        r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        r.receiveShadows = false;
        return quad;
    }

    // Turns a quad to face the head. The quad is visible from either side.
    public static void Billboard(Transform quad, Transform head)
    {
        if (head == null) return;
        Vector3 away = quad.position - head.position;
        if (away.sqrMagnitude > 0.0001f)
            quad.rotation = Quaternion.LookRotation(away, Vector3.up);
    }

    public static Transform FindHead()
    {
        var rig = Object.FindFirstObjectByType<OVRCameraRig>();
        if (rig != null && rig.centerEyeAnchor != null) return rig.centerEyeAnchor;
        return Camera.main != null ? Camera.main.transform : null;
    }
}
