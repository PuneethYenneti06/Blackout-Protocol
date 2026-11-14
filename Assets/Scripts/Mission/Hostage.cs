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

    // Where the player started. Bringing a hostage within safeRadius of it saves them:
    // they stop following and wait there, so the player can go back for the others.
    public static Vector3? SafeSpot;
    public static float safeRadius = 4f;
    static int savedCounter;
    int savedSlot = -1;
    Vector3 waitSpot;

    // Which room group this hostage was placed in by HostageRooms (-1 = not placed).
    public int Group { get; private set; } = -1;

    // Middle of where the hostages start (the hostage room). Hostage guards hold this spot.
    public static bool TryGetRoomCenter(out Vector3 center) => TryGetRoomCenter(-1, out center);

    // Same, for one group only (group < 0 = all hostages).
    public static bool TryGetRoomCenter(int group, out Vector3 center)
    {
        center = Vector3.zero;
        int n = 0;
        foreach (Hostage h in All)
        {
            if (group >= 0 && h.Group != group) continue;
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
        freedCounter = 0;
        savedCounter = 0;
        SafeSpot = null;
        terrifiedClip = null;
        terrifiedLoaded = false;
    }

    // Scared standing loop (Resources/Animations/Hostage_Terrified), played while a freed hostage
    // waits: stopped behind you, or waiting at the safe spot.
    static AnimationClip terrifiedClip;
    static bool terrifiedLoaded;
    static AnimationClip TerrifiedClip()
    {
        if (terrifiedLoaded) return terrifiedClip;
        terrifiedLoaded = true;
        foreach (AnimationClip c in Resources.LoadAll<AnimationClip>("Animations/Hostage_Terrified"))
            if (c != null && !c.name.StartsWith("__preview__")) { terrifiedClip = c; break; }
        if (terrifiedClip == null)
            Debug.LogWarning("[Hostage] Resources/Animations/Hostage_Terrified has no animation clip.");
        return terrifiedClip;
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
        if (npcAnimator.idleClip == null && TerrifiedClip() != null)
        {
            npcAnimator.idleClip = terrifiedClip;
            npcAnimator.Rebuild();
        }
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

    // Moves a captive hostage to a new starting spot (used by HostageRooms at the start of a run)
    public void PlaceAt(Vector3 position, Quaternion rotation, int group)
    {
        transform.SetPositionAndRotation(position, rotation);
        startPosition = position;
        Group = group;
    }

    void OnEnable() { if (!All.Contains(this)) All.Add(this); }
    void OnDisable() { All.Remove(this); }

    void Update()
    {
        if (State == HostageState.Saved)
        {
            WaitAtSafeSpot(Time.deltaTime);
            return;
        }
        if (State != HostageState.Following) return;

        if (player == null)
        {
            GameObject p = GameObject.FindWithTag("Player");
            if (p == null) return;
            player = p.transform;
            playerHealth = p.GetComponent<PlayerHealth>();
        }

        if (ExtractionZone.IsInside(transform.position)
            || (SafeSpot.HasValue && FlatDistance(transform.position, SafeSpot.Value) < safeRadius))
        {
            Extract();
            return;
        }

        bool playerDead = playerHealth != null && playerHealth.IsDead;
        if (playerDead)
        {
            StopAgent();
            return;
        }

        if (trail == null) trail = PlayerTrail.For(player);
        Follow(Time.deltaTime);
    }

    // ---- Following ----
    //
    // Each freed hostage walks to a spot on the path you actually walked, a few metres behind you
    // (2 m for the first, then about 1.3 m more for each extra hostage, so they line up).
    // The NavMeshAgent is used when it can find a full path there. Where it can't (a doorway
    // that isn't on the walk map, furniture), the hostage walks along your footsteps instead,
    // which always works because you just walked that way.

    PlayerTrail trail;
    bool onTrail;          // true: walking your footsteps; false: using the NavMeshAgent
    int trailIndex;        // footstep the hostage is heading to, when on the trail
    float stuckTime;
    Vector3 lastPos;
    NavMeshPath path;
    int freedOrder;
    static int freedCounter;

    int FollowerIndex()
    {
        int i = 0;
        foreach (Hostage h in All)
            if (h != this && h.State == HostageState.Following && h.freedOrder < freedOrder) i++;
        return i;
    }

    void Follow(float dt)
    {
        float behind = followDistance + FollowerIndex() * 1.3f;
        Vector3 target = trail.PointBehind(behind, out int targetIndex);
        float toPlayer = FlatDistance(transform.position, player.position);
        float toTarget = FlatDistance(transform.position, target);
        float speed = toPlayer > runDistance ? runSpeed : walkSpeed;

        // Close enough: wait here
        if (toTarget < 0.35f || toPlayer < followDistance * 0.9f)
        {
            StopAgent();
            stuckTime = 0f;
            return;
        }

        repathTimer -= dt;
        if (repathTimer <= 0f)
        {
            repathTimer = repathInterval;
            if (path == null) path = new NavMeshPath();

            if (!onTrail)
            {
                // Agent mode: keep it while it has a full path, otherwise switch to the footsteps
                bool full = agent.enabled && agent.isOnNavMesh
                            && NavMesh.CalculatePath(agent.nextPosition, target, NavMesh.AllAreas, path)
                            && path.status == NavMeshPathStatus.PathComplete;
                if (full) agent.SetPath(path);
                else StartTrail();
            }
            else if (NavMesh.SamplePosition(transform.position, out NavMeshHit hit, 0.5f, NavMesh.AllAreas)
                     && FlatDistance(hit.position, transform.position) < 0.3f
                     && NavMesh.CalculatePath(hit.position, target, NavMesh.AllAreas, path)
                     && path.status == NavMeshPathStatus.PathComplete)
            {
                // Footstep mode: back to the agent as soon as it can reach the spot properly
                onTrail = false;
                agent.enabled = true;
                agent.Warp(hit.position);
                agent.SetPath(path);
                lastPos = transform.position;
            }
        }

        if (!onTrail)
        {
            if (!agent.enabled || !agent.isOnNavMesh) { StartTrail(); return; }
            agent.isStopped = false;
            agent.speed = speed;

            // Agent has a path but isn't getting anywhere: walk the footsteps instead
            float moved = FlatDistance(transform.position, lastPos);
            stuckTime = moved < speed * dt * 0.2f ? stuckTime + dt : 0f;
            lastPos = transform.position;
            if (stuckTime > 1f) StartTrail();
            return;
        }

        // Walk the footsteps, one after another, up to the spot behind you
        if (trailIndex < targetIndex && FlatDistance(transform.position, trail.Point(trailIndex)) < 0.3f)
            trailIndex++;
        Vector3 next = trail.Point(Mathf.Min(trailIndex, targetIndex));

        Vector3 flat = next - transform.position;
        flat.y = 0f;
        if (flat.sqrMagnitude > 0.0001f)
        {
            float move = Mathf.Min(speed * dt, flat.magnitude);
            Vector3 pos = transform.position + flat.normalized * move;
            pos.y = Mathf.MoveTowards(transform.position.y, next.y, 2f * dt);
            transform.position = pos;
            transform.rotation = Quaternion.Slerp(transform.rotation, Quaternion.LookRotation(flat), 10f * dt);
        }
    }

    void StartTrail()
    {
        onTrail = true;
        stuckTime = 0f;
        if (agent.enabled)
        {
            if (agent.isOnNavMesh) agent.ResetPath();
            agent.enabled = false;
        }
        trailIndex = trail.NearestIndex(transform.position);
    }

    void StopAgent()
    {
        if (agent.enabled && agent.isOnNavMesh)
        {
            agent.ResetPath();
            agent.isStopped = true;
        }
    }

    static float FlatDistance(Vector3 a, Vector3 b)
    {
        a.y = 0f;
        b.y = 0f;
        return Vector3.Distance(a, b);
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

        freedOrder = ++freedCounter;
        trail = PlayerTrail.For(player);

        agent.enabled = true;
        if (NavMesh.SamplePosition(transform.position, out NavMeshHit hit, 2f, NavMesh.AllAreas))
            agent.Warp(hit.position); // stand up onto the floor
        onTrail = false;
        repathTimer = 0f;
        lastPos = transform.position;
        if (!agent.isOnNavMesh) StartTrail(); // no walk map here: follow your footsteps

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

    // Saved: counts as extracted, then walks to a free spot near where they were saved and waits
    // there (instead of disappearing), so the player can go back for the others.
    void Extract()
    {
        State = HostageState.Saved;
        if (savedSound != null) AudioSource.PlayClipAtPoint(savedSound, transform.position);
        if (MissionManager.Instance != null) MissionManager.Instance.NotifyHostageSaved();

        savedSlot = savedCounter++;
        Vector3 center = SafeSpot.HasValue && FlatDistance(transform.position, SafeSpot.Value) < safeRadius * 2f
            ? SafeSpot.Value
            : transform.position;
        // Spread out in a ring so they don't stand inside each other
        float angle = savedSlot * 72f;
        float radius = 1.5f + (savedSlot / 5) * 1.0f;
        Vector3 spot = center + Quaternion.Euler(0f, angle, 0f) * Vector3.forward * radius;
        if (NavMesh.SamplePosition(spot, out NavMeshHit hit, 1.5f, NavMesh.AllAreas)
            && !NavMesh.Raycast(transform.position, hit.position, out _, NavMesh.AllAreas))
            spot = hit.position;
        else
            spot = transform.position;
        waitSpot = spot;

        if (!agent.enabled && NavMesh.SamplePosition(transform.position, out NavMeshHit here, 0.5f, NavMesh.AllAreas))
        {
            agent.enabled = true;
            agent.Warp(here.position);
        }
        if (agent.enabled && agent.isOnNavMesh)
        {
            agent.isStopped = false;
            agent.speed = walkSpeed;
            agent.stoppingDistance = 0.1f;
            agent.SetDestination(waitSpot);
        }
    }

    void WaitAtSafeSpot(float dt)
    {
        if (agent.enabled && agent.isOnNavMesh) return; // the agent walks them there and stops

        Vector3 flat = waitSpot - transform.position;
        flat.y = 0f;
        if (flat.magnitude < 0.1f) return;
        transform.position += flat.normalized * Mathf.Min(walkSpeed * dt, flat.magnitude);
        transform.rotation = Quaternion.Slerp(transform.rotation, Quaternion.LookRotation(flat), 10f * dt);
    }

    void OnDrawGizmosSelected()
    {
        Gizmos.color = Color.cyan;
        Gizmos.DrawWireSphere(transform.position, followDistance);
    }
}
