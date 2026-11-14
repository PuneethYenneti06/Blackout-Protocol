using UnityEngine;
using UnityEngine.Rendering;

// The rifle you see in your hands in first person, held on the right side of the view.
// Added to the player camera automatically. Uses the AK-47 model (Resources/FirstPerson/ViewRifle).
//  - Bobs while you walk and more while you sprint
//  - Sways a little behind the mouse
//  - Kicks back on each shot
//  - Pulls back and dips when you stand close to a wall, so it never pokes through it
public class FirstPersonViewModel : MonoBehaviour
{
    [Header("Placement (metres, relative to your view)")]
    public Vector3 holdOffset = new Vector3(0.2f, -0.2f, 0.42f); // right, down, forward: where the middle of the rifle sits
    public Vector3 holdRotation = new Vector3(0f, -3f, 0f);      // slight turn inward toward the crosshair
    public float rifleScale = 1f;

    [Header("Feel")]
    public float bobAmount = 0.007f;
    public float sprintBobAmount = 0.016f;
    public float swayAmount = 0.02f;
    public float kickBack = 0.06f;
    public float kickUp = 4f;          // degrees

    // World-space point at the rifle's barrel tip. PlayerShooting fires from here in first person.
    public static Vector3? MuzzleWorld { get; private set; }

    Transform rifle;
    Vector3 centerLocal;      // middle of the rifle in its own space
    Vector3 muzzleLocal;      // barrel tip in its own space
    float rifleLength;
    Transform player;
    Vector3 lastPlayerPos;
    float bobTime;
    float bobStrengthSmooth;
    float speedSmooth;
    Vector2 sway;
    float kick;
    float wallPull;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics()
    {
        MuzzleWorld = null;
    }

    void Start()
    {
        GameObject prefab = Resources.Load<GameObject>("FirstPerson/ViewRifle");
        if (prefab == null)
        {
            Debug.LogWarning("[FirstPersonViewModel] Resources/FirstPerson/ViewRifle not found, no rifle in view.");
            enabled = false;
            return;
        }

        GameObject go = Instantiate(prefab, transform);
        go.name = "FirstPersonRifle";
        rifle = go.transform;
        rifle.localScale = Vector3.one * rifleScale;

        foreach (Collider c in go.GetComponentsInChildren<Collider>(true)) Destroy(c);
        foreach (Renderer r in go.GetComponentsInChildren<Renderer>(true))
        {
            r.shadowCastingMode = ShadowCastingMode.Off;
            r.receiveShadows = false;
        }

        // Find the rifle's middle and barrel tip from its mesh bounds. The barrel points along +Z.
        Bounds b = new Bounds();
        bool hasBounds = false;
        foreach (MeshFilter mf in go.GetComponentsInChildren<MeshFilter>(true))
        {
            if (mf.sharedMesh == null) continue;
            Bounds mb = mf.sharedMesh.bounds;
            // corners of this mesh, moved into the rifle's own space
            for (int i = 0; i < 8; i++)
            {
                Vector3 corner = new Vector3(
                    (i & 1) == 0 ? mb.min.x : mb.max.x,
                    (i & 2) == 0 ? mb.min.y : mb.max.y,
                    (i & 4) == 0 ? mb.min.z : mb.max.z);
                Vector3 p = rifle.InverseTransformPoint(mf.transform.TransformPoint(corner));
                if (!hasBounds) { b = new Bounds(p, Vector3.zero); hasBounds = true; }
                else b.Encapsulate(p);
            }
        }
        if (!hasBounds) b = new Bounds(Vector3.zero, new Vector3(0.05f, 0.2f, 0.85f));

        centerLocal = b.center;
        muzzleLocal = new Vector3(b.center.x, b.center.y + b.extents.y * 0.35f, b.max.z);
        rifleLength = b.size.z * rifleScale;

        ThirdPersonCamera camScript = GetComponent<ThirdPersonCamera>();
        if (camScript != null && camScript.target != null)
        {
            PlayerHealth ph = camScript.target.GetComponentInParent<PlayerHealth>();
            player = ph != null ? ph.transform : camScript.target;
            lastPlayerPos = player.position;
        }
    }

    // Called by the camera when a shot is fired
    public void Kick()
    {
        kick = 1f;
    }

    void LateUpdate()
    {
        if (rifle == null) return;

        bool show = ThirdPersonCamera.FirstPersonActive;
        if (rifle.gameObject.activeSelf != show) rifle.gameObject.SetActive(show);
        if (!show)
        {
            MuzzleWorld = null;
            return;
        }

        float dt = Time.deltaTime;

        // Walking bob, from how fast the player is actually moving
        float speed = 0f;
        if (player != null && dt > 0f)
        {
            Vector3 moved = player.position - lastPlayerPos; moved.y = 0f;
            speed = moved.magnitude / dt;
            lastPlayerPos = player.position;
        }
        speedSmooth = Mathf.Lerp(speedSmooth, speed, 1f - Mathf.Exp(-8f * dt));
        float wantedBob = speedSmooth > 0.5f
            ? Mathf.Lerp(bobAmount, sprintBobAmount, Mathf.InverseLerp(3f, 5f, speedSmooth))
            : 0f;
        bobStrengthSmooth = Mathf.Lerp(bobStrengthSmooth, wantedBob, 1f - Mathf.Exp(-6f * dt));
        bobTime += dt * speedSmooth * 2.1f;  // in step with the camera bob
        Vector3 bob = new Vector3(Mathf.Sin(bobTime) * bobStrengthSmooth,
                                  (Mathf.Cos(bobTime * 2f) - 1f) * 0.5f * bobStrengthSmooth, 0f);

        // Sway: the rifle lags slightly behind the mouse
        Vector2 mouse = new Vector2(Input.GetAxis("Mouse X"), Input.GetAxis("Mouse Y"));
        sway = Vector2.Lerp(sway, -mouse * swayAmount, 1f - Mathf.Exp(-8f * dt));
        sway = Vector2.ClampMagnitude(sway, swayAmount * 2f);

        // Recoil kick fades out quickly
        kick = Mathf.MoveTowards(kick, 0f, dt * 6f);

        // Close to a wall: pull the rifle back and dip it, so it never goes through
        float reach = holdOffset.z + rifleLength * 0.5f;
        float wantPull = 0f;
        if (Physics.Raycast(transform.position, transform.forward, out RaycastHit hit, reach + 0.05f,
                            ~0, QueryTriggerInteraction.Ignore)
            && (player == null || !hit.collider.transform.IsChildOf(player)))
        {
            wantPull = Mathf.Clamp01((reach + 0.05f - hit.distance) / Mathf.Max(reach, 0.01f));
        }
        wallPull = Mathf.Lerp(wallPull, wantPull, 1f - Mathf.Exp(-12f * dt));

        Vector3 center = holdOffset + bob + new Vector3(sway.x, sway.y, 0f)
                       + new Vector3(0f, -0.08f * wallPull, -(kickBack * kick) - rifleLength * 0.45f * wallPull);
        Quaternion rot = Quaternion.Euler(holdRotation)
                       * Quaternion.Euler(-kickUp * kick + 35f * wallPull, 0f, 0f);

        rifle.localRotation = rot;
        rifle.localPosition = center - rot * (centerLocal * rifleScale);

        MuzzleWorld = rifle.TransformPoint(muzzleLocal);
    }
}
