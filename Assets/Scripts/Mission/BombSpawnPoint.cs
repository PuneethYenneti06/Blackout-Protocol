using UnityEngine;

// A place where the bomb may appear. Put 3-4 in the level; the bomb picks one at random each run.
public class BombSpawnPoint : MonoBehaviour
{
    void OnDrawGizmos()
    {
        Gizmos.color = new Color(1f, 0.3f, 0.1f, 0.8f);
        Gizmos.DrawWireCube(transform.position + Vector3.up * 0.15f, new Vector3(0.5f, 0.3f, 0.35f));
        Gizmos.DrawLine(transform.position, transform.position + transform.forward * 0.5f);
    }
}
