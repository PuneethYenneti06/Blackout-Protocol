using UnityEngine;

// Only for Door.024. While it is open, its solid mesh collider is switched off so the
// doorway is clear, and an invisible trigger box keeps the E prompt working.
// Triggers don't block walking or bullets. While closed, the door is solid as normal.
// Nothing in the scene file is changed.
public class DoorClearWhenOpen : MonoBehaviour
{
    const string TargetName = "Door.024";

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Create()
    {
        GameObject door = GameObject.Find(TargetName);
        if (door != null && door.GetComponent<DoorClearWhenOpen>() == null)
            door.AddComponent<DoorClearWhenOpen>();
    }

    MeshCollider solid;
    BoxCollider trigger;
    Door door;

    void Awake()
    {
        solid = GetComponent<MeshCollider>();
    }

    void Update()
    {
        if (solid == null) return;

        // The Door component is added by the scene setup, so it may appear a moment later
        if (door == null) door = GetComponent<Door>();
        if (door == null) return;

        bool open = door.IsOpen;
        solid.enabled = !open;

        // A mesh collider can't be a trigger unless it's convex, so use a separate box
        if (open && trigger == null) CreateTrigger();
        if (trigger != null) trigger.enabled = open;
    }

    void CreateTrigger()
    {
        Mesh mesh = GetComponent<MeshFilter>().sharedMesh;
        trigger = gameObject.AddComponent<BoxCollider>();
        trigger.isTrigger = true;
        trigger.center = mesh.bounds.center;
        trigger.size = mesh.bounds.size;
    }
}