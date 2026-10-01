using UnityEngine;
using VaatusRevenge.Core;

namespace VaatusRevenge
{
    // The hit counter on the right of the screen (Spider-Man 2's combo meter), from the second hit of a combo:
    //   the count, big, in the colour of the element that landed the last hit; it pops (1.3x, back to 1x in 0.12 s) on
    //   every hit
    //   a bar draining towards the combo's timeout (ComboTimeRemaining01): land a hit before it empties
    //   "ON BEAT" and up to five gold pips for the on-beat presses in a row
    //   "MIX x2" (x3, x4) with an icon per element that has landed in the combo, when two or more have
    // When the combo ends it holds its last count and fades out over half a second.
    //
    // Real time throughout (the pop and the fade keep moving in hitstop). Draws allocate nothing: every number shown is
    // a string made once up front.
    public sealed class HudComboCounter
    {
        const int MaxCachedCount = 999;          // counts above show as "999+"
        const float PopScale = 1.3f;
        const float PopTime = 0.12f;
        const float FadeTime = 0.5f;
        const int StreakPips = 5;
        const int MinShownCount = 2;

        static readonly Color DrainBackground = new Color(0f, 0f, 0f, 0.5f);
        static readonly Color BeatGold = new Color(1f, 0.84f, 0.35f, 1f);
        static readonly Color PipEmpty = new Color(1f, 1f, 1f, 0.18f);
        static readonly Color LabelColor = new Color(1f, 1f, 1f, 0.75f);

        readonly string[] counts = new string[MaxCachedCount + 1];
        readonly string[] mixLabels = { "", "", "MIX ×2", "MIX ×3", "MIX ×4" };

        float popStart = -10f;
        float mixPopStart = -10f;
        float fadeStart = -10f;
        int fadingCount;
        int fadingMixMask;
        ElementId lastElement = ElementId.Fire;

        public HudComboCounter()
        {
            for (int i = 0; i <= MaxCachedCount; i++) counts[i] = i.ToString(System.Globalization.CultureInfo.InvariantCulture);
        }

        public void OnEvent(in PlayerEvent e, float now)
        {
            switch (e.Type)
            {
                case PlayerEventType.ComboHit:
                    popStart = now;
                    if (e.Element != ElementId.None) lastElement = e.Element;
                    fadeStart = -10f;
                    break;
                case PlayerEventType.MixChanged:
                    if (e.Count >= 2) mixPopStart = now;
                    break;
                case PlayerEventType.ComboEnded:
                    fadingCount = e.Count;
                    fadeStart = now;
                    break;
                case PlayerEventType.Respawned:
                    fadeStart = -10f;
                    break;
            }
        }

        // anchor: the counter's top-right corner (screen pixels).
        public void Draw(HudPainter p, PlayerCombatModel model, Vector2 anchor, float now)
        {
            int count = model.ComboCount;
            int mixMask = model.MixElementsMask;
            float alpha = 1f;
            if (count >= MinShownCount)
            {
                fadingMixMask = mixMask;
            }
            else
            {
                // Ended: the last count stays a moment and fades.
                float since = now - fadeStart;
                if (fadingCount < MinShownCount || since >= FadeTime) return;
                count = fadingCount;
                mixMask = fadingMixMask;
                alpha = 1f - since / FadeTime;
            }

            float width = p.U(240f);
            float x = anchor.x - width;
            float y = anchor.y;
            Color tint = ElementVfx.HudColor(lastElement);
            tint.a = alpha;

            // The count, popping on each hit.
            float pop = Mathf.Clamp01((now - popStart) / PopTime);
            float scale = Mathf.Lerp(PopScale, 1f, pop * (2f - pop));
            var numberRect = new Rect(x, y, width, p.U(64f));
            Matrix4x4 saved = p.BeginScaled(numberRect.center, scale);
            p.Text(numberRect, count <= MaxCachedCount ? counts[count] : "999+", p.Big, tint);
            p.EndScaled(saved);
            y += p.U(60f);
            p.Text(new Rect(x, y, width, p.U(22f)), "HITS", p.SmallCenter, WithAlpha(LabelColor, alpha));
            y += p.U(24f);

            // Time left to keep it going.
            var drain = new Rect(x + p.U(30f), y, width - p.U(60f), p.U(6f));
            p.Bar(drain, model.ComboCount >= MinShownCount ? model.ComboTimeRemaining01 : 0f, tint, WithAlpha(DrainBackground, 0.5f * alpha));
            y += p.U(16f);

            // On the beat: a gold streak.
            RhythmView rhythm = model.Rhythm;
            int streak = Mathf.Min(rhythm.Streak, StreakPips);
            if (streak > 0 && alpha >= 1f)
            {
                p.Text(new Rect(x, y, width, p.U(22f)), "ON BEAT", p.SmallCenter, BeatGold);
                y += p.U(22f);
                float pip = p.U(10f);
                float gap = p.U(6f);
                float pipsWidth = StreakPips * pip + (StreakPips - 1) * gap;
                float pipX = x + (width - pipsWidth) * 0.5f;
                for (int i = 0; i < StreakPips; i++) p.Diamond(new Rect(pipX + i * (pip + gap), y, pip, pip), i < streak ? BeatGold : PipEmpty);
                y += pip + p.U(8f);
            }

            // MIX: how many elements have landed in this combo, and which.
            int level = CountBits(mixMask);
            if (level >= 2)
            {
                float mixPop = Mathf.Clamp01((now - mixPopStart) / (PopTime * 2f));
                float mixScale = Mathf.Lerp(PopScale, 1f, mixPop);
                var mixRect = new Rect(x, y, width, p.U(30f));
                Matrix4x4 savedMix = p.BeginScaled(mixRect.center, mixScale);
                p.Text(mixRect, mixLabels[Mathf.Min(level, 4)], p.BodyCenter, WithAlpha(Color.white, alpha));
                p.EndScaled(savedMix);
                y += p.U(30f);
                float icon = p.U(16f);
                float iconGap = p.U(6f);
                float iconsWidth = level * icon + (level - 1) * iconGap;
                float iconX = x + (width - iconsWidth) * 0.5f;
                for (ElementId element = ElementId.Fire; element <= ElementId.Air; element++)
                {
                    if ((mixMask & (1 << (int)element)) == 0) continue;
                    p.Diamond(new Rect(iconX, y, icon, icon), WithAlpha(ElementVfx.HudColor(element), alpha));
                    iconX += icon + iconGap;
                }
            }
        }

        static int CountBits(int mask)
        {
            int count = 0;
            for (; mask != 0; mask &= mask - 1) count++;
            return count;
        }

        static Color WithAlpha(Color color, float alpha)
        {
            color.a *= alpha;
            return color;
        }
    }
}
