using UnityEngine;
using VaatusRevenge.Core;

namespace VaatusRevenge
{
    // The beat ring: when to press X for the next hit of the string. A ring closes from 2.2x onto a fixed circle and
    // touches it exactly on the beat (the moment the running hit lands); a gold disc fills the circle while a press would
    // count as on the beat. An on-beat press bursts the ring gold; an early or mashed press flashes a grey tick.
    //
    // The ring is driven only by the model's RhythmView.TimeToBeat (game time), so it lands on the beat's frame, slows in
    // slow motion and freezes in hitstop, exactly like the rule it shows. The burst and the tick are feedback on a press
    // and run in real time. Draws under the combo counter, and optionally at the player's feet (the tutorial turns that
    // on). Allocation-free.
    public sealed class HudBeatPulse
    {
        const float ApproachRadius = 2.2f;      // the ring starts this many times the circle's radius...
        const float ApproachSeconds = 0.45f;    // ...this long before the beat (shorter moves start it partly closed)
        const float BurstTime = 0.25f;
        const float TickTime = 0.3f;

        static readonly Color CircleColor = new Color(1f, 1f, 1f, 0.35f);
        static readonly Color RingColor = new Color(1f, 1f, 1f, 0.9f);
        static readonly Color WindowGold = new Color(1f, 0.82f, 0.3f, 0.55f);
        static readonly Color BurstGold = new Color(1f, 0.86f, 0.4f, 1f);
        static readonly Color TickGrey = new Color(0.62f, 0.62f, 0.66f, 1f);

        float burstStart = -10f;
        float tickStart = -10f;
        string tickLabel = "";

        public void OnEvent(in PlayerEvent e, float now)
        {
            if (e.Type != PlayerEventType.BeatJudged) return;
            switch (e.Grade)
            {
                case BeatGrade.OnBeat:
                    burstStart = now;
                    tickStart = -10f;
                    break;
                case BeatGrade.Early:
                    tickStart = now;
                    tickLabel = "EARLY";
                    break;
                case BeatGrade.Mashed:
                    // A downgrade from on-beat: the burst that was showing is taken back.
                    burstStart = -10f;
                    tickStart = now;
                    tickLabel = "MASH";
                    break;
            }
        }

        // center: the circle's centre; radius: its radius (screen pixels). squash < 1 draws it as an ellipse lying on
        // the floor. label: draw the early / mash word under it.
        public void Draw(HudPainter p, PlayerCombatModel model, Vector2 center, float radius, float now, float squash, bool label)
        {
            RhythmView rhythm = model.Rhythm;
            float line = Mathf.Max(1.5f, radius * 0.12f);
            bool live = rhythm.Active && rhythm.TimeToBeat >= -rhythm.LateWindow;
            if (live)
            {
                p.Ring(center, radius, line, CircleColor, squash);
                // Inside the window: gold.
                if (rhythm.TimeToBeat <= rhythm.EarlyWindow) p.Disc(center, radius * 0.9f, WindowGold, squash);
                float closing = Mathf.Clamp01(rhythm.TimeToBeat / ApproachSeconds);
                float approach = radius * (1f + (ApproachRadius - 1f) * closing);
                Color ring = WithAlpha(RingColor, 0.35f + 0.65f * (1f - closing));
                // The 'beat_ring' picture (Art/VFX/Common) when there is one, else a drawn ring.
                if (ElementVfx.TryGetTexture(ElementId.None, VfxSlot.BeatRing, out Texture2D picture, out _))
                    p.Picture(new Rect(center.x - approach, center.y - approach * squash, approach * 2f, approach * 2f * squash), picture, ring);
                else
                    p.Ring(center, approach, line, ring, squash);
            }

            float sinceBurst = now - burstStart;
            if (sinceBurst >= 0f && sinceBurst < BurstTime)
            {
                float u = sinceBurst / BurstTime;
                p.Ring(center, radius * (1f + u), line * 1.6f, WithAlpha(BurstGold, 1f - u), squash);
                p.Disc(center, radius * 0.9f, WithAlpha(BurstGold, 0.6f * (1f - u)), squash);
            }

            float sinceTick = now - tickStart;
            if (sinceTick >= 0f && sinceTick < TickTime)
            {
                float fade = 1f - sinceTick / TickTime;
                p.Ring(center, radius, line * 1.4f, WithAlpha(TickGrey, fade), squash);
                if (label)
                {
                    p.Text(new Rect(center.x - radius * 4f, center.y + radius * squash + p.U(4f), radius * 8f, p.U(22f)), tickLabel, p.SmallCenter,
                        WithAlpha(TickGrey, fade));
                }
            }
        }

        static Color WithAlpha(Color color, float alpha)
        {
            color.a *= Mathf.Clamp01(alpha);
            return color;
        }
    }
}
