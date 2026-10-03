using System.Collections.Generic;
using UnityEngine;

namespace VaatusRevenge
{
    // The controller buttons the HUD, the tutorial and the controls overlay draw as pictures.
    public enum HudGlyph { A, B, X, Y, LB, RB, LT, RT, LS, RS, View, Menu, DPadDown }

    // How each button looks: Xbox colours for the face buttons (so "RB + B" shows a red B, and red B is Fire), a grey
    // pill for the shoulders, and the keyboard key for the same action when the player is on the keyboard. One table,
    // so every screen says the same thing.
    public static class HudGlyphs
    {
        public static Color ColorOf(HudGlyph glyph)
        {
            switch (glyph)
            {
                case HudGlyph.A: return new Color(0.42f, 0.78f, 0.20f);
                case HudGlyph.B: return new Color(0.86f, 0.20f, 0.17f);
                case HudGlyph.X: return new Color(0.15f, 0.45f, 0.85f);
                case HudGlyph.Y: return new Color(0.95f, 0.75f, 0.10f);
                default: return new Color(0.35f, 0.35f, 0.38f);
            }
        }

        // Dark text on a light button (yellow Y, green A), white on a dark one: readable labels (verify R2-S10).
        public static Color LabelColorOn(Color button)
        {
            float luminance = 0.2126f * button.r + 0.7152f * button.g + 0.0722f * button.b;
            return luminance > 0.5f ? new Color(0.08f, 0.08f, 0.08f, 1f) : Color.white;
        }

        // Face buttons are round; everything else is a pill (shoulders, sticks, menu buttons) or a key cap.
        public static bool IsRound(HudGlyph glyph)
        {
            return glyph == HudGlyph.A || glyph == HudGlyph.B || glyph == HudGlyph.X || glyph == HudGlyph.Y;
        }

        public static string PadLabel(HudGlyph glyph)
        {
            switch (glyph)
            {
                case HudGlyph.A: return "A";
                case HudGlyph.B: return "B";
                case HudGlyph.X: return "X";
                case HudGlyph.Y: return "Y";
                case HudGlyph.LB: return "LB";
                case HudGlyph.RB: return "RB";
                case HudGlyph.LT: return "LT";
                case HudGlyph.RT: return "RT";
                case HudGlyph.LS: return "LS";
                case HudGlyph.RS: return "RS";
                case HudGlyph.View: return "View";
                case HudGlyph.Menu: return "Menu";
                default: return "D-pad";
            }
        }

        // The keyboard and mouse key for the same action (PlayerInputReader's bindings).
        public static string KeyLabel(HudGlyph glyph)
        {
            switch (glyph)
            {
                case HudGlyph.A: return "Space";
                case HudGlyph.B: return "Shift";
                case HudGlyph.X: return "LMB";
                case HudGlyph.Y: return "F";
                case HudGlyph.LB: return "Q";
                case HudGlyph.RB: return "RMB";
                case HudGlyph.LS: return "V";
                case HudGlyph.RS: return "Tab";
                case HudGlyph.View: return "F7";
                case HudGlyph.Menu: return "Esc";
                case HudGlyph.DPadDown: return "R";
                default: return "-";
            }
        }

        public static string Label(HudGlyph glyph, bool gamepad)
        {
            return gamepad ? PadLabel(glyph) : KeyLabel(glyph);
        }

        // "{RB}" -> HudGlyph.RB. Names are the enum's, case-sensitive.
        public static bool TryParse(string name, out HudGlyph glyph)
        {
            switch (name)
            {
                case "A": glyph = HudGlyph.A; return true;
                case "B": glyph = HudGlyph.B; return true;
                case "X": glyph = HudGlyph.X; return true;
                case "Y": glyph = HudGlyph.Y; return true;
                case "LB": glyph = HudGlyph.LB; return true;
                case "RB": glyph = HudGlyph.RB; return true;
                case "LT": glyph = HudGlyph.LT; return true;
                case "RT": glyph = HudGlyph.RT; return true;
                case "LS": glyph = HudGlyph.LS; return true;
                case "RS": glyph = HudGlyph.RS; return true;
                case "View": glyph = HudGlyph.View; return true;
                case "Menu": glyph = HudGlyph.Menu; return true;
                case "DPadDown": glyph = HudGlyph.DPadDown; return true;
                default: glyph = HudGlyph.A; return false;
            }
        }
    }

