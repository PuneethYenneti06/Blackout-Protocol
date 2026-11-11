using UnityEngine;

// Footsteps for the player.
// Walking is quiet and silent to guards. Sprinting is louder and alerts guards nearby.
// Attach to Soldier_demo (the player). Works without changing PlayerMovement.
public class FootstepSounds : MonoBehaviour
{
    [Header("Clips")]
    public AudioClip[] walkSteps;     // drag your footstep clips here
    public AudioClip[] sprintSteps;   // optional. If empty, the walk clips are used

    [Header("Volume and timing")]
    [Range(0f, 1f)] public float walkVolume = 0.4f;
    [Range(0f, 1f)] public float sprintVolume = 1f;
    public float walkInterval = 0.55f;   // seconds between steps while walking
    public float sprintInterval = 0.32f; // seconds between steps while sprinting
    public float minSpeed = 0.5f;        // slower than this (metres per second) counts as standing still

    [Header("Guards")]
    public float sprintNoiseRadius = 20f; // how far sprinting carries to guards, metres

    AudioSource source;
    Vector3 lastPosition;
    float stepTimer;

    void Start()
    {
        source = GetComponent<AudioSource>();
        if (source == null) source = gameObject.AddComponent<AudioSource>();

        source.playOnAwake = false;
        source.spatialBlend = 0f; // your own footsteps play flat, not from a point in the room

        lastPosition = transform.position;
    }

    void Update()
    {
        float dt = Time.deltaTime;

        // Measure speed from how far the player actually moved, so it works with any movement script
        Vector3 moved = transform.position - lastPosition;
        lastPosition = transform.position;
        moved.y = 0f;

        float speed = dt > 0f ? moved.magnitude / dt : 0f;
        bool moving = speed > minSpeed;

        if (!moving)
        {
            stepTimer = 0f; // the next step plays right away when you start moving again
            return;
        }

        bool sprinting = Input.GetKey(KeyCode.LeftShift);

        stepTimer -= dt;
        if (stepTimer > 0f) return;

        if (sprinting)
        {
            bool hasSprintClips = sprintSteps != null && sprintSteps.Length > 0;
            PlayRandom(hasSprintClips ? sprintSteps : walkSteps, sprintVolume);

            // Sprinting is loud: nearby guards turn toward it
            GuardAlert.MakeNoise(transform.position, sprintNoiseRadius);

            stepTimer = sprintInterval;
        }
        else
        {
            PlayRandom(walkSteps, walkVolume);
            stepTimer = walkInterval;
        }
    }

    void PlayRandom(AudioClip[] clips, float volume)
    {
        if (clips == null || clips.Length == 0) return;

        AudioClip clip = clips[Random.Range(0, clips.Length)];
        if (clip != null) source.PlayOneShot(clip, volume);
    }
}
