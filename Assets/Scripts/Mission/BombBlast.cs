using UnityEngine;

// The bomb going off when the timer runs out. Built in code, nothing to set up in the scene.
// A fireball grows from the bomb and sweeps the whole map in slow motion: flash, explosion sound,
// camera shake toward the bomb, everyone the blast reaches dies, the screen burns to white and then
// to black, and only then the "mission failed" screen appears (MissionHUD waits for BombBlast.Playing).
public class BombBlast : MonoBehaviour
{
    public static bool Playing { get; private set; }

    public static float blastSpeed = 60f;      // how fast the fireball grows, metres per second
    public static float slowMotion = 0.4f;     // game speed while the blast plays
    public static float volume = 1f;

    Vector3 origin;
    Transform fireball, core;
    Material fireMat, coreMat;
    Light flashLight;
    float t;
    float reachTime;       // when the fireball reaches the camera
    float total;
    bool playerKilled;
    TerroristGuard[] guards;
    Texture2D white;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics() => Playing = false;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Init()
    {
        MissionManager.MissionLost -= OnMissionLost;
        MissionManager.MissionLost += OnMissionLost;
    }

    static void OnMissionLost(string reason)
    {
        // Only when the timer ran out, not when the player was shot
        if (Playing || reason == null || !reason.ToLowerInvariant().Contains("explod")) return;
        Playing = true;
        new GameObject("Bomb Blast").AddComponent<BombBlast>();
    }

    void Awake()
    {
        Camera cam = Camera.main;
        PlayerHealth player = FindFirstObjectByType<PlayerHealth>();

        Bomb bomb = Bomb.Instance != null ? Bomb.Instance : FindFirstObjectByType<Bomb>();
        if (bomb != null)
        {
            origin = bomb.transform.position + Vector3.up * 0.3f;
            foreach (AudioSource a in bomb.GetComponentsInChildren<AudioSource>()) a.Stop();
            foreach (Renderer r in bomb.GetComponentsInChildren<Renderer>()) r.enabled = false;
        }
        else if (player != null) origin = player.transform.position + Vector3.up;

        Vector3 eye = cam != null ? cam.transform.position : origin;
        reachTime = Mathf.Max(0.15f, (Vector3.Distance(eye, origin) - 3f) / blastSpeed);
        total = Mathf.Max(reachTime + 1.7f, 2.6f);

        fireMat = MakeGlowMaterial();
        coreMat = MakeGlowMaterial();
        fireball = MakeSphere("Fireball", fireMat);
        core = MakeSphere("Fireball Core", coreMat);

        GameObject lightGo = new GameObject("Blast Light");
        lightGo.transform.SetParent(transform, false);
        lightGo.transform.position = origin + Vector3.up;
        flashLight = lightGo.AddComponent<Light>();
        flashLight.type = LightType.Point;
        flashLight.color = new Color(1f, 0.6f, 0.25f);
        flashLight.shadows = LightShadows.None;

        AudioClip boom = Resources.Load<AudioClip>("Sounds/bomb_explosion");
        if (boom != null)
        {
            AudioSource src = gameObject.AddComponent<AudioSource>();
            src.spatialBlend = 0f;
            src.playOnAwake = false;
            src.ignoreListenerPause = true;
            src.PlayOneShot(boom, volume);
        }

        // Whip the view toward the bomb and shake hard
        ThirdPersonCamera tp = FindFirstObjectByType<ThirdPersonCamera>();
        if (tp != null && tp.enabled) tp.Scare(origin);

        guards = FindObjectsByType<TerroristGuard>(FindObjectsSortMode.None);

        white = new Texture2D(1, 1) { hideFlags = HideFlags.HideAndDontSave };
        white.SetPixel(0, 0, Color.white);
        white.Apply();
    }

