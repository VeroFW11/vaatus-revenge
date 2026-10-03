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
        const int ShapeTextureSize = 128;

        // Font sizes in reference pixels.
        const int SmallFont = 16;
        const int BodyFont = 20;
        const int HeadingFont = 30;
        const int BigFont = 54;
        const int HugeFont = 76;
        const int DebugFont = 15;

        // Ring outlines are drawn from a few pre-made textures, each with a different line thickness (as a share of the
        // radius, see RingShare); Ring picks the closest, so drawing never creates a texture.
        const int RingTextures = 4;

        readonly GUIContent measureContent = new GUIContent();
        Texture2D circle;
        Texture2D diamond;
        Texture2D triangle;
        readonly Texture2D[] rings = new Texture2D[RingTextures];
        int builtForHeight = -1;

        public float Scale { get; private set; } = 1f;
        public GUIStyle Small { get; private set; }
        public GUIStyle SmallCenter { get; private set; }
        public GUIStyle SmallRight { get; private set; }
        public GUIStyle Body { get; private set; }
        public GUIStyle BodyCenter { get; private set; }
        public GUIStyle BodyRight { get; private set; }
        public GUIStyle Heading { get; private set; }
        public GUIStyle Big { get; private set; }        // the combo counter's number (bold, centred)
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

        // A filled circle of radius round center (screen pixels); squash < 1 flattens it into an ellipse.
        public void Disc(Vector2 center, float radius, Color color, float squash = 1f)
        {
            float height = radius * Mathf.Max(0.05f, squash);
            Dot(new Rect(center.x - radius, center.y - height, radius * 2f, height * 2f), color);
        }

        // A circle outline of radius round center, the line about thickness wide (screen pixels). squash < 1 flattens it
        // into an ellipse (a ring lying on the floor seen from the camera).
        public void Ring(Vector2 center, float radius, float thickness, Color color, float squash = 1f)
        {
            if (!(radius > 0f)) return;
            float share = Mathf.Clamp01(thickness / radius);
            int best = 0;
            for (int i = 1; i < RingTextures; i++)
            {
                if (Mathf.Abs(RingShare(i) - share) < Mathf.Abs(RingShare(best) - share)) best = i;
            }
            if (rings[best] == null) rings[best] = CreateRingTexture(ShapeTextureSize, RingShare(best));
            float height = radius * Mathf.Max(0.05f, squash);
            GUI.color = color;
            GUI.DrawTexture(new Rect(center.x - radius, center.y - height, radius * 2f, height * 2f), rings[best]);
        }

        // A filled diamond (a square standing on its corner) filling rect.
        public void Diamond(Rect rect, Color color)
        {
            if (diamond == null) diamond = CreateDiamondTexture(ShapeTextureSize);
            GUI.color = color;
            GUI.DrawTexture(rect, diamond);
        }

        // A filled triangle of the given size centred on center, its tip pointing angleDegrees clockwise from straight up
        // (the danger sense's arrows). Rotates the GUI matrix for the one draw and puts it back.
        public void Triangle(Vector2 center, float size, float angleDegrees, Color color)
        {
            if (triangle == null) triangle = CreateTriangleTexture(ShapeTextureSize);
            Matrix4x4 saved = GUI.matrix;
            GUIUtility.RotateAroundPivot(angleDegrees, center);
            GUI.color = color;
            GUI.DrawTexture(new Rect(center.x - size * 0.5f, center.y - size * 0.5f, size, size), triangle);
            GUI.matrix = saved;
        }

        // Any texture (an effect picture from the VFX library), tinted.
        public void Picture(Rect rect, Texture texture, Color color)
        {
            if (texture == null) return;
            GUI.color = color;
            GUI.DrawTexture(rect, texture, ScaleMode.StretchToFill, true);
        }

        // Draws the next calls scaled by scale round pivot (a number popping); call EndScaled with what this returned.
        public Matrix4x4 BeginScaled(Vector2 pivot, float scale)
        {
            Matrix4x4 saved = GUI.matrix;
            if (scale > 0f && !Mathf.Approximately(scale, 1f)) GUIUtility.ScaleAroundPivot(new Vector2(scale, scale), pivot);
            return saved;
        }

        public void EndScaled(Matrix4x4 saved)
        {
            GUI.matrix = saved;
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

        // Frees the textures made in code. Call from the owner's OnDestroy.
        public void Dispose()
        {
            GreyboxShapes.SafeDestroy(circle);
            GreyboxShapes.SafeDestroy(diamond);
            GreyboxShapes.SafeDestroy(triangle);
            circle = null;
            diamond = null;
            triangle = null;
            for (int i = 0; i < rings.Length; i++)
            {
                GreyboxShapes.SafeDestroy(rings[i]);
                rings[i] = null;
            }
        }

        static float RingShare(int index)
        {
            switch (index)
            {
                case 0: return 0.06f;
                case 1: return 0.12f;
                case 2: return 0.22f;
                default: return 0.4f;
            }
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
            Big = MakeStyle(TextAnchor.MiddleCenter, true, false);
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
            SetSize(Big, BigFont);
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

        // A white ring with soft edges whose line is share x the radius wide.
        static Texture2D CreateRingTexture(int size, float share)
        {
            Texture2D texture = NewShapeTexture("HudRing", size);
            var pixels = new Color32[size * size];
            float outer = size * 0.5f - 1f;
            float inner = outer * (1f - share);
            float half = size * 0.5f;
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float d = Mathf.Sqrt((x + 0.5f - half) * (x + 0.5f - half) + (y + 0.5f - half) * (y + 0.5f - half));
                    float alpha = Mathf.Clamp01(outer - d) * Mathf.Clamp01(d - inner);
                    pixels[y * size + x] = new Color32(255, 255, 255, (byte)Mathf.RoundToInt(alpha * 255f));
                }
            }
            return Finish(texture, pixels);
        }

        // A white diamond touching the middle of each edge, with soft edges.
        static Texture2D CreateDiamondTexture(int size)
        {
            Texture2D texture = NewShapeTexture("HudDiamond", size);
            var pixels = new Color32[size * size];
            float half = size * 0.5f;
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float d = Mathf.Abs(x + 0.5f - half) + Mathf.Abs(y + 0.5f - half);
                    float alpha = Mathf.Clamp01((half - 1f - d) * 0.75f);
                    pixels[y * size + x] = new Color32(255, 255, 255, (byte)Mathf.RoundToInt(alpha * 255f));
                }
            }
            return Finish(texture, pixels);
        }

        // A white triangle pointing up (GUI space: towards the top of the screen), with soft edges.
        static Texture2D CreateTriangleTexture(int size)
        {
            Texture2D texture = NewShapeTexture("HudTriangle", size);
            var pixels = new Color32[size * size];
            float half = size * 0.5f;
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    // Texture rows run bottom-up; GUI draws them top-down, so row size-1 is the top of the screen.
                    float up = (y + 0.5f) / size;                    // 1 at the top edge
                    float halfWidth = (1f - up) * (half - 1f);       // full width at the bottom, a point at the top
                    float edge = halfWidth - Mathf.Abs(x + 0.5f - half);
                    float alpha = Mathf.Clamp01(edge) * Mathf.Clamp01(up * size - 1f) * Mathf.Clamp01((1f - up) * size - 1f);
                    pixels[y * size + x] = new Color32(255, 255, 255, (byte)Mathf.RoundToInt(alpha * 255f));
                }
            }
            return Finish(texture, pixels);
        }

        static Texture2D NewShapeTexture(string name, int size)
        {
            return new Texture2D(size, size, TextureFormat.RGBA32, false)
            {
                name = name,
                hideFlags = HideFlags.HideAndDontSave,
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear,
            };
        }

        static Texture2D Finish(Texture2D texture, Color32[] pixels)
        {
            texture.SetPixels32(pixels);
            texture.Apply(false, true);
            return texture;
        }
    }
}
