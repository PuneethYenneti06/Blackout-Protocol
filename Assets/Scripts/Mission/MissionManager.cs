using UnityEngine;
using UnityEngine.SceneManagement;

public enum MissionState { Infiltration, Lockdown, Won, Lost }

// Runs the mission: 10:00 bomb timer, objectives, lockdown, win/lose, restart.
// One per scene. If the scene has guards but no MissionManager, one is created
// automatically at start, so the mission works before the setup tool has been run.
public class MissionManager : MonoBehaviour
{
    public static MissionManager Instance { get; private set; }

    [Header("Timer")]
    public float missionSeconds = 600f;       // 10:00 until the bomb explodes

    [Header("Objectives")]
    public int totalHostages = 4;
    public int hostagesRequired = 3;          // must be extracted to win
    public int maxHostageDeaths = 1;          // more than this many dead = mission lost
    public int totalTerrorists = 10;          // optional objective (score only)

    [Header("Restart")]
    public KeyCode restartKey = KeyCode.R;
    public bool freezeOnEnd = true;           // stop time while the end screen shows

    [Header("Sounds (optional)")]
    public AudioClip alarmSound;              // lockdown alarm
    [Range(0f, 1f)] public float alarmVolume = 0.6f;
    public bool loopAlarm = false;
    public AudioClip objectiveSound;          // objective completed
    public AudioClip winSound;
    public AudioClip loseSound;
    public AudioClip toastSound;              // pickup / info toast

    // ---- State ----
    public MissionState State { get; private set; } = MissionState.Infiltration;
    public float TimeLeft { get; private set; }
    public bool TimerRunning { get; private set; } = true;
    public bool BombDefused { get; private set; }
    public int HostagesFreed { get; private set; }
    public int HostagesSaved { get; private set; }
    public int HostagesDead { get; private set; }
    public int HostagesFollowing { get; private set; }   // freed, not yet saved or dead
    public int TerroristsKilled { get; private set; }
    public bool PlayerWasDetected { get; private set; }
    public bool IsLockdown => State == MissionState.Lockdown;
    public bool IsOver => State == MissionState.Won || State == MissionState.Lost;
    public string EndReason { get; private set; } = "";
    public string LockdownReason { get; private set; } = "";
    public Vector3 LastKnownPlayerPosition { get; private set; }
    public bool HasLastKnownPlayerPosition { get; private set; }

    // ---- Events ----
    public static event System.Action<string> ObjectiveCompleted;   // objective text
    public static event System.Action<string> LockdownStarted;      // reason
    public static event System.Action MissionWon;
    public static event System.Action<string> MissionLost;          // reason
    public static event System.Action HostageFreed;
    public static event System.Action HostageSaved;
    public static event System.Action HostageDied;
    public static event System.Action PlayerDetected;               // first time any guard spots the player
    public static event System.Action<string> Toast;                // short HUD message

    AudioSource audioSource;
    AudioSource alarmSource;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics()
    {
        Instance = null;
        ObjectiveCompleted = null;
        LockdownStarted = null;
        MissionWon = null;
        MissionLost = null;
        HostageFreed = null;
        HostageSaved = null;
        HostageDied = null;
        PlayerDetected = null;
        Toast = null;
    }

    // Creates a MissionManager in mission scenes (any scene with a TerroristGuard) that don't have one.
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void AutoCreate()
    {
        SceneManager.sceneLoaded -= OnAnySceneLoaded;
        SceneManager.sceneLoaded += OnAnySceneLoaded;
        EnsureForScene();
    }

    static void OnAnySceneLoaded(Scene scene, LoadSceneMode mode)
    {
        EnsureForScene();
    }

    static void EnsureForScene()
    {
        if (FindFirstObjectByType<MissionManager>() != null) return;
        if (FindFirstObjectByType<TerroristGuard>(FindObjectsInactive.Include) == null) return;
        new GameObject("MissionManager").AddComponent<MissionManager>();
        Debug.Log("[Mission] No MissionManager in the scene, created one automatically.");
    }

