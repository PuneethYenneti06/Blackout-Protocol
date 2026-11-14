using UnityEngine;
using UnityEngine.Rendering;

// Player camera. First person by default; press V to switch to the over-the-shoulder view.
// When the player dies it switches to third person so the death can be seen.
// (The class keeps its old name so the scene's existing camera component keeps working.)
public class ThirdPersonCamera : MonoBehaviour
{
    public Transform target;

    [Header("View")]
    public bool firstPerson = true;
    public KeyCode toggleViewKey = KeyCode.V;
    public float sensitivity = 3f;

    [Header("First person")]
    public float eyeHeight = 1.6f;        // fallback eye height above his feet (scaled), if no head bone is found
    public float eyeForward = 0.12f;      // how far in front of his head the camera sits (scaled)
    public float fpMinPitch = -80f;       // look up limit
    public float fpMaxPitch = 80f;        // look down limit
    public float fpNearClip = 0.05f;      // small, so walls right in front of you don't get cut open
    public float headBob = 0.022f;        // gentle up-down bob while running, metres (0 = off)

    [Header("Third person")]
    public float distance = 4f;          // how far behind
    public float height = 1.7f;          // pivot height above his feet
    public float shoulderOffset = 0.5f;  // slight shift to the right
    public float minPitch = -20f;        // look up limit
    public float maxPitch = 60f;         // look down limit
    public float smooth = 12f;
    public float collisionRadius = 0.25f;
    public LayerMask collisionMask = ~0;

    // True while the view is first person. Movement and shooting read this.
    public static bool FirstPersonActive { get; private set; }

    float yaw;
    float pitch = 15f;

    // Recoil: the gun kicks the view up a little on each shot, and it settles back
    public float recoilRecovery = 8f;
    float recoilPitch, recoilYaw;

    Camera cam;
    float thirdPersonNearClip;
    Transform head;
    float eyeOffsetY = -1f;          // smoothed head height above his feet
    Renderer[] bodyRenderers;
    ShadowCastingMode[] bodyShadowModes;
    bool bodyHidden;
    PlayerHealth health;

    // Jumpscare: snap the view toward a point, shake hard and zoom in for a moment
    public void Scare(Vector3 lookAt)
    {
        Vector3 d = lookAt - transform.position;
        Vector3 flat = new Vector3(d.x, 0f, d.z);
        if (flat.sqrMagnitude > 0.0001f)
        {
            scareYaw = Quaternion.LookRotation(flat).eulerAngles.y;
            scarePitch = Mathf.Clamp(-Mathf.Atan2(d.y, flat.magnitude) * Mathf.Rad2Deg, fpMinPitch, fpMaxPitch);
            scareTurnLeft = 0.12f;
        }
        shakeLeft = jumpscareShakeTime;
        shakeStrength = jumpscareShake;
        fovKick = -14f;
    }
    float scareYaw, scarePitch, scareTurnLeft;
    public float jumpscareShake = 7f;      // how hard the view shakes in the jumpscare, degrees
    public float jumpscareShakeTime = 0.9f;  // how long it shakes, seconds
    float shakeLeft, shakeStrength;
    Vector3 shakeOffset;
    float fovKick, baseFov = -1f;

    public void AddRecoil(float pitchDegrees, float yawDegrees)
    {
        recoilPitch += pitchDegrees;
        recoilYaw += yawDegrees;
        if (viewModel != null) viewModel.Kick();
    }

    FirstPersonViewModel viewModel;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics()
    {
        FirstPersonActive = false;
    }

    void Start()
    {
        cam = GetComponent<Camera>();
        if (cam != null) thirdPersonNearClip = cam.nearClipPlane;

        // The rifle held on the right side of the view in first person
        viewModel = GetComponent<FirstPersonViewModel>();
        if (viewModel == null) viewModel = gameObject.AddComponent<FirstPersonViewModel>();

        if (target != null)
        {
            yaw = target.eulerAngles.y;
            head = FindHead(target);
            health = target.GetComponentInParent<PlayerHealth>();
        }
        pitch = firstPerson ? 0f : 15f;

        Cursor.lockState = CursorLockMode.Locked;
        Cursor.visible = false;
    }

