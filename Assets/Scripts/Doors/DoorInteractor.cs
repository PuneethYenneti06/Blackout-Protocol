using UnityEngine;

// Lets the player open and close doors with E.
// Added to the object tagged "Player" automatically when a scene loads.
public class DoorInteractor : MonoBehaviour
{
    public KeyCode key = KeyCode.E;
    public float reach = 2f;

    Door target;
    GUIStyle promptStyle;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Install()
    {
        int doors = Door.SetupAll();
        GameObject player = GameObject.FindWithTag("Player");
        if (player != null && player.GetComponent<DoorInteractor>() == null)
            player.AddComponent<DoorInteractor>();
        Debug.Log($"[Doors] set up {doors} doors, player found: {player != null}");
    }

    void Update()
    {
        target = FindDoor();
        if (target != null && Input.GetKeyDown(key))
            target.Toggle(transform.position);
    }

    Door FindDoor()
    {
        Vector3 eye = transform.position + Vector3.up * 1.2f;
        Door closest = null;
        float closestDist = reach;
        foreach (Collider c in Physics.OverlapSphere(eye, reach, ~0, QueryTriggerInteraction.Ignore))
        {
            Door door = c.GetComponentInParent<Door>();
            if (door == null) continue;
            float d = Vector3.Distance(eye, c.ClosestPoint(eye));
            if (d < closestDist) { closestDist = d; closest = door; }
        }
        return closest;
    }

    public string promptFont = "Segoe UI"; // any font installed on the PC
    public int promptSize = 30;

    GUIStyle shadowStyle;
    Texture2D panel;

    void OnGUI()
    {
        if (target == null) return;
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

        string action = target.IsOpen ? "close" : "open";
        string text = $"Press <color=#FFD23F>[{key}]</color> to {action} door";
        string plain = $"Press [{key}] to {action} door";

        // Dark panel behind the text so it reads on white walls
        Vector2 size = promptStyle.CalcSize(new GUIContent(plain));
        Rect box = new Rect((Screen.width - size.x) * 0.5f - 24f, Screen.height * 0.72f,
                            size.x + 48f, size.y + 16f);
        GUI.DrawTexture(box, panel);

        // Drop shadow, then the text
        Rect shadow = new Rect(box.x + 2f, box.y + 2f, box.width, box.height);
        GUI.Label(shadow, plain, shadowStyle);
        GUI.Label(box, text, promptStyle);
    }
}
