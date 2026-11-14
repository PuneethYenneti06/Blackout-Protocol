using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.UI;

// Shared rules for guards and the player.
public static class GuardAlert
{
    // True while the lights are off. Guards then only see you up close.
    public static bool Blackout;

    // True while the player's flashlight is on. In the dark it lets guards spot you from far away.
    public static bool FlashlightOn;

    public const float DarkRange = 4f;
    public const float FlashlightRange = 30f;

    // Anything loud calls this: doors opening, sprinting, gunfire.
    // Guards inside the radius turn toward the sound and search there.
    public static event System.Action<Vector3, float> NoiseMade;

    public static void MakeNoise(Vector3 position, float radius)
    {
        NoiseMade?.Invoke(position, radius);
    }

    // Clears subscribers and flags when the game starts, so nothing carries over.
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics()
    {
        NoiseMade = null;
        Blackout = false;
        FlashlightOn = false;
    }
}

// A stationary terrorist guard for humanoid models (such as the Insurgent).
//
// Posing is done in code, so no animation clips are needed:
//  - Idle: arms hang at his sides, head sweeps left and right, body holds its post.
//  - Spotted: he brings the rifle up with the stock against his shoulder.
//    His right hand stays on the pistol grip, his left hand moves to the front grip,
//    and his right index finger bends onto the trigger.
//  - Firing: the rifle kicks back, and the shot comes from the rifle's muzzle.
//  - Lost you: he lowers the rifle and searches.
//
// The rifle needs two empty markers as children of the Ak47, named "Stock" (back of the
// stock, against the shoulder) and "Foregrip" (the front grip, where the left hand holds).
// Everything is measured from his own position, and the player is found by its tag.
//
// Mission AI: each guard has a role. Before the lockdown, Static and Bomb guards hold
// their posts and Patrol guards walk their PatrolRoute. A guard who keeps the player in sight
// for radioTime seconds radios an alarm (world-space RADIO bar). In LOCKDOWN, Hostage guards
// go to the hostage room and hold it; everyone else hunts the player's last known position.
// Movement uses a NavMeshAgent (added automatically when the guard stands on a baked NavMesh);
// without a NavMesh guards simply stay at their posts as before.
public enum GuardRole { Static, Patrol, Bomb, Hostage }

public class TerroristGuard : MonoBehaviour, IDamageable
{
    enum State { Scanning, Alerted, Searching, Dead }

    [Header("Rifle")]
    public bool holdRifle = true;                    // raise and aim the rifle when he spots you
    public float raiseTime = 0.4f;                   // seconds to bring the rifle up
    public Vector3 gunAimCorrection = Vector3.zero;  // rotate the rifle if the barrel doesn't point along its forward axis
    public Vector3 shoulderOffset = new Vector3(0.15f, 0.1f, 0f); // where the stock sits: right, up, forward from the chest (scaled)
    public Vector3 leftHandTwist = Vector3.zero;     // rotate the left hand around the front grip if the fingers look wrong
    public Vector3 triggerCurl = new Vector3(0f, 0f, 35f); // how far each index finger joint bends onto the trigger, degrees
    public float recoilAmount = 0.05f;               // how far the rifle kicks back on each shot, metres
    public AudioClip fireSound;                      // optional gunshot sound

    [Header("Rifle markers (auto-found if left empty)")]
    public Transform stockAnchor;                    // back of the stock, against the shoulder
    public Transform foregripAnchor;                 // front grip, where the left hand goes

    [Header("Scanning")]
    public float scanAngle = 60f;        // how far he looks to each side of his post, degrees
    public float sweepSmoothTime = 0.6f; // higher = slower, softer turns
    public float pauseAtEnds = 1.5f;     // seconds he holds his look at each side
    public float turnSpeed = 90f;        // how fast his body turns toward a new post, sound or player

    [Header("Head")]
    public float maxHeadYaw = 70f;       // the head never turns past this, degrees
    public float headTurnSpeed = 180f;   // how fast the head returns to centre or swings to a side

    [Header("Vision")]
    public float fieldOfView = 120f;     // full width of the cone, in degrees
    public float viewRange = 15f;        // range in normal light, metres
    public float eyeHeight = 1.6f;       // metres above his feet
    public float searchTime = 4f;        // seconds he looks around after losing you

    [Header("Combat")]
    public int maxHealth = 2;            // hits he can take
    public float reactionDelay = 0.7f;   // seconds from spotting you to his first shot
    public float fireInterval = 1.2f;    // seconds between shots
    [Range(0f, 1f)] public float missChance = 0.35f;
    public float missSpread = 1.2f;      // how far off a miss lands, metres
    public float accuracySpread = 0.1f;
    public float maxShotDistance = 100f;

    [Header("Mission role")]
    public GuardRole role = GuardRole.Static;
    public PatrolRoute patrolRoute;                  // Patrol role: waypoints to loop through
    public float patrolWaitTime = 2f;                // seconds at each waypoint
    public float walkSpeed = 1.6f;
    public float runSpeed = 4f;
    public float moveTurnSpeed = 360f;               // body turn speed while walking/running
    public float bombGuardDistance = 2.5f;           // Bomb role: post this far from the bomb
    public float hostageHoldRadius = 2f;             // Hostage role: spread around the hostage room centre
    public float huntWanderRadius = 8f;              // after reaching the last known position, search around it
    public bool shootFreedHostages = true;           // in lockdown, shoot hostages that follow the player

    [Header("Radio alarm")]
    public bool canRadio = true;
    public float radioTime = 3f;                     // seconds of continuous sight before the alarm
    public float radioBarHeight = 0.5f;              // metres above his eyes
    public AudioClip radioSound;                     // optional, plays when the alarm goes out

    [Header("Drop on death")]
    public GameObject magazineDropPrefab;            // optional; a placeholder box is used if empty
    public int magazineAmmo = 30;

    // Fired when any guard dies, so the mission logic can count them.
    public static event System.Action<TerroristGuard> GuardKilled;

    // Fired when a guard spots the player (each time he goes from not-alerted to alerted by sight).
    public static event System.Action<TerroristGuard> PlayerSpotted;

