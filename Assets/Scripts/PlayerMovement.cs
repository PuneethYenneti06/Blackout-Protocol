using System.Collections.Generic;
using UnityEngine;

// Moves the player with a Rigidbody + CapsuleCollider.
// The CharacterController on the object is only used as the source of the capsule
// size: it is switched off at start, because it sinks into the floor model's thin
// mesh colliders and cannot get back out. A Rigidbody is pushed out of them by physics.
[RequireComponent(typeof(CharacterController))]
public class PlayerMovement : MonoBehaviour
{
    public float walkSpeed = 3.5f;
    public float runSpeed = 5f;
    public float turnSpeed = 12f;
    public float gravity = -20f;
    public float jumpHeight = 1.2f;
    public LayerMask groundMask = ~0;

    Rigidbody rb;
    CapsuleCollider capsule;
    Animator animator;
    static readonly int IdleHash = Animator.StringToHash("Idle");
    static readonly int WalkHash = Animator.StringToHash("Walk");
    static readonly int RunHash = Animator.StringToHash("Run");
    int currentState;

    Vector3 moveDir;      // wanted horizontal direction, set in Update
    Vector3 aimDir;       // camera forward (flattened), set in Update
    bool aiming;          // shoot button held, set in Update
    float moveSpeed;
    bool jumpQueued;
    bool grounded;
    readonly List<Vector3> wallNormals = new List<Vector3>();

    void Awake()
    {
        animator = GetComponent<Animator>();

        CharacterController cc = GetComponent<CharacterController>();
        capsule = gameObject.AddComponent<CapsuleCollider>();
        capsule.direction = 1; // Y axis
        capsule.center = cc.center;
        capsule.radius = cc.radius;
        capsule.height = cc.height;
        // No friction, so he slides along walls instead of gripping them
        capsule.material = new PhysicsMaterial("PlayerNoFriction")
        {
            dynamicFriction = 0f,
            staticFriction = 0f,
            bounciness = 0f,
            frictionCombine = PhysicsMaterialCombine.Minimum,
            bounceCombine = PhysicsMaterialCombine.Minimum
        };
        cc.enabled = false;

        rb = gameObject.AddComponent<Rigidbody>();
        rb.mass = 70f;
        rb.useGravity = false; // gravity is applied below with our own value
        rb.constraints = RigidbodyConstraints.FreezeRotation;
        rb.interpolation = RigidbodyInterpolation.Interpolate;
        rb.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
        lastPos = rb.position;
    }

    void Update()
    {
        Vector3 input = new Vector3(
            Input.GetAxisRaw("Horizontal"), 0f, Input.GetAxisRaw("Vertical")).normalized;

        // Move relative to the camera if there is one, otherwise relative to the world
        Vector3 camF = Vector3.forward;
        Vector3 camR = Vector3.right;
        UnityEngine.Camera mainCam = UnityEngine.Camera.main;
        if (mainCam != null)
        {
            camF = mainCam.transform.forward; camF.y = 0f; camF.Normalize();
            camR = mainCam.transform.right; camR.y = 0f; camR.Normalize();
        }

        Vector3 dir = camF * input.z + camR * input.x;
        bool moving = dir.sqrMagnitude > 0.01f;
        bool running = Input.GetKey(KeyCode.LeftShift);

        moveDir = moving ? dir.normalized : Vector3.zero;
        moveSpeed = running ? runSpeed : walkSpeed;
        aimDir = camF;
        aiming = Input.GetMouseButton(0);
        if (grounded && Input.GetKeyDown(KeyCode.Space)) jumpQueued = true;

        if (animator != null && animator.runtimeAnimatorController != null)
        {
            float animSpeed = moving ? (running ? 1f : 0.5f) : 0f;
            animator.SetFloat("Speed", animSpeed, 0.1f, Time.deltaTime);

            int wanted = !moving ? IdleHash : (running ? RunHash : WalkHash);
            if (wanted != currentState)
            {
                animator.CrossFadeInFixedTime(wanted, 0.1f);
                currentState = wanted;
            }
        }
    }

    // ---- Wall safety net ----
    // The walls in the floor model are thin mesh shells, and physics sometimes lets the
    // capsule get wedged inside one. So every physics step we check whether his body is
    // inside a wall; if it is, we put him back where he last stood clear and give him a
    // small knock-back, instead of letting him get stuck.
    public float knockBackSpeed = 3f;
    public float knockBackTime = 0.15f;
    Vector3 lastSafePosition;
    bool hasSafePosition;
    Vector3 knockBack;
    float knockBackLeft;
    readonly Collider[] overlapHits = new Collider[8];

    bool BodyInsideWall()
    {
        Vector3 scale = transform.lossyScale;
        float radius = capsule.radius * Mathf.Max(scale.x, scale.z) * 0.9f;
        float height = capsule.height * scale.y;
        Vector3 center = rb.position + rb.rotation * Vector3.Scale(capsule.center, scale);
        // Bottom raised so the floor and small ledges don't count as walls
        Vector3 bottom = center + Vector3.up * (-height * 0.5f + 0.25f + radius);
        Vector3 top = center + Vector3.up * (height * 0.5f - radius);
        if (top.y < bottom.y) top.y = bottom.y;

        int count = Physics.OverlapCapsuleNonAlloc(bottom, top, radius, overlapHits, ~0,
                                                   QueryTriggerInteraction.Ignore);
        for (int i = 0; i < count; i++)
        {
            Collider c = overlapHits[i];
            if (c == capsule || c.transform.IsChildOf(transform)) continue;
            if (c.GetComponentInParent<Door>() != null) continue; // doors swing through him
            return true;
        }
        return false;
    }

