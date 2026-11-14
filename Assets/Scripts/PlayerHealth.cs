using UnityEngine;

// Player health, counted in hits (default 5).
// The MissionManager adds this to the player at start if it is missing, and guards also
// add it the first time they hit the player, so nothing has to be set up by hand.
public class PlayerHealth : MonoBehaviour
{
    public int maxHealth = 5;
    public AudioClip hitSound;           // optional
    [Range(0f, 1f)] public float hitVolume = 1f;
    public AudioClip deathSound;         // optional

    public int Health { get; private set; }
    public bool IsDead { get; private set; }

    // The mission screen listens to this ("You died", R to restart).
    public static event System.Action PlayerDied;

    // (health now, max health). Fired on every hit that lands.
    public static event System.Action<int, int> Damaged;

    // (health now, max health). Fired when a medkit heals.
    public static event System.Action<int, int> Healed;

    AudioSource audioSource;
    Animator animator;

    void Awake()
    {
        Health = maxHealth;
        animator = GetComponentInChildren<Animator>();
    }

    public void TakeHit()
    {
        if (IsDead) return;

        Health = Mathf.Max(0, Health - 1);
        Debug.Log($"[PlayerHealth] hit, health now {Health}");
        PlaySound(hitSound);
        Damaged?.Invoke(Health, maxHealth);

        if (Health <= 0)
        {
            Die();
            return;
        }

        AnimatorParams.TrySetTrigger(animator, "Hit");
    }

    // Restores hits, never above maxHealth. Returns how much was actually healed.
    public int Heal(int hits)
    {
        if (IsDead || hits <= 0) return 0;

        int before = Health;
        Health = Mathf.Min(maxHealth, Health + hits);
        int healed = Health - before;
        if (healed > 0) Healed?.Invoke(Health, maxHealth);
        return healed;
    }

    void Die()
    {
        IsDead = true;
        PlaySound(deathSound);

        // Stop the player in place and lock out movement, shooting and interaction
        if (TryGetComponent(out Rigidbody rb)) rb.linearVelocity = Vector3.zero;
        if (TryGetComponent(out PlayerMovement move)) move.enabled = false;
        if (TryGetComponent(out PlayerShooting shoot)) shoot.enabled = false;
        if (TryGetComponent(out Interactor interactor)) interactor.enabled = false;

        AnimatorParams.TrySetTrigger(animator, "Die");
        PlayerDied?.Invoke();
    }

    void PlaySound(AudioClip clip)
    {
        if (clip == null) return;
        if (audioSource == null)
        {
            audioSource = gameObject.AddComponent<AudioSource>();
            audioSource.playOnAwake = false;
            audioSource.spatialBlend = 0f;
        }
        audioSource.PlayOneShot(clip, hitVolume);
    }
}

// Sets animator parameters only if the Animator really has them, so code can ask for
// states (Reload, Hit, Die, Kneel, Heal) before they exist in the controller.
public static class AnimatorParams
{
    public static bool Has(Animator animator, string name, AnimatorControllerParameterType type)
    {
        if (animator == null || animator.runtimeAnimatorController == null) return false;
        foreach (AnimatorControllerParameter p in animator.parameters)
            if (p.type == type && p.name == name) return true;
        return false;
    }

    public static bool TrySetTrigger(Animator animator, string name)
    {
        if (!Has(animator, name, AnimatorControllerParameterType.Trigger)) return false;
        animator.SetTrigger(name);
        return true;
    }

    public static bool TrySetBool(Animator animator, string name, bool value)
    {
        if (!Has(animator, name, AnimatorControllerParameterType.Bool)) return false;
        animator.SetBool(name, value);
        return true;
    }
}
