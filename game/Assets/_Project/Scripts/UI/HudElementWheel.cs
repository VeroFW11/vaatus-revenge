using System.Collections.Generic;
using UnityEngine;
using VaatusRevenge.Core;

namespace VaatusRevenge
{
    // The element wheel, bottom right: four diamonds laid out like the pad's face buttons (Y top, B right, A bottom,
    // X left), each in its element's colour with the button that picks it in the button's own colour. Since the layout is
    // colour-matched (B red = Fire, X blue = Water, A green = Earth, Y yellow = Air), the button and the element agree at a
    // glance. On the keyboard each diamond shows its number key instead.
    //   the element in hand is lit and outlined; the others are dimmer; one not learned yet is grey
    //   holding RB grows the wheel (x1.25) and shows "RB +": a face button now switches
    //   a switch flashes the new diamond white, and a ring of dots round it runs down the switch cooldown
    //   picking an element you haven't learned flashes its diamond red
    //   a pick that didn't switch (still cooling down, or the element already in hand) shakes the wheel; a cooldown
    //   denial also lights the cooldown dots amber, so a switch that "didn't happen" always says why (verify J-04/J-05)
    // The button is a small badge on each diamond's outer corner, so the diamond's own state (dim, grey, flashes) stays
    // visible under it. Real time throughout; allocation-free.
    public sealed class HudElementWheel
    {
        const float HeldScale = 1.25f;
        const float FlashTime = 0.25f;
        const float DeniedTime = 0.35f;
        const int CooldownDots = 16;
        const float DimShare = 0.55f;
        const float ShakeTime = 0.3f;
        const float ShakePixels = 6f;
        const float ShakeCycles = 4f;
        const float GrowAfterHold = 0.1f;   // RB grows the wheel once held this long (a quick tap doesn't pulse it)

        static readonly Color NotLearnedColor = new Color(0.4f, 0.4f, 0.42f, 0.45f);
        static readonly Color OutlineColor = new Color(1f, 1f, 1f, 0.95f);
        static readonly Color DeniedColor = new Color(0.95f, 0.15f, 0.1f, 1f);
        static readonly Color ShadowColor = new Color(0f, 0f, 0f, 0.45f);
        static readonly Color CooldownColor = new Color(1f, 1f, 1f, 0.85f);
        static readonly Color CooldownDeniedColor = new Color(1f, 0.7f, 0.15f, 1f);

        readonly string[] keyLabels = { "1", "2", "3", "4" };
        float flashStart = -10f;
        float deniedStart = -10f;
        ElementId deniedElement = ElementId.None;
        float shakeStart = -10f;
        bool shakeForCooldown;
        float heldSince = -10f;
        bool wasHeld;

        public void OnEvent(in PlayerEvent e, float now)
        {
            switch (e.Type)
            {
                case PlayerEventType.ElementSwitched:
                    flashStart = now;
                    break;
                case PlayerEventType.ElementSwitchDenied:
                    if (e.DenyReason != SwitchDeniedReason.NotLearned)
                    {
                        shakeStart = now;
                        shakeForCooldown = e.DenyReason == SwitchDeniedReason.Cooldown;
                        heldSince = Mathf.Min(heldSince, now - GrowAfterHold);   // a face button was pressed: show the wheel
                        break;
                    }
                    deniedStart = now;
                    deniedElement = e.Element;
                    heldSince = Mathf.Min(heldSince, now - GrowAfterHold);
                    break;
            }
        }

