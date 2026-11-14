using UnityEngine;

// Replaced by Interactor (Assets/Scripts/Interaction), which handles doors and every other
// IInteractable. This class stays only so existing scenes/prefabs that reference it keep
// working: it swaps itself for an Interactor on the same object.
public class DoorInteractor : MonoBehaviour
{
    void Awake()
    {
        if (GetComponent<Interactor>() == null) gameObject.AddComponent<Interactor>();
        Destroy(this);
    }
}
