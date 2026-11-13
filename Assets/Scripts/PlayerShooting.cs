using UnityEngine;

public class PlayerShooting : MonoBehaviour
{
    [Header("Refs")]
    public Transform muzzle;
    public Camera cam;
    public ParticleSystem muzzleFlash;
    public GameObject hitEffect;

    [Header("Weapon")]
    public float damage = 25f;
    public float range = 100f;
    public float fireRate = 3f;
    public bool automatic = true;
    public LayerMask hitMask = ~0;

    float nextFireTime;
    Animator anim;

    void Awake()
    {
        if (cam == null) cam = Camera.main;
        anim = GetComponent<Animator>();
    }

    void Update()
    {
        bool wantsFire = automatic ? Input.GetMouseButton(0) : Input.GetMouseButtonDown(0);
        if (wantsFire && Time.time >= nextFireTime)
        {
            nextFireTime = Time.time + 1f / fireRate;
            Shoot();
        }
    }

    void Shoot()
    {
        if (anim != null) anim.SetTrigger("Shoot");

        Ray ray = cam.ViewportPointToRay(new Vector3(0.5f, 0.5f, 0f));
        Vector3 target = ray.origin + ray.direction * range;

        if (Physics.Raycast(ray, out RaycastHit hit, range, hitMask, QueryTriggerInteraction.Ignore))
        {
            target = hit.point;
            var dmg = hit.collider.GetComponentInParent<IDamageable>();
            if (dmg != null) dmg.TakeDamage(damage);

            if (hitEffect != null)
                Destroy(Instantiate(hitEffect, hit.point, Quaternion.LookRotation(hit.normal)), 2f);
        }

        if (muzzleFlash != null) muzzleFlash.Play();
        Debug.DrawLine(muzzle != null ? muzzle.position : ray.origin, target, Color.red, 0.1f);
    }
}

public interface IDamageable
{
    void TakeDamage(float amount);
}