    Transform MakeSphere(string name, Material m)
    {
        GameObject go = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        go.name = name;
        Destroy(go.GetComponent<Collider>());
        go.transform.SetParent(transform, false);
        go.transform.position = origin;
        go.transform.localScale = Vector3.zero;
        MeshRenderer r = go.GetComponent<MeshRenderer>();
        if (m != null) r.sharedMaterial = m;
        r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        r.receiveShadows = false;
        return go.transform;
    }

    void Update()
    {
        // The mission ending froze time; the blast plays in slow motion instead
        Time.timeScale = slowMotion;
        t += Time.unscaledDeltaTime;

        float radius = 3f + blastSpeed * t;
        float life = Mathf.Clamp01(t / total);
        fireball.localScale = Vector3.one * radius * 2f;
        core.localScale = Vector3.one * radius * 1.5f;
        SetColor(fireMat, new Color(1f, 0.45f, 0.1f, Mathf.Lerp(0.55f, 0.2f, life)));
        SetColor(coreMat, new Color(1f, 0.9f, 0.6f, Mathf.Lerp(0.6f, 0.1f, life)));

        flashLight.range = radius + 25f;
        flashLight.intensity = Mathf.Lerp(60f, 8f, life);

        // Everyone the fireball reaches dies
        foreach (TerroristGuard g in guards)
            if (g != null && !g.IsDead && Vector3.Distance(g.transform.position, origin) <= radius)
                g.TakeDamage(9999f);

        if (!playerKilled && t >= reachTime)
        {
            playerKilled = true;
            PlayerHealth player = FindFirstObjectByType<PlayerHealth>();
            for (int i = 0; player != null && !player.IsDead && i < 50; i++) player.TakeHit();
        }

        if (t >= total) Finish();
    }

    void Finish()
    {
        Playing = false;
        MissionManager m = MissionManager.Instance;
        Time.timeScale = m == null || m.freezeOnEnd ? 0f : 1f; // the fail screen takes over
        Destroy(gameObject);
    }

    void OnDestroy()
    {
        Playing = false;
        if (fireMat != null) Destroy(fireMat);
        if (coreMat != null) Destroy(coreMat);
    }

    // Screen: a flash at the start, burning to white when the fireball arrives, then fading to black
    void OnGUI()
    {
        GUI.depth = -500;
        float flash = 0.85f * Mathf.Exp(-t / 0.22f);
        float burn = Mathf.Clamp01((t - reachTime) / 0.25f);
        float toBlack = Mathf.Clamp01((t - (total - 0.9f)) / 0.9f);

        Color c = Color.Lerp(new Color(1f, 0.93f, 0.8f), Color.black, toBlack);
        c.a = Mathf.Max(flash, burn);
        if (c.a <= 0.001f) return;

        Color old = GUI.color;
        GUI.color = c;
        GUI.DrawTexture(new Rect(0, 0, Screen.width, Screen.height), white);
        GUI.color = old;
    }

    static void SetColor(Material m, Color c)
    {
        if (m == null) return;
        if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", c);
        if (m.HasProperty("_Color")) m.SetColor("_Color", c);
    }

    // Glowing see-through material, visible from inside the sphere too
    static Material MakeGlowMaterial()
    {
        Shader shader = Shader.Find("Universal Render Pipeline/Particles/Unlit");
        if (shader == null) shader = Shader.Find("Universal Render Pipeline/Unlit");
        if (shader == null) shader = Shader.Find("Sprites/Default");
        if (shader == null) return null;

        Material m = new Material(shader);
        if (m.HasProperty("_Surface")) m.SetFloat("_Surface", 1f);
        if (m.HasProperty("_Blend")) m.SetFloat("_Blend", 2f);
        if (m.HasProperty("_SrcBlend")) m.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.SrcAlpha);
        if (m.HasProperty("_DstBlend")) m.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.One);
        if (m.HasProperty("_ZWrite")) m.SetFloat("_ZWrite", 0f);
        if (m.HasProperty("_Cull")) m.SetFloat("_Cull", (float)UnityEngine.Rendering.CullMode.Off);
        m.SetOverrideTag("RenderType", "Transparent");
        m.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
        m.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
        return m;
    }
}
