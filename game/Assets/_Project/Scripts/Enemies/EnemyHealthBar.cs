using UnityEngine;

namespace VaatusRevenge
{
    // A name and health bar floating above an enemy's head, drawn with OnGUI (grey-box UI). Like Elden Ring it
    // only shows when it matters: for a few seconds after the enemy is hit, and while it's the lock-on target.
    // It hides when the enemy dies or is behind the camera.
    //
    // The red part is current health. The pale "chip" behind it is damage just taken: it lingers a moment and
    // then drains away, so you can see how big each hit was. Dummies add their combo readout underneath.
    //
    // Added by the Spawn factories; reads the EnemyFighter (EnemyController or TrainingDummy) on the same object.
    // UI timers use unscaled time, so hitstop and slow motion don't freeze the bar.
    [DisallowMultipleComponent]
    public class EnemyHealthBar : MonoBehaviour
    {
        [Tooltip("Bar size in pixels.")]
        [SerializeField] private Vector2 barSize = new Vector2(110f, 8f);
        [Tooltip("Metres above the top of the head.")]
        [SerializeField] private float headroom = 0.35f;
        [Tooltip("Width in pixels of the text above and below the bar.")]
        [SerializeField] private float labelWidth = 260f;
        [SerializeField] private int nameFontSize = 13;
        [SerializeField] private int statusFontSize = 11;
        [SerializeField] private Color fillColor = new Color(0.8f, 0.14f, 0.1f, 1f);
        [SerializeField] private Color chipColor = new Color(1f, 0.82f, 0.45f, 1f);
        [SerializeField] private Color backColor = new Color(0f, 0f, 0f, 0.65f);
        [SerializeField] private Color textColor = Color.white;
        [SerializeField] private Color textShadowColor = new Color(0f, 0f, 0f, 0.85f);
        [Tooltip("Seconds the damage chip waits before draining.")]
        [SerializeField] private float chipDelay = 0.45f;
        [Tooltip("How much of the bar the chip drains per second.")]
        [SerializeField] private float chipDrainRate = 0.9f;

        readonly GUIContent nameContent = new GUIContent();
        readonly GUIContent statusContent = new GUIContent();
        GUIStyle nameStyle;
        GUIStyle statusStyle;
        EnemyFighter fighter;
        float visibleUntil = float.NegativeInfinity;
        float lastHealth = 1f;
        float chip = 1f;
        float chipHoldUntil;

        // Keeps the bar up for at least this many more (real) seconds.
        public void Show(float seconds)
        {
            if (!(seconds > 0f)) return;
            visibleUntil = Mathf.Max(visibleUntil, Time.unscaledTime + seconds);
            RefreshName();
        }

        // Hides it now and forgets any damage chip (death, reset).
        public void Hide()
        {
            visibleUntil = float.NegativeInfinity;
            EnemyFighter source = Source;
            lastHealth = chip = source != null ? source.Health01 : 1f;
        }

        EnemyFighter Source
        {
            get
            {
                if (fighter == null) fighter = GetComponent<EnemyFighter>();
                return fighter;
            }
        }

        void Awake()
        {
            useGUILayout = false; // we only draw with GUI.*, so skip IMGUI's layout pass (and its garbage) every frame
        }

        void Start()
        {
            RefreshName();
            Hide();
        }

        void Update()
        {
            EnemyFighter source = Source;
            if (source == null) return;
            float health = source.Health01;
            float now = Time.unscaledTime;
            if (health < lastHealth)
            {
                chip = Mathf.Max(chip, lastHealth); // the chunk just lost
                chipHoldUntil = now + chipDelay;
            }
            lastHealth = health;
            if (chip <= health) chip = health;  // healed or refilled: no chip
            else if (now >= chipHoldUntil) chip = Mathf.MoveTowards(chip, health, chipDrainRate * Time.unscaledDeltaTime);
        }

