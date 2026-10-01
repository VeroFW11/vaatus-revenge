using UnityEngine;
using VaatusRevenge.Core;

namespace VaatusRevenge
{
    // The beat ring: when to press X for the next hit of the string. A ring closes from 2.2x onto a fixed circle and
    // touches it exactly on the beat (the moment the running hit lands); a gold disc fills the circle while a press would
    // count as on the beat. An on-beat press bursts the ring gold; an early or mashed press flashes a grey tick, a late one an amber LATE. After X X
    // with no press, the circle glows blue while the pause band is open (RhythmView.PauseReady): X now starts the pause
    // finisher. The wait is taught by that cue, not by seconds (the band opens at a different time in each element).
    //
    // The ring is driven only by the model's RhythmView cue (game time), so it lands on the beat's frame, slows in slow
    // motion and freezes in hitstop, exactly like the rule it shows. It is PREDICTIVE (J3-01): once you have pressed for
    // a hit, the next hit's ring is already closing: it appears, fainter and further out, as the running hit starts
    // (NextCue) and runs over the whole gap to its beat, so each touch can be anticipated rather than reacted to (every
    // follow-up's ring is up for 0.25 s or more). Only a string's first beat, straight after the opening X, is short. The burst and the tick are feedback on a press
    // and run in real time. Draws under the combo counter, and optionally at the player's feet (the tutorial turns that
    // on). Allocation-free.
    public sealed class HudBeatPulse
    {
        const float ApproachRadius = 2.2f;      // the ring starts this many times the circle's radius...
        const float MaxApproachSeconds = 0.8f;  // ...over the cue's whole lead, but never slower than this
        const float NextRingAlpha = 0.55f;      // the ring after the running one is fainter until it's the next to touch
        const float BurstTime = 0.25f;
        const float TickTime = 0.3f;

        static readonly Color CircleColor = new Color(1f, 1f, 1f, 0.35f);
        static readonly Color RingColor = new Color(1f, 0.86f, 0.45f, 0.95f);   // gold, as the tutorial and How-To-Play call it
        static readonly Color WindowGold = new Color(1f, 0.82f, 0.3f, 0.55f);
        static readonly Color BurstGold = new Color(1f, 0.86f, 0.4f, 1f);
        static readonly Color TickGrey = new Color(0.62f, 0.62f, 0.66f, 1f);
        static readonly Color TickAmber = new Color(1f, 0.6f, 0.22f, 1f);    // a late press: warm, to tell it from EARLY's grey
        static readonly Color PauseBlue = new Color(0.45f, 0.78f, 1f, 1f);

        float burstStart = -10f;
        float tickStart = -10f;
        string tickLabel = "";
        Color tickColor = TickGrey;

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
                    tickColor = TickGrey;
                    break;
                case BeatGrade.Late:
                    // Pressed after the beat (usually on seeing the hit land): the streak breaks, so say why (R2-05).
                    tickStart = now;
                    tickLabel = "LATE";
                    tickColor = TickAmber;
                    break;
                case BeatGrade.Mashed:
                    // A downgrade from on-beat: the burst that was showing is taken back.
                    burstStart = -10f;
                    tickStart = now;
                    tickLabel = "MASH";
                    tickColor = TickGrey;
                    break;
            }
        }

        // center: the circle's centre; radius: its radius (screen pixels). squash < 1 draws it as an ellipse lying on
        // the floor. label: draw the early / mash word under it.
        public void Draw(HudPainter p, PlayerCombatModel model, Vector2 center, float radius, float now, float squash, bool label)
        {
            RhythmView rhythm = model.Rhythm;
            float line = Mathf.Max(1.5f, radius * 0.12f);
            bool live = rhythm.Cue && rhythm.CueTimeToBeat >= -rhythm.LateWindow;
            if (live)
            {
                p.Ring(center, radius, line, CircleColor, squash);
                // Inside the window: gold.
                if (rhythm.CueTimeToBeat <= rhythm.EarlyWindow) Fill(p, center, radius * 0.9f, WindowGold, squash, line, !label);
                ApproachRing(p, center, radius, line, squash, rhythm.CueTimeToBeat, rhythm.CueLead, 1f);
                if (rhythm.NextCue) ApproachRing(p, center, radius, line, squash, rhythm.NextCueTimeToBeat, rhythm.NextCueLead, NextRingAlpha);
            }

            if (rhythm.PauseReady)
            {
                // The pause band is open: a steady blue circle, breathing a little.
                float breathe = 0.85f + 0.15f * Mathf.Sin(now * 9f);
                Fill(p, center, radius * 0.9f, WithAlpha(PauseBlue, 0.45f * breathe), squash, line, !label);
                p.Ring(center, radius * 1.15f, line * 1.4f, WithAlpha(PauseBlue, breathe), squash);
                if (label)
                {
                    p.Text(new Rect(center.x - radius * 4f, center.y + radius * squash + p.U(4f), radius * 8f, p.U(22f)), "PAUSE", p.SmallCenter,
                        PauseBlue);
                }
            }

            float sinceBurst = now - burstStart;
            if (sinceBurst >= 0f && sinceBurst < BurstTime)
            {
                float u = sinceBurst / BurstTime;
                p.Ring(center, radius * (1f + u), line * 1.6f, WithAlpha(BurstGold, 1f - u), squash);
                Fill(p, center, radius * 0.9f, WithAlpha(BurstGold, 0.6f * (1f - u)), squash, line, !label);
            }

            float sinceTick = now - tickStart;
            if (sinceTick >= 0f && sinceTick < TickTime)
            {
                float fade = 1f - sinceTick / TickTime;
                p.Ring(center, radius, line * 1.4f, WithAlpha(tickColor, fade), squash);
                if (label)
                {
                    p.Text(new Rect(center.x - radius * 4f, center.y + radius * squash + p.U(4f), radius * 8f, p.U(22f)), tickLabel, p.SmallCenter,
                        WithAlpha(tickColor, fade));
                }
            }
        }

        // The circle's fill. The ring on the floor at the player's feet (drawn without labels) is painted over the 3D view,
        // so a solid disc would cover the legs (J3-S12): there it is a thick band round the inside of the circle instead.
        static void Fill(HudPainter p, Vector2 center, float radius, Color color, float squash, float line, bool outline)
        {
            if (!outline) p.Disc(center, radius, color, squash);
            else p.Ring(center, radius - line, line * 2f, color, squash);
        }

        // A gold ring closing from ApproachRadius onto the circle over 'lead' seconds, touching it as timeToBeat reaches 0.
        static void ApproachRing(HudPainter p, Vector2 center, float radius, float line, float squash, float timeToBeat, float lead,
            float alpha)
        {
            float closing = Mathf.Clamp01(timeToBeat / Mathf.Clamp(lead, 0.05f, MaxApproachSeconds));
            float approach = radius * (1f + (ApproachRadius - 1f) * closing);
            Color ring = WithAlpha(RingColor, alpha * (0.35f + 0.65f * (1f - closing)));
            // The 'beat_ring' picture (Art/VFX/Common) when there is one, else a drawn ring.
            if (ElementVfx.TryGetTexture(ElementId.None, VfxSlot.BeatRing, out Texture2D picture, out _))
                p.Picture(new Rect(center.x - approach, center.y - approach * squash, approach * 2f, approach * 2f * squash), picture, ring);
            else
                p.Ring(center, approach, line, ring, squash);
        }

        static Color WithAlpha(Color color, float alpha)
        {
            color.a *= Mathf.Clamp01(alpha);
            return color;
        }
    }
}
