using System.Collections.Generic;
using System.Text.RegularExpressions;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.SceneManagement;

// Puts the hostages into random rooms at the start of every run:
// they are split into two groups (2 + 2 with four hostages), each group in a different room,
// with one hostage guard in each room watching the door.
//
// Rooms are found from the doors in the floor model, so nothing has to be placed in the scene:
// for every door, the side that is closed in on all sides (walls within a few metres) is a room;
// the side with a long open view is the corridor.
public static class HostageRooms
{
    public static int groups = 2;
    public static float minDistanceFromStart = 15f;   // rooms closer than this to the player or extraction are skipped
    public static float minDistanceBetweenRooms = 10f;

    static readonly Regex DoorName = new Regex(@"^Door(\.\d+)?$");

    struct Room
    {
        public Vector3 point;    // a spot inside, about 3 m in from the door
        public Vector3 inward;   // direction from the door into the room
        public Vector3 door;     // the door's position
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Install()
    {
        SceneManager.sceneLoaded -= OnSceneLoaded;
        SceneManager.sceneLoaded += OnSceneLoaded; // also after a restart
        Place();
    }

    static void OnSceneLoaded(Scene scene, LoadSceneMode mode) => Place();

    static int lastPlacedFrame = -1;

    static void Place()
    {
        // The first load reaches here twice (start-up and scene-loaded); place only once
        if (Time.frameCount == lastPlacedFrame) return;
        lastPlacedFrame = Time.frameCount;

        List<Hostage> hostages = new List<Hostage>(Object.FindObjectsByType<Hostage>(FindObjectsSortMode.None));
        if (hostages.Count < 2) return;
        hostages.Sort((a, b) => string.CompareOrdinal(a.name, b.name));

        List<TerroristGuard> guards = new List<TerroristGuard>();
        foreach (TerroristGuard g in Object.FindObjectsByType<TerroristGuard>(FindObjectsSortMode.None))
            if (g.role == GuardRole.Hostage) guards.Add(g);
        guards.Sort((a, b) => string.CompareOrdinal(a.name, b.name));

        List<Vector3> avoid = new List<Vector3>();
        GameObject player = GameObject.FindWithTag("Player");
        if (player != null)
        {
            avoid.Add(player.transform.position);
            Hostage.SafeSpot = player.transform.position; // bring hostages back here to save them
        }
        foreach (ExtractionZone z in Object.FindObjectsByType<ExtractionZone>(FindObjectsSortMode.None))
            avoid.Add(z.transform.position);

        List<Room> rooms = FindRooms(avoid);
        List<Room> chosen = PickRooms(rooms, Mathf.Min(groups, hostages.Count));
        if (chosen.Count < 2)
        {
            Debug.LogWarning($"[HostageRooms] Found only {rooms.Count} usable rooms; hostages stay where they were placed.");
            return;
        }

        // Split the hostages as evenly as possible: 4 -> 2 + 2
        List<Vector3> taken = new List<Vector3>();
        for (int i = 0; i < hostages.Count; i++)
        {
            int group = i % chosen.Count;
            int slot = i / chosen.Count;
            int inGroup = (hostages.Count - group + chosen.Count - 1) / chosen.Count;
            Room r = chosen[group];

            Vector3 right = Vector3.Cross(Vector3.up, r.inward);
            float side = (slot - (inGroup - 1) * 0.5f) * 1.2f;   // spread side by side
            Vector3 p = r.point + r.inward * 1.0f + right * side;
            p = FreeSpot(p, r.point, taken);
            taken.Add(p);
            hostages[i].PlaceAt(p, Quaternion.LookRotation(-r.inward), group);
        }

        // One guard per room, between the hostages and the door, facing the door
        for (int i = 0; i < guards.Count; i++)
        {
            int group = i % chosen.Count;
            Room r = chosen[group];
            Vector3 p = OnFloor(r.point - r.inward * 0.6f + Vector3.Cross(Vector3.up, r.inward) * 1.5f, r.point);
            guards[i].transform.SetPositionAndRotation(p, Quaternion.LookRotation(-r.inward));
            guards[i].hostageGroup = group;
        }

        PlaceJumpScare();

        Debug.Log($"[HostageRooms] {hostages.Count} hostages split over {chosen.Count} rooms (from {rooms.Count} found).");
    }

