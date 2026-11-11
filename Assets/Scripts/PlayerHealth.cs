using UnityEngine;

// Player health. Added automatically the first time a guard hits the player,
// so nothing has to be set up by hand.
public class PlayerHealth : MonoBehaviour
{
    public int maxHealth = 3;

    public int Health { get; private set; }
    public bool IsDead { get; private set; }

    // The mission screen can listen to this later ("You died", R to restart).
    public static event System.Action PlayerDied;

    void Awake()
    {
        Health = maxHealth;
    }

    public void TakeHit()
    {
        if (IsDead) return;

        Health--;
        Debug.Log($"[PlayerHealth] hit, health now {Health}");
        if (Health <= 0) Die();
    }

    void Die()
    {
        IsDead = true;

        // Stop the player in place and lock out movement and door use
        if (TryGetComponent(out Rigidbody rb)) rb.linearVelocity = Vector3.zero;
        if (TryGetComponent(out PlayerMovement move)) move.enabled = false;
        if (TryGetComponent(out DoorInteractor interact)) interact.enabled = false;

        PlayerDied?.Invoke();
    }
}
