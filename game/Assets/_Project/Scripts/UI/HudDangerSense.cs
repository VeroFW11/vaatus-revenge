using UnityEngine;
using VaatusRevenge.Core;

namespace VaatusRevenge
{
    // Danger sense: a mark above the player's head when an enemy strike is about to land, with an arrow on a circle
    // round it pointing at each attacker (up to three; the one landing soonest is bigger).
    //   gold  you can parry it (tap LB)
    //   red   you must dodge it (B): it can't be parried, or can't be blocked
    //   white now: press (Fluid only; DangerNow)
    // The mark pulses faster as the strike gets closer (6 to 14 times a second). It appears exactly WarningLead before
    // impact because it shows the model's own warned strikes (TryGetThreat), and goes when the strike lands or is
    // called off.
    //
    // The arrows work like a radar seen from behind the player: up is where the camera looks, so an attacker behind you
    // points down and one off screen still gets its arrow on the circle. That screen angle is worked out here, from the
    // camera; the core only knows where the attacker stands. The mark hides when the player's head is behind the camera.
    // Uses the 'danger' picture (Art/VFX/Common) when there is one. Allocation-free.
    public sealed class HudDangerSense
    {
        const int MaxArrows = 3;
        const float SlowPulseHz = 6f;
        const float FastPulseHz = 14f;
        const float HeadClearance = 0.45f;     // metres above the top of the head

        static readonly Color ParryGold = new Color(1f, 0.8f, 0.2f, 1f);
        static readonly Color DodgeRed = new Color(0.95f, 0.16f, 0.12f, 1f);
        static readonly Color NowWhite = new Color(1f, 1f, 1f, 1f);
        static readonly Color Shadow = new Color(0f, 0f, 0f, 0.55f);

        readonly int[] shown = new int[MaxArrows];      // indices into the model's threats, soonest first
        readonly double[] shownImpact = new double[MaxArrows];
        float phase;

        public void Draw(HudPainter p, PlayerController player, PlayerCombatModel model, Camera cam, float realDt)
        {
            if (cam == null || model.PendingThreatCount == 0) return;
            double clock = model.Clock;
            int count = CollectThreats(model, clock);
            if (count == 0) return;

            Transform body = player.transform;
            float height = player.TryGetComponent(out CharacterController controller) ? controller.height : 1.8f;
            Vector3 head = body.position + Vector3.up * (height + HeadClearance);
            Vector3 screen = cam.WorldToScreenPoint(head);
            if (screen.z <= 0f) return;    // behind the camera
            var center = new Vector2(screen.x, Screen.height - screen.y);

            model.TryGetThreat(shown[0], out IncomingStrike soonest);
            DangerSenseSettings rules = model.Tuning != null ? model.Tuning.DangerSense : null;
            float lead = rules != null && rules.WarningLead > 0f ? rules.WarningLead : 0.6f;
            float scale = soonest.LeadScale > 0f ? soonest.LeadScale : 1f;
            float urgency = 1f - Mathf.Clamp01((float)(soonest.ImpactClock - clock) / (lead * scale));
            phase += realDt * Mathf.Lerp(SlowPulseHz, FastPulseHz, urgency) * Mathf.PI * 2f;
            if (phase > Mathf.PI * 2f) phase -= Mathf.PI * 2f;
            float pulse = 1f + 0.15f * Mathf.Sin(phase);

            // The arrows first, so the mark sits on top.
            float circle = p.U(44f);
            Quaternion toCamera = Quaternion.Inverse(Quaternion.Euler(0f, cam.transform.eulerAngles.y, 0f));
            for (int i = count - 1; i >= 0; i--)
            {
                model.TryGetThreat(shown[i], out IncomingStrike strike);
                Vector3 toAttacker = new Vector3(strike.AttackerFeet.X, 0f, strike.AttackerFeet.Z) - new Vector3(body.position.x, 0f, body.position.z);
                Vector3 local = toCamera * toAttacker;
                if (local.sqrMagnitude < 1e-4f) local = Vector3.forward;
                float angle = Mathf.Atan2(local.x, local.z) * Mathf.Rad2Deg;   // 0 = straight ahead of the camera = up
                float radians = angle * Mathf.Deg2Rad;
                var at = new Vector2(center.x + Mathf.Sin(radians) * circle, center.y - Mathf.Cos(radians) * circle);
                float size = i == 0 ? p.U(22f) * pulse : p.U(15f);
                Color color = ColorOf(in strike);
                p.Triangle(at, size + p.U(4f), angle, Shadow);
                p.Triangle(at, size, angle, color);
            }

            // The mark.
            float markSize = p.U(34f) * pulse;
            Color markColor = ColorOf(in soonest);
            var mark = new Rect(center.x - markSize * 0.5f, center.y - markSize * 0.5f, markSize, markSize);
            if (ElementVfx.TryGetTexture(ElementId.None, VfxSlot.Danger, out Texture2D picture, out _))
            {
                p.Picture(mark, picture, markColor);
                return;
            }
            p.Diamond(new Rect(mark.x - p.U(3f), mark.y - p.U(3f), mark.width + p.U(6f), mark.height + p.U(6f)), Shadow);
            p.Diamond(mark, markColor);
            float textHeight = p.Body.fontSize * 1.2f;
            p.Text(new Rect(mark.x, mark.center.y - textHeight * 0.5f, mark.width, textHeight), "!", p.BodyCenter,
                soonest.NowFired ? new Color(0.1f, 0.1f, 0.1f, 1f) : Color.white);
        }

        // The warned, visible strikes that haven't landed yet, soonest first (at most MaxArrows).
        int CollectThreats(PlayerCombatModel model, double clock)
        {
            int count = 0;
            int pending = model.PendingThreatCount;
            for (int i = 0; i < pending; i++)
            {
                if (!model.TryGetThreat(i, out IncomingStrike strike) || !strike.Warned || strike.Hidden || strike.ImpactClock < clock) continue;
                // Insertion into the short sorted list.
                int at = count < MaxArrows ? count : MaxArrows - 1;
                if (count == MaxArrows && strike.ImpactClock >= shownImpact[at]) continue;
                while (at > 0 && shownImpact[at - 1] > strike.ImpactClock)
                {
                    shown[at] = shown[at - 1];
                    shownImpact[at] = shownImpact[at - 1];
                    at--;
                }
                shown[at] = i;
                shownImpact[at] = strike.ImpactClock;
                if (count < MaxArrows) count++;
            }
            return count;
        }

        static Color ColorOf(in IncomingStrike strike)
        {
            if (strike.NowFired) return NowWhite;
            return strike.MustDodge ? DodgeRed : ParryGold;
        }
    }
}
