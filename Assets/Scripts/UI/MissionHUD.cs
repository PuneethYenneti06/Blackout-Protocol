using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

// The mission HUD, built in code at runtime with uGUI (no prefab or scene setup needed).
// Added automatically by MissionManager.
//  - Top centre: bomb timer, LOCKDOWN banner
//  - Top left: objectives with checkmarks
//  - Bottom left: health pips, medkits
//  - Bottom right: ammo
//  - Centre: interaction prompt, hold-progress bar, toasts
//  - Full screen: red damage flash, end screen
public class MissionHUD : MonoBehaviour
{
    public static MissionHUD Instance { get; private set; }

    [Header("Look")]
    public Color accent = new Color(1f, 0.82f, 0.25f);
    public Color danger = new Color(0.95f, 0.2f, 0.15f);
    public Color good = new Color(0.35f, 0.9f, 0.45f);
    public float toastSeconds = 2.5f;
    public float damageFlashAlpha = 0.45f;
    public float damageFlashFade = 2.5f;     // alpha per second

    [Header("End screen")]
    public string credit = "Time Bomb by barism09 (CC BY)";

    Font font;
    Canvas canvas;
    Text timerText, lockdownText, objectivesText, ammoText, medkitText, promptText, holdLabel, powerText;
    Image holdFill, damageFlash;
    GameObject holdRoot, endRoot;
    Text endTitle, endReason, endStats;
    readonly List<Image> pips = new List<Image>();
    RectTransform pipRow;
    RectTransform toastColumn;

    class ToastEntry { public Text text; public float timeLeft; }
    readonly List<ToastEntry> toasts = new List<ToastEntry>();

    PlayerHealth playerHealth;
    PlayerShooting playerShooting;
    PlayerMedkits playerMedkits;
    float playerSearchTimer;
    string promptThisFrame;
    bool holdThisFrame;
    bool endShown;

    void Awake()
    {
        Instance = this;
        font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        Build();
    }

    void OnEnable()
    {
        MissionManager.Toast += AddToast;
        PlayerHealth.Damaged += OnDamaged;
    }

    void OnDisable()
    {
        MissionManager.Toast -= AddToast;
        PlayerHealth.Damaged -= OnDamaged;
    }

    void OnDestroy()
    {
        if (Instance == this) Instance = null;
        if (canvas != null) Destroy(canvas.gameObject);
    }

    // ---- Public API for gameplay scripts (call every frame while it should show) ----

    public void SetPrompt(string text)
    {
        promptThisFrame = text;
    }

    public void SetHold(string label, float progress01)
    {
        holdThisFrame = true;
        holdRoot.SetActive(true);
        holdLabel.text = label;
        holdFill.fillAmount = Mathf.Clamp01(progress01);
    }

    public void AddToast(string message)
    {
        Text t = MakeText(toastColumn, "Toast", message, 30, TextAnchor.MiddleCenter, Color.white);
        t.rectTransform.sizeDelta = new Vector2(900f, 40f);
        Outline o = t.gameObject.AddComponent<Outline>();
        o.effectColor = new Color(0f, 0f, 0f, 0.8f);
        toasts.Add(new ToastEntry { text = t, timeLeft = toastSeconds });
        while (toasts.Count > 4)
        {
            Destroy(toasts[0].text.gameObject);
            toasts.RemoveAt(0);
        }
    }

    // ---- Update ----

