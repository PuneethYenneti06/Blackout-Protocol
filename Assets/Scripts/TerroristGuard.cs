using UnityEngine;

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
public class TerroristGuard : MonoBehaviour
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

    // Fired when any guard dies, so the mission logic can count them.
    public static event System.Action<TerroristGuard> GuardKilled;

    State state = State.Scanning;
    int health;

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

        bool sees = CanSeePlayer(keepTracking: state == State.Alerted);

        if (sees)
        {
            if (state != State.Alerted)
            {
                state = State.Alerted;
                alertTimer = 0f; // start the reaction delay
            }

            alertTimer += dt;
            lastKnownPosition = PlayerAim();

            if (alertTimer >= reactionDelay && Time.time >= nextShotTime)
            {
                nextShotTime = Time.time + fireInterval;
                Fire();
            }
        }
        else if (state == State.Alerted)
        {
            // Lost sight of you: lower the rifle and go look where you were last seen
            state = State.Searching;
            searchTimer = searchTime;
            scanCenterYaw = YawToward(lastKnownPosition);
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

        bool alerted = state == State.Alerted;

        // Rifle: rises while alerted, lowers otherwise
        float aimTarget = (holdRifle && alerted) ? 1f : 0f;
        aimWeight = Mathf.MoveTowards(aimWeight, aimTarget, dt / Mathf.Max(raiseTime, 0.01f));
        recoilKick = Mathf.MoveTowards(recoilKick, 0f, dt * 8f);

        // Body: holds the post, turns to a sound, or turns to face you when alerted
        float bodyTarget;
        if (alerted) bodyTarget = YawToward(lastKnownPosition);
        else bodyTarget = scanCenterYaw + (head != null ? 0f : sweepOffset);

        currentYaw = Mathf.MoveTowardsAngle(currentYaw, bodyTarget, turnSpeed * dt);
        transform.rotation = Quaternion.Euler(0f, currentYaw, 0f);

        // Head: looks left and right while scanning, straight ahead when alerted
        float headTarget = 0f;
        if (!alerted && head != null)
            headTarget = Mathf.Clamp(sweepOffset, -maxHeadYaw, maxHeadYaw);
        headYaw = Mathf.MoveTowardsAngle(headYaw, headTarget, headTurnSpeed * dt);
    }

    // Runs after the animator. Every frame starts from the rest pose, so nothing builds up over time.
    void LateUpdate()
    {
        if (state == State.Dead) return;

        ResetPose();

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
        if (player != null) aimDir = (PlayerAim() - chestPos).normalized;

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
        GuardKilled?.Invoke(this);
        gameObject.SetActive(false);
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

    void Fire()
    {
        if (player == null) return;

        Vector3 origin = MuzzlePosition();
        Vector3 aim = PlayerAim();

        // Hitscan: aim at a point near the player. Usually close to the body,
        // sometimes far off, which makes it a miss. The first thing the ray hits decides the result.
        float spread = Random.value < missChance ? missSpread : accuracySpread;
        Vector3 target = aim + Random.insideUnitSphere * spread;
        Vector3 dir = (target - origin).normalized;

        Vector3 end = origin + dir * maxShotDistance;
        if (FirstHit(origin, dir, maxShotDistance, out RaycastHit hit))
        {
            end = hit.point;
            if (IsPlayer(hit.collider)) HitPlayer();
        }

        ShowTracer(origin, end);

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