    void KeepOutOfWalls()
    {
        if (!BodyInsideWall())
        {
            lastSafePosition = rb.position;
            hasSafePosition = true;
            return;
        }
        if (!hasSafePosition) return;

        Vector3 back = lastSafePosition - rb.position;
        back.y = 0f;
        Vector3 away = back.sqrMagnitude > 0.0001f ? back.normalized : -moveDir;
        rb.position = new Vector3(lastSafePosition.x, rb.position.y, lastSafePosition.z);
        knockBack = away * knockBackSpeed;
        knockBackLeft = knockBackTime;
    }

    void FixedUpdate()
    {
        KeepOutOfWalls();
        grounded = CheckGrounded();
        bool moving = moveDir != Vector3.zero;

        if (aiming && aimDir.sqrMagnitude > 0.001f)
        {
            // aiming: face where the camera looks
            rb.MoveRotation(Quaternion.Slerp(rb.rotation, Quaternion.LookRotation(aimDir), turnSpeed * Time.fixedDeltaTime));
        }
        else if (moving)
        {
            Quaternion target = Quaternion.LookRotation(moveDir);
            rb.MoveRotation(Quaternion.Slerp(rb.rotation, target, turnSpeed * Time.fixedDeltaTime));
        }

        float vy = rb.linearVelocity.y + gravity * Time.fixedDeltaTime;
        if (grounded && vy < 0f) vy = -1f; // small push down keeps him on the floor
        if (jumpQueued && grounded) vy = Mathf.Sqrt(jumpHeight * -2f * gravity);
        jumpQueued = false;

        Vector3 horizontal = moveDir * moveSpeed;
        // Don't push into walls he is touching: drop the part of the movement that goes
        // into each wall, so he slides along it and physics can push him back out.
        foreach (Vector3 n in wallNormals)
        {
            float into = Vector3.Dot(horizontal, n);
            if (into < 0f) horizontal -= n * into;
        }
        wallNormals.Clear();

        if (knockBackLeft > 0f)
        {
            // Knock-back replaces his own movement for a moment
            horizontal = knockBack * (knockBackLeft / knockBackTime);
            knockBackLeft -= Time.fixedDeltaTime;
        }
        rb.linearVelocity = new Vector3(horizontal.x, vy, horizontal.z);

        DebugStuck(moving);
    }

    bool CheckGrounded()
    {
        Vector3 scale = transform.lossyScale;
        float radius = capsule.radius * Mathf.Max(scale.x, scale.z);
        float halfHeight = Mathf.Max(radius, capsule.height * scale.y * 0.5f);
        Vector3 bottomSphere = transform.TransformPoint(capsule.center) + Vector3.down * (halfHeight - radius);

        // Cast a slightly smaller sphere down from inside the capsule's bottom
        return Physics.SphereCast(bottomSphere + Vector3.up * 0.05f, radius * 0.9f, Vector3.down,
                                  out RaycastHit hit, 0.05f + radius * 0.1f + 0.08f, groundMask,
                                  QueryTriggerInteraction.Ignore)
               && hit.normal.y > 0.5f;
    }

    void OnCollisionStay(Collision collision)
    {
        bool logged = false;
        for (int i = 0; i < collision.contactCount; i++)
        {
            ContactPoint c = collision.GetContact(i);
            if (c.normal.y > 0.7f) continue; // ignore the ground
            Vector3 n = c.normal; n.y = 0f;
            if (n.sqrMagnitude > 0.0001f && wallNormals.Count < 16) wallNormals.Add(n.normalized);
            if (!logged && touching.Length < 400)
            {
                touching.Append($"{collision.collider.name} n={c.normal:F2} sep={c.separation:F3}; ");
                logged = true;
            }
        }
    }

    // ---- TEMPORARY stuck diagnostics: remove once the wall problem is solved ----
    float stuckTime;
    float nextLog;
    Vector3 lastPos;
    readonly System.Text.StringBuilder touching = new System.Text.StringBuilder();

    void DebugStuck(bool moving)
    {
        Vector3 actual = (rb.position - lastPos) / Time.fixedDeltaTime; actual.y = 0f;
        lastPos = rb.position;

        stuckTime = moving && actual.magnitude < 0.1f * walkSpeed ? stuckTime + Time.fixedDeltaTime : 0f;
        if (stuckTime > 0.5f && Time.time > nextLog)
        {
            nextLog = Time.time + 1f;
            Debug.LogWarning($"[STUCK4] insideWall={BodyInsideWall()} pos={rb.position:F3} wanted={moveDir:F2} actualSpeed={actual.magnitude:F3} " +
                             $"grounded={grounded} touching=[{touching}]");
        }
        touching.Clear();
    }
}