    void LateUpdate()
    {
        float dt = Time.unscaledDeltaTime;
        FindPlayer(dt);

        MissionManager m = MissionManager.Instance;
        UpdateTimer(m);
        UpdateObjectives(m);
        UpdateHealth();
        UpdateAmmo();
        UpdatePower();
        UpdateToasts(dt);

        // Prompt and hold bar only show while someone keeps asking for them
        promptText.gameObject.SetActive(!string.IsNullOrEmpty(promptThisFrame) && !endShown);
        if (promptText.gameObject.activeSelf) promptText.text = promptThisFrame;
        promptThisFrame = null;
        if (!holdThisFrame || endShown) holdRoot.SetActive(false);
        holdThisFrame = false;

        if (damageFlash.color.a > 0f)
        {
            Color c = damageFlash.color;
            c.a = Mathf.Max(0f, c.a - damageFlashFade * dt);
            damageFlash.color = c;
        }

        if (m != null && m.IsOver && !endShown) ShowEndScreen(m);
    }

    void FindPlayer(float dt)
    {
        if (playerHealth != null) return;
        playerSearchTimer -= dt;
        if (playerSearchTimer > 0f) return;
        playerSearchTimer = 0.5f;

        GameObject player = GameObject.FindWithTag("Player");
        if (player == null) return;
        playerHealth = player.GetComponent<PlayerHealth>();
        playerShooting = player.GetComponent<PlayerShooting>();
        playerMedkits = player.GetComponent<PlayerMedkits>();
    }

    void UpdateTimer(MissionManager m)
    {
        if (m == null)
        {
            timerText.text = "";
            lockdownText.gameObject.SetActive(false);
            return;
        }

        float t = Mathf.Max(0f, m.TimeLeft);
        int minutes = Mathf.FloorToInt(t / 60f);
        int seconds = Mathf.FloorToInt(t % 60f);
        timerText.text = m.BombDefused ? $"BOMB DEFUSED  {minutes:00}:{seconds:00}" : $"{minutes:00}:{seconds:00}";
        timerText.color = m.BombDefused ? good : (t < 60f ? danger : Color.white);

        bool lockdown = m.IsLockdown;
        lockdownText.gameObject.SetActive(lockdown);
        if (lockdown)
        {
            float blink = 0.55f + 0.45f * Mathf.Abs(Mathf.Sin(Time.unscaledTime * 4f));
            lockdownText.color = new Color(danger.r, danger.g, danger.b, blink);
        }
    }

    void UpdateObjectives(MissionManager m)
    {
        if (m == null)
        {
            objectivesText.text = "";
            return;
        }

        bool hostagesDone = m.HostagesSaved >= m.hostagesRequired;
        string text =
            Line(m.BombDefused, "Defuse the bomb") + "\n" +
            Line(hostagesDone, $"Extract hostages {m.HostagesSaved}/{m.hostagesRequired}" +
                               (m.HostagesFollowing > 0 ? $"  ({m.HostagesFollowing} following)" : "") +
                               (m.HostagesDead > 0 ? $"  <color=#FF5544>{m.HostagesDead} dead</color>" : "")) + "\n" +
            "<size=22><color=#BBBBBB>" + Line(m.TerroristsKilled >= m.totalTerrorists,
                $"Optional: neutralize terrorists {m.TerroristsKilled}/{m.totalTerrorists}") + "</color></size>";
        objectivesText.text = text;
    }

    string Line(bool done, string text)
    {
        return done ? $"<color=#59E673>[x]</color> {text}" : $"[  ] {text}";
    }

    void UpdateHealth()
    {
        int max = playerHealth != null ? playerHealth.maxHealth : 5;
        int hp = playerHealth != null ? playerHealth.Health : max;
        while (pips.Count < max)
        {
            Image pip = MakeImage(pipRow, "Pip", Color.white);
            pip.rectTransform.sizeDelta = new Vector2(34f, 34f);
            pips.Add(pip);
        }
        for (int i = 0; i < pips.Count; i++)
        {
            pips[i].gameObject.SetActive(i < max);
            pips[i].color = i < hp ? good : new Color(0.2f, 0.2f, 0.2f, 0.8f);
        }

        if (playerMedkits != null)
        {
            medkitText.text = playerMedkits.IsHealing
                ? "HEALING..."
                : $"Medkits {playerMedkits.Count}/{playerMedkits.maxCarried}  [H]";
        }
        else medkitText.text = "";
    }

