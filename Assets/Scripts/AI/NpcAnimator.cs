using UnityEngine;
using UnityEngine.AI;
using UnityEngine.Animations;
using UnityEngine.Playables;

// Plays NPC clips from code with the Playables API (no Animator Controller needed).
// Crossfades idle / walk / run by movement speed, and has captive and dead states.
// Every clip is optional: a missing clip falls back to a similar one (run -> walk -> idle,
// captive -> idle). With nothing to play, the graph is switched off and the model keeps its
// rest pose (or whatever its own Animator does).
// Guards' arms and head are still posed by TerroristGuard.LateUpdate on top of this.
public class NpcAnimator : MonoBehaviour
{
    public enum Mode { Locomotion, Captive, Dead }

    [Header("Clips (all optional)")]
    public AnimationClip idleClip;
    public AnimationClip walkClip;
    public AnimationClip runClip;
    public AnimationClip captiveClip;    // e.g. Hostage_Scared@Praying
    public AnimationClip deathClip;      // e.g. character@Death

    [Header("Blending")]
    public float walkSpeed = 0.2f;       // m/s above this counts as walking
    public float runSpeed = 3f;          // m/s above this counts as running
    public float fadeTime = 0.25f;
    public bool scaleClipSpeed = true;   // speed walk/run clips up or down to match movement
    public float walkClipSpeed = 1.5f;   // m/s the walk clip was made for
    public float runClipSpeed = 4.5f;    // m/s the run clip was made for

    [Header("Refs (auto-found if empty)")]
    public Animator animator;
    public NavMeshAgent agent;

    public Mode CurrentMode { get; private set; } = Mode.Locomotion;
    public bool HasDeathClip => deathClip != null;

    const int Idle = 0, Walk = 1, Run = 2, Captive = 3, Death = 4, Count = 5;

    PlayableGraph graph;
    AnimationMixerPlayable mixer;
    readonly AnimationClipPlayable[] playables = new AnimationClipPlayable[Count];
    readonly bool[] has = new bool[Count];
    readonly float[] weights = new float[Count];
    AnimationPlayableOutput output;
    bool built;
    Vector3 lastPosition;
    float measuredSpeed;

    void Awake()
    {
        if (animator == null) animator = GetComponentInChildren<Animator>();
        if (agent == null) agent = GetComponent<NavMeshAgent>();
        lastPosition = transform.position;
    }

    void OnEnable()
    {
        Build();
    }

    void OnDisable()
    {
        Teardown();
    }

    // Call after changing clips at runtime
    public void Rebuild()
    {
        Teardown();
        Build();
    }

    public void SetCaptive(bool captive)
    {
        if (CurrentMode == Mode.Dead) return;
        CurrentMode = captive ? Mode.Captive : Mode.Locomotion;
    }

    // Returns false if there is no death clip (caller can fall back to something else).
    public bool PlayDeath()
    {
        CurrentMode = Mode.Dead;
        if (!built || !has[Death]) return false;
        playables[Death].SetTime(0);
        playables[Death].Play();
        // Snap to the death clip quickly
        for (int i = 0; i < Count; i++) weights[i] = i == Death ? 0.6f : weights[i] * 0.4f;
        return true;
    }

    void Build()
    {
        if (built || animator == null) return;

        // Guards have no idle clip. With nothing playing, a humanoid Animator falls back to its
        // default pose, which puts the hips at the object's origin: the body sinks into the floor
        // up to the waist. So a standing guard holds the first frame of the death clip instead
        // (every death clip starts from a normal standing pose), paused.
        AnimationClip idleSource = idleClip;
        bool stillIdle = false;
        if (idleSource == null && captiveClip == null && deathClip != null)
        {
            idleSource = deathClip;
            stillIdle = true;
        }

        AnimationClip[] clips = { idleSource, walkClip, runClip, captiveClip, deathClip };
        bool any = false;
        foreach (AnimationClip c in clips) if (c != null) any = true;
        if (!any) return;

        graph = PlayableGraph.Create(name + " NpcAnimator");
        graph.SetTimeUpdateMode(DirectorUpdateMode.GameTime);
        mixer = AnimationMixerPlayable.Create(graph, Count);
        for (int i = 0; i < Count; i++)
        {
            has[i] = clips[i] != null;
            weights[i] = 0f;
            if (!has[i]) continue;
            playables[i] = AnimationClipPlayable.Create(graph, clips[i]);
            playables[i].SetApplyFootIK(false);
            graph.Connect(playables[i], 0, mixer, i);
            mixer.SetInputWeight(i, 0f);
        }
        if (stillIdle)
        {
            playables[Idle].SetTime(0);
            playables[Idle].Pause();
        }

        animator.applyRootMotion = false;
        output = AnimationPlayableOutput.Create(graph, "NpcAnimation", animator);
        output.SetSourcePlayable(mixer);
        graph.Play();
        built = true;
    }

    void Teardown()
    {
        if (built && graph.IsValid()) graph.Destroy();
        built = false;
    }

    void Update()
    {
        float dt = Time.deltaTime;
        float speed;
        if (agent != null && agent.enabled && agent.isOnNavMesh)
        {
            speed = agent.velocity.magnitude;
        }
        else
        {
            Vector3 d = transform.position - lastPosition;
            d.y = 0f;
            speed = dt > 0f ? d.magnitude / dt : 0f;
        }
        lastPosition = transform.position;
        measuredSpeed = Mathf.Lerp(measuredSpeed, speed, Mathf.Clamp01(dt * 10f));

        if (!built) return;

        int wanted = Pick(measuredSpeed);
        float step = dt / Mathf.Max(fadeTime, 0.01f);
        float total = 0f;
        for (int i = 0; i < Count; i++)
        {
            weights[i] = Mathf.MoveTowards(weights[i], i == wanted ? 1f : 0f, step);
            total += weights[i];
        }
        for (int i = 0; i < Count; i++)
            if (has[i]) mixer.SetInputWeight(i, total > 0f ? weights[i] / total : 0f);

        // Nothing to play: hand the model back to its own Animator / rest pose
        output.SetWeight(wanted < 0 && total <= 0.001f ? 0f : 1f);

        if (scaleClipSpeed)
        {
            if (has[Walk]) playables[Walk].SetSpeed(Mathf.Clamp(measuredSpeed / Mathf.Max(walkClipSpeed, 0.1f), 0.5f, 2f));
            if (has[Run]) playables[Run].SetSpeed(Mathf.Clamp(measuredSpeed / Mathf.Max(runClipSpeed, 0.1f), 0.6f, 1.6f));
        }

        // Death plays once and holds the last frame
        if (has[Death] && CurrentMode == Mode.Dead)
        {
            double len = deathClip.length;
            if (playables[Death].GetTime() >= len)
            {
                playables[Death].SetTime(len);
                playables[Death].Pause();
            }
        }
    }

    // Which slot to play, or -1 for none
    int Pick(float speed)
    {
        switch (CurrentMode)
        {
            case Mode.Dead:
                return has[Death] ? Death : -1;
            case Mode.Captive:
                if (has[Captive]) return Captive;
                return has[Idle] ? Idle : -1;
        }

        if (speed > runSpeed)
        {
            if (has[Run]) return Run;
            if (has[Walk]) return Walk;
        }
        else if (speed > walkSpeed)
        {
            if (has[Walk]) return Walk;
            if (has[Run]) return Run;
        }
        if (has[Idle]) return Idle;
        // Freed hostages have no idle clip of their own: the praying clip is their idle too
        if (has[Captive]) return Captive;
        return -1;
    }
}
