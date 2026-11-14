using UnityEngine;

// The building's main power switch in the UPS room. Press E to cut the power (blackout through
// BlackoutDirector): guards then only see 4 m, unless your flashlight (F) is on (30 m).
// Press again to restore the power if allowRestore is on.
// Root object: this script + a collider; the model (if any) is a child "Visual".
public class PowerSwitch : MonoBehaviour, IInteractable
{
    public bool allowRestore = true;
    public float noiseRadius = 6f;           // the breaker is loud
    public AudioClip switchSound;            // optional (BlackoutDirector also plays its own click)
    public Transform lever;                  // optional: rotated when the power is off
    public Vector3 leverOffRotation = new Vector3(0f, 0f, -60f);

    Quaternion leverOnRotation;
    AudioSource audioSource;

    bool PowerOn => BlackoutDirector.Instance == null || !BlackoutDirector.Instance.IsBlackout;

    void Awake()
    {
        if (lever != null) leverOnRotation = lever.localRotation;
        if (GetComponent<Collider>() == null)
        {
            BoxCollider b = gameObject.AddComponent<BoxCollider>();
            b.size = new Vector3(0.6f, 0.8f, 0.3f);
            b.center = new Vector3(0f, 1.2f, 0f);
        }
    }

    void Update()
    {
        if (lever != null)
            lever.localRotation = PowerOn ? leverOnRotation : leverOnRotation * Quaternion.Euler(leverOffRotation);
    }

    public bool CanInteract(Interactor who)
    {
        MissionManager m = MissionManager.Instance;
        if (m != null && m.IsOver) return false;
        return PowerOn || allowRestore;
    }

    public string Prompt => PowerOn ? "cut the power" : "restore the power";
    public float HoldTime => 0f;
    public void HoldTick(Interactor who, float progress) { }
    public void HoldCancelled(Interactor who) { }

    public void Interact(Interactor who)
    {
        bool cut = PowerOn;
        BlackoutDirector.SetPower(!cut);
        GuardAlert.MakeNoise(transform.position, noiseRadius);

        if (switchSound != null)
        {
            if (audioSource == null)
            {
                audioSource = gameObject.AddComponent<AudioSource>();
                audioSource.playOnAwake = false;
                audioSource.spatialBlend = 1f;
            }
            audioSource.PlayOneShot(switchSound);
        }

        string msg = cut ? "Power cut. Guards see 4 m in the dark - [F] flashlight gives you away" : "Power restored";
        if (MissionManager.Instance != null) MissionManager.Instance.ShowToast(msg);
    }
}
