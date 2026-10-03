using System;
using System.Numerics;

namespace VaatusRevenge.Core
{
    // Danger sense: a mark over the player's head before an enemy strike lands (gold = parry it, red = dodge it),
    // turning white at the moment to press. The enemy side registers each strike through DangerSenseRelay
    // (NotifyIncomingStrike); this side times the cues with the player's own preset (DangerSenseSettings), so the
    // enemy brains never need to know it.
    //
    // Per tracked strike, every frame: WarningLead before it lands -> DangerWarning (once); NowLead before it lands
    // -> DangerNow (once, if NowLead > 0); ClearAfterImpact after it should have landed, or when it's called off ->
    // dropped, with DangerCleared if a warning was shown. A strike registered later than a lead raises that cue at
    // once. Cues follow the STRIKE, not the wind-up, so a delayed swing warns late: reacting to the mark beats it,
    // panicking at the glow doesn't. The cues never touch the combo or the string.
    // The strikes live in a fixed array (MaxTracked): no allocation while fighting.
    public sealed partial class PlayerCombatModel
    {
        IncomingStrike[] threats = new IncomingStrike[8];
        int threatCount;
        Vector3 lastPosition;          // the player's feet last frame: the cues point from the attacker to here
        float bodyRadius;              // the player's body radius last frame: a bolt lands when it touches the body

        // The player's body radius (as of the last frame): the enemy side times a bolt's landing to the touch.
        public float BodyRadius => bodyRadius;

        readonly DangerSenseSettings fallbackDanger = new DangerSenseSettings { Enabled = false };   // tuning without the section

        DangerSenseSettings DangerRules => tuning.DangerSense ?? fallbackDanger;

        public int PendingThreatCount => threatCount;

        public bool TryGetThreat(int index, out IncomingStrike strike)
        {
            if (index < 0 || index >= threatCount)
            {
                strike = default;
                return false;
            }
            strike = threats[index];
            return true;
        }

        // The visible strike that lands soonest.
        public bool TryGetMostImminentThreat(out IncomingStrike strike)
        {
            int best = MostImminentThreat(double.PositiveInfinity);
            if (best < 0)
            {
                strike = default;
                return false;
            }
            strike = threats[best];
            return true;
        }

        // Registers a strike (or refreshes it: one entry per attacker and hit index).
        public void NotifyIncomingStrike(in IncomingStrike strike)
        {
            if (state == PlayerState.Dead) return;
            EnsureThreatCapacity();
            int index = -1;
            for (int i = 0; i < threatCount; i++)
            {
                if (threats[i].AttackerId == strike.AttackerId && threats[i].HitIndex == strike.HitIndex)
                {
                    index = i;
                    break;
                }
            }
            if (index < 0)
            {
                if (threatCount >= threats.Length)
                {
                    // Full: the strike landing last makes room (the nearer ones matter more).
                    index = LatestThreat();
                    if (threats[index].ImpactClock <= strike.ImpactClock) return;
                    DropThreat(index);
                }
                index = threatCount++;
                threats[index] = strike;
                threats[index].Warned = false;
                threats[index].NowFired = false;
            }
            else
            {
                bool warned = threats[index].Warned;
                bool nowFired = threats[index].NowFired;
                threats[index] = strike;
                threats[index].Warned = warned;
                threats[index].NowFired = nowFired;
            }
            UpdateThreat(index);
        }

        // Every strike from this attacker is off (its attack was cut short, it died or was reset).
        public void CancelIncomingStrikes(int attackerId)
        {
            for (int i = threatCount - 1; i >= 0; i--)
            {
                if (threats[i].AttackerId == attackerId) DropThreat(i);
            }
        }

        // Once per frame, after the timers: raise the cues that are due and forget strikes that have passed.
        void UpdateThreats()
        {
            for (int i = threatCount - 1; i >= 0; i--) UpdateThreat(i);
        }

        void UpdateThreat(int index)
        {
            DangerSenseSettings rules = DangerRules;
            IncomingStrike strike = threats[index];
            if (clock > strike.ImpactClock + Math.Max(0f, rules.ClearAfterImpact))
            {
                DropThreat(index);
                return;
            }
            if (!rules.Enabled || strike.Hidden) return;
            float scale = strike.LeadScale > 0f ? strike.LeadScale : 1f;
            if (!strike.Warned && clock >= strike.ImpactClock - Math.Max(0f, rules.WarningLead) * scale)
            {
                threats[index].Warned = true;
                EmitThreat(PlayerEventType.DangerWarning, in threats[index]);
            }
            if (rules.NowLead > 0f && !strike.NowFired && clock >= strike.ImpactClock - rules.NowLead * scale)
            {
                threats[index].NowFired = true;
                if (!threats[index].Warned)
                {
                    threats[index].Warned = true;
                    EmitThreat(PlayerEventType.DangerWarning, in threats[index]);
                }
                EmitThreat(PlayerEventType.DangerNow, in threats[index]);
            }
        }

        void DropThreat(int index)
        {
            if (threats[index].Warned)
            {
                Emit(new PlayerEvent { Type = PlayerEventType.DangerCleared, AttackerId = threats[index].AttackerId, Count = threats[index].HitIndex });
            }
            threatCount--;
            threats[index] = threats[threatCount];    // order doesn't matter: swap the last one in
            threats[threatCount] = default;
        }

        void ClearThreats()
        {
            for (int i = threatCount - 1; i >= 0; i--) DropThreat(i);
        }

        void EmitThreat(PlayerEventType type, in IncomingStrike strike)
        {
            Vector3 toPlayer = Directions.SafeNormalize(Directions.Flatten(lastPosition - strike.AttackerFeet), strike.StrikeForward);
            Emit(new PlayerEvent
            {
                Type = type, AttackerId = strike.AttackerId, Count = strike.HitIndex, Origin = strike.AttackerFeet, Direction = toPlayer,
                Duration = (float)Math.Max(0.0, strike.ImpactClock - clock), MustDodge = strike.MustDodge, IsRanged = strike.Ranged
            });
        }

        // The visible strike landing soonest that hasn't landed yet (impact no earlier than ClearAfterImpact ago) and lands
        // within 'lookahead' seconds; -1 if none.
        int MostImminentThreat(double lookahead)
        {
            int best = -1;
            double clearAfter = Math.Max(0f, DangerRules.ClearAfterImpact);
            for (int i = 0; i < threatCount; i++)
            {
                IncomingStrike strike = threats[i];
                if (strike.Hidden || strike.ImpactClock < clock - clearAfter || strike.ImpactClock - clock > lookahead) continue;
                if (best < 0 || strike.ImpactClock < threats[best].ImpactClock) best = i;
            }
            return best;
        }

        int LatestThreat()
        {
            int latest = 0;
            for (int i = 1; i < threatCount; i++)
            {
                if (threats[i].ImpactClock > threats[latest].ImpactClock) latest = i;
            }
            return latest;
        }

        // MaxTracked is tuning: a live edit resizes the array (rare; never during a normal fight).
        void EnsureThreatCapacity()
        {
            int wanted = Math.Max(1, DangerRules.MaxTracked);
            if (threats.Length == wanted) return;
            var resized = new IncomingStrike[wanted];
            int keep = Math.Min(threatCount, wanted);
            Array.Copy(threats, resized, keep);
            threats = resized;
            threatCount = keep;
        }
    }
}
