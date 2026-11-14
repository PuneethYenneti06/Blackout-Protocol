using UnityEngine;

// Plays the background track (Assets/Resources/Sounds/music_scary) on a loop, quietly enough to
// sit under the gunfire and footsteps. Starts by itself when the game starts and keeps playing
// across restarts without starting over.
public class BackgroundMusic : MonoBehaviour
{
    public static float volume = 0.28f;   // 0..1
    public static float fadeInTime = 3f;

    static BackgroundMusic instance;
    AudioSource source;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Install()
    {
        if (instance != null) return;
        AudioClip clip = Resources.Load<AudioClip>("Sounds/music_scary");
        if (clip == null)
        {
            Debug.LogWarning("[BackgroundMusic] Assets/Resources/Sounds/music_scary not found.");
            return;
        }

        GameObject go = new GameObject("BackgroundMusic");
        DontDestroyOnLoad(go);
        instance = go.AddComponent<BackgroundMusic>();
        instance.source = go.AddComponent<AudioSource>();
        instance.source.clip = clip;
        instance.source.loop = true;
        instance.source.spatialBlend = 0f;   // not from a place in the level
        instance.source.priority = 0;        // never cut off by other sounds
        instance.source.ignoreListenerPause = true;
        instance.source.volume = 0f;
        instance.source.Play();
    }

    void Update()
    {
        // Fade in at the start; unscaled time so it also works on the end screen (time frozen)
        source.volume = Mathf.MoveTowards(source.volume, volume, Time.unscaledDeltaTime * volume / Mathf.Max(fadeInTime, 0.01f));
    }
}
