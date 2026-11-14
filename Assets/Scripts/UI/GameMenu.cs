using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

// Loading screen, start menu, pause menu and end screen, all drawn in code (nothing to set up in the scene).
// - Loading: the game is frozen behind a loading screen while the camera looks around once,
//   so every shader and texture is ready (no blue placeholder surfaces) before you can play.
// - Start menu (first launch, or after "Main menu"): Play, Instructions, Settings, Exit.
// - Esc during the game: pause menu with Resume, Instructions, Settings, Restart, Main menu, Exit.
// - Mission over: result, statistics and Play again / Main menu buttons.
public class GameMenu : MonoBehaviour
{
    enum State { Loading, Main, Instructions, Settings, Playing, Paused, End }

    public static bool IsBlocking => instance != null && instance.state != State.Playing;
    public static bool DrawsEndScreen => instance != null;

    static GameMenu instance;
    static bool startMenuShown;   // the start menu shows once; a restart goes straight back in

    State state = State.Loading;
    State backTo = State.Main;     // where Back goes from Instructions/Settings
    float loadingStarted;
    int warmStep;
    int calmFrames;
    bool compilingNow;
    float shownProgress;
    bool keypadWasOpen;

    // Player parts switched off while a menu is open
    readonly List<Behaviour> frozen = new List<Behaviour>();
    Vector3 savedCamPos; Quaternion savedCamRot; bool camSaved;

    // Statistics
    int hits, kills, hitsTaken;
    float playStarted = -1f, playTime;
    bool won;
    string endReason = "";
    readonly List<KeyValuePair<string, string>> stats = new List<KeyValuePair<string, string>>();
    string grade = "";

    // Settings (saved between sessions)
    float sensitivity;
    float volume;

    GUIStyle title, subtitle, heading, button, body, small, statLabel, statValue, gradeStyle, tipStyle;
    Texture2D white;
    Font font;
    float uiScale = -1f;

    static readonly Color Red = new Color(0.86f, 0.17f, 0.13f);
    static readonly Color Text = new Color(0.92f, 0.92f, 0.9f);
    static readonly Color Grey = new Color(0.62f, 0.62f, 0.6f);
    static readonly Color Green = new Color(0.35f, 0.9f, 0.45f);