    State state = State.Scanning;
    int health;
    float flinch;          // 1 right after a hit, fades: the body rocks back
    Transform modelRoot;   // the skeleton's root node under this object (not a bone), tilted for the flinch
    Quaternion modelRootBase;

    public bool IsDead => state == State.Dead;

    // Bones
    bool hasArms;
    Transform head;
    Transform chest;
    Transform leftUpper, leftLower, leftHand;
    Transform rightUpper, rightLower, rightHand;
    Transform indexProximal, indexIntermediate, indexDistal;
    Transform rifle;

    // Rest pose, captured once at start, so every frame can start from the same place
    Quaternion headBaseLocal;
    Quaternion leftUpperBase, leftLowerBase, leftHandBase;
    Quaternion rightUpperBase, rightLowerBase, rightHandBase;
    Quaternion indexProximalBase, indexIntermediateBase, indexDistalBase;
    float leftReach, rightReach;

    // Rifle markers, stored relative to the right hand so they stay correct while he moves
    Vector3 stockInHand;
    Vector3 foregripInHand;
    Vector3 handScale = Vector3.one;

    AudioSource audioSource;

    // Movement state
    float sweepOffset;     // how far he is looking left or right of his post right now
    float sweepTarget;     // the side he is turning toward
    float sweepVelocity;
    float pauseTimer;
    float headYaw;         // head turn on top of the body
    float currentYaw;      // body direction
    float postYaw;         // the direction he was placed facing
    float scanCenterYaw;   // the direction he is sweeping around right now

    // Combat state
    float alertTimer;
    float nextShotTime;
    float searchTimer;
    float aimWeight;       // 0 = arms hanging, 1 = rifle up and aimed
    float recoilKick;      // 1 right after a shot, fades to 0
    Vector3 lastKnownPosition;
    GameObject player;
    LineRenderer tracer;
    float tracerTimer;
    readonly RaycastHit[] hitBuffer = new RaycastHit[32];

    void Start()
    {
        health = maxHealth;
        postYaw = transform.eulerAngles.y;
        scanCenterYaw = postYaw;
        currentYaw = postYaw;

        Animator animator = GetComponentInChildren<Animator>();
        if (animator != null)
        {
            head = animator.GetBoneTransform(HumanBodyBones.Head);
            chest = animator.GetBoneTransform(HumanBodyBones.Chest);
            if (chest == null) chest = animator.GetBoneTransform(HumanBodyBones.Spine);

            leftUpper = animator.GetBoneTransform(HumanBodyBones.LeftUpperArm);
            leftLower = animator.GetBoneTransform(HumanBodyBones.LeftLowerArm);
            leftHand = animator.GetBoneTransform(HumanBodyBones.LeftHand);
            rightUpper = animator.GetBoneTransform(HumanBodyBones.RightUpperArm);
            rightLower = animator.GetBoneTransform(HumanBodyBones.RightLowerArm);
            rightHand = animator.GetBoneTransform(HumanBodyBones.RightHand);

            indexProximal = animator.GetBoneTransform(HumanBodyBones.RightIndexProximal);
            indexIntermediate = animator.GetBoneTransform(HumanBodyBones.RightIndexIntermediate);
            indexDistal = animator.GetBoneTransform(HumanBodyBones.RightIndexDistal);

            // The node between this object and the hips (the FBX root), if there is one
            Transform hips = animator.GetBoneTransform(HumanBodyBones.Hips);
            Transform node = hips;
            while (node != null && node.parent != transform) node = node.parent;
            if (node != null && node != hips && node != transform)
            {
                modelRoot = node;
                modelRootBase = node.localRotation;
            }
        }

        hasArms = leftUpper != null && leftLower != null && leftHand != null
               && rightUpper != null && rightLower != null && rightHand != null;

        if (head != null) headBaseLocal = head.localRotation;

        if (hasArms)
        {
            leftUpperBase = leftUpper.localRotation;
            leftLowerBase = leftLower.localRotation;
            leftHandBase = leftHand.localRotation;
            rightUpperBase = rightUpper.localRotation;
            rightLowerBase = rightLower.localRotation;
            rightHandBase = rightHand.localRotation;

            if (indexProximal != null) indexProximalBase = indexProximal.localRotation;
            if (indexIntermediate != null) indexIntermediateBase = indexIntermediate.localRotation;
            if (indexDistal != null) indexDistalBase = indexDistal.localRotation;

            leftReach = ArmReach(leftUpper, leftLower, leftHand);
            rightReach = ArmReach(rightUpper, rightLower, rightHand);

            rifle = FindRifle();
            if (rifle != null)
            {
                if (stockAnchor == null) stockAnchor = FindChildContaining(rifle, "stock");
                if (foregripAnchor == null) foregripAnchor = FindChildContaining(rifle, "foregrip");

                handScale = rightHand.lossyScale;
                if (stockAnchor != null) stockInHand = rightHand.InverseTransformPoint(stockAnchor.position);
                if (foregripAnchor != null) foregripInHand = rightHand.InverseTransformPoint(foregripAnchor.position);
            }
        }

        if (fireSound != null)
        {
            audioSource = gameObject.AddComponent<AudioSource>();
            audioSource.playOnAwake = false;
            audioSource.spatialBlend = 1f; // sound comes from where he is
        }

        npcAnimator = GetComponent<NpcAnimator>();
        if (npcAnimator == null) npcAnimator = gameObject.AddComponent<NpcAnimator>();

        EnsureHitbox();

        postPosition = transform.position;
        SetupNavigation();
        if (role == GuardRole.Bomb) TakeBombPost();
        if (npcAnimator.agent == null) npcAnimator.agent = agent;
        BuildRadioBar();

        // Start each guard looking at a different side, and at a different point in his pause
        sweepTarget = Random.value < 0.5f ? scanAngle : -scanAngle;
        pauseTimer = Random.Range(0f, pauseAtEnds);
    }

    void OnEnable()
    {
        GuardAlert.NoiseMade += OnNoise;
    }

    void OnDisable()
    {
        GuardAlert.NoiseMade -= OnNoise;
    }

