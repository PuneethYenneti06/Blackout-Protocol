using UnityEngine;
using UnityEngine.AI;
using UnityEngine.SceneManagement;

// The end of the mission:
//  1. At the start, the bomb is moved to the floor of the main area, beeping.
//  2. When every hostage has been brought to safety (none still tied up or following you),
//     the terrorist leader arrives: he enters out of sight and heads for the main area.
//  3. He is bigger, tougher (12 hits), quicker to react and shoots faster. A health bar shows at the top.
//  4. When he dies, the bomb's 6-digit disarm code is revealed and stays on screen.
// The boss is a copy of one of the scene's guards, so no extra model is needed.
// Created automatically in any scene with a bomb.
public class BossFight : MonoBehaviour
{
    [Header("Boss")]
    public int bossHealth = 12;
    public float bossScale = 1.15f;
    public float bossReaction = 0.35f;
    public float bossFireInterval = 0.45f;
    [Range(0f, 1f)] public float bossMissChance = 0.3f;
    public Color bossTint = new Color(0.55f, 0.32f, 0.3f);

    public static BossFight Instance { get; private set; }

    GameObject template;          // inactive copy of a guard, turned into the boss when needed
    TerroristGuard boss;
    bool spawned;
    bool bossDead;
    Vector3 mainArea;
    bool hasMainArea;
    GUIStyle barStyle, codeStyle, codeLabel;
    Texture2D white;

    static int lastSetupFrame = -1;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Install()
    {
        SceneManager.sceneLoaded -= OnSceneLoaded;
        SceneManager.sceneLoaded += OnSceneLoaded; // also after a restart
        Setup();
    }

    static void OnSceneLoaded(Scene scene, LoadSceneMode mode) => Setup();

    static void Setup()
    {
        if (Time.frameCount == lastSetupFrame) return;
        lastSetupFrame = Time.frameCount;
        if (FindFirstObjectByType<Bomb>() == null) return;
        if (FindFirstObjectByType<BossFight>() != null) return;
        new GameObject("BossFight").AddComponent<BossFight>();
    }

    void Awake()
    {
        Instance = this;
        TerroristGuard.GuardKilled += OnGuardKilled;
        MakeTemplate(); // before the guards start up, so the copy is clean
    }

    void OnDestroy()
    {
        TerroristGuard.GuardKilled -= OnGuardKilled;
        if (Instance == this) Instance = null;
    }

    void Start()
    {
        hasMainArea = MainArea.TryGet(out mainArea);
        PlaceBomb();
    }

    // ---- The bomb on the floor of the main area ----

    void PlaceBomb()
    {
        Bomb bomb = Bomb.Instance != null ? Bomb.Instance : FindFirstObjectByType<Bomb>();
        if (bomb == null || !hasMainArea) return;

        bomb.transform.SetPositionAndRotation(mainArea, Quaternion.Euler(0f, Random.Range(0f, 360f), 0f));

        // Rest it on the floor: lift or lower it so the bottom of the model touches the ground
        Renderer[] rs = bomb.GetComponentsInChildren<Renderer>();
        if (rs.Length > 0)
        {
            Bounds b = rs[0].bounds;
            foreach (Renderer r in rs) b.Encapsulate(r.bounds);
            bomb.transform.position += Vector3.up * (mainArea.y - b.min.y + 0.01f);
        }
        Debug.Log($"[BossFight] bomb placed in the main area at {bomb.transform.position}");
    }

    // ---- Boss ----

    void MakeTemplate()
    {
        TerroristGuard source = null;
        foreach (TerroristGuard g in FindObjectsByType<TerroristGuard>(FindObjectsSortMode.None))
        {
            if (g.role == GuardRole.Static && g.GetComponent<JumpScare>() == null) { source = g; break; }
            if (source == null) source = g;
        }
        if (source == null)
        {
            Debug.LogWarning("[BossFight] No guard to base the boss on.");
            return;
        }

        // Copy him while switched off, so the copy doesn't start up until the boss arrives
        bool wasActive = source.gameObject.activeSelf;
        source.gameObject.SetActive(false);
        template = Instantiate(source.gameObject);
        source.gameObject.SetActive(wasActive);
        template.name = "Boss";
        template.SetActive(false);
    }

    void Update()
    {
        if (spawned || template == null) return;
        MissionManager m = MissionManager.Instance;
        if (m != null && m.IsOver) return;
        if (AllHostagesSafe()) SpawnBoss();
    }

    // Every hostage is either saved or dead (and at least one was saved)
    static bool AllHostagesSafe()
    {
        if (Hostage.All.Count == 0) return false;
        int saved = 0;
        foreach (Hostage h in Hostage.All)
        {
            if (h == null) continue;
            if (h.State == Hostage.HostageState.Captive || h.State == Hostage.HostageState.Following) return false;
            if (h.State == Hostage.HostageState.Saved) saved++;
        }
        return saved > 0;
    }