    void UpdateAmmo()
    {
        if (playerShooting == null)
        {
            ammoText.text = "";
            return;
        }
        string mag = playerShooting.Magazine == 0 ? $"<color=#FF5544>{playerShooting.Magazine}</color>" : playerShooting.Magazine.ToString();
        string status = playerShooting.IsReloading ? "\n<size=24>RELOADING...</size>"
                      : (playerShooting.Magazine == 0 && playerShooting.Spare > 0 ? "\n<size=24>[R] RELOAD</size>"
                      : (playerShooting.Magazine == 0 && playerShooting.Spare == 0 ? "\n<size=24><color=#FF5544>NO AMMO</color></size>" : ""));
        ammoText.text = $"{mag} <size=28>/ {playerShooting.Spare}</size>{status}";
    }

    void UpdatePower()
    {
        BlackoutDirector d = BlackoutDirector.Instance;
        if (d == null || !d.IsBlackout)
        {
            powerText.text = "";
            return;
        }
        int battery = Mathf.RoundToInt(d.BatteryPercent * 100f);
        string light = GuardAlert.FlashlightOn ? "<color=#FFD23F>ON</color> (visible from 30 m)" : "OFF";
        powerText.text = $"POWER OFF   Flashlight [F] {light}  {battery}%";
    }

    void UpdateToasts(float dt)
    {
        for (int i = toasts.Count - 1; i >= 0; i--)
        {
            ToastEntry e = toasts[i];
            e.timeLeft -= dt;
            if (e.timeLeft <= 0f)
            {
                Destroy(e.text.gameObject);
                toasts.RemoveAt(i);
                continue;
            }
            Color c = e.text.color;
            c.a = Mathf.Clamp01(e.timeLeft / 0.5f);
            e.text.color = c;
        }
    }

    void OnDamaged(int health, int max)
    {
        Color c = danger;
        c.a = damageFlashAlpha;
        damageFlash.color = c;
    }

    void ShowEndScreen(MissionManager m)
    {
        endShown = true;
        endRoot.SetActive(true);
        bool won = m.State == MissionState.Won;
        endTitle.text = won ? "MISSION COMPLETE" : "MISSION FAILED";
        endTitle.color = won ? good : danger;
        endReason.text = m.EndReason;

        float t = Mathf.Max(0f, m.TimeLeft);
        endStats.text =
            $"Time left: {Mathf.FloorToInt(t / 60f):00}:{Mathf.FloorToInt(t % 60f):00}\n" +
            $"Hostages saved: {m.HostagesSaved}/{m.totalHostages}\n" +
            $"Terrorists neutralized: {m.TerroristsKilled}/{m.totalTerrorists}\n" +
            $"Detected: {(m.PlayerWasDetected ? "yes" : "no")}\n\n" +
            $"<color=#FFD23F>Press [{m.restartKey}] to restart</color>\n\n" +
            $"<size=20><color=#999999>{credit}</color></size>";
    }

    // ---- Building the UI ----