    void Update()
    {
        if (state == State.Dead) return;
        float dt = Time.deltaTime;

        if (tracerTimer > 0f)
        {
            tracerTimer -= dt;
            if (tracerTimer <= 0f && tracer != null) tracer.enabled = false;
        }

        UpdateSweep(dt);

        MissionManager mission = MissionManager.Instance;
        bool lockdown = mission != null && mission.IsLockdown;
        bool over = mission != null && mission.IsOver;

        bool sees = !over && CanSeePlayer(keepTracking: state == State.Alerted);

        if (sees)
        {
            hostageTarget = null;
            if (state != State.Alerted)
            {
                state = State.Alerted;
                alertTimer = 0f; // start the reaction delay
                PlayerSpotted?.Invoke(this);
            }

            alertTimer += dt;
            lastKnownPosition = PlayerAim();
            if (mission != null) mission.ReportPlayerPosition(player.transform.position);

            if (alertTimer >= reactionDelay && Time.time >= nextShotTime)
            {
                nextShotTime = Time.time + fireInterval;
                Fire(PlayerAim());
            }
        }
        else
        {
            if (state == State.Alerted)
            {
                // Lost sight of you: lower the rifle and go look where you were last seen
                state = State.Searching;
                searchTimer = searchTime;
                scanCenterYaw = YawToward(lastKnownPosition);
                if (lockdown && role != GuardRole.Hostage) SetHunt(lastKnownPosition);
            }
            else if (state == State.Searching)
            {
                searchTimer -= dt;
                if (searchTimer <= 0f)
                {
                    state = State.Scanning;
                    scanCenterYaw = postYaw;
                }
            }

            // In lockdown, freed hostages he can see are targets too
            hostageTarget = (lockdown && !over && shootFreedHostages) ? FindVisibleHostage() : null;
            if (hostageTarget != null)
            {
                hostageAlertTimer += dt;
                if (hostageAlertTimer >= reactionDelay && Time.time >= nextShotTime)
                {
                    nextShotTime = Time.time + fireInterval;
                    Fire(HostageAim(hostageTarget));
                }
            }
            else hostageAlertTimer = 0f;
        }

        UpdateRadio(dt, sees, mission);

        bool alerted = state == State.Alerted;
        bool engaging = alerted || hostageTarget != null;

        UpdateMovement(dt, lockdown && !over, engaging || over);
        KeepFeetOnFloor(dt);

        // Rifle: rises while engaging, lowers otherwise
        float aimTarget = (holdRifle && engaging) ? 1f : 0f;
        aimWeight = Mathf.MoveTowards(aimWeight, aimTarget, dt / Mathf.Max(raiseTime, 0.01f));
        recoilKick = Mathf.MoveTowards(recoilKick, 0f, dt * 8f);
        flinch = Mathf.MoveTowards(flinch, 0f, dt * 3f);

        // Body: holds the post / follows his path, turns to a sound, or faces his target
        float bodyTarget;
        if (alerted) bodyTarget = YawToward(lastKnownPosition);
        else if (hostageTarget != null) bodyTarget = YawToward(hostageTarget.transform.position);
        else bodyTarget = scanCenterYaw + (head != null ? 0f : sweepOffset);

        float turn = isMoving ? Mathf.Max(turnSpeed, moveTurnSpeed) : turnSpeed;
        currentYaw = Mathf.MoveTowardsAngle(currentYaw, bodyTarget, turn * dt);
        transform.rotation = Quaternion.Euler(0f, currentYaw, 0f);

        // Head: looks left and right while scanning, straight ahead when engaging
        float headTarget = 0f;
        if (!engaging && head != null)
            headTarget = Mathf.Clamp(sweepOffset, -maxHeadYaw, maxHeadYaw);
        headYaw = Mathf.MoveTowardsAngle(headYaw, headTarget, headTurnSpeed * dt);
    }

    // Runs after the animator. Every frame starts from the rest pose, so nothing builds up over time.
    void LateUpdate()
    {
        UpdateRadioBarTransform();
        if (state == State.Dead) return;

        ResetPose();

        // Hit reaction: the whole body rocks back from the feet, then straightens up again.
        // (Set outright each frame on the model's root node, which no animation touches,
        // so it can't build up.)
        if (modelRoot != null)
            modelRoot.localRotation = modelRootBase * Quaternion.Euler(-15f * flinch, 0f, 0f);

        if (head != null)
            head.rotation = Quaternion.AngleAxis(headYaw, Vector3.up) * head.rotation;

        if (hasArms)
            UpdateArms();
    }

    void ResetPose()
    {
        if (head != null) head.localRotation = headBaseLocal;

        if (hasArms)
        {
            leftUpper.localRotation = leftUpperBase;
            leftLower.localRotation = leftLowerBase;
            leftHand.localRotation = leftHandBase;
            rightUpper.localRotation = rightUpperBase;
            rightLower.localRotation = rightLowerBase;
            rightHand.localRotation = rightHandBase;

            if (indexProximal != null) indexProximal.localRotation = indexProximalBase;
            if (indexIntermediate != null) indexIntermediate.localRotation = indexIntermediateBase;
            if (indexDistal != null) indexDistal.localRotation = indexDistalBase;
        }
    }