    // The jumpscare goes to a terrorist who is on his own in a room (not a hostage guard):
    // he hides beside that room's doorway and jumps out when you step in.
    // The terrorist who does the jumpscare. Terrorist_6 patrols the room on his own (PatrolRoute_B).
    // Leave empty to let the script pick a terrorist who is alone in a room.
    public static string jumpScareGuard = "Terrorist_6";

    static void PlaceJumpScare()
    {
        if (!string.IsNullOrEmpty(jumpScareGuard))
        {
            GameObject named = GameObject.Find(jumpScareGuard);
            TerroristGuard pinned = named != null ? named.GetComponent<TerroristGuard>() : null;
            if (pinned != null)
            {
                // The middle of his patrol route is the middle of his room
                Vector3 inside = pinned.transform.position;
                if (pinned.patrolRoute != null && pinned.patrolRoute.Count > 0)
                {
                    inside = Vector3.zero;
                    for (int i = 0; i < pinned.patrolRoute.Count; i++) inside += pinned.patrolRoute.GetPoint(i);
                    inside /= pinned.patrolRoute.Count;
                    if (NavMesh.SamplePosition(inside, out NavMeshHit mid, 2f, NavMesh.AllAreas)) inside = mid.position;
                }
                if (FindRoomDoor(inside, 15f, out Room pr) && HideBesideDoor(pr, out Vector3 ps, out Quaternion pf))
                {
                    pinned.transform.SetPositionAndRotation(ps, pf);
                    pinned.wanderRadius = 0f;
                    pinned.gameObject.AddComponent<JumpScare>().door = DoorAt(pr.door);
                    Debug.Log($"[HostageRooms] {pinned.name} is hiding by his room's door for a jumpscare (at {ps}).");
                    return;
                }
                Debug.LogWarning($"[HostageRooms] Couldn't find the door of {pinned.name}'s room; picking another terrorist.");
            }
        }

        List<TerroristGuard> all = new List<TerroristGuard>(Object.FindObjectsByType<TerroristGuard>(FindObjectsSortMode.None));
        List<TerroristGuard> loners = new List<TerroristGuard>();
        foreach (TerroristGuard g in all)
        {
            if (g.role != GuardRole.Static) continue;
            if (!NavMesh.SamplePosition(g.transform.position, out NavMeshHit hit, 1.5f, NavMesh.AllAreas)) continue;
            if (!Enclosed(hit.position, out _)) continue; // standing in a corridor or the open

            bool alone = true;
            foreach (TerroristGuard other in all)
                if (other != g && Flat(other.transform.position - g.transform.position).magnitude < 7f
                    && !NavMesh.Raycast(hit.position, other.transform.position, out _, NavMesh.AllAreas))
                    alone = false;
            if (alone) loners.Add(g);
        }
        if (loners.Count == 0)
        {
            Debug.LogWarning("[HostageRooms] No terrorist alone in a room, so no jumpscare this run.");
            return;
        }

        // Several candidates: pick one at random each run
        for (int attempt = 0; attempt < loners.Count; attempt++)
        {
            TerroristGuard g = loners[(Random.Range(0, loners.Count) + attempt) % loners.Count];
            if (!FindRoomDoor(g.transform.position, 9f, out Room room)) continue;
            if (!HideBesideDoor(room, out Vector3 spot, out Quaternion facing)) continue;

            g.transform.SetPositionAndRotation(spot, facing);
            g.wanderRadius = 0f;
            g.gameObject.AddComponent<JumpScare>().door = DoorAt(room.door);
            Debug.Log($"[HostageRooms] {g.name} is hiding by his room's door for a jumpscare.");
            return;
        }
        Debug.LogWarning("[HostageRooms] Couldn't find a door to hide the jumpscare terrorist behind.");
    }

