using UnityEngine;

// Marks a panel as a pop-up. While any pop-up is open the modal layer dims everything behind it.
public class ModalPanel : MonoBehaviour
{
    void OnEnable() => ModalLayer.Instance?.Opened(this);
    void OnDisable() => ModalLayer.Instance?.Closed(this);
}
