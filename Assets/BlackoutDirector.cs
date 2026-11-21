using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;

// Power cut / blackout for Blackout Protocol.
//
// Changes from the original:
//  - B now toggles the power in builds too (the README promised it, but it was editor-only).
//    Set allowBlackoutKey to false in the Inspector if you don't want it in the release.
//  - The colour grade is applied to a runtime copy of the Volume profile, so play mode no longer
//    writes overrides into the project's profile asset (which persisted after you stopped playing).
//  - Area (Rectangle) lights can be dimmed too: includeAreaLights (off by default, as before).
//  - A global Volume in the scene is preferred. If there isn't one, this script creates its own.
//  - The flashlight is attached again if the camera appears after the scene has loaded.
//  - Pressing F with an empty battery no longer plays the click for nothing.
//  - B and F are ignored while a menu is open (GameMenu.IsBlocking).
//
// Public API (used by PowerSwitch, MapLighting, MissionHUD) is unchanged.
public sealed class BlackoutDirector : MonoBehaviour
{
    private const float DefaultBatterySeconds = 120f;
    private const int AudioSampleRate = 44100;
    private static BlackoutDirector instance;

    [Header("Controls")]
    [SerializeField] private bool allowBlackoutKey = true;   // B toggles the power (also in builds)
    [SerializeField] private KeyCode blackoutKey = KeyCode.B;
    [SerializeField] private KeyCode flashlightKey = KeyCode.F;

    [Header("Lights")]
    [SerializeField] private bool includeAreaLights = false; // true = Rectangle (area) lights go dark too

    [Header("Flashlight")]
    [SerializeField] private float flashlightRange = 18f;
    [SerializeField] private float flashlightAngle = 42f;
    [SerializeField] private float flashlightIntensity = 4.5f;
    [SerializeField] private float batterySeconds = DefaultBatterySeconds;
    [SerializeField] private float transitionDuration = 0.35f;

    [Header("Blackout grading")]
    [SerializeField] private float blackoutExposure = -1.2f;
    [SerializeField] private Color blackoutColorFilter = new Color(0.55f, 0.68f, 0.82f);
    [SerializeField] private float blackoutContrast = 18f;
    [SerializeField] private float blackoutSaturation = -25f;
    [SerializeField] private Color blackoutAmbientColor = new Color(0.015f, 0.025f, 0.05f);

    private readonly List<Light> managedLights = new List<Light>();
    private readonly Dictionary<Light, bool> originalLightStates = new Dictionary<Light, bool>();
    private readonly Dictionary<Light, float> originalLightIntensities = new Dictionary<Light, float>();
    private Light flashlight;
    private Volume volume;
    private VolumeProfile ownedProfile;      // the runtime copy we grade, never the project asset
    private ColorAdjustments colorAdjustments;
    private AudioSource blackoutAudio;
    private AudioClip humClip;
    private AudioClip switchClip;
    private float normalExposure;
    private float normalContrast;
    private float normalSaturation;
    private Color normalColorFilter = Color.white;
    private Color normalAmbientColor;
    private Color normalSkyAmbient;
    private Color normalEquatorAmbient;
    private Color normalGroundAmbient;
    private bool isBlackout;
    private bool flashlightEnabled = true;
    private float transition;
    private float transitionTarget;
    private bool sceneStateCaptured;

    public bool IsBlackout => isBlackout;
    public float BatteryPercent => Mathf.Clamp01(batterySeconds / DefaultBatterySeconds);

    // The one director in the game (created automatically after the scene loads).
    public static BlackoutDirector Instance => instance;

    // Fired whenever the power goes off (true) or comes back (false).
    public static event System.Action<bool> BlackoutChanged;