    void Build()
    {
        GameObject canvasGo = new GameObject("MissionHUD Canvas");
        canvas = canvasGo.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 100;
        CanvasScaler scaler = canvasGo.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.matchWidthOrHeight = 0.5f;
        RectTransform root = (RectTransform)canvasGo.transform;

        // Damage flash first, so it draws under everything else
        damageFlash = MakeImage(root, "DamageFlash", new Color(1f, 0f, 0f, 0f));
        Stretch(damageFlash.rectTransform);
        damageFlash.raycastTarget = false;

        // Timer + lockdown
        timerText = MakeText(root, "Timer", "10:00", 52, TextAnchor.UpperCenter, Color.white);
        Place(timerText.rectTransform, new Vector2(0.5f, 1f), new Vector2(0f, -20f), new Vector2(700f, 70f));
        AddShadow(timerText);
        lockdownText = MakeText(root, "Lockdown", "LOCKDOWN", 44, TextAnchor.UpperCenter, danger);
        lockdownText.fontStyle = FontStyle.Bold;
        Place(lockdownText.rectTransform, new Vector2(0.5f, 1f), new Vector2(0f, -88f), new Vector2(700f, 60f));
        AddShadow(lockdownText);
        lockdownText.gameObject.SetActive(false);

        // Objectives
        objectivesText = MakeText(root, "Objectives", "", 26, TextAnchor.UpperLeft, Color.white);
        Place(objectivesText.rectTransform, new Vector2(0f, 1f), new Vector2(30f, -24f), new Vector2(760f, 160f), new Vector2(0f, 1f));
        AddShadow(objectivesText);

        // Health pips
        GameObject row = new GameObject("HealthPips", typeof(RectTransform));
        pipRow = (RectTransform)row.transform;
        pipRow.SetParent(root, false);
        Place(pipRow, new Vector2(0f, 0f), new Vector2(30f, 70f), new Vector2(400f, 40f), new Vector2(0f, 0f));
        HorizontalLayoutGroup h = row.AddComponent<HorizontalLayoutGroup>();
        h.spacing = 8f;
        h.childControlWidth = false;
        h.childControlHeight = false;
        h.childForceExpandWidth = false;
        h.childForceExpandHeight = false;

        medkitText = MakeText(root, "Medkits", "", 26, TextAnchor.LowerLeft, Color.white);
        Place(medkitText.rectTransform, new Vector2(0f, 0f), new Vector2(30f, 24f), new Vector2(500f, 40f), new Vector2(0f, 0f));
        AddShadow(medkitText);

        powerText = MakeText(root, "Power", "", 24, TextAnchor.LowerLeft, new Color(0.7f, 0.85f, 1f));
        Place(powerText.rectTransform, new Vector2(0f, 0f), new Vector2(30f, 120f), new Vector2(800f, 36f), new Vector2(0f, 0f));
        AddShadow(powerText);

        // Ammo
        ammoText = MakeText(root, "Ammo", "", 48, TextAnchor.LowerRight, Color.white);
        Place(ammoText.rectTransform, new Vector2(1f, 0f), new Vector2(-30f, 24f), new Vector2(500f, 120f), new Vector2(1f, 0f));
        AddShadow(ammoText);

        // Prompt
        promptText = MakeText(root, "Prompt", "", 30, TextAnchor.MiddleCenter, Color.white);
        Place(promptText.rectTransform, new Vector2(0.5f, 0.28f), Vector2.zero, new Vector2(1000f, 50f));
        AddShadow(promptText);
        promptText.gameObject.SetActive(false);

        // Hold progress bar
        holdRoot = new GameObject("HoldProgress", typeof(RectTransform));
        RectTransform holdRect = (RectTransform)holdRoot.transform;
        holdRect.SetParent(root, false);
        Place(holdRect, new Vector2(0.5f, 0.36f), Vector2.zero, new Vector2(420f, 70f));
        holdLabel = MakeText(holdRect, "Label", "", 26, TextAnchor.UpperCenter, Color.white);
        Place(holdLabel.rectTransform, new Vector2(0.5f, 1f), Vector2.zero, new Vector2(420f, 34f));
        AddShadow(holdLabel);
        Image back = MakeImage(holdRect, "Back", new Color(0f, 0f, 0f, 0.7f));
        Place(back.rectTransform, new Vector2(0.5f, 0f), new Vector2(0f, 12f), new Vector2(400f, 18f));
        holdFill = MakeImage(back.rectTransform, "Fill", accent);
        Stretch(holdFill.rectTransform);
        holdFill.sprite = WhiteSprite();
        holdFill.type = Image.Type.Filled;
        holdFill.fillMethod = Image.FillMethod.Horizontal;
        holdFill.fillAmount = 0f;
        holdRoot.SetActive(false);

        // Toasts
        GameObject col = new GameObject("Toasts", typeof(RectTransform));
        toastColumn = (RectTransform)col.transform;
        toastColumn.SetParent(root, false);
        Place(toastColumn, new Vector2(0.5f, 0.62f), Vector2.zero, new Vector2(900f, 200f));
        VerticalLayoutGroup v = col.AddComponent<VerticalLayoutGroup>();
        v.childAlignment = TextAnchor.LowerCenter;
        v.spacing = 4f;
        v.childControlWidth = false;
        v.childControlHeight = false;
        v.childForceExpandWidth = false;
        v.childForceExpandHeight = false;

        // End screen
        Image endBack = MakeImage(root, "EndScreen", new Color(0f, 0f, 0f, 0.82f));
        Stretch(endBack.rectTransform);
        endRoot = endBack.gameObject;
        endTitle = MakeText(endBack.rectTransform, "Title", "", 80, TextAnchor.MiddleCenter, Color.white);
        endTitle.fontStyle = FontStyle.Bold;
        Place(endTitle.rectTransform, new Vector2(0.5f, 0.75f), Vector2.zero, new Vector2(1400f, 110f));
        endReason = MakeText(endBack.rectTransform, "Reason", "", 36, TextAnchor.MiddleCenter, Color.white);
        Place(endReason.rectTransform, new Vector2(0.5f, 0.64f), Vector2.zero, new Vector2(1400f, 60f));
        endStats = MakeText(endBack.rectTransform, "Stats", "", 32, TextAnchor.UpperCenter, Color.white);
        Place(endStats.rectTransform, new Vector2(0.5f, 0.57f), Vector2.zero, new Vector2(1200f, 420f), new Vector2(0.5f, 1f));
        endRoot.SetActive(false);
    }

