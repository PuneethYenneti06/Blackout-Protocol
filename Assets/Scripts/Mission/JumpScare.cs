using UnityEngine;
using UnityEngine.AI;

// One hostage guard per run hides just inside his room's doorway, out of sight of the corridor.
// He can't see you until you step in next to him; then he lunges at you with a scream:
// your view snaps toward him, the camera shakes and zooms in, the screen flashes,
// and a moment later he opens fire. Added by HostageRooms; nothing to set up in the scene.
public class JumpScare : MonoBehaviour
{
    public Door door;                      // his room's door: opening it sets him off (set by HostageRooms)
    public float triggerDistance = 2.8f;   // otherwise: how close you have to get, metres
    public float lungeDistance = 0.7f;     // how far he jumps at you
    public float lungeTime = 0.14f;
    public float extraReaction = 0.8f;     // seconds on top of his normal reaction before he shoots
    public float flashTime = 0.45f;

    TerroristGuard guard;
    NavMeshAgent agent;
    Transform player;
    AudioClip scream;
    bool triggered;
    bool doorWasOpen = true; // checked on the first frame, so a door that starts open doesn't count
    float lungeLeft;
    Vector3 lungeStep;     // metres per second during the lunge
    float flashLeft;
    Texture2D white;

    void Start()
    {
        guard = GetComponent<TerroristGuard>();
        if (door != null) doorWasOpen = door.IsOpen;
        agent = GetComponent<NavMeshAgent>();
        scream = Resources.Load<AudioClip>("Sounds/jumpscare");
        if (guard != null) guard.hidingForScare = true;
    }

    void Update()
    {
        if (guard == null || guard.IsDead) { Cancel(); return; }

        if (!triggered)
        {
            // Mission over: nothing left to scare
            MissionManager m = MissionManager.Instance;
            if (m != null && m.IsOver) { Cancel(); return; }

            if (player == null)
            {
                GameObject p = GameObject.FindWithTag("Player");
                if (p == null) return;
                player = p.transform;
            }
            PlayerHealth ph = player.GetComponent<PlayerHealth>();
            if (ph != null && ph.IsDead) return;

            Vector3 flat = player.position - transform.position;
            flat.y = 0f;

            // The door swings open while you're at it: he jumps out right away
            bool doorJustOpened = door != null && door.IsOpen && !doorWasOpen && flat.magnitude < 5f;
            if (door != null) doorWasOpen = door.IsOpen;

            // While his door is shut, only opening it sets him off (never through the closed door)
            bool doorShut = door != null && !door.IsOpen;
            if (doorJustOpened || (!doorShut && flat.magnitude < triggerDistance && ClearLine())) Trigger(flat);
            return;
        }

        // The lunge
        if (lungeLeft > 0f)
        {
            float dt = Mathf.Min(Time.deltaTime, lungeLeft);
            lungeLeft -= dt;
            Vector3 delta = lungeStep * dt;
            if (agent != null && agent.enabled && agent.isOnNavMesh) agent.Move(delta);
            else transform.position += delta;
        }

        flashLeft -= Time.deltaTime;
        if (lungeLeft <= 0f && flashLeft <= 0f) enabled = false; // done; the guard fights normally
    }

    void Trigger(Vector3 toPlayer)
    {
        triggered = true;
        guard.hidingForScare = false;
        guard.Startle(player.position, extraReaction);

        float dist = toPlayer.magnitude;
        float jump = Mathf.Clamp(dist - 0.9f, 0f, lungeDistance);
        lungeStep = toPlayer.normalized * (jump / lungeTime);
        lungeLeft = lungeTime;
        flashLeft = flashTime;

        ThirdPersonCamera cam = FindFirstObjectByType<ThirdPersonCamera>();
        if (cam != null)
            cam.Scare(transform.position + Vector3.up * 1.5f * transform.lossyScale.y);

        if (scream != null)
        {
            AudioSource src = gameObject.AddComponent<AudioSource>();
            src.playOnAwake = false;
            src.spatialBlend = 0f; // right in your ears
            src.PlayOneShot(scream, 1f);
        }
    }

    // True if nothing solid (a wall) is between his eyes and the player's chest
    bool ClearLine()
    {
        Vector3 eye = transform.position + Vector3.up * 1.5f * transform.lossyScale.y;
        Vector3 chest = player.position + Vector3.up * 1.0f * player.lossyScale.y;
        Vector3 dir = chest - eye;
        float dist = dir.magnitude;
        foreach (RaycastHit h in Physics.RaycastAll(eye, dir / dist, dist, ~0, QueryTriggerInteraction.Ignore))
        {
            if (h.collider.transform.IsChildOf(transform) || h.collider.transform.IsChildOf(player)) continue;
            if (h.collider.attachedRigidbody != null) continue; // other people, props
            Door d = h.collider.GetComponentInParent<Door>();
            if (d != null && d.IsOpen) continue; // an open door isn't a wall
            return false;
        }
        return true;
    }

    void Cancel()
    {
        if (guard != null) guard.hidingForScare = false;
        enabled = false;
    }

    void OnGUI()
    {
        if (!triggered || flashLeft <= 0f) return;
        if (white == null)
        {
            white = new Texture2D(1, 1);
            white.SetPixel(0, 0, Color.white);
            white.Apply();
        }
        float a = Mathf.Clamp01(flashLeft / flashTime);
        GUI.color = new Color(1f, 0.85f, 0.85f, 0.75f * a * a);
        GUI.DrawTexture(new Rect(0, 0, Screen.width, Screen.height), white);
        GUI.color = Color.white;
    }
}
