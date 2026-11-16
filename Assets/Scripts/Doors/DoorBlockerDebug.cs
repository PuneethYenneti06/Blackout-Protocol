using UnityEngine;

// Debug helper: in Play mode, stand at the door and press P.
// It logs every collider touching the player's body and the doorway.
// It creates itself at runtime and changes nothing in the scene.
public class DoorBlockerDebug : MonoBehaviour
{
    const string DoorName = "Door.024";

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Create()
    {
        new GameObject("DoorBlockerDebug").AddComponent<DoorBlockerDebug>();
    }

    void Update()
    {
        if (!Input.GetKeyDown(KeyCode.P)) return;

        PlayerMovement player = FindFirstObjectByType<PlayerMovement>();
        GameObject door = GameObject.Find(DoorName);
        if (player == null || door == null)
        {
            Debug.Log($"[DoorBlockerDebug] Player or {DoorName} not found.");
            return;
        }

        CapsuleCollider capsule = player.GetComponent<CapsuleCollider>();
        float playerHeight = capsule != null ? capsule.height * player.transform.lossyScale.y : 1.8f;

        Renderer doorRend = door.GetComponent<Renderer>();
        Bounds db = doorRend != null ? doorRend.bounds : new Bounds(door.transform.position, Vector3.one);

        Vector3 feet = player.transform.position;
        Debug.Log($"[DoorBlockerDebug] Player feet y={feet.y:F2}, head y={feet.y + playerHeight:F2}. " +
                  $"Door bottom y={db.min.y:F2}, top y={db.max.y:F2}.");

        // Colliders touching a box around the player's body
        Vector3 playerCenter = feet + Vector3.up * (playerHeight * 0.5f);
        Vector3 playerHalf = new Vector3(0.6f, playerHeight * 0.5f, 0.6f);
        LogOverlaps("Around player", playerCenter, playerHalf, player.transform);

        // Colliders touching a box around the doorway
        LogOverlaps("Around door", db.center, db.extents + Vector3.one * 0.6f, player.transform);
    }

    static void LogOverlaps(string label, Vector3 center, Vector3 halfExtents, Transform self)
    {
        Collider[] hits = Physics.OverlapBox(center, halfExtents, Quaternion.identity, ~0, QueryTriggerInteraction.Ignore);
        Debug.Log($"[DoorBlockerDebug] {label}: {hits.Length} collider(s)");
        foreach (Collider c in hits)
        {
            if (c.transform.IsChildOf(self)) continue; // skip the player's own colliders
            Bounds b = c.bounds;
            Debug.Log($"  {PathOf(c.transform)}  [{c.GetType().Name}]  y {b.min.y:F2} to {b.max.y:F2}");
        }
    }

    static string PathOf(Transform t)
    {
        string s = t.name;
        while (t.parent != null)
        {
            t = t.parent;
            s = t.name + "/" + s;
        }
        return s;
    }
}