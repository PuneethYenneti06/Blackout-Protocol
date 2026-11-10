using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;

public sealed class BlackoutDirector : MonoBehaviour
{
    private const float DefaultBatterySeconds = 120f;
    private static BlackoutDirector instance;

    [Header("Controls")]
    [SerializeField] private KeyCode blackoutKey = KeyCode.B;
    [SerializeField] private KeyCode flashlightKey = KeyCode.F;

    [Header("Flashlight")]
    [SerializeField] private float flashlightRange = 18f;
    [SerializeField] private float flashlightAngle = 42f;
    [SerializeField] private float flashlightIntensity = 4.5f;
    [SerializeField] private float batterySeconds = DefaultBatterySeconds;

    [Header("Blackout grading")]
    [SerializeField] private float blackoutExposure = -1.2f;
    [SerializeField] private Color blackoutColorFilter = new Color(0.55f, 0.68f, 0.82f);

    private readonly List<Light> managedLights = new List<Light>();
    private readonly Dictionary<Light, bool> originalLightStates = new Dictionary<Light, bool>();
    private Light flashlight;
    private Volume volume;
    private ColorAdjustments colorAdjustments;
    private float normalExposure;
    private Color normalColorFilter = Color.white;
    private bool isBlackout;
    private bool flashlightEnabled = true;

    public bool IsBlackout => isBlackout;
    public float BatteryPercent => Mathf.Clamp01(batterySeconds / DefaultBatterySeconds);

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
        if (Input.GetKeyDown(blackoutKey))
        {
            SetBlackout(!isBlackout);
        }

        if (Input.GetKeyDown(flashlightKey))
        {
            flashlightEnabled = !flashlightEnabled;
            UpdateFlashlight();
        }

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
        originalLightStates.Clear();
        foreach (Light sceneLight in FindObjectsByType<Light>(FindObjectsSortMode.None))
        {
            if (sceneLight == null || sceneLight.type == LightType.Rectangle)
            {
                continue;
            }

            managedLights.Add(sceneLight);
            originalLightStates[sceneLight] = sceneLight.enabled;
        }

        CreateFlashlight();
        FindOrCreateColorAdjustments();
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

        normalExposure = colorAdjustments.postExposure.value;
        normalColorFilter = colorAdjustments.colorFilter.value;
    }

    private void SetBlackout(bool enabled)
    {
        EnsureSceneReferences();
        isBlackout = enabled;

        foreach (Light sceneLight in managedLights)
        {
            if (sceneLight == null || sceneLight == flashlight)
            {
                continue;
            }

            if (enabled)
            {
                sceneLight.enabled = false;
            }
            else if (originalLightStates.TryGetValue(sceneLight, out bool originalState))
            {
                sceneLight.enabled = originalState;
            }
        }

        if (colorAdjustments != null)
        {
            colorAdjustments.postExposure.overrideState = enabled;
            colorAdjustments.postExposure.value = enabled ? blackoutExposure : normalExposure;
            colorAdjustments.colorFilter.overrideState = enabled;
            colorAdjustments.colorFilter.value = enabled ? blackoutColorFilter : normalColorFilter;
        }

        UpdateFlashlight();
    }

    private void UpdateFlashlight()
    {
        if (flashlight == null)
        {
            return;
        }

        flashlight.enabled = flashlightEnabled && batterySeconds > 0f && isBlackout;
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
        isBlackout = false;
        EnsureSceneReferences();
    }
}
