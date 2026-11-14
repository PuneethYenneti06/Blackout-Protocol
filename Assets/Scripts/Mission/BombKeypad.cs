using UnityEngine;

// The bomb's code panel. Press E at the bomb to open it, type the 6-digit disarm code on the
// keyboard (number keys), Enter to confirm, Backspace to delete, Esc to close.
// The code is random every run; the boss carries it and it's revealed when he dies.
// A wrong code costs time off the bomb timer.
public class BombKeypad : MonoBehaviour
{
    public static string Code { get; private set; } = "";
    public static bool CodeKnown { get; private set; }
    public static bool IsOpen { get; private set; }

    public float wrongCodePenalty = 30f;   // seconds taken off the timer for a wrong code
    public float closeDistance = 3f;       // walking this far away closes the panel

    Bomb bomb;
    Transform user;
    string entered = "";
    float errorFlash;
    PlayerShooting shooting;
    AudioClip errorSound;
    AudioClip pressSound;
    AudioSource audioSource;
    GUIStyle title, digits, hint, small, keyStyle;
    Texture2D white;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics()
    {
        Code = "";
        CodeKnown = false;
        IsOpen = false;
    }

    void Awake()
    {
        bomb = GetComponent<Bomb>();
        // New code every run (also after a restart)
        Code = Random.Range(0, 1000000).ToString("D6");
        CodeKnown = false;
        IsOpen = false;

        errorSound = Resources.Load<AudioClip>("Sounds/keypad_error");
        pressSound = Resources.Load<AudioClip>("Sounds/bomb_beep");
        audioSource = gameObject.AddComponent<AudioSource>();
        audioSource.playOnAwake = false;
        audioSource.spatialBlend = 0f;
    }

    // Called by the boss when he dies
    public static void Reveal()
    {
        CodeKnown = true;
    }

    public void Open(Transform who)
    {
        if (IsOpen) return;
        user = who;
        entered = "";
        IsOpen = true;
        shooting = who != null ? who.GetComponent<PlayerShooting>() : null;
        if (shooting != null) shooting.enabled = false; // left click doesn't fire while typing
    }

    public void Close()
    {
        if (!IsOpen) return;
        IsOpen = false;
        if (shooting != null) shooting.enabled = true;
        shooting = null;
    }

    void OnDisable()
    {
        Close();
    }

    void Update()
    {
        errorFlash = Mathf.MoveTowards(errorFlash, 0f, Time.deltaTime * 1.5f);
        if (!IsOpen) return;

        MissionManager m = MissionManager.Instance;
        PlayerHealth ph = user != null ? user.GetComponent<PlayerHealth>() : null;
        bool tooFar = user == null || Vector3.Distance(user.position, transform.position) > closeDistance;
        if (tooFar || (ph != null && ph.IsDead) || (m != null && m.IsOver) || (bomb != null && bomb.Defused)
            || Input.GetKeyDown(KeyCode.Escape))
        {
            Close();
            return;
        }

        foreach (char ch in Input.inputString)
        {
            if (ch >= '0' && ch <= '9' && entered.Length < 6)
            {
                entered += ch;
                if (pressSound != null) audioSource.PlayOneShot(pressSound, 0.25f);
            }
            else if (ch == '\b' && entered.Length > 0)
            {
                entered = entered.Substring(0, entered.Length - 1);
            }
            else if ((ch == '\n' || ch == '\r') && entered.Length == 6)
            {
                Submit();
                return;
            }
        }
    }

    void Submit()
    {
        if (entered == Code)
        {
            Close();
            if (bomb != null) bomb.Defuse();
            return;
        }

        entered = "";
        errorFlash = 1f;
        if (errorSound != null) audioSource.PlayOneShot(errorSound);
        MissionManager m = MissionManager.Instance;
        if (m != null)
        {
            m.AddTimePenalty(wrongCodePenalty);
            m.ShowToast($"Wrong code! -{wrongCodePenalty:0} seconds");
        }
    }