        void OnGUI()
        {
            if (Event.current.type != EventType.Repaint) return;
            EnemyFighter source = Source;
            if (source == null || source.IsDead) return;
            Combatant body = source.Combatant;
            if (body == null) return;
            if (Time.unscaledTime >= visibleUntil && !IsLockOnTarget(body)) return;

            ThirdPersonCameraRig cameraRig = ThirdPersonCameraRig.Instance;
            Camera cam = cameraRig != null ? cameraRig.Camera : null;
            if (cam == null) return;
            Vector3 screen = cam.WorldToScreenPoint(body.Feet + Vector3.up * (body.Height + headroom));
            // Behind the camera, WorldToScreenPoint mirrors the point onto the screen: don't draw a ghost bar.
            if (screen.z <= 0f) return;
            float x = screen.x;
            float y = Screen.height - screen.y; // the GUI's y axis points down; the screen's points up
            if (x < -labelWidth || x > Screen.width + labelWidth || y < -labelWidth || y > Screen.height + labelWidth) return;

            EnsureStyles();
            if (string.IsNullOrEmpty(nameContent.text)) RefreshName();
            float width = Mathf.Max(1f, barSize.x);
            float height = Mathf.Max(1f, barSize.y);
            float left = x - width * 0.5f;
            float health = source.Health01;
            Color oldColor = GUI.color;

            GUI.color = backColor;
            GUI.DrawTexture(new Rect(left - 1f, y - 1f, width + 2f, height + 2f), Texture2D.whiteTexture);
            if (chip > health)
            {
                GUI.color = chipColor;
                GUI.DrawTexture(new Rect(left, y, width * Mathf.Clamp01(chip), height), Texture2D.whiteTexture);
            }
            GUI.color = fillColor;
            GUI.DrawTexture(new Rect(left, y, width * health, height), Texture2D.whiteTexture);

            float nameHeight = nameFontSize + 6f;
            DrawShadowedLabel(new Rect(x - labelWidth * 0.5f, y - nameHeight - 1f, labelWidth, nameHeight), nameContent, nameStyle);
            string status = source.StatusText;
            if (!string.IsNullOrEmpty(status))
            {
                statusContent.text = status; // the dummy caches this string, so this doesn't allocate
                DrawShadowedLabel(new Rect(x - labelWidth * 0.5f, y + height + 2f, labelWidth, statusFontSize + 6f), statusContent, statusStyle);
            }
            GUI.color = oldColor;
        }

        void DrawShadowedLabel(Rect rect, GUIContent content, GUIStyle style)
        {
            // A dark copy one pixel down-right keeps the text readable over bright bloom and pale floors.
            GUI.color = textShadowColor;
            GUI.Label(new Rect(rect.x + 1f, rect.y + 1f, rect.width, rect.height), content, style);
            GUI.color = textColor;
            GUI.Label(rect, content, style);
        }

        static bool IsLockOnTarget(Combatant body)
        {
            LockOnController lockOn = LockOnController.Instance;
            return lockOn != null && lockOn.Target == body;
        }

        void RefreshName()
        {
            EnemyFighter source = Source;
            nameContent.text = source != null ? source.DisplayName : "";
        }

        // GUI.skin is only available inside OnGUI, so the styles are made there, once.
        void EnsureStyles()
        {
            if (nameStyle != null && statusStyle != null) return;
            nameStyle = new GUIStyle(GUI.skin.label)
            {
                alignment = TextAnchor.LowerCenter,
                fontStyle = FontStyle.Bold,
                fontSize = nameFontSize,
                clipping = TextClipping.Overflow,
                wordWrap = false
            };
            nameStyle.normal.textColor = Color.white; // tinted by GUI.color when drawn
            statusStyle = new GUIStyle(nameStyle)
            {
                alignment = TextAnchor.UpperCenter,
                fontStyle = FontStyle.Normal,
                fontSize = statusFontSize
            };
        }
    }
}
