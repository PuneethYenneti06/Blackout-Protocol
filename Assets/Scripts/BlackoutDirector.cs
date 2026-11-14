using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;

public sealed class BlackoutDirector : MonoBehaviour
{
    private const float DefaultBatterySeconds = 120f;
    private const int AudioSampleRate = 44100;
    private static BlackoutDirector instance;

    [Header("Controls")]
    [SerializeField] private KeyCode blackoutKey = KeyCode.B;
    [SerializeField] private KeyCode flashlightKey = KeyCode.F;

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
    private ColorAdjustments colorAdjustments;
    private AudioSource blackoutAudio;
    private AudioClip humClip;
    private AudioClip switchClip;
    private float normalExposure;
    private float normalContrast;
    private float normalSaturation;
    private Color normalColorFilter = Color.white;
    private Color normalAmbientColor;
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
        EnsureSceneReferences();
    }

    private void Update()
    {
#if UNITY_EDITOR
        // Debug shortcut, editor only. In the game the UPS power switch cuts the power.
        if (Input.GetKeyDown(blackoutKey))
        {
            SetBlackout(!isBlackout);
        }
#endif

        if (Input.GetKeyDown(flashlightKey))
        {
            if (isBlackout)
            {
                flashlightEnabled = !flashlightEnabled;
                UpdateFlashlight();
                PlayToggleClick();
            }
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

    private void EnsureSceneReferences()
    {
        managedLights.Clear();
        foreach (Light sceneLight in FindObjectsByType<Light>(FindObjectsSortMode.None))
        {
            if (sceneLight == null || sceneLight == flashlight || sceneLight.type == LightType.Rectangle)
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
        transition = isBlackout ? 1f : 0f;
        transitionTarget = transition;
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
        volume = FindFirstObjectByType<Volume>();
        if (volume == null)
        {
            GameObject volumeObject = new GameObject("Blackout Color Grade");
            volume = volumeObject.AddComponent<Volume>();
            volume.isGlobal = true;
            volume.priority = 10f;
            volume.profile = ScriptableObject.CreateInstance<VolumeProfile>();
        }

        if (volume.profile == null)
        {
            volume.profile = ScriptableObject.CreateInstance<VolumeProfile>();
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
            sceneStateCaptured = true;
        }
    }

    private void SetBlackout(bool enabled)
    {
        EnsureSceneReferences();
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
        EnsureSceneReferences();
    }
}