    void SpawnBoss()
    {
        spawned = true;
        Vector3 target = hasMainArea ? mainArea : template.transform.position;
        Vector3 entry = PickEntry(target);

        template.transform.SetPositionAndRotation(entry, Quaternion.LookRotation(Flat(target - entry).normalized + Vector3.forward * 0.0001f));
        template.transform.localScale *= bossScale;

        boss = template.GetComponent<TerroristGuard>();
        boss.role = GuardRole.Static;
        boss.wanderRadius = 0f;
        boss.maxHealth = bossHealth;
        boss.reactionDelay = bossReaction;
        boss.fireInterval = bossFireInterval;
        boss.missChance = bossMissChance;
        boss.viewRange = Mathf.Max(boss.viewRange, 25f);
        Tint(template, bossTint);

        template.SetActive(true);
        boss.HuntAt(target); // heads for the main area, fights whoever he finds there

        MissionManager m = MissionManager.Instance;
        if (m != null)
        {
            m.RaiseAlarm("The terrorist leader has arrived");
            m.ShowToast("The terrorist leader has arrived. He has the disarm code!");
        }
        Debug.Log($"[BossFight] boss spawned at {entry}, heading to {target}");
    }

    // An entry point 14-26 m from the main area, out of the player's sight, reachable on foot
    Vector3 PickEntry(Vector3 target)
    {
        GameObject player = GameObject.FindWithTag("Player");
        Vector3 playerEye = player != null ? player.transform.position + Vector3.up * 1.3f : target;
        Vector3 best = target;
        float bestScore = float.MinValue;
        NavMeshPath path = new NavMeshPath();
        for (int i = 0; i < 40; i++)
        {
            Vector2 r = Random.insideUnitCircle.normalized * Random.Range(14f, 26f);
            if (!NavMesh.SamplePosition(target + new Vector3(r.x, 0f, r.y), out NavMeshHit hit, 2f, NavMesh.AllAreas)) continue;
            if (!NavMesh.CalculatePath(hit.position, target, NavMesh.AllAreas, path) || path.status != NavMeshPathStatus.PathComplete) continue;

            bool hidden = Physics.Linecast(playerEye, hit.position + Vector3.up * 1.5f, ~0, QueryTriggerInteraction.Ignore);
            float score = (hidden ? 100f : 0f) + Flat(hit.position - playerEye).magnitude;
            if (score > bestScore) { bestScore = score; best = hit.position; }
        }
        return best;
    }

    static void Tint(GameObject go, Color tint)
    {
        foreach (Renderer r in go.GetComponentsInChildren<Renderer>(true))
        {
            if (r is LineRenderer || r is ParticleSystemRenderer) continue;
            foreach (Material mat in r.materials) // copies, so other guards keep their colours
            {
                if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", mat.GetColor("_BaseColor") * tint);
                else if (mat.HasProperty("_Color")) mat.SetColor("_Color", mat.GetColor("_Color") * tint);
            }
        }
    }

    void OnGuardKilled(TerroristGuard g)
    {
        if (boss == null || g != boss || bossDead) return;
        bossDead = true;
        BombKeypad.Reveal();
        MissionManager m = MissionManager.Instance;
        if (m != null) m.ShowToast($"Leader down! Disarm code: {BombKeypad.Code}. Get to the bomb!");
        Debug.Log("[BossFight] boss killed, code revealed");
    }

    // ---- HUD: boss health bar, and the code once you have it ----

    void OnGUI()
    {
        bool showBar = boss != null && spawned && !bossDead;
        if (!showBar && !BombKeypad.CodeKnown) return;
        if (BombKeypad.IsOpen) return;

        if (white == null)
        {
            white = new Texture2D(1, 1);
            white.SetPixel(0, 0, Color.white);
            white.Apply();
            barStyle = new GUIStyle(GUI.skin.label) { fontSize = 15, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleRight };
            codeLabel = new GUIStyle(GUI.skin.label) { fontSize = 14, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter };
            codeStyle = new GUIStyle(GUI.skin.label) { fontSize = 30, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter };
        }

        if (showBar)
        {
            float w = 260f;
            Rect back = new Rect(Screen.width - w - 24f, 44f, w, 16f);
            float frac = Mathf.Clamp01((float)boss.Health / Mathf.Max(1, boss.maxHealth));
            GUI.color = new Color(0f, 0f, 0f, 0.7f);
            GUI.DrawTexture(new Rect(back.x - 3f, back.y - 3f, back.width + 6f, back.height + 6f), white);
            GUI.color = new Color(0.25f, 0.05f, 0.05f, 1f);
            GUI.DrawTexture(back, white);
            GUI.color = new Color(0.85f, 0.12f, 0.1f, 1f);
            GUI.DrawTexture(new Rect(back.x, back.y, back.width * frac, back.height), white);
            GUI.color = Color.white;
            GUI.Label(new Rect(back.x, back.y - 26f, back.width, 24f), $"TERRORIST LEADER  {boss.Health}/{boss.maxHealth}", barStyle);
        }

        if (BombKeypad.CodeKnown)
        {
            Rect box = new Rect(Screen.width - 284f, 78f, 260f, 74f);
            GUI.color = new Color(0f, 0f, 0f, 0.65f);
            GUI.DrawTexture(box, white);
            GUI.color = new Color(1f, 0.82f, 0.3f);
            GUI.Label(new Rect(box.x, box.y + 4f, box.width, 20f), "DISARM CODE", codeLabel);
            GUI.color = Color.white;
            GUI.Label(new Rect(box.x, box.y + 24f, box.width, 44f), BombKeypad.Code, codeStyle);
        }
        GUI.color = Color.white;
    }

    static Vector3 Flat(Vector3 v)
    {
        v.y = 0f;
        return v;
    }
}