        // center: the wheel's centre (screen pixels).
        public void Draw(HudPainter p, PlayerCombatModel model, PlayerInputReader reader, Vector2 center, float now)
        {
            bool gamepad = reader == null || reader.UsingGamepad;
            bool held = reader != null && reader.ElementModifierHeld;
            if (held && !wasHeld) heldSince = now;
            wasHeld = held;
            float scale = held && now - heldSince >= GrowAfterHold ? HeldScale : 1f;
            float sinceShake = now - shakeStart;
            bool shaking = sinceShake >= 0f && sinceShake < ShakeTime;
            if (shaking)
            {
                float fade = 1f - sinceShake / ShakeTime;
                center.x += Mathf.Sin(sinceShake / ShakeTime * ShakeCycles * Mathf.PI * 2f) * p.U(ShakePixels) * fade;
            }
            float spacing = p.U(46f) * scale;
            float size = p.U(50f) * scale;
            ElementId active = model.ActiveElement;
            IReadOnlyList<ElementId> slots = reader != null ? reader.ElementSlots : null;
            ElementButtonLayout layout = reader != null ? reader.ElementLayout : null;

            // A soft shadow behind the wheel keeps it readable over the arena.
            p.Disc(center, spacing + size * 0.7f, ShadowColor);

            for (int slot = 0; slot < ElementButtonLayout.SlotCount; slot++)
            {
                ElementId element = slots != null && slot < slots.Count ? slots[slot] : DefaultSlot(slot);
                Vector2 at = center + SlotOffset(slot) * spacing;
                bool isActive = element == active;
                bool learned = model.IsLearned(element);
                float diamondSize = isActive ? size * 1.12f : size;
                Rect rect = Centered(at, diamondSize);
                if (isActive) p.Diamond(Centered(at, diamondSize + p.U(8f) * scale), OutlineColor);
                Color fill = learned ? ElementVfx.HudColor(element) : NotLearnedColor;
                if (learned && !isActive) fill.a *= DimShare;
                p.Diamond(rect, fill);

                // The button: a badge on the diamond's outer corner (dim when not learned).
                Vector2 badge = at + SlotOffset(slot) * (diamondSize * 0.42f);
                DrawButton(p, badge, size * 0.24f, slot, element, gamepad, layout, isActive ? 1f : learned ? 0.8f : 0.45f);

                float sinceFlash = now - flashStart;
                if (isActive && sinceFlash >= 0f && sinceFlash < FlashTime)
                    p.Diamond(rect, new Color(1f, 1f, 1f, 1f - sinceFlash / FlashTime));
                float sinceDenied = now - deniedStart;
                if (element == deniedElement && sinceDenied >= 0f && sinceDenied < DeniedTime)
                    p.Diamond(rect, WithAlpha(DeniedColor, 0.8f * (1f - sinceDenied / DeniedTime)));

                if (isActive)
                {
                    bool cooldownDenied = shaking && shakeForCooldown;
                    DrawCooldown(p, at, diamondSize * 0.72f, model.SwitchCooldown01, cooldownDenied ? CooldownDeniedColor : CooldownColor,
                        cooldownDenied ? 1.6f : 1f);
                }
            }

            // "RB +": a face button picks an element now. Shown once RB has been held GrowAfterHold (like the grow), so the
            // pill doesn't flicker on every ranged-skill tap (J6-S08 / V6-04).
            if (held && gamepad && now - heldSince >= GrowAfterHold)
            {
                float pillHeight = p.U(26f);
                var pill = new Rect(center.x - spacing - size - p.U(78f), center.y - pillHeight * 0.5f, p.U(44f), pillHeight);
                p.Fill(pill, HudGlyphs.ColorOf(HudGlyph.RB));
                p.Text(new Rect(pill.x, pill.y + (pillHeight - p.Small.fontSize * 1.2f) * 0.5f, pill.width, pillHeight), HudGlyphs.PadLabel(HudGlyph.RB),
                    p.SmallCenter, Color.white);
                p.Text(new Rect(pill.xMax + p.U(4f), pill.y - p.U(2f), p.U(24f), pillHeight), "+", p.Body, Color.white);
            }

            // The element in hand, by name (data), under the wheel.
            ElementMoveSet set = model.MoveSet;
            string name = set != null ? set.DisplayName : null;
            if (!string.IsNullOrEmpty(name))
            {
                p.Text(new Rect(center.x - p.U(150f), center.y + spacing + size * 0.65f, p.U(300f), p.U(24f)), name, p.BodyCenter,
                    ElementVfx.HudColor(active));
            }
        }