    static readonly string[] Tips =
    {
        "Hold E next to a hostage to untie them. They follow you back to the start.",
        "Guards hear gunfire. Shooting brings company.",
        "Press H to use a medkit. It heals you fully, but takes 3 seconds.",
        "The terrorist leader carries the bomb code. He only shows up once every hostage is safe.",
        "A wrong bomb code costs 30 seconds.",
        "Not every closed door hides an empty room.",
        "Press V to switch between first and third person.",
    };

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics()
    {
        instance = null;
        startMenuShown = false;
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Init()
    {
        SceneManager.sceneLoaded -= OnSceneLoaded;
        SceneManager.sceneLoaded += OnSceneLoaded;
        Create();
    }

    static void OnSceneLoaded(Scene s, LoadSceneMode m) => Create();

    static void Create()
    {
        if (instance != null) return;
        if (FindFirstObjectByType<PlayerHealth>() == null) return; // not a game scene
        new GameObject("Game Menu").AddComponent<GameMenu>();
    }

    void Awake()
    {
        instance = this;
        sensitivity = PlayerPrefs.GetFloat("bp_sensitivity", 3f);
        volume = PlayerPrefs.GetFloat("bp_volume", 1f);
        AudioListener.volume = volume;
        loadingStarted = Time.unscaledTime;
        font = Resources.Load<Font>("Fonts/Oswald-Bold");
        Freeze();
    }

    void OnEnable()
    {
        PlayerShooting.TargetHit += OnTargetHit;
        PlayerHealth.Damaged += OnDamaged;
    }

    void OnDisable()
    {
        PlayerShooting.TargetHit -= OnTargetHit;
        PlayerHealth.Damaged -= OnDamaged;
    }

    void OnDestroy()
    {
        if (instance == this) instance = null;
    }

    void OnTargetHit(Vector3 point, IDamageable target, bool killed)
    {
        hits++;
        if (killed) kills++;
    }

    void OnDamaged(int health, int max) => hitsTaken++;

    void Update()
    {
        switch (state)
        {
            case State.Loading: UpdateLoading(); break;
            case State.Playing: UpdatePlaying(); break;
            case State.End: Freeze(); break;
            default:
                Freeze();
                if (Input.GetKeyDown(KeyCode.Escape))
                {
                    if (state == State.Instructions || state == State.Settings) state = backTo;
                    else if (state == State.Paused) Resume();
                }
                break;
        }
    }

    // ---------- Loading ----------

    const int WarmSteps = 24;

    void UpdateLoading()
    {
        Freeze();
        Camera cam = Camera.main;
        PlayerHealth player = FindFirstObjectByType<PlayerHealth>();

        // Look around once (all directions, up and down) so everything gets drawn and its shaders are made
        if (cam != null && player != null && warmStep < WarmSteps)
        {
            if (!camSaved) { savedCamPos = cam.transform.position; savedCamRot = cam.transform.rotation; camSaved = true; }
            float pitch = warmStep < 8 ? 0f : (warmStep < 16 ? -35f : 35f);
            cam.transform.SetPositionAndRotation(
                player.transform.position + Vector3.up * 1.6f * player.transform.lossyScale.y,
                Quaternion.Euler(pitch, (warmStep % 8) * 45f, 0f));
            warmStep++;
            return;
        }
        warmStep = WarmSteps;
        if (camSaved && cam != null) { cam.transform.SetPositionAndRotation(savedCamPos, savedCamRot); camSaved = false; }

        // In the editor shaders are compiled in the background: wait until it has been quiet for a moment
        compilingNow = false;
#if UNITY_EDITOR
        compilingNow = UnityEditor.ShaderUtil.anythingCompiling;
#endif
        calmFrames = compilingNow ? 0 : calmFrames + 1;

        float waited = Time.unscaledTime - loadingStarted;
        bool ready = calmFrames >= 15 && waited >= 2f && shownProgress > 0.98f;
        if (ready || waited > 30f)
        {
            if (startMenuShown) StartPlaying();
            else { startMenuShown = true; state = State.Main; }
        }
    }

    float LoadingTarget()
    {
        float warm = warmStep / (float)WarmSteps;
        float time = Mathf.Clamp01((Time.unscaledTime - loadingStarted) / 2f);
        float calm = Mathf.Clamp01(calmFrames / 15f);
        return Mathf.Clamp01(warm * 0.45f + time * 0.3f + calm * 0.25f);
    }

    string LoadingStatus()
    {
        if (warmStep < WarmSteps) return "SECURING THE BUILDING";
        if (compilingNow) return "PREPARING SURFACES";
        if (calmFrames < 15) return "FINAL CHECKS";
        return "READY";
    }

    // ---------- Playing / pause / end ----------

    void UpdatePlaying()
    {
        MissionManager m = MissionManager.Instance;
        bool over = m != null && m.IsOver;

        if (over && !BombBlast.Playing)
        {
            ShowEnd(m);
            return;
        }

        bool keypadOpen = BombKeypad.IsOpen;
        if (Input.GetKeyDown(KeyCode.Escape) && !keypadOpen && !keypadWasOpen && !over)
        {
            state = State.Paused;
            Freeze();
        }
        keypadWasOpen = keypadOpen;

#if UNITY_EDITOR
        // Test shortcut, editor only: F9 runs the timer out so the bomb blast can be seen
        if (Input.GetKeyDown(KeyCode.F9) && m != null && !over) m.AddTimePenalty(99999f);
#endif
    }

    void StartPlaying()
    {
        state = State.Playing;
        Unfreeze();
        Time.timeScale = 1f;
        Cursor.lockState = CursorLockMode.Locked;
        Cursor.visible = false;
        ApplySettings();
        if (playStarted < 0f) playStarted = Time.unscaledTime;
    }

    void Resume() => StartPlaying();

    // Stops time and switches off the player's controls and camera
    void Freeze()
    {
        Time.timeScale = 0f;
        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;
        PlayerHealth player = FindFirstObjectByType<PlayerHealth>();
        if (player != null)
        {
            FreezeOne(player.GetComponent<PlayerMovement>());
            FreezeOne(player.GetComponent<PlayerShooting>());
            FreezeOne(player.GetComponent<Interactor>());
            FreezeOne(player.GetComponent<PlayerMedkits>());
        }
        FreezeOne(FindFirstObjectByType<ThirdPersonCamera>());
    }

    void FreezeOne(Behaviour b)
    {
        if (b == null || !b.enabled) return;
        b.enabled = false;
        if (!frozen.Contains(b)) frozen.Add(b);
    }

    void Unfreeze()
    {
        foreach (Behaviour b in frozen) if (b != null) b.enabled = true;
        frozen.Clear();
    }

    void ApplySettings()
    {
        AudioListener.volume = volume;
        ThirdPersonCamera tp = FindFirstObjectByType<ThirdPersonCamera>();
        if (tp != null) tp.sensitivity = sensitivity;
        PlayerPrefs.SetFloat("bp_sensitivity", sensitivity);
        PlayerPrefs.SetFloat("bp_volume", volume);
    }

    void ShowEnd(MissionManager m)
    {
        state = State.End;
        won = m.State == MissionState.Won;
        endReason = m.EndReason;
        playTime = playStarted >= 0f ? Time.unscaledTime - playStarted : 0f;

        PlayerShooting gun = FindFirstObjectByType<PlayerShooting>();
        int shots = gun != null ? gun.ShotsFired : 0;
        float accuracy = shots > 0 ? 100f * Mathf.Min(hits, shots) / shots : 0f;
        float left = Mathf.Max(0f, m.TimeLeft);
        float used = Mathf.Max(0f, m.missionSeconds - left);

        stats.Clear();
        stats.Add(Stat("TIME TAKEN", Clock(used)));
        stats.Add(Stat("TIME LEFT ON THE BOMB", m.BombDefused ? Clock(left) : "-"));
        stats.Add(Stat("HOSTAGES SAVED", $"{m.HostagesSaved} / {m.totalHostages}"));
        if (m.HostagesDead > 0) stats.Add(Stat("HOSTAGES LOST", m.HostagesDead.ToString()));
        stats.Add(Stat("TERRORISTS NEUTRALIZED", $"{m.TerroristsKilled} / {m.totalTerrorists}"));
        stats.Add(Stat("SHOTS FIRED", shots.ToString()));
        stats.Add(Stat("ACCURACY", shots > 0 ? $"{accuracy:0}%" : "-"));
        stats.Add(Stat("HITS TAKEN", hitsTaken.ToString()));
        stats.Add(Stat("DETECTED", m.PlayerWasDetected ? "YES" : "NO"));

        // Grade: only for a win
        if (won)
        {
            int score = 0;
            score += m.HostagesSaved >= m.totalHostages ? 2 : 0;
            score += m.HostagesDead == 0 ? 1 : 0;
            score += left >= m.missionSeconds * 0.4f ? 1 : 0;
            score += hitsTaken <= 1 ? 1 : 0;
            score += accuracy >= 40f ? 1 : 0;
            score += !m.PlayerWasDetected ? 1 : 0;
            grade = score >= 6 ? "S" : score >= 5 ? "A" : score >= 3 ? "B" : "C";
        }
        else grade = "";

        Freeze();
    }

    static KeyValuePair<string, string> Stat(string k, string v) => new KeyValuePair<string, string>(k, v);
    static string Clock(float s) => $"{Mathf.FloorToInt(s / 60f):00}:{Mathf.FloorToInt(s % 60f):00}";

    void Reload(bool toMainMenu)
    {
        if (toMainMenu) startMenuShown = false;
        Time.timeScale = 1f;
        MissionManager m = MissionManager.Instance;
        if (m != null) { m.Restart(); return; }
        Scene s = SceneManager.GetActiveScene();
        if (s.buildIndex >= 0) SceneManager.LoadScene(s.buildIndex);
        else SceneManager.LoadScene(s.name);
    }

    static void Quit()
    {
#if UNITY_EDITOR
        UnityEditor.EditorApplication.isPlaying = false;
#else
        Application.Quit();
#endif
    }

    // ---------- Drawing ----------

    void OnGUI()
    {
        if (state == State.Playing) return;
        GUI.depth = -1000;
        Styles();

        float alpha = state == State.Loading || state == State.Main
                      || (state == State.Instructions || state == State.Settings) && backTo == State.Main ? 1f
                    : state == State.End ? 0.9f : 0.82f;
        Fill(new Rect(0, 0, Screen.width, Screen.height), new Color(0.03f, 0.03f, 0.035f, alpha));

        switch (state)
        {
            case State.Loading: DrawLoading(); break;
            case State.Main: DrawMain(); break;
            case State.Paused: DrawPause(); break;
            case State.Instructions: DrawInstructions(); break;
            case State.Settings: DrawSettings(); break;
            case State.End: DrawEnd(); break;
        }
    }

    void Fill(Rect r, Color c)
    {
        Color old = GUI.color;
        GUI.color = c;
        GUI.DrawTexture(r, white);
        GUI.color = old;
    }

    float S(float v) => v * uiScale;

    // Title block centred on y; returns the y under it
    float DrawTitle(float y)
    {
        GUI.Label(new Rect(0, y, Screen.width, S(110)), "BLACKOUT PROTOCOL", title);
        y += S(112);
        Fill(new Rect((Screen.width - S(120)) / 2, y, S(120), S(4)), Red);
        y += S(16);
        GUI.Label(new Rect(0, y, Screen.width, S(36)), "OPERATION: HOSTAGE RESCUE", subtitle);
        return y + S(40);
    }

    void DrawLoading()
    {
        DrawTitle(Screen.height * 0.30f);

        // Progress glides toward the real value so the bar never jumps
        shownProgress = Mathf.MoveTowards(shownProgress, LoadingTarget(), Time.unscaledDeltaTime * 0.8f);

        float w = Mathf.Min(S(760), Screen.width - 64);
        float x = (Screen.width - w) / 2;
        float y = Screen.height * 0.78f;

        GUI.Label(new Rect(x, y - S(40), w, S(34)), LoadingStatus(), small);
        GUIStyle right = new GUIStyle(small) { alignment = TextAnchor.MiddleRight };
        right.normal.textColor = Text;
        GUI.Label(new Rect(x, y - S(40), w, S(34)), $"{Mathf.RoundToInt(shownProgress * 100)}%", right);

        Fill(new Rect(x, y, w, S(6)), new Color(1f, 1f, 1f, 0.12f));
        Fill(new Rect(x, y, w * shownProgress, S(6)), Red);

        int tip = Mathf.FloorToInt((Time.unscaledTime - loadingStarted) / 4f) % Tips.Length;
        GUI.Label(new Rect(x, y + S(26), w, S(60)), "TIP:  " + Tips[tip], tipStyle);
    }

    bool Button(float y, string text, float width = 360f)
    {
        float w = S(width), h = S(58);
        return GUI.Button(new Rect((Screen.width - w) / 2, y, w, h), text, button);
    }

    bool ButtonAt(Rect r, string text) => GUI.Button(r, text, button);

    void DrawMain()
    {
        float y = DrawTitle(Screen.height * 0.14f) + S(50);
        float step = S(72);
        if (Button(y, "PLAY")) StartPlaying();
        if (Button(y + step, "INSTRUCTIONS")) { backTo = State.Main; state = State.Instructions; }
        if (Button(y + step * 2, "SETTINGS")) { backTo = State.Main; state = State.Settings; }
        if (Button(y + step * 3, "EXIT")) Quit();
    }

    void DrawPause()
    {
        float y = Screen.height * 0.12f;
        GUI.Label(new Rect(0, y, Screen.width, S(110)), "PAUSED", title);
        y += S(150);
        float step = S(72);
        if (Button(y, "RESUME")) Resume();
        if (Button(y + step, "INSTRUCTIONS")) { backTo = State.Paused; state = State.Instructions; }
        if (Button(y + step * 2, "SETTINGS")) { backTo = State.Paused; state = State.Settings; }
        if (Button(y + step * 3, "RESTART")) Reload(false);
        if (Button(y + step * 4, "MAIN MENU")) Reload(true);
        if (Button(y + step * 5, "EXIT")) Quit();
    }

    void DrawEnd()
    {
        float y = Screen.height * 0.07f;
        GUIStyle head = new GUIStyle(title);
        head.normal.textColor = won ? Green : Red;
        GUI.Label(new Rect(0, y, Screen.width, S(110)), won ? "MISSION COMPLETE" : "MISSION FAILED", head);
        y += S(108);
        GUI.Label(new Rect(0, y, Screen.width, S(40)), endReason.ToUpperInvariant(), subtitle);
        y += S(64);

        // Statistics panel
        float w = Mathf.Min(S(720), Screen.width - 64);
        float x = (Screen.width - w) / 2;
        float rowH = S(42);
        float panelH = rowH * stats.Count + S(36);
        Fill(new Rect(x, y, w, panelH), new Color(1f, 1f, 1f, 0.05f));
        Fill(new Rect(x, y, S(4), panelH), won ? Green : Red);

        float ry = y + S(18);
        for (int i = 0; i < stats.Count; i++)
        {
            if (i > 0) Fill(new Rect(x + S(24), ry - 1, w - S(48), 1), new Color(1f, 1f, 1f, 0.07f));
            GUI.Label(new Rect(x + S(28), ry, w * 0.62f, rowH), stats[i].Key, statLabel);
            GUI.Label(new Rect(x + w * 0.55f, ry, w * 0.45f - S(28), rowH), stats[i].Value, statValue);
            ry += rowH;
        }
        y += panelH + S(22);

        if (grade != "")
        {
            GUI.Label(new Rect(0, y, Screen.width, S(30)), "RATING", small);
            GUI.Label(new Rect(0, y + S(22), Screen.width, S(90)), grade, gradeStyle);
            y += S(118);
        }
        else y += S(16);

        // Buttons side by side
        float bw = S(300), gap = S(24), bh = S(58);
        float bx = (Screen.width - bw * 2 - gap) / 2;
        y = Mathf.Min(y, Screen.height - bh - S(24));
        if (ButtonAt(new Rect(bx, y, bw, bh), "PLAY AGAIN")) Reload(false);
        if (ButtonAt(new Rect(bx + bw + gap, y, bw, bh), "MAIN MENU")) Reload(true);
    }

    const string Objective =
        "1.  Find the 4 hostages. They are tied up in rooms, guarded by terrorists.\n" +
        "2.  Hold E next to a hostage to untie them. They follow you.\n" +
        "3.  Lead them back to where you started. They wait there, safe.\n" +
        "4.  When all hostages are safe, the terrorist leader attacks the main area.\n" +
        "5.  Kill him to get the 6-digit bomb code.\n" +
        "6.  Enter the code at the beeping bomb before time runs out. A wrong code costs 30 s.\n\n" +
        "Watch out: not every room is as empty as it looks.";

    static readonly string[,] Controls =
    {
        { "W A S D", "Move" }, { "SHIFT", "Sprint" }, { "SPACE", "Jump" }, { "MOUSE", "Aim" },
        { "LEFT CLICK", "Shoot" }, { "R", "Reload" }, { "E", "Doors / untie / bomb" },
        { "H", "Medkit (2, full heal)" }, { "F", "Flashlight (blackout)" }, { "V", "First / third person" },
        { "ESC", "Pause" },
    };

    void DrawInstructions()
    {
        float w = Mathf.Min(S(1240), Screen.width - 48);
        float x = (Screen.width - w) / 2;
        float y = Screen.height * 0.07f;
        GUI.Label(new Rect(0, y, Screen.width, S(70)), "INSTRUCTIONS", heading);
        y += S(96);

        float colW = w * 0.56f;
        GUI.Label(new Rect(x, y, colW, S(30)), "OBJECTIVE", small);
        GUI.Label(new Rect(x, y + S(40), colW - S(40), Screen.height * 0.6f), Objective, body);

        float cx = x + colW + S(20);
        GUI.Label(new Rect(cx, y, w - colW, S(30)), "CONTROLS", small);
        float ry = y + S(40);
        GUIStyle key = new GUIStyle(statValue) { alignment = TextAnchor.MiddleLeft };
        for (int i = 0; i < Controls.GetLength(0); i++)
        {
            GUI.Label(new Rect(cx, ry, S(170), S(36)), Controls[i, 0], key);
            GUI.Label(new Rect(cx + S(180), ry, w - colW - S(200), S(36)), Controls[i, 1], statLabel);
            ry += S(38);
        }
        if (Button(Screen.height - S(100), "BACK")) state = backTo;
    }

    void DrawSettings()
    {
        float w = Mathf.Min(S(560), Screen.width - 64);
        float x = (Screen.width - w) / 2;
        float y = Screen.height * 0.16f;
        GUI.Label(new Rect(0, y, Screen.width, S(70)), "SETTINGS", heading);
        y += S(130);

        GUIStyle right = new GUIStyle(statValue);
        GUI.Label(new Rect(x, y, w, S(34)), "MOUSE SENSITIVITY", small);
        GUI.Label(new Rect(x, y, w, S(34)), sensitivity.ToString("0.0"), right);
        sensitivity = GUI.HorizontalSlider(new Rect(x, y + S(46), w, S(20)), sensitivity, 0.5f, 8f);
        y += S(110);
        GUI.Label(new Rect(x, y, w, S(34)), "VOLUME", small);
        GUI.Label(new Rect(x, y, w, S(34)), $"{Mathf.RoundToInt(volume * 100)}%", right);
        volume = GUI.HorizontalSlider(new Rect(x, y + S(46), w, S(20)), volume, 0f, 1f);
        AudioListener.volume = volume;

        if (Button(Screen.height - S(130), "BACK"))
        {
            ApplySettings();
            state = backTo;
        }
    }

    void Styles()
    {
        float s = Mathf.Clamp(Screen.height / 1080f, 0.5f, 2f);
        if (title != null && Mathf.Approximately(s, uiScale)) return;
        uiScale = s;

        if (white == null)
        {
            white = new Texture2D(1, 1) { hideFlags = HideFlags.HideAndDontSave };
            white.SetPixel(0, 0, Color.white);
            white.Apply();
        }

        GUIStyle Label(int size, Color color, TextAnchor anchor, bool wrap = false)
        {
            GUIStyle st = new GUIStyle(GUI.skin.label)
            {
                font = font, fontSize = Mathf.RoundToInt(size * s), alignment = anchor, wordWrap = wrap,
                richText = true, clipping = TextClipping.Overflow,
            };
            st.normal.textColor = color;
            return st;
        }

        title = Label(88, Text, TextAnchor.MiddleCenter);
        heading = Label(56, Text, TextAnchor.MiddleCenter);
        subtitle = Label(24, Red, TextAnchor.MiddleCenter);
        small = Label(22, Red, TextAnchor.MiddleLeft);
        body = Label(25, Text, TextAnchor.UpperLeft, true);
        body.font = null; // long text reads better in the plain font
        statLabel = Label(24, Grey, TextAnchor.MiddleLeft);
        statValue = Label(26, Text, TextAnchor.MiddleRight);
        gradeStyle = Label(84, Green, TextAnchor.MiddleCenter);
        tipStyle = Label(21, Grey, TextAnchor.UpperLeft, true);
        tipStyle.font = null;
        if (small.fontSize > 0) small.normal.textColor = Red;
        if (gradeStyle != null) gradeStyle.normal.textColor = new Color(1f, 0.82f, 0.25f);

        button = new GUIStyle(GUI.skin.button)
        {
            font = font, fontSize = Mathf.RoundToInt(26 * s), alignment = TextAnchor.MiddleCenter,
            border = new RectOffset(0, 0, 0, 0),
        };
        button.normal.background = Tex(new Color(1f, 1f, 1f, 0.07f));
        button.hover.background = Tex(new Color(Red.r, Red.g, Red.b, 0.9f));
        button.active.background = Tex(new Color(0.6f, 0.1f, 0.08f, 1f));
        button.focused.background = button.normal.background;
        button.normal.textColor = Text;
        button.hover.textColor = Color.white;
        button.active.textColor = Color.white;
        button.focused.textColor = Text;
    }

    static Texture2D Tex(Color c)
    {
        Texture2D t = new Texture2D(1, 1) { hideFlags = HideFlags.HideAndDontSave };
        t.SetPixel(0, 0, c);
        t.Apply();
        return t;
    }
}