    // The Door component of the door panel at a position (the one the player opens with E)
    static Door DoorAt(Vector3 doorFloor)
    {
        Door best = null;
        float bestDist = 2.5f;
        foreach (MeshRenderer r in Object.FindObjectsByType<MeshRenderer>(FindObjectsSortMode.None))
        {
            if (!DoorName.IsMatch(r.name)) continue;
            Vector3 c = r.bounds.center;
            float d = Flat(c - doorFloor).magnitude;
            if (d >= bestDist) continue;
            Door door = r.GetComponent<Door>();
            if (door == null) door = r.gameObject.AddComponent<Door>(); // same as the door setup does
            best = door;
            bestDist = d;
        }
        return best;
    }

    // The door of the room a position is in: the nearest door whose inside reaches that position
    static bool FindRoomDoor(Vector3 inside, float maxDistance, out Room room)
    {
        room = default;
        float best = float.MaxValue;
        foreach (MeshRenderer r in Object.FindObjectsByType<MeshRenderer>(FindObjectsSortMode.None))
        {
            if (!DoorName.IsMatch(r.name)) continue;
            Bounds b = r.bounds;
            if (b.size.y < 1.5f) continue;
            Vector3 normal = b.size.x < b.size.z ? Vector3.right : Vector3.forward;
            Vector3 doorFloor = new Vector3(b.center.x, b.min.y, b.center.z);
            float dist = Flat(inside - doorFloor).magnitude;
            if (dist > maxDistance || dist >= best) continue;

            Vector3 inward = normal * Mathf.Sign(Vector3.Dot(inside - doorFloor, normal));
            if (!NavMesh.SamplePosition(doorFloor + inward * 0.8f, out NavMeshHit hit, 0.8f, NavMesh.AllAreas)) continue;
            if (NavMesh.Raycast(hit.position, inside, out _, NavMesh.AllAreas)) continue; // not the same room

            best = dist;
            room = new Room { point = hit.position, inward = inward, door = doorFloor };
        }
        return best < float.MaxValue;
    }

    // A spot just inside the room, against the wall next to the doorway, where someone in the
    // corridor can't see him. He faces the opening, so he's right beside you as you step in.
    static bool HideBesideDoor(Room r, out Vector3 spot, out Quaternion facing)
    {
        spot = default;
        facing = Quaternion.identity;
        Vector3 right = Vector3.Cross(Vector3.up, r.inward);
        Vector3 corridorEye = r.door - r.inward * 2.5f + Vector3.up * 1.5f;

        bool found = false;
        float first = Random.value < 0.5f ? 1f : -1f;
        foreach (float side in new[] { first, -first })
        {
            Vector3 p = r.door + r.inward * 0.8f + right * side * 1.0f;
            if (!NavMesh.SamplePosition(p, out NavMeshHit hit, 0.6f, NavMesh.AllAreas)) continue;
            if (NavMesh.Raycast(r.point, hit.position, out _, NavMesh.AllAreas)) continue; // not in the room

            Vector3 candidate = hit.position;
            Quaternion look = Quaternion.LookRotation(-right * side);
            bool hidden = Physics.Linecast(corridorEye, candidate + Vector3.up * 1.5f, ~0, QueryTriggerInteraction.Ignore);
            if (!found || hidden)
            {
                spot = candidate;
                facing = look;
                found = true;
            }
            if (hidden) break;
        }
        return found;
    }