    // Places both arms and the right index finger. Hanging when idle, holding the rifle when alerted.
    void UpdateArms()
    {
        float w = aimWeight;

        // Across the body, from his left arm to his right arm
        Vector3 bodyRight = FlatDirection(rightUpper.position - leftUpper.position);
        float s = transform.lossyScale.y;

        Vector3 chestPos = chest != null
            ? chest.position
            : transform.position + Vector3.up * 1.2f * s;

        Vector3 aimDir = transform.forward;
        if (hostageTarget != null && state != State.Alerted) aimDir = (HostageAim(hostageTarget) - chestPos).normalized;
        else if (player != null) aimDir = (PlayerAim() - chestPos).normalized;

        float kick = recoilKick;

        // Where the stock rests: in the shoulder pocket, pushed back a little when the rifle fires
        Vector3 shoulderPoint = chestPos
            + bodyRight * shoulderOffset.x * s
            + Vector3.up * shoulderOffset.y * s
            + aimDir * shoulderOffset.z * s
            - aimDir * recoilAmount * kick;

        // The rifle points along the aim direction
        Quaternion gunRot = Quaternion.LookRotation(aimDir, Vector3.up) * Quaternion.Euler(gunAimCorrection);

        Vector3 handPos;
        Quaternion handRot;

        if (rifle != null)
        {
            // The hand's rotation that makes the rifle point the right way
            handRot = gunRot * Quaternion.Inverse(rifle.localRotation);

            // The stock sits on the shoulder, so the hand has to be where the rifle's stock
            // marker minus the stock's offset from the hand puts it
            handPos = stockAnchor != null
                ? shoulderPoint - handRot * Vector3.Scale(stockInHand, handScale)
                : shoulderPoint;
        }
        else
        {
            handRot = gunRot;
            handPos = shoulderPoint;
        }

        // Left hand goes to the front grip, or a fallback spot forward of the right hand
        Vector3 foregripTarget = foregripAnchor != null
            ? handPos + handRot * Vector3.Scale(foregripInHand, handScale)
            : handPos + gunRot * new Vector3(0f, 0f, 0.3f * s);

        // Hanging positions: straight down, slightly away from the body
        Vector3 rightHang = rightUpper.position + Vector3.Normalize(Vector3.down + bodyRight * 0.25f) * rightReach;
        Vector3 leftHang = leftUpper.position + Vector3.Normalize(Vector3.down - bodyRight * 0.25f) * leftReach;

        // Elbows bend down and outward
        SolveArm(rightUpper, rightLower, rightHand, rightReach,
                 Vector3.Lerp(rightHang, handPos, w),
                 Vector3.down + bodyRight * 0.5f);

        SolveArm(leftUpper, leftLower, leftHand, leftReach,
                 Vector3.Lerp(leftHang, foregripTarget, w),
                 Vector3.down - bodyRight * 0.5f);

        if (w > 0.001f)
        {
            // Right hand turns to the grip, the left hand wraps the front grip
            if (rifle != null)
                rightHand.rotation = Quaternion.Slerp(rightHand.rotation, handRot, w);

            leftHand.rotation = Quaternion.Slerp(leftHand.rotation,
                                                 gunRot * Quaternion.Euler(leftHandTwist), w);

            // Index finger bends onto the trigger
            Vector3 curl = triggerCurl * w;
            if (indexProximal != null) indexProximal.localRotation = indexProximalBase * Quaternion.Euler(curl);
            if (indexIntermediate != null) indexIntermediate.localRotation = indexIntermediateBase * Quaternion.Euler(curl);
            if (indexDistal != null) indexDistal.localRotation = indexDistalBase * Quaternion.Euler(curl);
        }
    }

    // Two-bone IK: bends an arm (shoulder, elbow, wrist) so the wrist reaches the target.
    // The pole sets which way the elbow bends.
    void SolveArm(Transform upper, Transform lower, Transform hand, float reach, Vector3 target, Vector3 pole)
    {
        Vector3 shoulder = upper.position;
        Vector3 toTarget = target - shoulder;
        if (toTarget.sqrMagnitude < 0.0001f) return;

        float l1 = Vector3.Distance(shoulder, lower.position);      // upper arm length
        float l2 = Vector3.Distance(lower.position, hand.position); // forearm length
        if (l1 < 0.0001f || l2 < 0.0001f) return;

        float dist = Mathf.Min(toTarget.magnitude, reach);
        if (dist < 0.01f) return;
        Vector3 dir = toTarget.normalized;

        // Law of cosines gives the angle at the shoulder
        float cos = Mathf.Clamp((l1 * l1 + dist * dist - l2 * l2) / (2f * l1 * dist), -1f, 1f);
        float sin = Mathf.Sqrt(1f - cos * cos);

        // Bend direction: the part of the pole that sits across the shoulder-to-target line
        Vector3 bend = pole - Vector3.Dot(pole, dir) * dir;
        if (bend.sqrMagnitude < 0.0001f) bend = Vector3.down - Vector3.Dot(Vector3.down, dir) * dir;
        if (bend.sqrMagnitude < 0.0001f) bend = Vector3.up;
        bend.Normalize();

        Vector3 elbow = shoulder + dir * (l1 * cos) + bend * (l1 * sin);
        Vector3 wristGoal = shoulder + dir * dist;

        // Upper arm: swing so the elbow lands on the computed spot
        upper.rotation = Quaternion.FromToRotation(lower.position - shoulder, elbow - shoulder) * upper.rotation;

        // Forearm: swing so the wrist points at the goal
        lower.rotation = Quaternion.FromToRotation(hand.position - lower.position, wristGoal - lower.position) * lower.rotation;
    }

    // Eases from one side to the other, pausing at each end
    void UpdateSweep(float dt)
    {
        if (state == State.Alerted) return; // stay still while he's locked on

        if (pauseTimer > 0f)
        {
            pauseTimer -= dt;
            return;
        }

        sweepOffset = Mathf.SmoothDampAngle(sweepOffset, sweepTarget, ref sweepVelocity,
                                            sweepSmoothTime, Mathf.Infinity, dt);

        if (Mathf.Abs(Mathf.DeltaAngle(sweepOffset, sweepTarget)) < 0.5f)
        {
            sweepOffset = sweepTarget;
            sweepVelocity = 0f;
            pauseTimer = pauseAtEnds;
            sweepTarget = -sweepTarget;
        }
    }

    // IDamageable: every bullet that hits him counts as one hit, whatever its damage value.
    public void TakeDamage(float amount)
    {
        TakeHit();
    }

    // Called by the player's gun (or anything else) when it hits this guard.
    public void TakeHit()
    {
        if (state == State.Dead) return;

        health--;
        if (health <= 0)
        {
            Die();
            return;
        }

        flinch = 1f;
        if (state != State.Alerted)
        {
            if (player != null) lastKnownPosition = PlayerAim();
            state = State.Alerted;
            alertTimer = 0f;
        }
    }

