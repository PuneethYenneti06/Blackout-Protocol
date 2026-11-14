using UnityEngine;

// Hitscan rifle with a magazine, spare ammo and reload.
// Left mouse fires, R reloads (2 s, no shooting meanwhile), empty magazine = dry-fire click.
// Gunfire is loud: guards in gunshotNoiseRadius hear it.
// Every shot shows: a tracer from the muzzle, a muzzle flash, sparks where it lands (a red puff
// on a person), a small camera kick, and a hit marker on the crosshair when it hits someone.
// The crosshair itself (Crosshair) is created here, so nothing has to be set up in the scene.
public class PlayerShooting : MonoBehaviour
{
    [Header("Refs")]
    public Transform muzzle;                 // optional; the right hand (or chest) is used if empty
    public Camera cam;
    public ParticleSystem muzzleFlash;       // optional extra effect; a built-in flash is always shown
    public GameObject hitEffect;             // optional extra prefab; built-in sparks are always shown

    [Header("Weapon")]
    public float damage = 25f;
    public float range = 100f;
    public float fireRate = 3f;
    public bool automatic = true;
    public LayerMask hitMask = ~0;

    [Header("Feel")]
    public Color tracerColor = new Color(1f, 0.9f, 0.55f, 0.9f);
    public float recoilPitch = 1.2f;         // degrees the camera kicks up per shot
    public float recoilYaw = 0.35f;          // random left/right kick per shot
    public bool showCrosshair = true;

    [Header("Ammo")]
    public int magazineSize = 30;
    public int startSpare = 60;
    public int maxSpare = 240;
    public float reloadTime = 2f;
    public KeyCode reloadKey = KeyCode.R;
    public float gunshotNoiseRadius = 25f;

    [Header("Sounds (optional)")]
    public AudioClip fireSound;
    public AudioClip dryFireSound;
    public AudioClip reloadSound;
    [Range(0f, 1f)] public float volume = 1f;

    public int Magazine { get; private set; }
    public int Spare { get; private set; }
    public bool IsReloading { get; private set; }
    public float ReloadProgress => IsReloading ? 1f - reloadTimer / Mathf.Max(reloadTime, 0.01f) : 0f;

    // (hit point, what was hit, killed). Fired when a shot hits something that can take damage.
    public static event System.Action<Vector3, IDamageable, bool> TargetHit;

    float nextFireTime;
    float reloadTimer;
    Animator anim;
    AudioSource audioSource;
    PlayerMedkits medkits;
    Interactor interactor;
    Rigidbody body;
    PlayerMovement movement;
    ThirdPersonCamera tpCamera;
    Transform rightHand;
    Crosshair crosshair;
    float fireSpread;    // crosshair spread from recent fire, fades

    void Awake()
    {
        if (cam == null) cam = Camera.main;
        anim = GetComponentInChildren<Animator>();
        Magazine = magazineSize;
        Spare = startSpare;
    }

    void Start()
    {
        medkits = GetComponent<PlayerMedkits>();
        interactor = GetComponent<Interactor>();
        body = GetComponent<Rigidbody>();
        movement = GetComponent<PlayerMovement>();
        tpCamera = FindFirstObjectByType<ThirdPersonCamera>();

        if (anim != null && anim.isHuman)
            rightHand = anim.GetBoneTransform(HumanBodyBones.RightHand);

        if (showCrosshair) crosshair = Crosshair.Ensure();
    }

    void OnDisable()
    {
        if (crosshair != null) crosshair.SetVisible(false);
    }

    void OnEnable()
    {
        if (crosshair != null) crosshair.SetVisible(true);
    }

    // Busy with something else: healing, defusing, untying, or the mission is over
    bool Busy()
    {
        if (medkits != null && medkits.IsHealing) return true;
        if (interactor != null && interactor.IsHolding) return true;
        MissionManager m = MissionManager.Instance;
        return m != null && m.IsOver;
    }

    void Update()
    {
        if (medkits == null) medkits = GetComponent<PlayerMedkits>();
        if (interactor == null) interactor = GetComponent<Interactor>();
        if (body == null) body = GetComponent<Rigidbody>();

        UpdateCrosshair();

        if (IsReloading)
        {
            reloadTimer -= Time.deltaTime;
            if (reloadTimer <= 0f) FinishReload();
            return;
        }

        if (Busy()) return;

        if (Input.GetKeyDown(reloadKey))
        {
            StartReload();
            return;
        }

        // With the mouse freed (Escape), the first click only locks the cursor again
        if (tpCamera != null && Cursor.visible) return;

        bool wantsFire = automatic ? Input.GetMouseButton(0) : Input.GetMouseButtonDown(0);
        if (wantsFire && Time.time >= nextFireTime)
        {
            nextFireTime = Time.time + 1f / Mathf.Max(fireRate, 0.01f);
            if (Magazine > 0) Shoot();
            else
            {
                PlaySound(dryFireSound);
                nextFireTime = Time.time + 0.35f; // clicks don't machine-gun
            }
        }
    }