    void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(this);
            return;
        }
        Instance = this;
        Time.timeScale = 1f;
        TimeLeft = missionSeconds;

        audioSource = gameObject.AddComponent<AudioSource>();
        audioSource.playOnAwake = false;
        audioSource.spatialBlend = 0f;

        if (GetComponent<MissionHUD>() == null) gameObject.AddComponent<MissionHUD>();
    }

    void Start()
    {
        EnsurePlayerComponents();
    }

    void OnEnable()
    {
        TerroristGuard.GuardKilled += OnGuardKilled;
        TerroristGuard.PlayerSpotted += OnPlayerSpotted;
        PlayerHealth.PlayerDied += OnPlayerDied;
    }

    void OnDisable()
    {
        TerroristGuard.GuardKilled -= OnGuardKilled;
        TerroristGuard.PlayerSpotted -= OnPlayerSpotted;
        PlayerHealth.PlayerDied -= OnPlayerDied;
    }

    void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }

    // The player needs health and a gun for the mission; add them if the scene doesn't have them.
    void EnsurePlayerComponents()
    {
        GameObject player = GameObject.FindWithTag("Player");
        if (player == null)
        {
            Debug.LogWarning("[Mission] No object tagged Player found.");
            return;
        }
        if (player.GetComponent<PlayerHealth>() == null) player.AddComponent<PlayerHealth>();
        if (player.GetComponent<PlayerShooting>() == null) player.AddComponent<PlayerShooting>();
        if (player.GetComponent<PlayerMedkits>() == null) player.AddComponent<PlayerMedkits>();
        if (player.GetComponent<Interactor>() == null) player.AddComponent<Interactor>();
    }

    void Update()
    {
        if (IsOver)
        {
            if (Input.GetKeyDown(restartKey)) Restart();
            return;
        }

        if (TimerRunning)
        {
            TimeLeft -= Time.deltaTime;
            if (TimeLeft <= 0f)
            {
                TimeLeft = 0f;
                Lose("The bomb exploded.");
            }
        }
    }

    // ---- Called by gameplay scripts ----

    public void NotifyBombDefused()
    {
        if (IsOver || BombDefused) return;
        BombDefused = true;
        TimerRunning = false;
        CompleteObjective("Bomb defused");
        StartLockdown("The bomb was defused");
        CheckWin();
    }

    public void NotifyHostageFreed()
    {
        if (IsOver) return;
        HostagesFreed++;
        HostagesFollowing++;
        HostageFreed?.Invoke();
        ShowToast($"Hostage freed ({HostagesFreed}/{totalHostages})");
        if (HostagesFreed == 1) StartLockdown("A hostage was freed");
    }

    public void NotifyHostageSaved()
    {
        if (IsOver) return;
        HostagesSaved++;
        HostagesFollowing = Mathf.Max(0, HostagesFollowing - 1);
        HostageSaved?.Invoke();
        ShowToast($"Hostage extracted ({HostagesSaved}/{hostagesRequired})");
        if (HostagesSaved == hostagesRequired) CompleteObjective("Hostages extracted");
        CheckWin();
    }

    public void NotifyHostageDied(bool wasFollowing)
    {
        if (IsOver) return;
        HostagesDead++;
        if (wasFollowing) HostagesFollowing = Mathf.Max(0, HostagesFollowing - 1);
        HostageDied?.Invoke();
        ShowToast("A hostage was killed!");

        if (HostagesDead > maxHostageDeaths)
            Lose($"{HostagesDead} hostages were killed.");
        else
            CheckWin();
    }

    // Radio alarm from a guard, or any other reason. One-way: once on, lockdown stays.
    public void RaiseAlarm(string reason)
    {
        StartLockdown(reason);
    }

    public void StartLockdown(string reason)
    {
        if (State != MissionState.Infiltration) return;
        State = MissionState.Lockdown;
        LockdownReason = reason;
        Debug.Log($"[Mission] LOCKDOWN: {reason}");

        // The alarm gives away where the player is right now: hunters start there
        GameObject player = GameObject.FindWithTag("Player");
        if (player != null) ReportPlayerPosition(player.transform.position);

        if (alarmSound != null)
        {
            if (loopAlarm)
            {
                alarmSource = gameObject.AddComponent<AudioSource>();
                alarmSource.clip = alarmSound;
                alarmSource.loop = true;
                alarmSource.volume = alarmVolume;
                alarmSource.spatialBlend = 0f;
                alarmSource.Play();
            }
            else audioSource.PlayOneShot(alarmSound, alarmVolume);
        }
        LockdownStarted?.Invoke(reason);
    }

    // Guards report where they last saw the player, so hunters know where to go.
    public void ReportPlayerPosition(Vector3 position)
    {
        LastKnownPlayerPosition = position;
        HasLastKnownPlayerPosition = true;
    }

    public void ShowToast(string message)
    {
        if (toastSound != null && audioSource != null) audioSource.PlayOneShot(toastSound);
        Toast?.Invoke(message);
    }

    // ---- Internals ----

    void CompleteObjective(string text)
    {
        if (objectiveSound != null) audioSource.PlayOneShot(objectiveSound);
        ObjectiveCompleted?.Invoke(text);
        Toast?.Invoke(text);
    }

    // Wins once the bomb is defused and enough hostages are out, and nobody freed is still on the way.
    void CheckWin()
    {
        if (IsOver) return;
        if (!BombDefused || HostagesSaved < hostagesRequired) return;
        if (HostagesFollowing > 0) return;
        Win();
    }

    void OnGuardKilled(TerroristGuard guard)
    {
        if (IsOver) return;
        TerroristsKilled++;
        if (TerroristsKilled == totalTerrorists) CompleteObjective("All terrorists neutralized");
    }

    void OnPlayerSpotted(TerroristGuard guard)
    {
        if (IsOver) return;
        if (!PlayerWasDetected)
        {
            PlayerWasDetected = true;
            PlayerDetected?.Invoke();
        }
    }

    void OnPlayerDied()
    {
        Lose("You were killed.");
    }

    void Win()
    {
        State = MissionState.Won;
        TimerRunning = false;
        EndReason = "Bomb defused and hostages extracted.";
        StopAlarm();
        if (winSound != null) audioSource.PlayOneShot(winSound);
        MissionWon?.Invoke();
        if (freezeOnEnd) Time.timeScale = 0f;
    }

    void Lose(string reason)
    {
        if (IsOver) return;
        State = MissionState.Lost;
        TimerRunning = false;
        EndReason = reason;
        StopAlarm();
        if (loseSound != null) audioSource.PlayOneShot(loseSound);
        MissionLost?.Invoke(reason);
        if (freezeOnEnd) Time.timeScale = 0f;
    }

    void StopAlarm()
    {
        if (alarmSource != null) alarmSource.Stop();
    }

    public void Restart()
    {
        Time.timeScale = 1f;
        Scene scene = SceneManager.GetActiveScene();
#if UNITY_EDITOR
        // Scenes that aren't in Build Settings can still be reloaded in the editor
        if (scene.buildIndex < 0)
        {
            UnityEditor.SceneManagement.EditorSceneManager.LoadSceneInPlayMode(
                scene.path, new LoadSceneParameters(LoadSceneMode.Single));
            return;
        }
#endif
        if (scene.buildIndex >= 0) SceneManager.LoadScene(scene.buildIndex);
        else SceneManager.LoadScene(scene.name);
    }
}
