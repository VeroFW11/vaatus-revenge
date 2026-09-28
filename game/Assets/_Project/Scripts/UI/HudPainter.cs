using UnityEngine;

namespace VaatusRevenge
{
    // Drawing helpers and cached text styles for the grey-box HUD. The HUD uses Unity's immediate-mode GUI
    // (OnGUI): everything is redrawn from code every frame, with no UI objects in the scene, which makes it
    // quick to build and change while prototyping.
    //
    // Sizes are written in "reference pixels" for a 1080p screen and multiplied by Scale (screen height / 1080),
    // so the HUD keeps its proportions at any resolution. Styles and the one texture are created once and only
    // resized when the screen height changes: creating them every frame would allocate memory every frame.
    // Only call these from OnGUI (Unity's GUI functions don't work anywhere else).
    public sealed class HudPainter
    {
        public const float ReferenceHeight = 1080f;
        const float MinScale = 0.5f;
        const int MinFontSize = 10;
        const int CircleTextureSize = 64;

        // Font sizes in reference pixels.
        const int SmallFont = 16;
        const int BodyFont = 20;
        const int HeadingFont = 30;
        const int HugeFont = 76;
        const int DebugFont = 15;

        readonly GUIContent measureContent = new GUIContent();
        Texture2D circle;
        int builtForHeight = -1;

        public float Scale { get; private set; } = 1f;
        public GUIStyle Small { get; private set; }
        public GUIStyle SmallCenter { get; private set; }
        public GUIStyle SmallRight { get; private set; }
        public GUIStyle Body { get; private set; }
        public GUIStyle BodyCenter { get; private set; }
        public GUIStyle BodyRight { get; private set; }
        public GUIStyle Heading { get; private set; }
        public GUIStyle Huge { get; private set; }
        public GUIStyle Wrapped { get; private set; }

        // Call first thing in OnGUI: creates the styles on first use (GUI.skin only exists inside OnGUI) and
        // resizes them when the screen height changed.
        public void Begin()
        {
            if (Small == null) CreateStyles();
            if (Screen.height != builtForHeight) Resize();
        }

        // Reference pixels -> screen pixels.
        public float U(float referencePixels)
        {
            return referencePixels * Scale;
        }

        public void Fill(Rect rect, Color color)
        {
            GUI.color = color;
            GUI.DrawTexture(rect, Texture2D.whiteTexture);
        }

        // A hollow rectangle drawn just outside rect.
        public void Outline(Rect rect, float thickness, Color color)
        {
            Fill(new Rect(rect.x - thickness, rect.y - thickness, rect.width + thickness * 2f, thickness), color);
            Fill(new Rect(rect.x - thickness, rect.yMax, rect.width + thickness * 2f, thickness), color);
            Fill(new Rect(rect.x - thickness, rect.y, thickness, rect.height), color);
            Fill(new Rect(rect.xMax, rect.y, thickness, rect.height), color);
        }

        // A horizontal bar filled from the left. NaN or out-of-range fills are clamped, never drawn wrong.
        public void Bar(Rect rect, float fill01, Color fill, Color background)
        {
            Fill(rect, background);
            float amount = Clamp01(fill01);
            if (amount > 0f) Fill(new Rect(rect.x, rect.y, rect.width * amount, rect.height), fill);
        }

        // Text with a soft drop shadow, so it stays readable over bright floors and fire effects.
        public void Text(Rect rect, string text, GUIStyle style, Color color)
        {
            if (string.IsNullOrEmpty(text) || style == null) return;
            float offset = Mathf.Max(1f, Mathf.Round(U(1.5f)));
            GUI.color = new Color(0f, 0f, 0f, color.a * 0.8f);
            GUI.Label(new Rect(rect.x + offset, rect.y + offset, rect.width, rect.height), text, style);
            GUI.color = color;
            GUI.Label(rect, text, style);
        }

        // A filled circle (heal charges).
        public void Dot(Rect rect, Color color)
        {
            GUI.color = color;
            GUI.DrawTexture(rect, Circle);
        }

