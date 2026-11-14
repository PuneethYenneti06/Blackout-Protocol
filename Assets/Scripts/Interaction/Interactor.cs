using UnityEngine;
using UnityEngine.SceneManagement;

// Lets the player use anything with an IInteractable (doors, bomb, hostages, power switch) with E.
// Press interactions fire on key down. Hold interactions fill a progress bar; releasing E,
// moving out of reach or taking a hit resets them.
// Added to the player automatically when a scene loads, together with the door setup.
public class Interactor : MonoBehaviour
{
    public KeyCode key = KeyCode.E;
    public float reach = 2f;
    public float eyeHeight = 1.2f;

    [Header("Fallback prompt (only when there is no MissionHUD)")]
    public string promptFont = "Segoe UI";
    public int promptSize = 30;

    public IInteractable Target { get; private set; }
    public bool IsHolding => holding != null;
    public float HoldProgress => holding != null ? holdTimer / Mathf.Max(holding.HoldTime, 0.01f) : 0f;

    IInteractable holding;
    float holdTimer;
    Animator anim;
    readonly Collider[] overlap = new Collider[32];

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Install()
    {
        SceneManager.sceneLoaded -= OnSceneLoaded;
        SceneManager.sceneLoaded += OnSceneLoaded;
        SetupScene();
    }

    static void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        SetupScene();
    }

    static void SetupScene()
    {
        int doors = Door.SetupAll();
        GameObject player = GameObject.FindWithTag("Player");
        if (player != null && player.GetComponent<Interactor>() == null)
            player.AddComponent<Interactor>();
        Debug.Log($"[Interactor] set up {doors} doors, player found: {player != null}");
    }

    void Awake()
    {
        anim = GetComponentInChildren<Animator>();
    }

    void OnEnable()
    {
        PlayerHealth.Damaged += OnDamaged;
    }

    void OnDisable()
    {
        PlayerHealth.Damaged -= OnDamaged;
        Cancel();
    }

    void OnDamaged(int health, int max)
    {
        Cancel();
    }

    void Update()
    {
        MissionManager m = MissionManager.Instance;
        bool blocked = (m != null && m.IsOver) || IsBusy();

        Target = blocked ? null : FindTarget();

        if (holding != null)
        {
            if (Target != holding || !Input.GetKey(key))
            {
                Cancel();
            }
            else
            {
                holdTimer += Time.deltaTime;
                float p = HoldProgress;
                holding.HoldTick(this, Mathf.Clamp01(p));
                if (MissionHUD.Instance != null)
                    MissionHUD.Instance.SetHold(Capitalize(holding.Prompt), p);

                if (p >= 1f)
                {
                    IInteractable done = holding;
                    holding = null;
                    holdTimer = 0f;
                    done.Interact(this);
                }
                return;
            }
        }

        if (Target == null) return;

        if (MissionHUD.Instance != null) MissionHUD.Instance.SetPrompt(PromptText(true));

        if (Input.GetKeyDown(key))
        {
            if (Target.HoldTime <= 0f)
            {
                Target.Interact(this);
            }
            else
            {
                holding = Target;
                holdTimer = 0f;
                AnimatorParams.TrySetTrigger(anim, "Kneel");
            }
        }
    }

    // Healing or reloading keeps the hands busy
    bool IsBusy()
    {
        if (TryGetComponent(out PlayerMedkits med) && med.IsHealing) return true;
        if (TryGetComponent(out PlayerShooting gun) && gun.IsReloading) return true;
        return false;
    }

    public void Cancel()
    {
        if (holding == null) return;
        IInteractable was = holding;
        holding = null;
        holdTimer = 0f;
        was.HoldCancelled(this);
    }

    IInteractable FindTarget()
    {
        Vector3 eye = transform.position + Vector3.up * eyeHeight * transform.lossyScale.y;
        int count = Physics.OverlapSphereNonAlloc(eye, reach, overlap, ~0, QueryTriggerInteraction.Collide);
        IInteractable closest = null;
        float closestDist = reach;
        for (int i = 0; i < count; i++)
        {
            Collider c = overlap[i];
            if (c.transform.IsChildOf(transform)) continue;
            IInteractable it = c.GetComponentInParent<IInteractable>();
            if (it == null || !it.CanInteract(this)) continue;
            // Bounds.ClosestPoint works on every collider type, including mesh colliders
            float d = Vector3.Distance(eye, c.bounds.ClosestPoint(eye));
            if (d < closestDist)
            {
                closestDist = d;
                closest = it;
            }
        }
        return closest;
    }

    string PromptText(bool rich)
    {
        string verb = Target.HoldTime > 0f ? "Hold" : "Press";
        string k = rich ? $"<color=#FFD23F>[{key}]</color>" : $"[{key}]";
        return $"{verb} {k} to {Target.Prompt}";
    }

    static string Capitalize(string s)
    {
        if (string.IsNullOrEmpty(s)) return s;
        return char.ToUpperInvariant(s[0]) + s.Substring(1) + "...";
    }

    // ---- Fallback prompt for scenes without a MissionHUD (e.g. door test scenes) ----

    GUIStyle promptStyle, shadowStyle;
    Texture2D panel;

    void OnGUI()
    {
        if (MissionHUD.Instance != null || Target == null || holding != null) return;
        if (promptStyle == null)
        {
            Font font = Font.CreateDynamicFontFromOSFont(new[] { promptFont, "Arial" }, promptSize);
            promptStyle = new GUIStyle(GUI.skin.label)
            {
                font = font,
                fontSize = promptSize,
                fontStyle = FontStyle.Bold,
                alignment = TextAnchor.MiddleCenter,
                richText = true
            };
            promptStyle.normal.textColor = Color.white;
            shadowStyle = new GUIStyle(promptStyle);
            shadowStyle.normal.textColor = new Color(0f, 0f, 0f, 0.9f);

            panel = new Texture2D(1, 1);
            panel.SetPixel(0, 0, new Color(0f, 0f, 0f, 0.6f));
            panel.Apply();
        }

        string plain = PromptText(false);
        Vector2 size = promptStyle.CalcSize(new GUIContent(plain));
        Rect box = new Rect((Screen.width - size.x) * 0.5f - 24f, Screen.height * 0.72f, size.x + 48f, size.y + 16f);
        GUI.DrawTexture(box, panel);
        GUI.Label(new Rect(box.x + 2f, box.y + 2f, box.width, box.height), plain, shadowStyle);
        GUI.Label(box, PromptText(true), promptStyle);
    }
}