    Text MakeText(RectTransform parent, string name, string value, int size, TextAnchor anchor, Color color)
    {
        GameObject go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(parent, false);
        Text t = go.AddComponent<Text>();
        t.font = font;
        t.fontSize = size;
        t.alignment = anchor;
        t.color = color;
        t.text = value;
        t.supportRichText = true;
        t.raycastTarget = false;
        t.horizontalOverflow = HorizontalWrapMode.Overflow;
        t.verticalOverflow = VerticalWrapMode.Overflow;
        return t;
    }

    Image MakeImage(RectTransform parent, string name, Color color)
    {
        GameObject go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(parent, false);
        Image img = go.AddComponent<Image>();
        img.color = color;
        img.raycastTarget = false;
        return img;
    }

    static Sprite whiteSprite;
    static Sprite WhiteSprite()
    {
        if (whiteSprite == null)
            whiteSprite = Sprite.Create(Texture2D.whiteTexture, new Rect(0f, 0f, 4f, 4f), new Vector2(0.5f, 0.5f));
        return whiteSprite;
    }

    void AddShadow(Text t)
    {
        Shadow s = t.gameObject.AddComponent<Shadow>();
        s.effectColor = new Color(0f, 0f, 0f, 0.85f);
        s.effectDistance = new Vector2(2f, -2f);
    }

    static void Stretch(RectTransform r)
    {
        r.anchorMin = Vector2.zero;
        r.anchorMax = Vector2.one;
        r.offsetMin = Vector2.zero;
        r.offsetMax = Vector2.zero;
    }

    static void Place(RectTransform r, Vector2 anchor, Vector2 position, Vector2 size)
    {
        Place(r, anchor, position, size, anchor);
    }

    static void Place(RectTransform r, Vector2 anchor, Vector2 position, Vector2 size, Vector2 pivot)
    {
        r.anchorMin = anchor;
        r.anchorMax = anchor;
        r.pivot = pivot;
        r.anchoredPosition = position;
        r.sizeDelta = size;
    }
}