    void LateUpdate()
    {
        if (target == null) return;

        if (Input.GetKeyDown(KeyCode.Escape))
        {
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
        }
        if (Input.GetMouseButtonDown(0) && Cursor.lockState != CursorLockMode.Locked)
        {
            Cursor.lockState = CursorLockMode.Locked;
            Cursor.visible = false;
        }
        if (Input.GetKeyDown(toggleViewKey)) firstPerson = !firstPerson;

        if (health == null) health = target.GetComponentInParent<PlayerHealth>();
        bool dead = health != null && health.IsDead;
        bool fp = firstPerson && !dead;
        FirstPersonActive = fp;

        yaw += Input.GetAxis("Mouse X") * sensitivity;
        pitch -= Input.GetAxis("Mouse Y") * sensitivity;
        pitch = fp ? Mathf.Clamp(pitch, fpMinPitch, fpMaxPitch) : Mathf.Clamp(pitch, minPitch, maxPitch);

        float settle = 1f - Mathf.Exp(-recoilRecovery * Time.deltaTime);
        recoilPitch = Mathf.Lerp(recoilPitch, 0f, settle);
        recoilYaw = Mathf.Lerp(recoilYaw, 0f, settle);

        // Jumpscare: whip the view toward him
        if (scareTurnLeft > 0f)
        {
            scareTurnLeft -= Time.deltaTime;
            yaw = Mathf.MoveTowardsAngle(yaw, scareYaw, 1100f * Time.deltaTime);
            pitch = Mathf.MoveTowardsAngle(pitch, scarePitch, 600f * Time.deltaTime);
        }

        Quaternion rot = Quaternion.Euler(pitch - recoilPitch, yaw + recoilYaw, 0f);

        // Jumpscare: shake and zoom, both fading out
        if (shakeLeft > 0f)
        {
            shakeLeft -= Time.deltaTime;
            float k = Mathf.Clamp01(shakeLeft / jumpscareShakeTime) * shakeStrength;
            rot *= Quaternion.Euler(Random.Range(-k, k), Random.Range(-k, k), Random.Range(-k, k) * 0.8f);
            shakeOffset = new Vector3(Random.Range(-1f, 1f), Random.Range(-1f, 1f), 0f) * (k * 0.015f);
        }
        if (cam != null)
        {
            if (baseFov < 0f) baseFov = cam.fieldOfView;
            fovKick = Mathf.Lerp(fovKick, 0f, 1f - Mathf.Exp(-4f * Time.deltaTime));
            cam.fieldOfView = baseFov + fovKick;
        }

        SetBodyHidden(fp);
        if (cam != null) cam.nearClipPlane = fp ? fpNearClip : thirdPersonNearClip;

        if (fp)
        {
            if (shakeLeft <= 0f) shakeOffset = Vector3.zero;
            transform.SetPositionAndRotation(EyePosition() + rot * shakeOffset, rot);
            return;
        }

        Vector3 pivot = target.position + Vector3.up * height;
        Vector3 desired = pivot + rot * new Vector3(shoulderOffset, 0f, -distance);

        // Pull the camera in if a wall is between it and the player
        Vector3 toCam = desired - pivot;
        if (Physics.SphereCast(pivot, collisionRadius, toCam.normalized, out RaycastHit hit,
                               toCam.magnitude, collisionMask, QueryTriggerInteraction.Ignore))
        {
            desired = pivot + toCam.normalized * Mathf.Max(0.5f, hit.distance);
        }

        transform.position = Vector3.Lerp(transform.position, desired, smooth * Time.deltaTime);
        transform.rotation = rot;
    }

