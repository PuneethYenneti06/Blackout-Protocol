using UnityEngine;

// A patrol loop for Patrol-role guards. Each child is a waypoint, visited in hierarchy order,
// then back to the first. Assign it to TerroristGuard.patrolRoute.
public class PatrolRoute : MonoBehaviour
{
    public int Count => transform.childCount;

    public Vector3 GetPoint(int index)
    {
        if (transform.childCount == 0) return transform.position;
        return transform.GetChild(((index % transform.childCount) + transform.childCount) % transform.childCount).position;
    }

    void OnDrawGizmos()
    {
        int n = transform.childCount;
        if (n == 0) return;
        Gizmos.color = new Color(0.3f, 0.7f, 1f, 0.9f);
        for (int i = 0; i < n; i++)
        {
            Vector3 a = transform.GetChild(i).position;
            Vector3 b = transform.GetChild((i + 1) % n).position;
            Gizmos.DrawSphere(a, 0.15f);
            Gizmos.DrawLine(a + Vector3.up * 0.05f, b + Vector3.up * 0.05f);
        }
    }
}