    // Called by the UPS power switch (PowerSwitch) to cut or restore the building's power.
    public static void SetPower(bool on)
    {
        if (instance == null) CreateForScene();
        if (instance != null && instance.isBlackout == on) instance.SetBlackout(!on);
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void CreateForScene()
    {
        if (FindFirstObjectByType<BlackoutDirector>() != null)
        {
            return;
        }

        GameObject manager = new GameObject("Blackout Director");
        DontDestroyOnLoad(manager);
        manager.AddComponent<BlackoutDirector>();
    }

    private void Awake()
    {
        if (instance != null && instance != this)
        {
            Destroy(gameObject);
            return;
        }

        instance = this;
        DontDestroyOnLoad(gameObject);
        SceneManager.sceneLoaded += OnSceneLoaded;
        EnsureSceneReferences(true);
    }

    private void Update()
    {
        bool menuOpen = GameMenu.IsBlocking;

        if (allowBlackoutKey && !menuOpen && Input.GetKeyDown(blackoutKey))
        {
            SetBlackout(!isBlackout);
        }

        if (!menuOpen && Input.GetKeyDown(flashlightKey) && isBlackout && batterySeconds > 0f)
        {
            flashlightEnabled = !flashlightEnabled;
            UpdateFlashlight();
            PlayToggleClick();
        }

        // The camera can appear after the scene loaded; attach the flashlight once it does
        if (flashlight == null && UnityEngine.Camera.main != null)
        {
            CreateFlashlight();
        }

        transition = Mathf.MoveTowards(transition, transitionTarget, Time.deltaTime / Mathf.Max(0.01f, transitionDuration));
        ApplyTransition(transition);

        if (flashlight != null && flashlight.enabled)
        {
            batterySeconds = Mathf.Max(0f, batterySeconds - Time.deltaTime);
            if (batterySeconds <= 0f)
            {
                flashlightEnabled = false;
                UpdateFlashlight();
            }
        }
    }

    // Re-reads every light's normal brightness and the ambient light, after other code changed them
    // at startup (MapLighting adds ceiling lights and dims the sun).
    public void RefreshLights()
    {
        if (isBlackout) return;
        originalLightStates.Clear();
        originalLightIntensities.Clear();
        sceneStateCaptured = false;
        EnsureSceneReferences(true);
    }

    private void EnsureSceneReferences(bool resetTransition)
    {
        managedLights.Clear();
        foreach (Light sceneLight in FindObjectsByType<Light>(FindObjectsSortMode.None))
        {
            if (sceneLight == null || sceneLight == flashlight)
            {
                continue;
            }

            if (sceneLight.type == LightType.Rectangle && !includeAreaLights)
            {
                continue;
            }

            managedLights.Add(sceneLight);
            if (!originalLightStates.ContainsKey(sceneLight))
            {
                originalLightStates[sceneLight] = sceneLight.enabled;
                originalLightIntensities[sceneLight] = sceneLight.intensity;
            }
        }

        CreateFlashlight();
        FindOrCreateColorAdjustments();
        CreateBlackoutAudio();

        // Only a freshly loaded scene snaps the fade to the current power state. A press of the
        // power switch during a fade keeps the fade where it is instead of jumping first.
        if (resetTransition)
        {
            transition = isBlackout ? 1f : 0f;
            transitionTarget = transition;
        }

        ApplyTransition(transition);
        SyncGuardAlert();
    }

    private void CreateFlashlight()
    {
        UnityEngine.Camera mainCamera = UnityEngine.Camera.main;
        if (mainCamera == null)
        {
            return;
        }

        Transform existing = mainCamera.transform.Find("Blackout Flashlight");
        GameObject flashlightObject = existing != null
            ? existing.gameObject
            : new GameObject("Blackout Flashlight");
        flashlightObject.transform.SetParent(mainCamera.transform, false);
        flashlightObject.transform.localPosition = new Vector3(0f, -0.08f, 0.2f);
        flashlightObject.transform.localRotation = Quaternion.identity;

        flashlight = flashlightObject.GetComponent<Light>();
        if (flashlight == null)
        {
            flashlight = flashlightObject.AddComponent<Light>();
        }

        flashlight.type = LightType.Spot;
        flashlight.range = flashlightRange;
        flashlight.spotAngle = flashlightAngle;
        flashlight.innerSpotAngle = flashlightAngle * 0.65f;
        flashlight.intensity = flashlightIntensity;
        flashlight.color = new Color(0.82f, 0.92f, 1f);
        flashlight.shadows = LightShadows.Soft;
        UpdateFlashlight();
    }

    private void CreateBlackoutAudio()
    {
        if (blackoutAudio != null)
        {
            return;
        }

        GameObject audioObject = new GameObject("Blackout Soundscape");
        audioObject.transform.SetParent(transform, false);
        blackoutAudio = audioObject.AddComponent<AudioSource>();
        blackoutAudio.loop = true;
        blackoutAudio.playOnAwake = false;
        blackoutAudio.spatialBlend = 0f;
        blackoutAudio.volume = 0.16f;
        humClip = CreateHumClip();
        switchClip = CreateSwitchClip();
        blackoutAudio.clip = humClip;
    }

    // 2-second loop. 52 Hz and 104 Hz both fit a whole number of cycles in 2 s, so the loop is seamless.
    private AudioClip CreateHumClip()
    {
        const int seconds = 2;
        AudioClip clip = AudioClip.Create("Blackout Hum", AudioSampleRate * seconds, 1, AudioSampleRate, false);
        float[] samples = new float[AudioSampleRate * seconds];
        for (int i = 0; i < samples.Length; i++)
        {
            float time = i / (float)AudioSampleRate;
            samples[i] = Mathf.Sin(time * Mathf.PI * 2f * 52f) * 0.22f
                + Mathf.Sin(time * Mathf.PI * 2f * 104f) * 0.06f;
        }

        clip.SetData(samples, 0);
        return clip;
    }

    private AudioClip CreateSwitchClip()
    {
        const int sampleCount = AudioSampleRate / 8;
        AudioClip clip = AudioClip.Create("Blackout Switch", sampleCount, 1, AudioSampleRate, false);
        float[] samples = new float[sampleCount];
        for (int i = 0; i < samples.Length; i++)
        {
            float envelope = 1f - i / (float)samples.Length;
            samples[i] = Mathf.Sin(i * 0.42f) * envelope * 0.35f;
        }

        clip.SetData(samples, 0);
        return clip;
    }

    private void FindOrCreateColorAdjustments()
    {
        // Prefer a global Volume from the scene. A local Volume only grades inside its box.
        Volume chosen = null;
        foreach (Volume candidate in FindObjectsByType<Volume>(FindObjectsSortMode.None))
        {
            if (candidate.isGlobal)
            {
                chosen = candidate;
                break;
            }
        }

        if (chosen == null)
        {
            GameObject volumeObject = new GameObject("Blackout Color Grade");
            chosen = volumeObject.AddComponent<Volume>();
            chosen.isGlobal = true;
            chosen.priority = 10f;
            chosen.profile = ScriptableObject.CreateInstance<VolumeProfile>();
        }

        volume = chosen;

        // Work on a runtime copy. Writing overrides into the project's own profile asset
        // would change that asset and it would stay changed after play mode ends.
        if (volume.profile == null || volume.profile != ownedProfile)
        {
            ownedProfile = volume.profile != null
                ? Instantiate(volume.profile)
                : ScriptableObject.CreateInstance<VolumeProfile>();
            volume.profile = ownedProfile;
        }

        if (!volume.profile.TryGet(out colorAdjustments))
        {
            colorAdjustments = volume.profile.Add<ColorAdjustments>(true);
        }

        if (!sceneStateCaptured)
        {
            normalExposure = colorAdjustments.postExposure.value;
            normalContrast = colorAdjustments.contrast.value;
            normalSaturation = colorAdjustments.saturation.value;
            normalColorFilter = colorAdjustments.colorFilter.value;
            normalAmbientColor = RenderSettings.ambientLight;
            normalSkyAmbient = RenderSettings.ambientSkyColor;
            normalEquatorAmbient = RenderSettings.ambientEquatorColor;
            normalGroundAmbient = RenderSettings.ambientGroundColor;
            sceneStateCaptured = true;
        }
    }

    private void SetBlackout(bool enabled)
    {
        EnsureSceneReferences(false);
        isBlackout = enabled;
        transitionTarget = enabled ? 1f : 0f;
        if (blackoutAudio != null)
        {
            blackoutAudio.PlayOneShot(switchClip);
            if (enabled)
            {
                blackoutAudio.Play();
            }
            else
            {
                blackoutAudio.Stop();
            }
        }

        UpdateFlashlight();
        BlackoutChanged?.Invoke(enabled);
    }

    private void PlayToggleClick()
    {
        if (blackoutAudio != null && switchClip != null)
        {
            blackoutAudio.PlayOneShot(switchClip, 0.5f);
        }
    }

    // Keeps the guards' vision rules in step with the lights and the flashlight.
    private void SyncGuardAlert()
    {
        GuardAlert.Blackout = isBlackout;
        GuardAlert.FlashlightOn = flashlight != null && flashlight.enabled;
    }

    private void ApplyTransition(float amount)
    {
        foreach (Light sceneLight in managedLights)
        {
            if (sceneLight == null)
            {
                continue;
            }

            if (originalLightIntensities.TryGetValue(sceneLight, out float originalIntensity))
            {
                sceneLight.intensity = Mathf.Lerp(originalIntensity, 0f, amount);
            }

            if (amount <= 0f && originalLightStates.TryGetValue(sceneLight, out bool originalState))
            {
                sceneLight.enabled = originalState;
            }
            else if (amount > 0f)
            {
                sceneLight.enabled = true;
            }
        }

        if (colorAdjustments != null)
        {
            colorAdjustments.postExposure.overrideState = true;
            colorAdjustments.postExposure.value = Mathf.Lerp(normalExposure, blackoutExposure, amount);
            colorAdjustments.contrast.overrideState = true;
            colorAdjustments.contrast.value = Mathf.Lerp(normalContrast, blackoutContrast, amount);
            colorAdjustments.saturation.overrideState = true;
            colorAdjustments.saturation.value = Mathf.Lerp(normalSaturation, blackoutSaturation, amount);
            colorAdjustments.colorFilter.overrideState = true;
            colorAdjustments.colorFilter.value = Color.Lerp(normalColorFilter, blackoutColorFilter, amount);
        }

        RenderSettings.ambientLight = Color.Lerp(normalAmbientColor, blackoutAmbientColor, amount);

        // MapLighting switches the scene to Trilight (or Gradient) ambient, which takes its colours
        // from the sky / equator / ground values and ignores ambientLight. Dim those as well,
        // otherwise the room keeps its ambient light during a power cut.
        if (RenderSettings.ambientMode != AmbientMode.Flat)
        {
            RenderSettings.ambientSkyColor = Color.Lerp(normalSkyAmbient, blackoutAmbientColor, amount);
            RenderSettings.ambientEquatorColor = Color.Lerp(normalEquatorAmbient, blackoutAmbientColor, amount);
            RenderSettings.ambientGroundColor = Color.Lerp(normalGroundAmbient, blackoutAmbientColor, amount);
        }
    }

    private void UpdateFlashlight()
    {
        if (flashlight != null)
        {
            flashlight.enabled = flashlightEnabled && batterySeconds > 0f && isBlackout;
        }

        SyncGuardAlert();
    }

    private void OnDestroy()
    {
        SceneManager.sceneLoaded -= OnSceneLoaded;
        if (instance == this)
        {
            instance = null;
        }
    }

    private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        // A new or restarted mission always starts with the power on and a full battery.
        isBlackout = false;
        flashlightEnabled = true;
        batterySeconds = DefaultBatterySeconds;
        if (blackoutAudio != null)
        {
            blackoutAudio.Stop();
        }

        sceneStateCaptured = false;
        originalLightStates.Clear();
        originalLightIntensities.Clear();
        flashlight = null;
        EnsureSceneReferences(true);
    }
}