        // The button that picks this slot: the face button in its pad colour, or the number key.
        void DrawButton(HudPainter p, Vector2 at, float radius, int slot, ElementId element, bool gamepad, ElementButtonLayout layout, float alpha)
        {
            string label;
            Color color;
            if (gamepad)
            {
                HudGlyph glyph = SlotGlyph(slot);
                label = HudGlyphs.PadLabel(glyph);
                color = HudGlyphs.ColorOf(glyph);
            }
            else
            {
                label = KeyLabel(element, layout);
                color = HudGlyphs.ColorOf(HudGlyph.LB);
            }
            color.a = alpha;
            p.Disc(at, radius, new Color(0f, 0f, 0f, 0.5f * alpha));
            p.Disc(at, radius * 0.86f, color);
            float textHeight = p.Small.fontSize * 1.2f;
            // Dark text on a light button (Y's yellow): white on yellow is barely readable.
            float luminance = 0.2126f * color.r + 0.7152f * color.g + 0.0722f * color.b;
            Color text = luminance > 0.6f ? new Color(0.08f, 0.08f, 0.1f, alpha) : new Color(1f, 1f, 1f, alpha);
            p.Text(new Rect(at.x - radius, at.y - textHeight * 0.5f, radius * 2f, textHeight), label, p.SmallCenter, text);
        }

        // Dots round the diamond, clockwise from the top, for the share of the switch cooldown still to run.
        static void DrawCooldown(HudPainter p, Vector2 at, float radius, float remaining01, Color color, float dotScale)
        {
            if (!(remaining01 > 0f)) return;
            int lit = Mathf.CeilToInt(remaining01 * CooldownDots);
            float dot = Mathf.Max(2f, radius * 0.12f) * dotScale;
            for (int i = 0; i < lit; i++)
            {
                float angle = i * Mathf.PI * 2f / CooldownDots;
                var position = new Vector2(at.x + Mathf.Sin(angle) * radius, at.y - Mathf.Cos(angle) * radius);
                p.Disc(position, dot, color);
            }
        }

        string KeyLabel(ElementId element, ElementButtonLayout layout)
        {
            for (int i = 0; i < keyLabels.Length; i++)
            {
                ElementId key = layout != null ? layout.KeySlot(i) : (ElementId)(i + 1);
                if (key == element) return keyLabels[i];
            }
            return "-";
        }

        // Face-button slots in the reader's order: 0 Up (Y), 1 Right (B), 2 Down (A), 3 Left (X).
        static Vector2 SlotOffset(int slot)
        {
            switch (slot)
            {
                case 0: return new Vector2(0f, -1f);
                case 1: return new Vector2(1f, 0f);
                case 2: return new Vector2(0f, 1f);
                default: return new Vector2(-1f, 0f);
            }
        }

        static HudGlyph SlotGlyph(int slot)
        {
            switch (slot)
            {
                case 0: return HudGlyph.Y;
                case 1: return HudGlyph.B;
                case 2: return HudGlyph.A;
                default: return HudGlyph.X;
            }
        }

        // The colour-matched layout (spec 8.1) when there is no input reader to ask.
        static ElementId DefaultSlot(int slot)
        {
            switch (slot)
            {
                case 0: return ElementId.Air;
                case 1: return ElementId.Fire;
                case 2: return ElementId.Earth;
                default: return ElementId.Water;
            }
        }

        static Rect Centered(Vector2 at, float size)
        {
            return new Rect(at.x - size * 0.5f, at.y - size * 0.5f, size, size);
        }

        static Color WithAlpha(Color color, float alpha)
        {
            color.a *= alpha;
            return color;
        }
    }
}
