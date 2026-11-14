using System.Collections.Generic;
using UnityEngine;

// The extraction point near the entrance. Each following hostage that walks into it is saved
// and removed. The area is this object's BoxCollider (added as a trigger if missing).
// Hostages check IsInside themselves every frame, so no Rigidbody is needed.
public class ExtractionZone : MonoBehaviour
{
    static readonly List<ExtractionZone> zones = new List<ExtractionZone>();

    public Vector3 defaultSize = new Vector3(4f, 3f, 4f);

    BoxCollider box;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics()
    {
        zones.Clear();
    }

    void Awake()
    {
        box = GetComponent<BoxCollider>();
        if (box == null)
        {
            box = gameObject.AddComponent<BoxCollider>();
            box.size = defaultSize;
            box.center = new Vector3(0f, defaultSize.y * 0.5f, 0f);
        }
        box.isTrigger = true;
    }

    void OnEnable() { if (!zones.Contains(this)) zones.Add(this); }
    void OnDisable() { zones.Remove(this); }

    public bool Contains(Vector3 worldPoint)
    {
        if (box == null) return false;
        Vector3 local = transform.InverseTransformPoint(worldPoint + Vector3.up * 0.1f) - box.center;
        Vector3 half = box.size * 0.5f;
        return Mathf.Abs(local.x) <= half.x && Mathf.Abs(local.y) <= half.y && Mathf.Abs(local.z) <= half.z;
    }

    public static bool IsInside(Vector3 worldPoint)
    {
        foreach (ExtractionZone z in zones)
            if (z != null && z.Contains(worldPoint)) return true;
        return false;
    }

    void OnDrawGizmos()
    {
        BoxCollider b = GetComponent<BoxCollider>();
        Vector3 size = b != null ? b.size : defaultSize;
        Vector3 center = b != null ? b.center : new Vector3(0f, defaultSize.y * 0.5f, 0f);
        Gizmos.matrix = transform.localToWorldMatrix;
        Gizmos.color = new Color(0.2f, 1f, 0.3f, 0.25f);
        Gizmos.DrawCube(center, size);
        Gizmos.color = new Color(0.2f, 1f, 0.3f, 0.9f);
        Gizmos.DrawWireCube(center, size);
    }
}