    static List<Room> FindRooms(List<Vector3> avoid)
    {
        List<Room> rooms = new List<Room>();
        foreach (MeshRenderer r in Object.FindObjectsByType<MeshRenderer>(FindObjectsSortMode.None))
        {
            if (!DoorName.IsMatch(r.name)) continue;
            Bounds b = r.bounds;
            if (b.size.y < 1.5f) continue; // not a standing door

            // The door is thin along one horizontal axis: that axis points into the rooms on either side
            Vector3 normal = b.size.x < b.size.z ? Vector3.right : Vector3.forward;
            Vector3 doorFloor = new Vector3(b.center.x, b.min.y, b.center.z);

            Room best = default;
            float bestOpen = float.MaxValue;
            bool found = false;
            foreach (float s in new[] { 1f, -1f })
            {
                Vector3 inward = normal * s;
                Vector3 probe = doorFloor + inward * 3f;
                if (!NavMesh.SamplePosition(probe, out NavMeshHit hit, 1.2f, NavMesh.AllAreas)) continue;
                if (Mathf.Abs(hit.position.y - doorFloor.y) > 0.6f) continue;

                if (!Enclosed(hit.position, out float longest)) continue;
                if (longest < bestOpen)
                {
                    bestOpen = longest;
                    best = new Room { point = hit.position, inward = inward, door = doorFloor };
                    found = true;
                }
            }
            if (!found) continue;

            bool tooClose = false;
            foreach (Vector3 a in avoid)
                if (Flat(best.point - a).magnitude < minDistanceFromStart) tooClose = true;
            if (tooClose) continue;

            // Two doors into the same room count once
            bool duplicate = false;
            foreach (Room existing in rooms)
                if (Flat(existing.point - best.point).magnitude < 5f) duplicate = true;
            if (!duplicate) rooms.Add(best);
        }
        return rooms;
    }

    // A room: walls close by in every direction, and enough clear floor for four people
    static bool Enclosed(Vector3 p, out float longest)
    {
        longest = 0f;
        int roomy = 0;
        const int rays = 16;
        for (int i = 0; i < rays; i++)
        {
            Vector3 dir = Quaternion.Euler(0f, i * 360f / rays, 0f) * Vector3.forward;
            float d = 20f;
            if (NavMesh.Raycast(p, p + dir * 20f, out NavMeshHit hit, NavMesh.AllAreas)) d = hit.distance;
            longest = Mathf.Max(longest, d);
            if (d > 1.2f) roomy++;
        }
        return longest < 11f && roomy >= 8;
    }

    static List<Room> PickRooms(List<Room> rooms, int count)
    {
        List<Room> pool = new List<Room>(rooms);
        for (int i = 0; i < pool.Count; i++) // shuffle
        {
            int j = Random.Range(i, pool.Count);
            (pool[i], pool[j]) = (pool[j], pool[i]);
        }

        List<Room> chosen = new List<Room>();
        foreach (Room r in pool)
        {
            bool farEnough = true;
            foreach (Room c in chosen)
                if (Flat(c.point - r.point).magnitude < minDistanceBetweenRooms) farEnough = false;
            if (!farEnough) continue;
            chosen.Add(r);
            if (chosen.Count == count) break;
        }
        return chosen;
    }

    // Snaps a spot to the walkable floor; falls back to the room's own spot if it is inside furniture
    static Vector3 OnFloor(Vector3 p, Vector3 fallback)
    {
        if (NavMesh.SamplePosition(p, out NavMeshHit hit, 0.8f, NavMesh.AllAreas)
            && !NavMesh.Raycast(fallback, hit.position, out _, NavMesh.AllAreas))
            return hit.position;
        return fallback;
    }

    // A floor spot near 'wanted', inside the room, at least 1 m from everyone already placed
    static Vector3 FreeSpot(Vector3 wanted, Vector3 roomPoint, List<Vector3> taken)
    {
        for (int ring = 0; ring < 4; ring++)
        {
            int steps = ring == 0 ? 1 : 8;
            for (int k = 0; k < steps; k++)
            {
                Vector3 p = wanted + Quaternion.Euler(0f, k * 45f, 0f) * Vector3.forward * (ring * 0.7f);
                if (!NavMesh.SamplePosition(p, out NavMeshHit hit, 0.6f, NavMesh.AllAreas)) continue;
                if (NavMesh.Raycast(roomPoint, hit.position, out _, NavMesh.AllAreas)) continue; // other side of a wall
                bool clear = true;
                foreach (Vector3 t in taken)
                    if (Flat(t - hit.position).magnitude < 1f) clear = false;
                if (clear) return hit.position;
            }
        }
        // Last resort: beside the room point, spread by how many are already there
        return roomPoint + Quaternion.Euler(0f, taken.Count * 90f, 0f) * Vector3.forward * 1f;
    }

    static Vector3 Flat(Vector3 v)
    {
        v.y = 0f;
        return v;
    }
}
