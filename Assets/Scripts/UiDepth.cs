using UnityEngine;
using UnityEngine.UI;
using TMPro;

// Draws a world-space canvas without a depth test, so it is not hidden inside the volume.
public static class UiDepth
{
    public static void MakeAlwaysVisible(GameObject root)
    {
        int zTest = Shader.PropertyToID("unity_GUIZTestMode");
        foreach (var g in root.GetComponentsInChildren<Graphic>(true))
        {
            if (g is TMP_Text tmp)
            {
                tmp.fontMaterial.SetInt(zTest, (int)UnityEngine.Rendering.CompareFunction.Always);
            }
            else
            {
                var m = new Material(g.material);
                m.SetInt(zTest, (int)UnityEngine.Rendering.CompareFunction.Always);
                g.material = m;
            }
        }
    }
}
