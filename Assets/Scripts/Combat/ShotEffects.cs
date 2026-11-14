using System.Collections.Generic;
using UnityEngine;

// Visual feedback for gunfire, built in code so no prefabs or Inspector setup are needed:
//  - Tracer: a thin bright line from the muzzle to where the shot landed, gone in a few frames
//  - Impact: a burst of sparks and a little dust where a shot hits a wall / floor,
//            or a dark red puff when it hits a person
//  - MuzzleFlash: a short orange light plus a few hot particles at the muzzle
// Everything lives on one hidden "Shot Effects" object that is created on first use.
public static class ShotEffects
{
    static ShotEffectsRunner runner;

    static ShotEffectsRunner Runner
    {
        get
        {
            if (runner == null)
            {
                GameObject go = new GameObject("Shot Effects");
                go.hideFlags = HideFlags.DontSave;
                runner = go.AddComponent<ShotEffectsRunner>();
            }
            return runner;
        }
    }

    public static void Tracer(Vector3 from, Vector3 to, Color color, float life = 0.07f, float width = 0.025f)
    {
        Runner.ShowTracer(from, to, color, life, width);
    }

    public static void Impact(Vector3 point, Vector3 normal, bool flesh)
    {
        Runner.ShowImpact(point, normal, flesh);
    }

    public static void MuzzleFlash(Vector3 position, Vector3 direction)
    {
        Runner.ShowMuzzleFlash(position, direction);
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics()
    {
        runner = null;
    }
}

public class ShotEffectsRunner : MonoBehaviour
{
    class Tracer { public LineRenderer line; public float life; public float maxLife; public Color color; }

    readonly List<Tracer> tracers = new List<Tracer>();
    ParticleSystem sparks, dust, blood, flashParticles;
    Light flashLight;
    float flashLeft;
    Material lineMaterial;

    void Awake()
    {
        lineMaterial = MakeTransparentMaterial(true);

        sparks = MakeSystem("Sparks", new Color(1f, 0.85f, 0.4f), new Color(1f, 0.45f, 0.1f, 0f), 0.02f, 0.05f, 2.5f, true);
        dust = MakeSystem("Dust", new Color(0.6f, 0.58f, 0.55f, 0.6f), new Color(0.6f, 0.58f, 0.55f, 0f), 0.08f, 0.2f, 0.2f, false);
        blood = MakeSystem("Blood", new Color(0.55f, 0.03f, 0.03f), new Color(0.3f, 0.02f, 0.02f, 0f), 0.04f, 0.1f, 1.5f, false);
        flashParticles = MakeSystem("Flash", new Color(1f, 0.9f, 0.5f), new Color(1f, 0.5f, 0.1f, 0f), 0.04f, 0.12f, 0f, true);

        GameObject l = new GameObject("MuzzleLight");
        l.transform.SetParent(transform, false);
        flashLight = l.AddComponent<Light>();
        flashLight.type = LightType.Point;
        flashLight.color = new Color(1f, 0.75f, 0.4f);
        flashLight.intensity = 6f;
        flashLight.range = 6f;
        flashLight.shadows = LightShadows.None;
        flashLight.enabled = false;
    }

    ParticleSystem MakeSystem(string name, Color start, Color end, float sizeMin, float sizeMax, float gravity, bool additive)
    {
        GameObject go = new GameObject(name);
        go.transform.SetParent(transform, false);
        ParticleSystem ps = go.AddComponent<ParticleSystem>();

        // Looping with emission off: the system stays "playing" so Emit() particles are simulated
        ParticleSystem.MainModule main = ps.main;
        main.loop = true;
        main.playOnAwake = true;
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.startLifetime = new ParticleSystem.MinMaxCurve(0.15f, 0.45f);
        main.startSize = new ParticleSystem.MinMaxCurve(sizeMin, sizeMax);
        main.startSpeed = 0f;
        main.gravityModifier = gravity;
        main.maxParticles = 500;
        main.startColor = start;

        ParticleSystem.EmissionModule emission = ps.emission;
        emission.enabled = false;

        ParticleSystem.ShapeModule shape = ps.shape;
        shape.enabled = false;

        ParticleSystem.ColorOverLifetimeModule col = ps.colorOverLifetime;
        col.enabled = true;
        Gradient g = new Gradient();
        g.SetKeys(
            new[] { new GradientColorKey(start, 0f), new GradientColorKey(end, 1f) },
            new[] { new GradientAlphaKey(start.a, 0f), new GradientAlphaKey(end.a, 1f) });
        col.color = g;

        ParticleSystem.SizeOverLifetimeModule size = ps.sizeOverLifetime;
        size.enabled = true;
        size.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.EaseInOut(0f, 1f, 1f, 0.2f));

        ParticleSystemRenderer r = ps.GetComponent<ParticleSystemRenderer>();
        Material mat = MakeTransparentMaterial(additive);
        if (mat != null) r.material = mat;
        r.renderMode = additive ? ParticleSystemRenderMode.Stretch : ParticleSystemRenderMode.Billboard;
        if (additive)
        {
            r.velocityScale = 0.02f;
            r.lengthScale = 1.5f;
        }
        r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        r.receiveShadows = false;
        ps.Play();
        return ps;
    }

