using UnityEngine;

public enum PickupType { Ammo, Medkit }

// Walk-over pickup: ammo box / magazine (+spare ammo) or medkit (+1 carried).
// Needs no setup: if the object has no trigger collider, one is added.
// If the player is full, the pickup stays and a toast says so.
public class Pickup : MonoBehaviour
{
    public PickupType type = PickupType.Ammo;
    public int amount = 30;                  // rounds for ammo, medkits for medkit
    public string displayName = "";          // toast name; empty = "Ammo" / "Medkit"
    public float triggerRadius = 0.9f;
    public AudioClip pickupSound;            // optional
    public bool spinVisual = true;           // spins the child named "Visual"
    public float spinSpeed = 60f;

    Transform visual;
    float fullToastCooldown;

    void Awake()
    {
        visual = transform.Find("Visual");

        bool hasTrigger = false;
        foreach (Collider c in GetComponents<Collider>())
            if (c.isTrigger) hasTrigger = true;
        if (!hasTrigger)
        {
            SphereCollider s = gameObject.AddComponent<SphereCollider>();
            s.isTrigger = true;
            s.radius = triggerRadius / Mathf.Max(0.01f, transform.lossyScale.x);
        }
    }

    void Update()
    {
        if (fullToastCooldown > 0f) fullToastCooldown -= Time.deltaTime;
        if (spinVisual && visual != null) visual.Rotate(Vector3.up, spinSpeed * Time.deltaTime, Space.World);
    }

    void OnTriggerStay(Collider other)
    {
        GameObject player = PlayerFrom(other);
        if (player == null) return;
        if (player.TryGetComponent(out PlayerHealth hp) && hp.IsDead) return;

        string name = string.IsNullOrEmpty(displayName) ? (type == PickupType.Ammo ? "ammo" : "medkit") : displayName;
        int taken = 0;
        if (type == PickupType.Ammo)
        {
            PlayerShooting gun = player.GetComponent<PlayerShooting>();
            if (gun != null) taken = gun.AddAmmo(amount);
            if (taken > 0) Collected($"+{taken} {name}");
            else FullToast("Ammo full");
        }
        else
        {
            PlayerMedkits med = player.GetComponent<PlayerMedkits>();
            if (med == null) med = player.AddComponent<PlayerMedkits>();
            taken = med.Add(amount);
            if (taken > 0) Collected($"+{taken} {name} ({med.Count}/{med.maxCarried})  [H] to use");
            else FullToast($"Can't carry more medkits ({med.maxCarried})");
        }
    }

    static GameObject PlayerFrom(Collider other)
    {
        if (other.CompareTag("Player")) return other.gameObject;
        if (other.attachedRigidbody != null && other.attachedRigidbody.CompareTag("Player"))
            return other.attachedRigidbody.gameObject;
        return null;
    }

    void Collected(string message)
    {
        if (MissionManager.Instance != null) MissionManager.Instance.ShowToast(message);
        else if (MissionHUD.Instance != null) MissionHUD.Instance.AddToast(message);
        if (pickupSound != null) AudioSource.PlayClipAtPoint(pickupSound, transform.position);
        Destroy(gameObject);
    }

    void FullToast(string message)
    {
        if (fullToastCooldown > 0f) return;
        fullToastCooldown = 3f;
        if (MissionHUD.Instance != null) MissionHUD.Instance.AddToast(message);
    }

    // Creates a pickup at runtime: from a prefab if given, otherwise a small placeholder box.
    public static Pickup Spawn(GameObject prefab, PickupType type, int amount, Vector3 position, string displayName = "")
    {
        GameObject go;
        if (prefab != null)
        {
            go = Instantiate(prefab, position, Quaternion.Euler(0f, Random.Range(0f, 360f), 0f));
        }
        else
        {
            go = new GameObject(type == PickupType.Ammo ? "Magazine Pickup" : "Medkit Pickup");
            go.transform.SetPositionAndRotation(position, Quaternion.Euler(0f, Random.Range(0f, 360f), 0f));
            GameObject box = GameObject.CreatePrimitive(PrimitiveType.Cube);
            box.name = "Visual";
            Destroy(box.GetComponent<Collider>());
            box.transform.SetParent(go.transform, false);
            box.transform.localPosition = Vector3.up * 0.1f;
            box.transform.localScale = type == PickupType.Ammo ? new Vector3(0.08f, 0.2f, 0.04f) : new Vector3(0.3f, 0.2f, 0.12f);
            Renderer r = box.GetComponent<Renderer>();
            if (r != null) r.material.color = type == PickupType.Ammo ? new Color(0.25f, 0.22f, 0.12f) : new Color(0.8f, 0.1f, 0.1f);
        }

        Pickup p = go.GetComponent<Pickup>();
        if (p == null) p = go.AddComponent<Pickup>();
        p.type = type;
        p.amount = amount;
        if (!string.IsNullOrEmpty(displayName)) p.displayName = displayName;
        return p;
    }
}