    // A line of text with buttons in it: "{RB}+{B} mid-combo" draws an RB pill, "+", a red B and " mid-combo". Parse it
    // once (keep the object) and Draw it every frame from OnGUI: drawing allocates nothing. An unknown {name} is kept
    // as plain text, so a typo shows up on screen instead of vanishing.
    // Text is kept as words (each with the spaces after it) so DrawWrapped can break lines at spaces; a button picture
    // and the text touching it ("{RB}+{B}") stay together on one line (verify R2-04: hints ran off the panel).
    public sealed class HudGlyphText
    {
        // Glyph size relative to the line height, and the gap around a glyph (reference pixels).
        const float GlyphHeightShare = 1.0f;
        const float PillWidthShare = 1.7f;
        const float GlyphGap = 3f;
        // Gap between wrapped lines, as a share of the line height.
        public const float LineSpacingShare = 0.25f;

        struct Segment
        {
            public bool IsGlyph;
            public HudGlyph Glyph;
            public string Text;          // the word with its trailing spaces
            public string Trimmed;       // the word without them (for a line's last word)
            public bool BreakBefore;     // a line may start here (the previous piece ended with a space)
        }

        readonly Segment[] segments;
        readonly float[] widths;         // scratch: each segment's width this layout
        readonly int[] lineStarts;       // scratch: the first segment of each line
        readonly float[] lineWidths;     // scratch: each line's width (without its trailing spaces)

        HudGlyphText(string source, Segment[] segments)
        {
            Source = source;
            this.segments = segments;
            widths = new float[segments.Length];
            lineStarts = new int[segments.Length + 1];
            lineWidths = new float[segments.Length + 1];
        }

        public string Source { get; }

        public static HudGlyphText Parse(string text)
        {
            text = text ?? "";
            var parts = new List<Segment>();
            int i = 0;
            while (i < text.Length)
            {
                int open = text.IndexOf('{', i);
                int close = open >= 0 ? text.IndexOf('}', open + 1) : -1;
                if (open < 0 || close < 0)
                {
                    AddWords(parts, text.Substring(i));
                    break;
                }
                if (open > i) AddWords(parts, text.Substring(i, open - i));
                string name = text.Substring(open + 1, close - open - 1);
                if (HudGlyphs.TryParse(name, out HudGlyph glyph)) parts.Add(new Segment { IsGlyph = true, Glyph = glyph, BreakBefore = EndsWithSpace(parts) });
                else AddWords(parts, text.Substring(open, close - open + 1));
                i = close + 1;
            }
            return new HudGlyphText(text, parts.ToArray());
        }

        static bool EndsWithSpace(List<Segment> parts)
        {
            if (parts.Count == 0) return true;
            Segment last = parts[parts.Count - 1];
            return !last.IsGlyph && last.Text.Length > 0 && last.Text[last.Text.Length - 1] == ' ';
        }

        // Splits plain text into words, each keeping the spaces after it.
        static void AddWords(List<Segment> parts, string text)
        {
            int i = 0;
            while (i < text.Length)
            {
                int end = i;
                while (end < text.Length && text[end] == ' ') end++;            // leading spaces (only at the very start)
                while (end < text.Length && text[end] != ' ') end++;
                int wordEnd = end;
                while (end < text.Length && text[end] == ' ') end++;
                string word = text.Substring(i, end - i);
                parts.Add(new Segment { Text = word, Trimmed = text.Substring(i, wordEnd - i), BreakBefore = EndsWithSpace(parts) });
                i = end;
            }
        }

        // Width in screen pixels when drawn at this line height, on one line.
        public float Measure(HudPainter painter, float lineHeight, bool gamepad)
        {
            float width = 0f;
            for (int i = 0; i < segments.Length; i++) width += SegmentWidth(painter, segments[i], lineHeight, gamepad, false);
            return width;
        }

        // Draws left to right inside rect (vertically centred), white text, buttons in their colours.
        public void Draw(HudPainter painter, Rect rect, bool gamepad)
        {
            Draw(painter, rect, gamepad, Color.white);
        }

        public void Draw(HudPainter painter, Rect rect, bool gamepad, Color textColor)
        {
            if (painter == null) return;
            DrawRange(painter, 0, segments.Length, rect.x, rect.y, rect.height, gamepad, textColor);
        }

        // How many lines the text takes wrapped to maxWidth (at least 1), and the widest line.
        public int Layout(HudPainter painter, float lineHeight, bool gamepad, float maxWidth, out float widest)
        {
            widest = 0f;
            if (painter == null || segments.Length == 0) return 1;
            for (int i = 0; i < segments.Length; i++) widths[i] = SegmentWidth(painter, segments[i], lineHeight, gamepad, false);
            int lines = 0;
            int start = 0;
            while (start < segments.Length)
            {
                // Take whole unbreakable runs (a word, or glyphs and the text touching them) while they fit.
                int end = start;
                float lineWidth = 0f;
                do
                {
                    int runEnd = end + 1;
                    while (runEnd < segments.Length && !segments[runEnd].BreakBefore) runEnd++;
                    float run = 0f;
                    for (int k = end; k < runEnd; k++) run += widths[k];
                    if (end > start && lineWidth + TrailTrim(painter, runEnd - 1, lineHeight, gamepad) + run - widths[runEnd - 1] > maxWidth) break;
                    lineWidth += run;
                    end = runEnd;
                } while (end < segments.Length);
                lineStarts[lines] = start;
                lineWidths[lines] = lineWidth - widths[end - 1] + TrailTrim(painter, end - 1, lineHeight, gamepad);
                widest = Mathf.Max(widest, lineWidths[lines]);
                lines++;
                start = end;
            }
            lineStarts[lines] = segments.Length;
            return lines;
        }

