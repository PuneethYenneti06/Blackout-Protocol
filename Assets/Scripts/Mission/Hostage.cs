using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;

// A tied-up hostage. Hold E for untieTime seconds to free them; then they follow the player
// on a NavMeshAgent (about followDistance behind, running to catch up when far away).
// One hit kills a hostage. An ExtractionZone saves and removes them.
// Root object: this script + a collider (for E and bullets) + NavMeshAgent; the model is a child "Visual".
public class Hostage : MonoBehaviour, IInteractable, IDamageable
{
    public enum HostageState { Captive, Following, Saved, Dead }

    public static readonly List<Hostage> All = new List<Hostage>();

    [Header("Untie")]
    public float untieTime = 2f;

    [Header("Follow")]
    public float followDistance = 2f;    // stops this close behind the player
    public float runDistance = 6f;       // farther than this: runs to catch up
    public float walkSpeed = 1.8f;
    public float runSpeed = 4.8f;
    public float repathInterval = 0.25f;

    [Header("Sounds (optional)")]
    public AudioClip freedSound;
    public AudioClip deathSound;
    public AudioClip savedSound;

    public HostageState State { get; private set; } = HostageState.Captive;
    public bool IsAlive => State == HostageState.Captive || State == HostageState.Following;

    // Middle of where the hostages start (the hostage room). Hostage guards hold this spot.
    public static bool TryGetRoomCenter(out Vector3 center)
    {
        center = Vector3.zero;
        int n = 0;
        foreach (Hostage h in All)
        {
            center += h.startPosition;
            n++;
        }
        if (n == 0) return false;
        center /= n;
        return true;
    }

    NavMeshAgent agent;
    NpcAnimator npcAnimator;
    Transform player;
    PlayerHealth playerHealth;
    Vector3 startPosition;
    float repathTimer;
    AudioSource audioSource;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics()
    {
        All.Clear();
    }

    void Awake()
    {
        startPosition = transform.position;
        agent = GetComponent<NavMeshAgent>();
        if (agent == null) agent = gameObject.AddComponent<NavMeshAgent>();
        agent.enabled = false; // stays put while captive
        agent.stoppingDistance = followDistance;
        agent.autoBraking = true;
        agent.angularSpeed = 540f;
        agent.acceleration = 12f;

        npcAnimator = GetComponent<NpcAnimator>();
        if (npcAnimator == null) npcAnimator = gameObject.AddComponent<NpcAnimator>();
        npcAnimator.agent = agent;
        npcAnimator.SetCaptive(true);

        if (GetComponent<Collider>() == null)
        {
            CapsuleCollider c = gameObject.AddComponent<CapsuleCollider>();
            c.center = new Vector3(0f, 0.9f, 0f);
            c.height = 1.8f;
            c.radius = 0.3f;
        }

        audioSource = gameObject.AddComponent<AudioSource>();
        audioSource.playOnAwake = false;
        audioSource.spatialBlend = 1f;
    }

    void OnEnable() { if (!All.Contains(this)) All.Add(this); }
    void OnDisable() { All.Remove(this); }

    void Update()
    {
        if (State != HostageState.Following) return;

        if (player == null)
        {
            GameObject p = GameObject.FindWithTag("Player");
            if (p == null) return;
            player = p.transform;
            playerHealth = p.GetComponent<PlayerHealth>();
        }

        if (ExtractionZone.IsInside(transform.position))
        {
            Extract();
            return;
        }

        bool playerDead = playerHealth != null && playerHealth.IsDead;
        Vector3 target = player.position;
        float dist = Vector3.Distance(transform.position, target);
        float speed = dist > runDistance ? runSpeed : walkSpeed;

        if (agent.enabled && agent.isOnNavMesh)
        {
            agent.speed = speed;
            repathTimer -= Time.deltaTime;
            if (repathTimer <= 0f)
            {
                repathTimer = repathInterval;
                if (playerDead) agent.ResetPath();
                else agent.SetDestination(target);
            }
        }
        else if (!playerDead && dist > followDistance)
        {
            // No NavMesh here (not baked yet?): walk straight toward the player
            Vector3 flat = target - transform.position;
            flat.y = 0f;
            transform.position += flat.normalized * Mathf.Min(speed * Time.deltaTime, dist - followDistance);
            if (flat.sqrMagnitude > 0.01f)
                transform.rotation = Quaternion.Slerp(transform.rotation, Quaternion.LookRotation(flat), 10f * Time.deltaTime);
        }
    }

    // ---- IInteractable ----

    public bool CanInteract(Interactor who)
    {
        MissionManager m = MissionManager.Instance;
        return State == HostageState.Captive && (m == null || !m.IsOver);
    }

    public string Prompt => "untie the hostage";
    public float HoldTime => untieTime;
    public void HoldTick(Interactor who, float progress) { }
    public void HoldCancelled(Interactor who) { }

    public void Interact(Interactor who)
    {
        if (State != HostageState.Captive) return;
        State = HostageState.Following;
        player = who.transform;
        playerHealth = who.GetComponent<PlayerHealth>();
        npcAnimator.SetCaptive(false);

        agent.enabled = true;
        if (!agent.isOnNavMesh && NavMesh.SamplePosition(transform.position, out NavMeshHit hit, 2f, NavMesh.AllAreas))
            agent.Warp(hit.position);
        if (!agent.isOnNavMesh)
        {
            Debug.LogWarning($"[Hostage] {name} is not on a NavMesh (bake it). Using simple follow instead.");
            agent.enabled = false;
        }

        if (freedSound != null) audioSource.PlayOneShot(freedSound);
        if (MissionManager.Instance != null) MissionManager.Instance.NotifyHostageFreed();
    }

    // ---- IDamageable: one hit kills ----

    public void TakeDamage(float amount)
    {
        Kill();
    }

    public void Kill()
    {
        if (!IsAlive) return;
        bool wasFollowing = State == HostageState.Following;
        State = HostageState.Dead;

        if (agent.enabled) agent.enabled = false;
        foreach (Collider c in GetComponentsInChildren<Collider>()) c.enabled = false;
        if (deathSound != null) AudioSource.PlayClipAtPoint(deathSound, transform.position);

        if (!npcAnimator.PlayDeath())
        {
            // No death clip: tip the body over so it's clear they're down
            transform.rotation = Quaternion.Euler(-90f, transform.eulerAngles.y, 0f) ;
            transform.position += Vector3.up * 0.2f;
        }

        if (MissionManager.Instance != null) MissionManager.Instance.NotifyHostageDied(wasFollowing);
    }

    void Extract()
    {
        State = HostageState.Saved;
        if (savedSound != null) AudioSource.PlayClipAtPoint(savedSound, transform.position);
        if (MissionManager.Instance != null) MissionManager.Instance.NotifyHostageSaved();
        Destroy(gameObject);
    }

    void OnDrawGizmosSelected()
    {
        Gizmos.color = Color.cyan;
        Gizmos.DrawWireSphere(transform.position, followDistance);
    }
}