        public Vector2 Measure(string text, GUIStyle style)
        {
            measureContent.text = text;
            return style.CalcSize(measureContent);
        }

        public float MeasureHeight(string text, GUIStyle style, float width)
        {
            measureContent.text = text;
            return style.CalcHeight(measureContent, width);
        }

        public static float Clamp01(float value)
        {
            return value > 0f ? (value < 1f ? value : 1f) : 0f; // also turns NaN into 0
        }

        // Frees the circle texture. Call from the owner's OnDestroy.
        public void Dispose()
        {
            if (circle == null) return;
            GreyboxShapes.SafeDestroy(circle);
            circle = null;
        }

        Texture2D Circle
        {
            get
            {
                if (circle == null) circle = CreateCircleTexture(CircleTextureSize);
                return circle;
            }
        }

        void CreateStyles()
        {
            Small = MakeStyle(TextAnchor.UpperLeft, false, false);
            SmallCenter = MakeStyle(TextAnchor.UpperCenter, false, false);
            SmallRight = MakeStyle(TextAnchor.UpperRight, false, false);
            Body = MakeStyle(TextAnchor.UpperLeft, true, false);
            BodyCenter = MakeStyle(TextAnchor.UpperCenter, true, false);
            BodyRight = MakeStyle(TextAnchor.UpperRight, true, false);
            Heading = MakeStyle(TextAnchor.MiddleCenter, true, false);
            Huge = MakeStyle(TextAnchor.MiddleCenter, false, false);
            // Debug text comes from gameplay code; it wraps inside its panel and is never parsed as rich text.
            Wrapped = MakeStyle(TextAnchor.UpperLeft, false, true);
        }

        void Resize()
        {
            builtForHeight = Screen.height;
            Scale = Mathf.Max(MinScale, Screen.height / ReferenceHeight);
            SetSize(Small, SmallFont);
            SetSize(SmallCenter, SmallFont);
            SetSize(SmallRight, SmallFont);
            SetSize(Body, BodyFont);
            SetSize(BodyCenter, BodyFont);
            SetSize(BodyRight, BodyFont);
            SetSize(Heading, HeadingFont);
            SetSize(Huge, HugeFont);
            SetSize(Wrapped, DebugFont);
        }

        void SetSize(GUIStyle style, int referenceSize)
        {
            style.fontSize = Mathf.Max(MinFontSize, Mathf.RoundToInt(referenceSize * Scale));
        }

        static GUIStyle MakeStyle(TextAnchor anchor, bool bold, bool wrap)
        {
            var style = new GUIStyle(GUI.skin.label)
            {
                alignment = anchor,
                wordWrap = wrap,
                richText = false,
                clipping = TextClipping.Overflow,
                padding = new RectOffset(0, 0, 0, 0),
                margin = new RectOffset(0, 0, 0, 0),
                fontStyle = bold ? FontStyle.Bold : FontStyle.Normal,
            };
            // White text in every state, so GUI.color alone decides the colour of each label.
            style.normal.textColor = Color.white;
            style.hover.textColor = Color.white;
            style.active.textColor = Color.white;
            style.focused.textColor = Color.white;
            return style;
        }

        // A white disc with a soft edge; tinted with GUI.color when drawn.
        static Texture2D CreateCircleTexture(int size)
        {
            var texture = new Texture2D(size, size, TextureFormat.RGBA32, false)
            {
                name = "HudCircle",
                hideFlags = HideFlags.HideAndDontSave,
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear,
            };
            var pixels = new Color32[size * size];
            float radius = size * 0.5f;
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float dx = x + 0.5f - radius;
                    float dy = y + 0.5f - radius;
                    float alpha = Mathf.Clamp01(radius - 1f - Mathf.Sqrt(dx * dx + dy * dy));
                    pixels[y * size + x] = new Color32(255, 255, 255, (byte)Mathf.RoundToInt(alpha * 255f));
                }
            }
            texture.SetPixels32(pixels);
            texture.Apply(false, true);
            return texture;
        }
    }
}
