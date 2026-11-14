using System.Collections.Generic;
using UnityEngine;

// Records where the player has walked (a footstep every 0.35 m), so freed hostages can follow
// the exact same route: through doorways and around furniture, wherever the player went.
// Added to the player automatically by the first hostage that needs it.
public class PlayerTrail : MonoBehaviour
{
    public float spacing = 0.35f;     // metres between recorded footsteps
    public int maxPoints = 400;       // about 140 m of path

    readonly List<Vector3> points = new List<Vector3>();
    int dropped;                      // footsteps removed from the start (keeps indexes stable)

    public static PlayerTrail For(Transform player)
    {
        PlayerTrail t = player.GetComponent<PlayerTrail>();
        if (t == null) t = player.gameObject.AddComponent<PlayerTrail>();
        return t;
    }

    void Awake()
    {
        points.Add(transform.position);
    }

    void LateUpdate()
    {
        Vector3 p = transform.position;
        Vector3 last = points[points.Count - 1];
        Vector3 d = p - last; d.y = 0f;
        if (d.magnitude < spacing) return;

        // Long jump (respawn/teleport): start a fresh trail rather than drawing a line through walls
        if (d.magnitude > 3f)
        {
            dropped += points.Count;
            points.Clear();
        }

        points.Add(p);
        if (points.Count > maxPoints)
        {
            points.RemoveAt(0);
            dropped++;
        }
    }

    // Footstep by absolute index (stays valid as old footsteps are dropped)
    public Vector3 Point(int index)
    {
        int i = Mathf.Clamp(index - dropped, 0, points.Count - 1);
        return points[i];
    }

    // The footstep nearest to a position (where a hostage should join the trail)
    public int NearestIndex(Vector3 position)
    {
        int best = 0;
        float bestDist = float.MaxValue;
        for (int i = 0; i < points.Count; i++)
        {
            Vector3 d = points[i] - position; d.y = 0f;
            float dist = d.sqrMagnitude;
            if (dist < bestDist) { bestDist = dist; best = i; }
        }
        return best + dropped;
    }

    // The spot on the trail a given distance behind the player, measured along the path walked
    public Vector3 PointBehind(float distance, out int index)
    {
        Vector3 prev = transform.position;
        float walked = 0f;
        for (int i = points.Count - 1; i >= 0; i--)
        {
            Vector3 d = points[i] - prev; d.y = 0f;
            walked += d.magnitude;
            prev = points[i];
            if (walked >= distance)
            {
                index = i + dropped;
                return points[i];
            }
        }
        index = dropped;
        return points[0];
    }
}