    void Die()
    {
        state = State.Dead;
        Pickup.Spawn(magazineDropPrefab, PickupType.Ammo, magazineAmmo,
                     transform.position + transform.forward * 0.5f + Vector3.up * 0.05f, "rounds");
        GuardKilled?.Invoke(this);

        radioTimer = 0f;
        if (radioBar != null) radioBar.SetActive(false);
        if (tracer != null) tracer.enabled = false;
        if (agent != null && agent.enabled) agent.enabled = false;

        // Shots pass through the body from now on
        foreach (Collider c in GetComponentsInChildren<Collider>()) c.enabled = false;

        // With a death clip he falls and stays; without one the body is tipped over by code
        if (npcAnimator == null || !npcAnimator.PlayDeath())
            StartCoroutine(FallOver());
    }

    // Fallback death without an animation clip: the whole body tips over backwards and lies there.
    System.Collections.IEnumerator FallOver()
    {
        Quaternion start = transform.rotation;
        Quaternion end = Quaternion.AngleAxis(-88f, transform.right) * start;
        Vector3 pivot = transform.position;
        float t = 0f;
        while (t < 1f)
        {
            t += Time.deltaTime / 0.6f;
            float k = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(t));
            transform.rotation = Quaternion.Slerp(start, end, k);
            // Keep the hips roughly on the floor as he goes over
            transform.position = pivot + Vector3.up * 0.1f * transform.lossyScale.y * k;
            yield return null;
        }
    }

    // Guards' models ship without colliders, so the player's shots would pass straight through
    // them. Add a body-sized capsule (plus a kinematic Rigidbody so physics treats him as a
    // moving object) if the model has none.
    void EnsureHitbox()
    {
        if (GetComponentInChildren<Collider>() != null) return;

        float s = Mathf.Max(transform.lossyScale.y, 0.0001f);
        float height = eyeHeight * 1.12f;    // fallback: a bit above the eyes
        Bounds b = default;
        bool any = false;
        foreach (Renderer r in GetComponentsInChildren<Renderer>())
        {
            if (r is ParticleSystemRenderer || r is LineRenderer) continue;
            if (!any) { b = r.bounds; any = true; } else b.Encapsulate(r.bounds);
        }
        if (any && b.size.y > 0.5f * s) height = b.size.y / s;

        CapsuleCollider capsule = gameObject.AddComponent<CapsuleCollider>();
        capsule.direction = 1;
        capsule.height = height;
        capsule.radius = Mathf.Min(0.32f, height * 0.25f);
        capsule.center = new Vector3(0f, height * 0.5f, 0f);

        Rigidbody body = GetComponent<Rigidbody>();
        if (body == null) body = gameObject.AddComponent<Rigidbody>();
        body.isKinematic = true;
        body.useGravity = false;
        body.interpolation = RigidbodyInterpolation.None;
    }

    // The NavMesh is built from render meshes and can sit below a floor's collider, which lets a
    // moving guard sink into the floor. Each frame, look for a floor surface above his feet (and
    // below his hips) and lift him onto it. Only ever lifts: raycasts ignore back faces, and some
    // of the level's mesh colliders have flipped faces, so a "floor" found below the feet may be
    // the inside of a block he is correctly standing on.
    void KeepFeetOnFloor(float dt)
    {
        float s = transform.lossyScale.y;
        Vector3 origin = transform.position + Vector3.up * (1.0f * s);
        int count = Physics.RaycastNonAlloc(origin, Vector3.down, hitBuffer, 1.0f * s + 1.5f, ~0,
                                            QueryTriggerInteraction.Ignore);
        float floorY = 0f;
        float best = float.MaxValue;
        bool found = false;
        for (int i = 0; i < count; i++)
        {
            RaycastHit h = hitBuffer[i];
            if (h.collider.transform.IsChildOf(transform)) continue;
            if (h.collider.attachedRigidbody != null) continue;                        // player, other guards, props
            if (h.collider.GetComponentInParent<NavMeshAgent>() != null) continue;    // hostages
            if (h.normal.y < 0.5f) continue;                                           // walls
            if (h.distance < best) { best = h.distance; floorY = h.point.y; found = true; }
        }
        if (!found) return;

        float offset = floorY - transform.position.y;   // > 0: his feet are below the floor
        if (offset < 0.01f) return;
        float step = Mathf.Min(offset, 2f * dt);

        if (navReady && agent != null && agent.enabled)
            agent.baseOffset = Mathf.Min(agent.baseOffset + step, 0.75f);
        else
            transform.position += Vector3.up * step;
    }

    void OnNoise(Vector3 position, float radius)
    {
        if (state == State.Dead || state == State.Alerted) return;

        Vector3 offset = position - transform.position;
        offset.y = 0f;
        if (offset.sqrMagnitude > radius * radius || offset.sqrMagnitude < 0.01f) return;

        scanCenterYaw = Quaternion.LookRotation(offset).eulerAngles.y;
        state = State.Searching;
        searchTimer = searchTime;

        MissionManager m = MissionManager.Instance;
        if (m != null && m.IsLockdown && role != GuardRole.Hostage) SetHunt(position);
    }

    // ---- Seeing the player ----

    // Direction he is looking: body direction plus head turn
    Vector3 GazeDirection()
    {
        return Quaternion.Euler(0f, currentYaw + headYaw, 0f) * Vector3.forward;
    }

    bool CanSeePlayer(bool keepTracking)
    {
        if (player == null || !player.activeInHierarchy)
            player = GameObject.FindWithTag("Player");
        if (player == null) return false;

        if (player.TryGetComponent(out PlayerHealth ph) && ph.IsDead) return false;

        Vector3 eye = EyePosition();
        Vector3 aim = PlayerAim();
        Vector3 toAim = aim - eye;
        float dist = toAim.magnitude;

        if (dist > CurrentRange()) return false;

        // Once he has spotted you, he keeps tracking you even when you move behind him
        if (!keepTracking && Vector3.Angle(GazeDirection(), toAim) > fieldOfView * 0.5f)
            return false;

        return HasLineOfSight(eye, aim);
    }

    float CurrentRange()
    {
        if (!GuardAlert.Blackout) return viewRange;
        return GuardAlert.FlashlightOn ? GuardAlert.FlashlightRange : GuardAlert.DarkRange;
    }

    bool HasLineOfSight(Vector3 from, Vector3 to)
    {
        Vector3 dir = to - from;
        float dist = dir.magnitude;
        if (dist < 0.01f) return true;

        if (!FirstHit(from, dir / dist, dist, out RaycastHit hit)) return false;
        return IsPlayer(hit.collider);
    }

    // ---- Shooting ----

    void Fire(Vector3 aim)
    {
        if (player == null && hostageTarget == null) return;

        Vector3 origin = MuzzlePosition();

        // Hitscan: aim at a point near the player. Usually close to the body,
        // sometimes far off, which makes it a miss. The first thing the ray hits decides the result.
        float spread = Random.value < missChance ? missSpread : accuracySpread;
        Vector3 target = aim + Random.insideUnitSphere * spread;
        Vector3 dir = (target - origin).normalized;

        Vector3 end = origin + dir * maxShotDistance;
        if (FirstHit(origin, dir, maxShotDistance, out RaycastHit hit))
        {
            end = hit.point;
            bool flesh = false;
            if (IsPlayer(hit.collider))
            {
                HitPlayer();
                flesh = true;
            }
            else
            {
                // Only hostages who are following the player can be shot; captives are his leverage
                Hostage h = hit.collider.GetComponentInParent<Hostage>();
                if (h != null)
                {
                    flesh = true;
                    if (h.State == Hostage.HostageState.Following) h.Kill();
                }
            }
            ShotEffects.Impact(hit.point, hit.normal, flesh);
        }

        ShowTracer(origin, end);
        ShotEffects.MuzzleFlash(origin, dir);

        recoilKick = 1f;
        if (audioSource != null && fireSound != null)
            audioSource.PlayOneShot(fireSound);
    }

    // The shot comes from the rifle if he has one, otherwise from his eyes
    Vector3 MuzzlePosition()
    {
        return rifle != null ? rifle.position : EyePosition();
    }

    void HitPlayer()
    {
        if (!player.TryGetComponent(out PlayerHealth ph))
            ph = player.AddComponent<PlayerHealth>();
        ph.TakeHit();
    }

    // Returns the closest hit that isn't this guard's own collider
    bool FirstHit(Vector3 origin, Vector3 dir, float maxDist, out RaycastHit result)
    {
        result = default;
        int count = Physics.RaycastNonAlloc(origin, dir, hitBuffer, maxDist, ~0,
                                            QueryTriggerInteraction.Ignore);
        float best = float.MaxValue;
        bool found = false;
        for (int i = 0; i < count; i++)
        {
            RaycastHit h = hitBuffer[i];
            if (h.collider.transform.IsChildOf(transform)) continue;
            if (h.distance < best)
            {
                best = h.distance;
                result = h;
                found = true;
            }
        }
        return found;
    }

    bool IsPlayer(Collider c)
    {
        return player != null && c.transform.IsChildOf(player.transform);
    }

    void ShowTracer(Vector3 from, Vector3 to)
    {
        if (tracer == null)
        {
            Shader shader = Shader.Find("Universal Render Pipeline/Unlit");
            if (shader == null) shader = Shader.Find("Sprites/Default");
            if (shader == null) return; // no tracer if neither shader is available

            tracer = gameObject.AddComponent<LineRenderer>();
            tracer.material = new Material(shader);
            tracer.startWidth = 0.02f;
            tracer.endWidth = 0.02f;
            tracer.useWorldSpace = true;
            tracer.positionCount = 2;
        }

        tracer.SetPosition(0, from);
        tracer.SetPosition(1, to);
        tracer.enabled = true;
        tracerTimer = 0.06f;
    }

    // ---- Mission AI: movement ----

    NavMeshAgent agent;
    NpcAnimator npcAnimator;
    bool navReady;
    bool isMoving;
    Vector3 postPosition;
    bool awayFromPost;
    int patrolIndex;
    float patrolWait;
    Vector3 huntPoint;
    bool hasHunt;
    bool huntArrived;
    float huntSearchTimer;
    Vector3 lastMissionReport = new Vector3(float.MaxValue, 0f, 0f);
    Vector3 holdPoint;
    bool holdPointSet;
    Hostage hostageTarget;
    float hostageAlertTimer;

    const float ArriveDistance = 0.6f;

    void SetupNavigation()
    {
        agent = GetComponent<NavMeshAgent>();
        bool onMesh = NavMesh.SamplePosition(transform.position, out NavMeshHit hit, 2f, NavMesh.AllAreas);
        if (agent == null)
        {
            if (!onMesh)
            {
                Debug.LogWarning($"[TerroristGuard] {name}: no NavMesh under him (bake one). He will stay at his post.");
                return;
            }
            agent = gameObject.AddComponent<NavMeshAgent>();
        }

        agent.updateRotation = false;   // the body yaw is turned by this script
        agent.speed = walkSpeed;
        agent.acceleration = 10f;
        agent.stoppingDistance = 0.2f;
        agent.autoBraking = true;
        agent.radius = 0.35f;

        if (agent.enabled && !agent.isOnNavMesh && onMesh) agent.Warp(hit.position);
        navReady = agent.enabled && agent.isOnNavMesh;
        if (!navReady) agent.enabled = false;
    }

    // Bomb guards stand around wherever the bomb spawned this run, facing outward
    void TakeBombPost()
    {
        Bomb bomb = Bomb.Instance != null ? Bomb.Instance : FindFirstObjectByType<Bomb>();
        if (bomb == null) return;

        int index = RoleIndex(GuardRole.Bomb);
        Vector3 offset = Quaternion.Euler(0f, 45f + index * 180f, 0f) * Vector3.forward * bombGuardDistance;
        Vector3 p = bomb.transform.position + offset;
        if (NavMesh.SamplePosition(p, out NavMeshHit hit, 2f, NavMesh.AllAreas)) p = hit.position;

        if (navReady) agent.Warp(p);
        else transform.position = p;

        postPosition = p;
        postYaw = Quaternion.LookRotation(offset).eulerAngles.y;
        scanCenterYaw = postYaw;
        currentYaw = postYaw;
        transform.rotation = Quaternion.Euler(0f, postYaw, 0f);
    }

    // This guard's place among the guards with the same role (stable within a run)
    int RoleIndex(GuardRole r)
    {
        List<TerroristGuard> same = new List<TerroristGuard>();
        foreach (TerroristGuard g in FindObjectsByType<TerroristGuard>(FindObjectsSortMode.None))
            if (g.role == r) same.Add(g);
        same.Sort((a, b) =>
        {
            int byName = string.CompareOrdinal(a.name, b.name);
            if (byName != 0) return byName;
            int byX = a.transform.position.x.CompareTo(b.transform.position.x);
            return byX != 0 ? byX : a.transform.position.z.CompareTo(b.transform.position.z);
        });
        return Mathf.Max(0, same.IndexOf(this));
    }

    void SetHunt(Vector3 point)
    {
        huntPoint = point;
        hasHunt = true;
        huntArrived = false;
    }

    void UpdateMovement(float dt, bool lockdown, bool holdStill)
    {
        isMoving = false;
        if (!navReady || agent == null || !agent.enabled || !agent.isOnNavMesh) return;

        if (holdStill)
        {
            StopMoving();
            return;
        }

        if (lockdown)
        {
            if (role == GuardRole.Hostage)
            {
                if (!holdPointSet) holdPointSet = PickHoldPoint();
                if (!Near(holdPoint)) Go(holdPoint, true);
                else StopMoving();
                return;
            }

            // Everyone else hunts the newest last-known position (from any guard or the alarm)
            MissionManager m = MissionManager.Instance;
            if (m != null && m.HasLastKnownPlayerPosition && FlatDistance(m.LastKnownPlayerPosition, lastMissionReport) > 1f)
            {
                lastMissionReport = m.LastKnownPlayerPosition;
                SetHunt(lastMissionReport);
            }

            if (!hasHunt)
            {
                StopMoving();
                return;
            }

            if (!huntArrived)
            {
                Go(huntPoint, true);
                if (Near(huntPoint))
                {
                    huntArrived = true;
                    huntSearchTimer = searchTime;
                }
            }
            else
            {
                StopMoving();
                huntSearchTimer -= dt;
                if (huntSearchTimer <= 0f)
                {
                    // Nothing here: search somewhere nearby
                    Vector3 random = huntPoint + Random.insideUnitSphere * huntWanderRadius;
                    if (NavMesh.SamplePosition(random, out NavMeshHit hit, huntWanderRadius, NavMesh.AllAreas))
                        huntPoint = hit.position;
                    huntArrived = false;
                }
            }
            return;
        }

        // Before the lockdown: a noise makes him stop and look
        if (state == State.Searching)
        {
            StopMoving();
            return;
        }

        if (role == GuardRole.Patrol && patrolRoute != null && patrolRoute.Count > 0)
        {
            Vector3 wp = patrolRoute.GetPoint(patrolIndex);
            if (Near(wp))
            {
                StopMoving();
                patrolWait += dt;
                if (patrolWait >= patrolWaitTime)
                {
                    patrolWait = 0f;
                    patrolIndex = (patrolIndex + 1) % patrolRoute.Count;
                }
            }
            else Go(wp, false);
            return;
        }

        if (!Near(postPosition))
        {
            Go(postPosition, false);
            awayFromPost = true;
        }
        else
        {
            StopMoving();
            if (awayFromPost)
            {
                awayFromPost = false;
                scanCenterYaw = postYaw;
            }
        }
    }

    bool PickHoldPoint()
    {
        if (!Hostage.TryGetRoomCenter(out Vector3 center))
        {
            holdPoint = postPosition;
            return true;
        }
        int index = RoleIndex(GuardRole.Hostage);
        Vector3 p = center + Quaternion.Euler(0f, index * 180f, 0f) * Vector3.forward * hostageHoldRadius;
        if (NavMesh.SamplePosition(p, out NavMeshHit hit, 3f, NavMesh.AllAreas)) p = hit.position;
        holdPoint = p;
        return true;
    }

    void Go(Vector3 point, bool run)
    {
        agent.isStopped = false;
        agent.speed = run ? runSpeed : walkSpeed;
        if ((!agent.hasPath && !agent.pathPending) || FlatDistance(agent.destination, point) > 0.5f)
            agent.SetDestination(point);

        Vector3 v = agent.velocity;
        v.y = 0f;
        isMoving = v.sqrMagnitude > 0.04f;
        if (isMoving) scanCenterYaw = Quaternion.LookRotation(v).eulerAngles.y;
    }

    void StopMoving()
    {
        if (agent.hasPath) agent.ResetPath();
        agent.isStopped = true;
    }

    bool Near(Vector3 point)
    {
        return FlatDistance(transform.position, point) <= ArriveDistance;
    }

    static float FlatDistance(Vector3 a, Vector3 b)
    {
        a.y = 0f;
        b.y = 0f;
        return Vector3.Distance(a, b);
    }

    // ---- Mission AI: hostages as targets ----

    Hostage FindVisibleHostage()
    {
        Vector3 eye = EyePosition();
        Hostage best = null;
        float bestDist = CurrentRange();
        foreach (Hostage h in Hostage.All)
        {
            if (h == null || h.State != Hostage.HostageState.Following) continue;
            Vector3 to = HostageAim(h) - eye;
            float d = to.magnitude;
            if (d > bestDist || d < 0.01f) continue;
            if (h != hostageTarget && Vector3.Angle(GazeDirection(), to) > fieldOfView * 0.5f) continue;
            if (!FirstHit(eye, to / d, d + 0.5f, out RaycastHit hit)) continue;
            if (hit.collider.GetComponentInParent<Hostage>() != h) continue;
            best = h;
            bestDist = d;
        }
        return best;
    }

    static Vector3 HostageAim(Hostage h)
    {
        return h.transform.position + Vector3.up * (1.0f * h.transform.lossyScale.y);
    }

    // ---- Mission AI: radio alarm ----

    float radioTimer;
    GameObject radioBar;
    Image radioFill;
    static Sprite radioSprite;

    void UpdateRadio(float dt, bool seesPlayer, MissionManager m)
    {
        bool active = canRadio && m != null && m.State == MissionState.Infiltration;
        if (active && seesPlayer)
        {
            radioTimer += dt;
            if (radioTimer >= radioTime)
            {
                radioTimer = 0f;
                if (radioSound != null)
                {
                    if (audioSource == null)
                    {
                        audioSource = gameObject.AddComponent<AudioSource>();
                        audioSource.playOnAwake = false;
                        audioSource.spatialBlend = 1f;
                    }
                    audioSource.PlayOneShot(radioSound);
                }
                m.RaiseAlarm($"{name} radioed an alarm");
            }
        }
        else radioTimer = 0f;

        if (radioBar != null)
        {
            bool show = radioTimer > 0f;
            if (radioBar.activeSelf != show) radioBar.SetActive(show);
            if (show) radioFill.fillAmount = radioTimer / Mathf.Max(radioTime, 0.01f);
        }
    }

    void BuildRadioBar()
    {
        if (!canRadio) return;
        if (radioSprite == null)
            radioSprite = Sprite.Create(Texture2D.whiteTexture, new Rect(0f, 0f, 4f, 4f), new Vector2(0.5f, 0.5f));

        radioBar = new GameObject("RadioBar", typeof(RectTransform));
        radioBar.transform.SetParent(transform, false);
        Canvas c = radioBar.AddComponent<Canvas>();
        c.renderMode = RenderMode.WorldSpace;
        c.sortingOrder = 50;
        RectTransform rt = (RectTransform)radioBar.transform;
        rt.sizeDelta = new Vector2(120f, 40f);

        GameObject label = new GameObject("Label", typeof(RectTransform));
        label.transform.SetParent(rt, false);
        Text t = label.AddComponent<Text>();
        t.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        t.text = "RADIO";
        t.fontSize = 20;
        t.fontStyle = FontStyle.Bold;
        t.alignment = TextAnchor.MiddleCenter;
        t.color = new Color(1f, 0.25f, 0.2f);
        RectTransform lr = (RectTransform)label.transform;
        lr.anchorMin = new Vector2(0f, 0.5f);
        lr.anchorMax = new Vector2(1f, 1f);
        lr.offsetMin = Vector2.zero;
        lr.offsetMax = Vector2.zero;

        GameObject back = new GameObject("Back", typeof(RectTransform));
        back.transform.SetParent(rt, false);
        Image bi = back.AddComponent<Image>();
        bi.color = new Color(0f, 0f, 0f, 0.7f);
        RectTransform br = (RectTransform)back.transform;
        br.anchorMin = new Vector2(0f, 0.1f);
        br.anchorMax = new Vector2(1f, 0.45f);
        br.offsetMin = Vector2.zero;
        br.offsetMax = Vector2.zero;

        GameObject fill = new GameObject("Fill", typeof(RectTransform));
        fill.transform.SetParent(back.transform, false);
        radioFill = fill.AddComponent<Image>();
        radioFill.sprite = radioSprite;
        radioFill.type = Image.Type.Filled;
        radioFill.fillMethod = Image.FillMethod.Horizontal;
        radioFill.fillAmount = 0f;
        radioFill.color = new Color(1f, 0.3f, 0.2f);
        RectTransform fr = (RectTransform)fill.transform;
        fr.anchorMin = Vector2.zero;
        fr.anchorMax = Vector2.one;
        fr.offsetMin = new Vector2(2f, 2f);
        fr.offsetMax = new Vector2(-2f, -2f);

        radioBar.SetActive(false);
    }

    // Keeps the bar above his head (1.2 m wide in the world), facing the camera
    void UpdateRadioBarTransform()
    {
        if (radioBar == null || !radioBar.activeSelf) return;
        Transform bt = radioBar.transform;
        bt.position = EyePosition() + Vector3.up * radioBarHeight;
        Vector3 ls = transform.lossyScale;
        const float k = 0.01f;
        bt.localScale = new Vector3(k / Mathf.Max(ls.x, 0.0001f), k / Mathf.Max(ls.y, 0.0001f), k / Mathf.Max(ls.z, 0.0001f));
        UnityEngine.Camera cam = UnityEngine.Camera.main;
        if (cam != null) bt.rotation = Quaternion.LookRotation(bt.position - cam.transform.position, Vector3.up);
    }

    // ---- Helpers ----

    Transform FindRifle()
    {
        // Prefer a rifle under the right hand, then fall back to anything named Ak47 on him
        foreach (Transform t in rightHand.GetComponentsInChildren<Transform>(true))
            if (t.name.ToLower().Contains("ak47")) return t;

        foreach (Transform t in GetComponentsInChildren<Transform>(true))
            if (t.name.ToLower().Contains("ak47")) return t;

        return null;
    }

    // Finds a child of the rifle whose name contains the key, such as "stock" or "foregrip"
    Transform FindChildContaining(Transform root, string key)
    {
        foreach (Transform t in root.GetComponentsInChildren<Transform>(true))
            if (t != root && t.name.ToLower().Contains(key)) return t;

        return null;
    }

    // Arm length from shoulder to wrist, slightly shortened so the arm never locks straight
    float ArmReach(Transform upper, Transform lower, Transform hand)
    {
        return (Vector3.Distance(upper.position, lower.position)
              + Vector3.Distance(lower.position, hand.position)) * 0.97f;
    }

    Vector3 FlatDirection(Vector3 v)
    {
        v.y = 0f;
        return v.sqrMagnitude < 0.0001f ? transform.right : v.normalized;
    }

    Vector3 EyePosition()
    {
        return transform.position + Vector3.up * (eyeHeight * transform.lossyScale.y);
    }

    // Roughly the middle of the player's body, scaled to the player's size
    Vector3 PlayerAim()
    {
        Transform t = player.transform;
        return t.position + Vector3.up * (1.0f * t.lossyScale.y);
    }

    float YawToward(Vector3 point)
    {
        Vector3 flat = point - transform.position;
        flat.y = 0f;
        if (flat.sqrMagnitude < 0.01f) return currentYaw;
        return Quaternion.LookRotation(flat).eulerAngles.y;
    }
}
