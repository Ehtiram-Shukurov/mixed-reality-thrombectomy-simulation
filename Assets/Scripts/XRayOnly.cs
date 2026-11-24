using System.Collections.Generic;
using UnityEngine;

// A renderer that only shows on the X-ray monitor, never in the 3D view.
// Used for things a real X-ray shows but the eye does not, like the stent's metal markers.
[RequireComponent(typeof(Renderer))]
public class XRayOnly : MonoBehaviour
{
    public static readonly List<XRayOnly> All = new List<XRayOnly>();

    // Whether it should appear on the monitor right now (for example only once the stent is open).
    public bool wanted;
    public Renderer Renderer { get; private set; }

    void Awake()
    {
        Renderer = GetComponent<Renderer>();
        Renderer.enabled = false;
    }

    void OnEnable() => All.Add(this);
    void OnDisable() => All.Remove(this);
}