        // Draws the text wrapped to rect.width, each line centred, from rect.y down (rect.height is ignored: size it from
        // Layout's line count). Returns the height used.
        public float DrawWrapped(HudPainter painter, Rect rect, float lineHeight, bool gamepad, Color textColor)
        {
            if (painter == null) return 0f;
            int lines = Layout(painter, lineHeight, gamepad, rect.width, out _);
            float y = rect.y;
            for (int line = 0; line < lines; line++)
            {
                float x = rect.center.x - lineWidths[line] * 0.5f;
                DrawRange(painter, lineStarts[line], lineStarts[line + 1], x, y, lineHeight, gamepad, textColor);
                y += lineHeight * (1f + LineSpacingShare);
            }
            return lines * lineHeight + (lines - 1) * lineHeight * LineSpacingShare;
        }

        // Height of the wrapped text at this width (see DrawWrapped).
        public float WrappedHeight(HudPainter painter, float lineHeight, bool gamepad, float maxWidth)
        {
            int lines = Layout(painter, lineHeight, gamepad, maxWidth, out _);
            return lines * lineHeight + (lines - 1) * lineHeight * LineSpacingShare;
        }

        // The width of segment i when it ends a line (its trailing spaces dropped).
        float TrailTrim(HudPainter painter, int i, float lineHeight, bool gamepad)
        {
            return SegmentWidth(painter, segments[i], lineHeight, gamepad, true);
        }

        void DrawRange(HudPainter painter, int from, int to, float x, float y, float lineHeight, bool gamepad, Color textColor)
        {
            for (int i = from; i < to; i++)
            {
                Segment segment = segments[i];
                bool last = i == to - 1;
                float width = SegmentWidth(painter, segment, lineHeight, gamepad, last);
                string text = last ? segment.Trimmed : segment.Text;
                if (segment.IsGlyph) DrawGlyph(painter, new Rect(x, y, width, lineHeight), segment.Glyph, gamepad, textColor.a);
                else painter.Text(new Rect(x, y + (lineHeight - painter.Measure(text, painter.Body).y) * 0.5f, width + 1f, lineHeight),
                    text, painter.Body, textColor);
                x += width;
            }
        }

        float SegmentWidth(HudPainter painter, Segment segment, float lineHeight, bool gamepad, bool lineEnd)
        {
            if (!segment.IsGlyph) return painter.Measure(lineEnd ? segment.Trimmed : segment.Text, painter.Body).x;
            float size = lineHeight * GlyphHeightShare;
            float gap = painter.U(GlyphGap) * 2f;
            if (gamepad && HudGlyphs.IsRound(segment.Glyph)) return size + gap;
            float labelWidth = painter.Measure(HudGlyphs.Label(segment.Glyph, gamepad), painter.SmallCenter).x + painter.U(10f);
            return Mathf.Max(size * PillWidthShare, labelWidth) + gap;
        }

        static void DrawGlyph(HudPainter painter, Rect slot, HudGlyph glyph, bool gamepad, float alpha)
        {
            float gap = painter.U(GlyphGap);
            var body = new Rect(slot.x + gap, slot.y, slot.width - gap * 2f, slot.height);
            Color color = gamepad ? HudGlyphs.ColorOf(glyph) : HudGlyphs.ColorOf(HudGlyph.LB);
            color.a *= alpha;
            if (gamepad && HudGlyphs.IsRound(glyph)) painter.Dot(body, color);
            else painter.Fill(body, color);
            string label = HudGlyphs.Label(glyph, gamepad);
            float labelHeight = painter.Measure(label, painter.SmallCenter).y;
            Color labelColor = HudGlyphs.LabelColorOn(gamepad ? HudGlyphs.ColorOf(glyph) : HudGlyphs.ColorOf(HudGlyph.LB));
            labelColor.a = alpha;
            painter.Text(new Rect(body.x, body.y + (body.height - labelHeight) * 0.5f, body.width, labelHeight), label, painter.SmallCenter,
                labelColor);
        }
    }
}
