using UnityEngine;

// A simple stent shown around the clot when it is deployed, then carried back with the catheter.
public class StentVisual : MonoBehaviour
{
    public float length = 0.07f;
    public float diameter = 0.028f;
    public float growSeconds = 0.4f;
    public Color color = new Color(0.18f, 0.83f, 0.75f, 0.55f);

    private CatheterSimulator _catheter;
    private GameObject _mesh;
    private float _t = -1f;

    void Awake()
    {
        _mesh = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        _mesh.name = "StentMesh";
        Destroy(_mesh.GetComponent<Collider>());
        _mesh.transform.SetParent(transform, false);
        var mat = new Material(Shader.Find("Sprites/Default"));
        mat.color = color;
        _mesh.GetComponent<Renderer>().sharedMaterial = mat;
        _mesh.SetActive(false);
    }

    void Start()
    {
        _catheter = FindFirstObjectByType<CatheterSimulator>();
    }

    public void Deploy()
    {
        _t = 0f;
        _mesh.SetActive(true);
        Place(0f);
    }

    public void Hide()
    {
        _t = -1f;
        _mesh.SetActive(false);
    }

    void LateUpdate()
    {
        if (_t < 0f) return;
        _t += Time.deltaTime;
        Place(Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(_t / growSeconds)));
    }

    private void Place(float grow)
    {
        if (_catheter == null || _catheter.catheterTipTransform == null) return;
        transform.position = _catheter.catheterTipTransform.position;
        transform.rotation = Quaternion.FromToRotation(Vector3.up, _catheter.TipDirection);
        // The Unity cylinder is 2 units tall and 1 unit wide.
        float d = diameter * grow;
        _mesh.transform.localScale = new Vector3(d, length * 0.5f, d);
    }
}
