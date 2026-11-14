using UnityEngine;

// Medkits the player carries (max 2). H uses one: 3 s heal, slowed and can't shoot,
// then +2 hits (never above max health).
public class PlayerMedkits : MonoBehaviour
{
    public int maxCarried = 2;
    public int startCount = 0;
    public int healHits = 2;
    public float healTime = 3f;
    [Range(0.1f, 1f)] public float slowMultiplier = 0.4f;   // movement speed while healing
    public KeyCode useKey = KeyCode.H;

    [Header("Sounds (optional)")]
    public AudioClip healStartSound;
    public AudioClip healDoneSound;

    public int Count { get; private set; }
    public bool IsHealing { get; private set; }
    public float HealProgress => IsHealing ? 1f - healTimer / Mathf.Max(healTime, 0.01f) : 0f;

    float healTimer;
    PlayerHealth health;
    PlayerMovement movement;
    Animator anim;
    AudioSource audioSource;

    void Awake()
    {
        Count = Mathf.Clamp(startCount, 0, maxCarried);
        anim = GetComponentInChildren<Animator>();
    }

    void Start()
    {
        health = GetComponent<PlayerHealth>();
        movement = GetComponent<PlayerMovement>();
    }

    // Returns how many were actually taken (0 when already carrying the maximum).
    public int Add(int amount)
    {
        int taken = Mathf.Clamp(maxCarried - Count, 0, amount);
        Count += taken;
        return taken;
    }

    void Update()
    {
        if (health == null) health = GetComponent<PlayerHealth>();
        MissionManager m = MissionManager.Instance;
        bool over = m != null && m.IsOver;

        if (IsHealing)
        {
            if (health == null || health.IsDead || over)
            {
                StopHealing();
                return;
            }

            if (MissionHUD.Instance != null) MissionHUD.Instance.SetHold("Healing", HealProgress);
            healTimer -= Time.deltaTime;
            if (healTimer <= 0f)
            {
                health.Heal(healHits);
                Count--;
                StopHealing();
                PlaySound(healDoneSound);
                if (m != null) m.ShowToast($"+{healHits} health");
            }
            return;
        }

        if (!over && Input.GetKeyDown(useKey)) TryUse();
    }

    void TryUse()
    {
        if (health == null || health.IsDead) return;
        MissionManager m = MissionManager.Instance;
        if (Count <= 0)
        {
            if (m != null) m.ShowToast("No medkits");
            return;
        }
        if (health.Health >= health.maxHealth)
        {
            if (m != null) m.ShowToast("Health is full");
            return;
        }
        PlayerShooting gun = GetComponent<PlayerShooting>();
        if (gun != null && gun.IsReloading) return;

        IsHealing = true;
        healTimer = healTime;
        if (movement != null) movement.speedMultiplier = slowMultiplier;
        PlaySound(healStartSound);
        AnimatorParams.TrySetTrigger(anim, "Heal");
    }

    void StopHealing()
    {
        IsHealing = false;
        if (movement != null) movement.speedMultiplier = 1f;
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
        audioSource.PlayOneShot(clip);
    }
}