    // A material that shows vertex colours (particles, line renderers) and blends: additive for
    // glowing things (sparks, tracers, flash), normal alpha for dust and blood.
    // The URP shader's blend state is set by hand, because setting _Surface/_Blend alone only
    // works through the Inspector.
    static Material MakeTransparentMaterial(bool additive)
    {
        Shader shader = Shader.Find("Universal Render Pipeline/Particles/Unlit");
        if (shader == null) shader = Shader.Find("Particles/Standard Unlit");
        if (shader == null) shader = Shader.Find("Sprites/Default");
        if (shader == null) return null;

        Material m = new Material(shader);
        if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", Color.white);
        if (m.HasProperty("_Color")) m.SetColor("_Color", Color.white);
        if (m.HasProperty("_Surface")) m.SetFloat("_Surface", 1f);
        if (m.HasProperty("_Blend")) m.SetFloat("_Blend", additive ? 2f : 0f);
        if (m.HasProperty("_SrcBlend")) m.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.SrcAlpha);
        if (m.HasProperty("_DstBlend")) m.SetFloat("_DstBlend", (float)(additive ? UnityEngine.Rendering.BlendMode.One : UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha));
        if (m.HasProperty("_ZWrite")) m.SetFloat("_ZWrite", 0f);
        if (m.HasProperty("_Cull")) m.SetFloat("_Cull", (float)UnityEngine.Rendering.CullMode.Off);
        m.SetOverrideTag("RenderType", "Transparent");
        m.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
        m.DisableKeyword("_ALPHATEST_ON");
        m.DisableKeyword("_ALPHAPREMULTIPLY_ON");
        m.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
        return m;
    }

    static void Burst(ParticleSystem ps, int count, Vector3 point, Vector3 dir, float coneDeg, float speedMin, float speedMax)
    {
        if (ps == null) return;
        if (dir.sqrMagnitude < 0.0001f) dir = Vector3.up;
        dir.Normalize();
        ParticleSystem.EmitParams ep = new ParticleSystem.EmitParams();
        for (int i = 0; i < count; i++)
        {
            Vector3 v = Vector3.Slerp(dir, Random.onUnitSphere, Random.value * coneDeg / 180f);
            if (Vector3.Dot(v, dir) < 0f) v = Vector3.Reflect(v, dir);
            ep.position = point;
            ep.velocity = v * Random.Range(speedMin, speedMax);
            ps.Emit(ep, 1);
        }
    }

    public void ShowImpact(Vector3 point, Vector3 normal, bool flesh)
    {
        if (flesh)
        {
            Burst(blood, 14, point, normal, 70f, 0.8f, 2.5f);
        }
        else
        {
            Burst(sparks, 12, point + normal * 0.01f, normal, 60f, 3f, 7f);
            Burst(dust, 6, point + normal * 0.02f, normal, 80f, 0.3f, 1.2f);
        }
    }

    public void ShowMuzzleFlash(Vector3 position, Vector3 direction)
    {
        Burst(flashParticles, 6, position, direction, 25f, 2f, 5f);
        flashLight.transform.position = position + direction.normalized * 0.1f;
        flashLight.enabled = true;
        flashLeft = 0.05f;
    }

    public void ShowTracer(Vector3 from, Vector3 to, Color color, float life, float width)
    {
        if (lineMaterial == null) return;

        Tracer t = null;
        foreach (Tracer existing in tracers)
            if (existing.life <= 0f) { t = existing; break; }

        if (t == null)
        {
            GameObject go = new GameObject("Tracer");
            go.transform.SetParent(transform, false);
            LineRenderer lr = go.AddComponent<LineRenderer>();
            lr.material = lineMaterial;
            lr.useWorldSpace = true;
            lr.positionCount = 2;
            lr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            lr.receiveShadows = false;
            lr.numCapVertices = 2;
            t = new Tracer { line = lr };
            tracers.Add(t);
        }

        t.line.startWidth = width;
        t.line.endWidth = width * 0.6f;
        t.line.SetPosition(0, from);
        t.line.SetPosition(1, to);
        t.color = color;
        t.line.startColor = color;
        t.line.endColor = color;
        t.line.enabled = true;
        t.life = life;
        t.maxLife = life;
    }

    void Update()
    {
        float dt = Time.deltaTime;

        for (int i = 0; i < tracers.Count; i++)
        {
            Tracer t = tracers[i];
            if (t.life <= 0f) continue;
            t.life -= dt;
            if (t.life <= 0f)
            {
                t.line.enabled = false;
                continue;
            }
            Color c = t.color;
            c.a *= Mathf.Clamp01(t.life / t.maxLife);
            t.line.startColor = c;
            t.line.endColor = c;
        }

        if (flashLeft > 0f)
        {
            flashLeft -= dt;
            if (flashLeft <= 0f) flashLight.enabled = false;
        }
    }
}
