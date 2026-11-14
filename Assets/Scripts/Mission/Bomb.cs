using UnityEngine;

// The time bomb. At start it moves to a random BombSpawnPoint (if any exist).
// Hold E for defuseTime seconds to defuse; releasing E, moving away or taking a hit resets.
// While being defused it makes noise every second. Defusing stops the mission timer and
// starts the lockdown (through MissionManager). The emission blinks faster as time runs out.
public class Bomb : MonoBehaviour, IInteractable
{
    public static Bomb Instance { get; private set; }

    [Header("Defusing")]
    public float defuseTime = 5f;
    public float noiseRadius = 10f;
    public float noiseInterval = 1f;

    [Header("Spawn")]
    public bool randomSpawn = true;          // move to a random BombSpawnPoint at start

    [Header("Blinking emission")]
    public Color emissionColor = new Color(1f, 0.1f, 0.05f);  // used if the material has no emission colour
    public float emissionIntensity = 2.5f;
    public float slowBlinkInterval = 1.2f;   // seconds per blink at 10:00
    public float fastBlinkInterval = 0.12f;  // seconds per blink at 0:00
    [Range(0.05f, 0.9f)] public float blinkOnFraction = 0.35f;

    [Header("Sounds (optional)")]
    public AudioClip beepSound;              // plays on every blink
    [Range(0f, 1f)] public float beepVolume = 0.5f;
    public AudioClip defuseLoopSound;        // loops while E is held
    public AudioClip defusedSound;

    public bool Defused { get; private set; }

    Renderer[] renderers;
    MaterialPropertyBlock block;
    Color baseEmission;
    static readonly int EmissionId = Shader.PropertyToID("_EmissionColor");
    float blinkTimer;
    bool lit;
    float noiseTimer;
    AudioSource audioSource;
    AudioSource loopSource;

    void Awake()
    {
        Instance = this;
        renderers = GetComponentsInChildren<Renderer>();
        block = new MaterialPropertyBlock();

        baseEmission = emissionColor;
        foreach (Renderer r in renderers)
        {
            Material m = r.sharedMaterial;
            if (m != null && m.HasProperty(EmissionId))
            {
                Color c = m.GetColor(EmissionId);
                if (c.maxColorComponent > 0.01f) baseEmission = c;
                // Blinking needs the material's Emission checkbox on (TimeBomb.mat has it).
                // Not switched on from code, because that would change the material asset.
                if (!m.IsKeywordEnabled("_EMISSION"))
                    Debug.LogWarning($"[Bomb] Turn on Emission in material {m.name} so the bomb can blink.");
                break;
            }
        }

        audioSource = gameObject.AddComponent<AudioSource>();
        audioSource.playOnAwake = false;
        audioSource.spatialBlend = 1f;
        audioSource.minDistance = 2f;
        audioSource.maxDistance = 20f;

        if (randomSpawn) MoveToRandomSpawn();
    }

    void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }

    void MoveToRandomSpawn()
    {
        BombSpawnPoint[] points = FindObjectsByType<BombSpawnPoint>(FindObjectsSortMode.None);
        if (points.Length == 0) return;
        BombSpawnPoint p = points[Random.Range(0, points.Length)];
        transform.SetPositionAndRotation(p.transform.position, p.transform.rotation);
        Debug.Log($"[Bomb] spawned at {p.name}");
    }

    void Update()
    {
        MissionManager m = MissionManager.Instance;
        if (Defused || (m != null && m.IsOver))
        {
            if (lit) SetLit(false);
            return;
        }

        // 0 = full time left, 1 = about to explode
        float urgency = 0f;
        if (m != null && m.missionSeconds > 0f) urgency = 1f - Mathf.Clamp01(m.TimeLeft / m.missionSeconds);
        float interval = Mathf.Lerp(slowBlinkInterval, fastBlinkInterval, urgency * urgency);

        blinkTimer += Time.deltaTime;
        if (blinkTimer >= interval)
        {
            blinkTimer = 0f;
            if (beepSound != null) audioSource.PlayOneShot(beepSound, beepVolume);
        }
        bool shouldLight = blinkTimer < interval * blinkOnFraction;
        if (shouldLight != lit) SetLit(shouldLight);
    }

    void SetLit(bool on)
    {
        lit = on;
        Color c = on ? baseEmission * emissionIntensity : Color.black;
        foreach (Renderer r in renderers)
        {
            if (r == null) continue;
            r.GetPropertyBlock(block);
            block.SetColor(EmissionId, c);
            r.SetPropertyBlock(block);
        }
    }

    // ---- IInteractable ----

    public bool CanInteract(Interactor who)
    {
        MissionManager m = MissionManager.Instance;
        return !Defused && (m == null || !m.IsOver);
    }

    public string Prompt => "defuse the bomb";
    public float HoldTime => defuseTime;

    public void HoldTick(Interactor who, float progress)
    {
        if (loopSource == null && defuseLoopSound != null)
        {
            loopSource = gameObject.AddComponent<AudioSource>();
            loopSource.clip = defuseLoopSound;
            loopSource.loop = true;
            loopSource.spatialBlend = 1f;
        }
        if (loopSource != null && !loopSource.isPlaying) loopSource.Play();

        noiseTimer -= Time.deltaTime;
        if (noiseTimer <= 0f)
        {
            noiseTimer = noiseInterval;
            GuardAlert.MakeNoise(transform.position, noiseRadius);
        }
    }

    public void HoldCancelled(Interactor who)
    {
        noiseTimer = 0f;
        if (loopSource != null) loopSource.Stop();
    }

    public void Interact(Interactor who)
    {
        if (Defused) return;
        Defused = true;
        if (loopSource != null) loopSource.Stop();
        if (defusedSound != null) audioSource.PlayOneShot(defusedSound);
        SetLit(false);
        Debug.Log("[Bomb] defused");
        if (MissionManager.Instance != null) MissionManager.Instance.NotifyBombDefused();
    }
}
