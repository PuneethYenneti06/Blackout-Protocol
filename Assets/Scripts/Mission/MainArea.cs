using UnityEngine;
using UnityEngine.AI;
using UnityEngine.SceneManagement;

// Finds the floor's main area: the most open spot on the walk map (NavMesh), away from where
// the player starts. The bomb sits there and the boss fight happens there.
// Worked out from the map itself, so nothing has to be placed in the scene.
public static class MainArea
{
    public static float minDistanceFromStart = 12f;

    static bool computed;
    static Scene computedForScene;
    static Vector3 center;

    public static bool TryGet(out Vector3 point)
    {
        Scene scene = SceneManager.GetActiveScene();
        if (!computed || computedForScene != scene)
        {
            computed = Compute(out center);
            computedForScene = scene;
            if (computed) Debug.Log($"[MainArea] main area at {center}");
            else Debug.LogWarning("[MainArea] Could not find an open area on the walk map.");
        }
        point = center;
        return computed;
    }

    static bool Compute(out Vector3 best)
    {
        best = Vector3.zero;
        NavMeshTriangulation tri = NavMesh.CalculateTriangulation();
        if (tri.indices == null || tri.indices.Length < 3) return false;

        Vector3? start = Hostage.SafeSpot;
        if (!start.HasValue)
        {
            GameObject player = GameObject.FindWithTag("Player");
            if (player != null) start = player.transform.position;
        }

        int triangles = tri.indices.Length / 3;
        int samples = Mathf.Min(500, triangles);
        float bestScore = -1f;
        Random.State saved = Random.state;
        Random.InitState(12345); // same main area every run
        for (int s = 0; s < samples; s++)
        {
            int t = triangles <= samples ? s : Random.Range(0, triangles);
            Vector3 a = tri.vertices[tri.indices[t * 3]];
            Vector3 b = tri.vertices[tri.indices[t * 3 + 1]];
            Vector3 c = tri.vertices[tri.indices[t * 3 + 2]];
            Vector3 p = (a + b + c) / 3f;

            if (start.HasValue)
            {
                Vector3 d = p - start.Value; d.y = 0f;
                if (d.magnitude < minDistanceFromStart) continue;
            }

            // Open space: how far you can walk in 16 directions (capped), and no wall right next to it
            float score = 0f;
            float nearest = float.MaxValue;
            for (int i = 0; i < 16; i++)
            {
                Vector3 dir = Quaternion.Euler(0f, i * 22.5f, 0f) * Vector3.forward;
                float dist = 20f;
                if (NavMesh.Raycast(p, p + dir * 20f, out NavMeshHit hit, NavMesh.AllAreas)) dist = hit.distance;
                score += dist;
                nearest = Mathf.Min(nearest, dist);
            }
            if (nearest < 1.5f) continue;
            if (score > bestScore)
            {
                bestScore = score;
                best = p;
            }
        }
        Random.state = saved;
        return bestScore > 0f;
    }
}