    void UpdateCrosshair()
    {
        if (crosshair == null) return;
        fireSpread = Mathf.MoveTowards(fireSpread, 0f, Time.deltaTime * 2.5f);

        float moveSpread = 0f;
        if (body != null)
        {
            Vector3 v = body.linearVelocity;
            v.y = 0f;
            float top = movement != null ? Mathf.Max(movement.runSpeed, 0.1f) : 5f;
            moveSpread = Mathf.Clamp01(v.magnitude / top);
        }
        crosshair.SetSpread(Mathf.Max(moveSpread * 0.8f, fireSpread));
        crosshair.SetVisible(!IsBusyForCrosshair());
    }

    bool IsBusyForCrosshair()
    {
        if (TryGetComponent(out PlayerHealth hp) && hp.IsDead) return true;
        return medkits != null && medkits.IsHealing;
    }

    // Adds spare ammo. Returns how much was actually taken.
    public int AddAmmo(int amount)
    {
        int taken = Mathf.Clamp(maxSpare - Spare, 0, amount);
        Spare += taken;
        return taken;
    }

    public void StartReload()
    {
        if (IsReloading || Magazine >= magazineSize || Spare <= 0) return;
        IsReloading = true;
        reloadTimer = reloadTime;
        PlaySound(reloadSound);
        AnimatorParams.TrySetTrigger(anim, "Reload");
    }

    void FinishReload()
    {
        IsReloading = false;
        int moved = Mathf.Min(magazineSize - Magazine, Spare);
        Magazine += moved;
        Spare -= moved;
    }

    void Shoot()
    {
        if (cam == null) cam = Camera.main;
        if (cam == null) return;

        Magazine--;
        AnimatorParams.TrySetTrigger(anim, "Shoot");
        PlaySound(fireSound);
        GuardAlert.MakeNoise(transform.position, gunshotNoiseRadius);

        Ray ray = cam.ViewportPointToRay(new Vector3(0.5f, 0.5f, 0f));
        Vector3 target = ray.origin + ray.direction * range;

        bool hitSomething = FirstHit(ray, out RaycastHit hit);
        if (hitSomething)
        {
            target = hit.point;
            IDamageable dmg = hit.collider.GetComponentInParent<IDamageable>();
            bool flesh = dmg != null;
            if (dmg != null)
            {
                bool wasDead = dmg is TerroristGuard g0 && g0.IsDead;
                dmg.TakeDamage(damage);
                bool killed = !wasDead && dmg is TerroristGuard g1 && g1.IsDead;
                if (crosshair != null && !wasDead) crosshair.Hit(killed);
                TargetHit?.Invoke(hit.point, dmg, killed);
            }

            ShotEffects.Impact(hit.point, hit.normal, flesh);
            if (hitEffect != null)
                Destroy(Instantiate(hitEffect, hit.point, Quaternion.LookRotation(hit.normal)), 2f);
        }

        // Muzzle: assigned marker, else just in front of the right hand, else chest height
        Vector3 muzzlePos = MuzzlePosition();
        Vector3 shotDir = (target - muzzlePos).normalized;
        ShotEffects.Tracer(muzzlePos, target, tracerColor);
        ShotEffects.MuzzleFlash(muzzlePos, shotDir);
        if (muzzleFlash != null) muzzleFlash.Play();

        if (tpCamera != null) tpCamera.AddRecoil(recoilPitch, Random.Range(-recoilYaw, recoilYaw));
        if (crosshair != null) crosshair.Kick();
        fireSpread = Mathf.Clamp01(fireSpread + 0.35f);
    }

    Vector3 MuzzlePosition()
    {
        if (muzzle != null) return muzzle.position;
        float s = transform.lossyScale.y;
        if (rightHand != null) return rightHand.position + transform.forward * 0.45f * s + Vector3.up * 0.05f * s;
        return transform.position + Vector3.up * 1.3f * s + transform.forward * 0.6f * s + transform.right * 0.2f * s;
    }

    // The camera sits behind the player, so skip the player's own colliders
    readonly RaycastHit[] hits = new RaycastHit[16];
    bool FirstHit(Ray ray, out RaycastHit result)
    {
        result = default;
        int count = Physics.RaycastNonAlloc(ray, hits, range, hitMask, QueryTriggerInteraction.Ignore);
        float best = float.MaxValue;
        bool found = false;
        for (int i = 0; i < count; i++)
        {
            if (hits[i].collider.transform.IsChildOf(transform)) continue;
            if (hits[i].distance < best)
            {
                best = hits[i].distance;
                result = hits[i];
                found = true;
            }
        }
        return found;
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
        audioSource.PlayOneShot(clip, volume);
    }
}

public interface IDamageable
{
    void TakeDamage(float amount);
}
