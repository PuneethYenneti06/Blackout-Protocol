using UnityEngine;
using UnityEngine.UI;

// Aim pointer in the middle of the screen, built in code with uGUI (no scene setup needed).
// Four ticks around a dot. The ticks spread out while the player moves or fires and close in
// again when he stands still, and an X-shaped hit marker flashes when a shot hits someone
// (red for a kill, white for a hit). PlayerShooting creates it with Crosshair.Ensure().
public class Crosshair : MonoBehaviour
{
    public static Crosshair Instance { get; private set; }

    [Header("Look")]
    public Color color = new Color(1f, 1f, 1f, 0.9f);
    public Color hitColor = Color.white;
    public Color killColor = new Color(1f, 0.25f, 0.2f);
    public float thickness = 2f;
    public float length = 11f;
    public float gap = 7f;             // distance from the centre when standing still
    public float spreadGap = 14f;      // extra distance at full spread
    public float dotSize = 3f;
    public float settleSpeed = 10f;    // how fast the ticks close in again

    Canvas canvas;
    RectTransform root;
    RectTransform[] ticks = new RectTransform[4];
    Image[] tickImages = new Image[4];
    Image[] markerImages = new Image[4];
    Image dot;
    float spread;        // 0..1, set from outside each frame
    float kick;          // bump from firing, fades
    float currentGap;
    float markerAlpha;
    Color markerColor;
    bool visible = true;

    // Finds or creates the crosshair.
    public static Crosshair Ensure()
    {
        if (Instance != null) return Instance;
        Crosshair existing = FindFirstObjectByType<Crosshair>();
        if (existing != null) return existing;
        GameObject go = new GameObject("Crosshair");
        return go.AddComponent<Crosshair>();
    }

    void Awake()
    {
        Instance = this;
        Build();
        currentGap = gap;
    }

    void OnDestroy()
    {
        if (Instance == this) Instance = null;
        if (canvas != null) Destroy(canvas.gameObject);
    }

    // 0 = standing still and not firing, 1 = full spread. Call every frame.
    public void SetSpread(float amount01)
    {
        spread = Mathf.Clamp01(amount01);
    }

    // Short bump when a shot is fired
    public void Kick(float amount = 0.6f)
    {
        kick = Mathf.Clamp01(kick + amount);
    }

    // Hit marker: white for a hit, red for a kill
    public void Hit(bool kill)
    {
        markerColor = kill ? killColor : hitColor;
        markerAlpha = 1f;
    }

    public void SetVisible(bool show)
    {
        visible = show;
    }

    void LateUpdate()
    {
        float dt = Time.unscaledDeltaTime;
        kick = Mathf.MoveTowards(kick, 0f, dt * 4f);

        bool show = visible && !Cursor.visible;
        MissionManager m = MissionManager.Instance;
        if (m != null && m.IsOver) show = false;
        if (root.gameObject.activeSelf != show) root.gameObject.SetActive(show);
        if (!show) return;

        float wantedGap = gap + spreadGap * Mathf.Clamp01(spread + kick);
        currentGap = Mathf.Lerp(currentGap, wantedGap, 1f - Mathf.Exp(-settleSpeed * dt));

        // Up, down, left, right
        ticks[0].anchoredPosition = new Vector2(0f, currentGap + length * 0.5f);
        ticks[1].anchoredPosition = new Vector2(0f, -(currentGap + length * 0.5f));
        ticks[2].anchoredPosition = new Vector2(-(currentGap + length * 0.5f), 0f);
        ticks[3].anchoredPosition = new Vector2(currentGap + length * 0.5f, 0f);

        if (markerAlpha > 0f)
        {
            markerAlpha = Mathf.Max(0f, markerAlpha - dt * 3f);
            Color c = markerColor;
            c.a *= markerAlpha;
            foreach (Image img in markerImages)
            {
                if (!img.gameObject.activeSelf) img.gameObject.SetActive(true);
                img.color = c;
            }
        }
        else
        {
            foreach (Image img in markerImages)
                if (img.gameObject.activeSelf) img.gameObject.SetActive(false);
        }
    }

    void Build()
    {
        GameObject canvasGo = new GameObject("Crosshair Canvas");
        canvas = canvasGo.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 90;
        // Real pixels, so the thin ticks stay crisp and visible at any window size
        CanvasScaler scaler = canvasGo.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ConstantPixelSize;
        scaler.scaleFactor = 1f;

        GameObject rootGo = new GameObject("Crosshair", typeof(RectTransform));
        root = (RectTransform)rootGo.transform;
        root.SetParent(canvasGo.transform, false);
        root.anchorMin = root.anchorMax = new Vector2(0.5f, 0.5f);
        root.anchoredPosition = Vector2.zero;
        root.sizeDelta = new Vector2(200f, 200f);

        // Ticks
        for (int i = 0; i < 4; i++)
        {
            bool vertical = i < 2;
            tickImages[i] = MakeImage(root, "Tick" + i, color,
                vertical ? new Vector2(thickness, length) : new Vector2(length, thickness));
            ticks[i] = tickImages[i].rectTransform;
        }

        // Dot
        dot = MakeImage(root, "Dot", color, new Vector2(dotSize, dotSize));

        // Hit marker: four short diagonal lines around the centre, hidden until a hit
        for (int i = 0; i < 4; i++)
        {
            Image img = MakeImage(root, "Marker" + i, hitColor, new Vector2(thickness, length * 0.9f));
            float angle = 45f + 90f * i;
            Vector2 dir = new Vector2(Mathf.Cos((angle + 90f) * Mathf.Deg2Rad), Mathf.Sin((angle + 90f) * Mathf.Deg2Rad));
            img.rectTransform.anchoredPosition = dir * (gap + length);
            img.rectTransform.localRotation = Quaternion.Euler(0f, 0f, angle);
            img.gameObject.SetActive(false);
            markerImages[i] = img;
        }
    }

    static Image MakeImage(RectTransform parent, string name, Color c, Vector2 size)
    {
        GameObject go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(parent, false);
        Image img = go.AddComponent<Image>();
        img.color = c;
        img.raycastTarget = false;
        img.rectTransform.anchorMin = img.rectTransform.anchorMax = new Vector2(0.5f, 0.5f);
        img.rectTransform.sizeDelta = size;
        // Dark edge so it reads on bright walls
        Outline o = go.AddComponent<Outline>();
        o.effectColor = new Color(0f, 0f, 0f, 0.8f);
        o.effectDistance = new Vector2(1f, -1f);
        return img;
    }
}
