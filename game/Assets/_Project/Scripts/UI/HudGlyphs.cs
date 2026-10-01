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
    public sealed class HudGlyphText
    {
        // Glyph size relative to the line height, and the gap around a glyph (reference pixels).
        const float GlyphHeightShare = 1.0f;
        const float PillWidthShare = 1.7f;
        const float GlyphGap = 3f;

        struct Segment
        {
            public bool IsGlyph;
            public HudGlyph Glyph;
            public string Text;
        }

        readonly Segment[] segments;

        HudGlyphText(string source, Segment[] segments)
        {
            Source = source;
            this.segments = segments;
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
                    parts.Add(new Segment { Text = text.Substring(i) });
                    break;
                }
                if (open > i) parts.Add(new Segment { Text = text.Substring(i, open - i) });
                string name = text.Substring(open + 1, close - open - 1);
                if (HudGlyphs.TryParse(name, out HudGlyph glyph)) parts.Add(new Segment { IsGlyph = true, Glyph = glyph });
                else parts.Add(new Segment { Text = text.Substring(open, close - open + 1) });
                i = close + 1;
            }
            return new HudGlyphText(text, parts.ToArray());
        }

        // Width in screen pixels when drawn at this line height.
        public float Measure(HudPainter painter, float lineHeight, bool gamepad)
        {
            float width = 0f;
            for (int i = 0; i < segments.Length; i++) width += SegmentWidth(painter, segments[i], lineHeight, gamepad);
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
            float x = rect.x;
            float lineHeight = rect.height;
            for (int i = 0; i < segments.Length; i++)
            {
                Segment segment = segments[i];
                float width = SegmentWidth(painter, segment, lineHeight, gamepad);
                if (segment.IsGlyph) DrawGlyph(painter, new Rect(x, rect.y, width, lineHeight), segment.Glyph, gamepad, textColor.a);
                else painter.Text(new Rect(x, rect.y + (lineHeight - painter.Measure(segment.Text, painter.Body).y) * 0.5f, width, lineHeight),
                    segment.Text, painter.Body, textColor);
                x += width;
            }
        }

        float SegmentWidth(HudPainter painter, Segment segment, float lineHeight, bool gamepad)
        {
            if (!segment.IsGlyph) return painter.Measure(segment.Text, painter.Body).x;
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
            painter.Text(new Rect(body.x, body.y + (body.height - labelHeight) * 0.5f, body.width, labelHeight), label, painter.SmallCenter,
                new Color(1f, 1f, 1f, alpha));
        }
    }
}