    // Eye position: above his feet at head height, a little in front of his face.
    // Only the head's height is used (smoothed), so running animations don't shake the view.
    Vector3 EyePosition()
    {
        float dt = Time.deltaTime;
        float s = target.lossyScale.y;

        // How fast he is moving (flat)
        Vector3 moved = target.position - lastTargetPos; moved.y = 0f;
        float speed = dt > 0f ? moved.magnitude / dt : 0f;
        lastTargetPos = target.position;
        smoothSpeed = Mathf.Lerp(smoothSpeed, speed, 1f - Mathf.Exp(-8f * dt));

        // Eye height comes from the head while he stands still, and is held while he moves,
        // so the running animation's head bounce doesn't shake the view
        float headHeight = head != null ? head.position.y - target.position.y + 0.05f * s : eyeHeight * s;
        if (eyeOffsetY < 0f) eyeOffsetY = headHeight;
        else if (smoothSpeed < 0.3f) eyeOffsetY = Mathf.Lerp(eyeOffsetY, headHeight, 1f - Mathf.Exp(-3f * dt));

        // Smooth out small jumps of the body (steps, thresholds)
        float baseY = target.position.y;
        smoothBaseY = float.IsNaN(smoothBaseY) || Mathf.Abs(baseY - smoothBaseY) > 1f
            ? baseY
            : Mathf.Lerp(smoothBaseY, baseY, 1f - Mathf.Exp(-18f * dt));

        // Our own soft bob: one dip per step, stronger the faster he goes, fading out when he stops
        bobPhase += dt * smoothSpeed * 2.1f;
        float bobStrength = headBob * Mathf.Clamp01(smoothSpeed / 5f) * s;
        float bobY = (Mathf.Cos(bobPhase * 2f) - 1f) * 0.5f * bobStrength;
        float bobX = Mathf.Sin(bobPhase) * 0.35f * bobStrength;

        Quaternion flatRot = Quaternion.Euler(0f, yaw, 0f);
        Vector3 basePos = new Vector3(target.position.x, smoothBaseY, target.position.z);
        return basePos + Vector3.up * (eyeOffsetY + bobY)
             + flatRot * new Vector3(bobX, 0f, eyeForward * s);
    }
    Vector3 lastTargetPos;
    float smoothSpeed;
    float smoothBaseY = float.NaN;
    float bobPhase;

    // Hides his body in first person (it would block the view), but keeps his shadow.
    void SetBodyHidden(bool hide)
    {
        if (hide == bodyHidden && bodyRenderers != null) return;

        if (bodyRenderers == null)
        {
            Transform root = target.GetComponentInParent<PlayerHealth>() != null
                ? target.GetComponentInParent<PlayerHealth>().transform
                : target;
            bodyRenderers = root.GetComponentsInChildren<Renderer>(true);
            bodyShadowModes = new ShadowCastingMode[bodyRenderers.Length];
            for (int i = 0; i < bodyRenderers.Length; i++)
                if (bodyRenderers[i] != null) bodyShadowModes[i] = bodyRenderers[i].shadowCastingMode;
        }

        for (int i = 0; i < bodyRenderers.Length; i++)
        {
            Renderer r = bodyRenderers[i];
            if (r == null || r is LineRenderer || r is ParticleSystemRenderer || r is TrailRenderer) continue;
            r.shadowCastingMode = hide ? ShadowCastingMode.ShadowsOnly : bodyShadowModes[i];
        }
        bodyHidden = hide;
    }

    static Transform FindHead(Transform root)
    {
        Transform player = root.GetComponentInParent<PlayerHealth>() != null
            ? root.GetComponentInParent<PlayerHealth>().transform
            : root;

        Animator animator = player.GetComponentInChildren<Animator>();
        if (animator != null && animator.isHuman)
        {
            Transform h = animator.GetBoneTransform(HumanBodyBones.Head);
            if (h != null) return h;
        }

        // Generic rigs: a bone named like "Bip001 Head" or "Head"
        foreach (Transform t in player.GetComponentsInChildren<Transform>(true))
        {
            string n = t.name.ToLower();
            if ((n.EndsWith(" head") || n == "head" || n.EndsWith("_head") || n.EndsWith(":head"))
                && !n.Contains("top") && !n.Contains("end"))
                return t;
        }
        return null;
    }
}