    void OnGUI()
    {
        if (!IsOpen) return;
        GUI.depth = -100; // on top of everything else
        if (white == null)
        {
            white = new Texture2D(1, 1);
            white.SetPixel(0, 0, Color.white);
            white.Apply();
            title = new GUIStyle(GUI.skin.label) { fontSize = 28, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter, wordWrap = false };
            digits = new GUIStyle(GUI.skin.label) { fontSize = 44, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter, wordWrap = false };
            small = new GUIStyle(GUI.skin.label) { fontSize = 19, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter, wordWrap = false };
            hint = new GUIStyle(GUI.skin.label) { fontSize = 15, alignment = TextAnchor.MiddleCenter, wordWrap = false };
            keyStyle = new GUIStyle(GUI.skin.label) { fontSize = 14, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter, wordWrap = false };
        }

        const float w = 560f, h = 360f;
        Rect panel = new Rect((Screen.width - w) * 0.5f, (Screen.height - h) * 0.5f, w, h);

        // Dim the game behind the panel, then the panel itself
        GUI.color = new Color(0f, 0f, 0f, 0.45f);
        GUI.DrawTexture(new Rect(0, 0, Screen.width, Screen.height), white);
        GUI.color = new Color(0.05f, 0.05f, 0.06f, 0.97f);
        GUI.DrawTexture(panel, white);
        GUI.color = errorFlash > 0f ? Color.Lerp(new Color(0.6f, 0.1f, 0.1f), new Color(1f, 0.15f, 0.15f), errorFlash)
                                    : new Color(0.6f, 0.1f, 0.1f);
        GUI.DrawTexture(new Rect(panel.x, panel.y, w, 4f), white);
        GUI.DrawTexture(new Rect(panel.x, panel.yMax - 4f, w, 4f), white);

        float y = panel.y + 24f;

        // Title
        GUI.color = new Color(1f, 0.3f, 0.25f);
        GUI.Label(new Rect(panel.x, y, w, 36f), "ENTER DISARM CODE", title);
        y += 36f + 22f;

        // Six digit boxes
        const float box = 64f, gap = 12f;
        float total = box * 6 + gap * 5;
        float x0 = panel.x + (w - total) * 0.5f;
        for (int i = 0; i < 6; i++)
        {
            Rect r = new Rect(x0 + i * (box + gap), y, box, 80f);
            GUI.color = new Color(0.14f, 0.14f, 0.16f, 1f);
            GUI.DrawTexture(r, white);
            GUI.color = i == entered.Length ? new Color(1f, 0.82f, 0.25f) : new Color(0.32f, 0.32f, 0.35f);
            GUI.DrawTexture(new Rect(r.x, r.yMax - 4f, r.width, 4f), white);
            GUI.color = errorFlash > 0.3f ? new Color(1f, 0.3f, 0.3f) : new Color(0.45f, 1f, 0.5f);
            if (i < entered.Length) GUI.Label(r, entered[i].ToString(), digits);
        }
        y += 80f + 22f;

        // Status
        GUI.color = errorFlash > 0.3f ? new Color(1f, 0.35f, 0.35f) : new Color(0.85f, 0.85f, 0.87f);
        string status = errorFlash > 0.3f ? "WRONG CODE"
                      : CodeKnown ? $"Code from the leader: {Code}"
                      : "The terrorist leader has the code";
        GUI.Label(new Rect(panel.x, y, w, 28f), status, small);
        y += 28f + 26f;

        // Controls: a row of key chips, each with what it does underneath
        string[] keys = { "0 - 9", "ENTER", "BACKSPACE", "ESC" };
        string[] acts = { "Type", "Confirm", "Delete", "Close" };
        float colW = (w - 60f) / keys.Length;
        for (int i = 0; i < keys.Length; i++)
        {
            float cx = panel.x + 30f + i * colW;
            Vector2 ks = keyStyle.CalcSize(new GUIContent(keys[i]));
            Rect chip = new Rect(cx + (colW - ks.x - 18f) * 0.5f, y, ks.x + 18f, 24f);
            GUI.color = new Color(0.22f, 0.22f, 0.25f, 1f);
            GUI.DrawTexture(chip, white);
            GUI.color = new Color(1f, 0.82f, 0.3f);
            GUI.Label(chip, keys[i], keyStyle);
            GUI.color = new Color(0.72f, 0.72f, 0.75f);
            GUI.Label(new Rect(cx, y + 26f, colW, 22f), acts[i], hint);
        }
        y += 24f + 22f + 18f;

        // Penalty warning
        GUI.color = new Color(0.62f, 0.62f, 0.66f);
        GUI.Label(new Rect(panel.x, y, w, 22f), $"A wrong code takes {wrongCodePenalty:0} seconds off the timer", hint);
        GUI.color = Color.white;
    }
}
