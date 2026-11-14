using System.Text.RegularExpressions;
using UnityEngine;

// A door that swings open around its hinge edge.
// Doors inside the floor model are found by name ("Door", "Door.001", ...) and set up
// automatically when the scene starts, so nothing has to be added to the scene by hand.
// Door sounds are loaded from Assets/Resources/Sounds/door_open and door_close, so every
// door (including ones added at runtime) plays them without any Inspector setup.
public class Door : MonoBehaviour, IInteractable
{
    public float openAngle = 90f;
    public float swingSpeed = 180f; // degrees per second

    [Range(0f, 1f)] public float soundVolume = 1f;

    public bool IsOpen { get; private set; }

    Transform hinge;
    float currentAngle;
    float targetAngle;
    AudioSource audioSource;

    static AudioClip openClip;
    static AudioClip closeClip;
    static bool soundsLoaded;

    static readonly Regex DoorName = new Regex(@"^Door(\.\d+)?$");
    static readonly Regex HandleName = new Regex(@"^Handle(\.\d+)?$");

    public static int SetupAll()
    {
        int count = 0;
        foreach (MeshRenderer r in FindObjectsByType<MeshRenderer>(FindObjectsSortMode.None))
        {
            if (!DoorName.IsMatch(r.name) || r.GetComponent<Door>() != null) continue;
            if (r.GetComponent<MeshFilter>() == null || r.GetComponent<MeshFilter>().sharedMesh == null) continue;
            r.gameObject.AddComponent<Door>();
            count++;
        }
        return count;
    }

    void Start()
    {
        LoadSounds();

        audioSource = GetComponent<AudioSource>();
        if (audioSource == null) audioSource = gameObject.AddComponent<AudioSource>();
        audioSource.playOnAwake = false;
        audioSource.spatialBlend = 1f; // the sound comes from the door's position
        audioSource.minDistance = 2f;
        audioSource.maxDistance = 25f;

        Mesh mesh = GetComponent<MeshFilter>().sharedMesh;
        Bounds b = mesh.bounds; // local space

        // Which local axis points up, and which one is the door's width
        Vector3[] axes = { Vector3.right, Vector3.up, Vector3.forward };
        int upAxis = 0;
        float best = -1f;
        for (int i = 0; i < 3; i++)
        {
            float d = Mathf.Abs(Vector3.Dot(transform.TransformDirection(axes[i]).normalized, Vector3.up));
            if (d > best) { best = d; upAxis = i; }
        }
        int widthAxis = -1;
        float widest = -1f;
        for (int i = 0; i < 3; i++)
        {
            if (i == upAxis) continue;
            float w = transform.TransformVector(axes[i] * b.size[i]).magnitude;
            if (w > widest) { widest = w; widthAxis = i; }
        }

        Vector3 edgeMin = b.center; edgeMin[widthAxis] = b.min[widthAxis];
        Vector3 edgeMax = b.center; edgeMax[widthAxis] = b.max[widthAxis];
        Vector3 worldMin = transform.TransformPoint(edgeMin);
        Vector3 worldMax = transform.TransformPoint(edgeMax);

        Vector3 hingePoint;
        float size = Mathf.Max(b.size[widthAxis], 0.0001f);
        if (Mathf.Abs(b.min[widthAxis]) < size * 0.1f) hingePoint = worldMin;      // pivot sits on an edge:
        else if (Mathf.Abs(b.max[widthAxis]) < size * 0.1f) hingePoint = worldMax; // that edge is the hinge
        else
        {
            // Hinge is the edge farther from the door handle
            Transform handle = FindNearbyHandle(widest);
            hingePoint = handle != null &&
                         Vector3.Distance(handle.position, worldMin) < Vector3.Distance(handle.position, worldMax)
                ? worldMax : worldMin;
            if (handle != null && handle.parent != transform) handle.SetParent(transform, true);
        }

        hinge = new GameObject(name + "_Hinge").transform;
        hinge.SetPositionAndRotation(new Vector3(hingePoint.x, transform.position.y, hingePoint.z), Quaternion.identity);
        hinge.SetParent(transform.parent, true);
        transform.SetParent(hinge, true);
    }

    static void LoadSounds()
    {
        if (soundsLoaded) return;
        soundsLoaded = true;

        openClip = Resources.Load<AudioClip>("Sounds/door_open");
        closeClip = Resources.Load<AudioClip>("Sounds/door_close");

        if (closeClip == null) closeClip = openClip; // one sound is enough for both
        if (openClip == null)
            Debug.LogWarning("[Door] No door_open clip found. Put it in Assets/Resources/Sounds/door_open.");
    }

    Transform FindNearbyHandle(float doorWidth)
    {
        Vector3 center = GetComponent<Renderer>().bounds.center;
        Transform closest = null;
        float closestDist = doorWidth;
        foreach (MeshRenderer r in FindObjectsByType<MeshRenderer>(FindObjectsSortMode.None))
        {
            if (!HandleName.IsMatch(r.name)) continue;
            float d = Vector3.Distance(r.bounds.center, center);
            if (d < closestDist) { closestDist = d; closest = r.transform; }
        }
        return closest;
    }

    // ---- IInteractable (E, used through Interactor) ----
    public bool CanInteract(Interactor who) { return hinge != null; }
    public string Prompt => IsOpen ? "close door" : "open door";
    public float HoldTime => 0f;
    public void Interact(Interactor who) { Toggle(who.transform.position); }
    public void HoldTick(Interactor who, float progress) { }
    public void HoldCancelled(Interactor who) { }

    // Open away from whoever is using the door, or close it again
    public void Toggle(Vector3 userPosition)
    {
        if (hinge == null) return;

        // Doors are loud: guards nearby hear them
        GuardAlert.MakeNoise(transform.position, 15f);

        if (IsOpen)
        {
            targetAngle = 0f;
            IsOpen = false;
            PlaySound(closeClip);
            return;
        }

        Vector3 center = GetComponent<Renderer>().bounds.center - hinge.position;
        center.y = 0f;
        Vector3 toUser = userPosition - hinge.position;
        toUser.y = 0f;
        Vector3 closedOffset = Quaternion.Inverse(Quaternion.AngleAxis(currentAngle, Vector3.up)) * center;
        Vector3 swungPlus = Quaternion.AngleAxis(openAngle, Vector3.up) * closedOffset;
        targetAngle = Vector3.Dot(swungPlus, toUser) < 0f ? openAngle : -openAngle;
        IsOpen = true;
        PlaySound(openClip);
    }

    void PlaySound(AudioClip clip)
    {
        if (clip != null && audioSource != null)
            audioSource.PlayOneShot(clip, soundVolume);
    }

    void Update()
    {
        if (hinge == null || Mathf.Approximately(currentAngle, targetAngle)) return;
        currentAngle = Mathf.MoveTowards(currentAngle, targetAngle, swingSpeed * Time.deltaTime);
        hinge.localRotation = Quaternion.Inverse(hinge.parent != null ? hinge.parent.rotation : Quaternion.identity)
                              * Quaternion.AngleAxis(currentAngle, Vector3.up);
    }
}
