using UnityEngine;

// Makes specific doors taller at runtime, keeping their bottom on the floor.
// Only the listed doors' Transforms change (their mesh colliders follow automatically).
// The scene file is not modified, so the change exists only while the game is playing.
public static class DoorHeightBoost
{
    // Exact GameObject names to change. Add more names here if needed.
    static readonly string[] TargetDoorNames = { "Door.024" };

    // 1.3 = 30% taller. Try values between 1.2 and 1.5.
    const float HeightMultiplier = 1.3f;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Apply()
    {
        foreach (MeshFilter mf in Object.FindObjectsByType<MeshFilter>(FindObjectsSortMode.None))
        {
            if (mf.sharedMesh == null || System.Array.IndexOf(TargetDoorNames, mf.gameObject.name) < 0)
                continue;

            Transform t = mf.transform;
            Mesh mesh = mf.sharedMesh;

            // A point on the bottom edge of the door, in local space
            Vector3 bottom = new Vector3(mesh.bounds.center.x, mesh.bounds.min.y, mesh.bounds.center.z);
            Vector3 worldBefore = t.TransformPoint(bottom);

            // Stretch only the vertical axis
            Vector3 s = t.localScale;
            t.localScale = new Vector3(s.x, s.y * HeightMultiplier, s.z);

            // Slide the door back so its bottom edge stays where it was
            // (this works even if the pivot is in the middle of the door)
            Vector3 worldAfter = t.TransformPoint(bottom);
            t.position += worldBefore - worldAfter;
        }
    }
}