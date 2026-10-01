using System;

namespace VaatusRevenge.Core
{
    // The hit counter (Spider-Man 2's combo meter) and MIX (the multi-element bonus).
    //
    // COMBO COUNT: +1 for every clean hit, once per AttackId (a wide kick hitting two enemies counts once; each sub-hit
    //   of a multi-hit move counts; a projectile counts when it lands), raised as ComboHit. A perfect dodge or a deflect
    //   keeps it alive (refreshes the timer) without adding to it. Dodges, switches, jumps, whiffs and blocks leave it
    //   alone. It ends (ComboEnded) when you take a hit (any damage, even through hyper armour, or a guard break), go
    //   ComboTimeout without landing anything, are staggered, die, respawn or change preset. Ending it clears MIX and the
    //   string memory (a timeout while the string is still remembered only ends the counter).
    // MIX: the elements that LANDED a clean hit in this combo (switching alone earns nothing; Fire, Water, Fire, Water is
    //   2). While the combo lives every hit is multiplied by MixTuning.DamageByLevel; a string finisher (main or pause) at
    //   MIX 2+ hits harder, at 3+ launches, at 4 breaks poise (stagger immunity and break-out armour still hold) and refills
    //   the identity meters. Poise is never multiplied by MIX.
    // HEAL ON HIT (Water): each clean hit restores MoveData.HealOnHit, at most HealPerMoveMax per move.
    public sealed partial class PlayerCombatModel
    {
        readonly ComboTuning fallbackCombo = new ComboTuning();
        readonly MixTuning fallbackMix = new MixTuning();

        int comboCount;
        float comboTimeRemaining;
        int mixMask;                  // bit (1 << (int)ElementId) for every element that landed a clean hit in this combo

        ComboTuning ComboRules => tuning.Combo ?? fallbackCombo;
        MixTuning MixRules => tuning.Mix ?? fallbackMix;

        void ResetCombo()
        {
            comboCount = 0;
            comboTimeRemaining = 0f;
            mixMask = 0;
        }

        // targetAirborne: the target was in the air (juggled) when it was hit (ComboHit.InAir).
        public void OnAttackLanded(in HitResult result, int attackId, bool targetAirborne)
        {
            if (result.Outcome != HitOutcome.Hit || attackId == 0 || state == PlayerState.Dead) return;
            int index = FindAttack(attackId);
            if (index < 0 || recentAttacks[index].Rewarded) return;
            recentAttacks[index].Rewarded = true;
            AttackRecord attack = recentAttacks[index];

            MeterOf(attack.Element).Gain(attack.MomentumGain, MomentumRulesOf(attack.Element));
            HealFromHit(attack);

            int levelBefore = MixLevel;
            comboCount++;
            comboTimeRemaining = Math.Max(0f, ComboRules.ComboTimeout);
            int bit = 1 << (int)attack.Element;
            if (attack.Element != ElementId.None && (mixMask & bit) == 0)
            {
                mixMask |= bit;
                Emit(new PlayerEvent
                {
                    Type = PlayerEventType.MixChanged, Count = MixLevel, Amount = MixRules.DamageFor(MixLevel), Element = attack.Element
                });
            }
            // The MIX finisher: announced on its first clean hit, at the level its damage was built with.
            if (attack.IsFinisher && levelBefore >= 2 && IsFirstLandedSubHit(attack))
            {
                Emit(new PlayerEvent
                {
                    Type = PlayerEventType.MixFinisher, Count = levelBefore, Move = attack.Move, AttackId = attack.AttackId, Element = attack.Element
                });
                if (levelBefore >= 4) RefillMeters();
            }
            Emit(new PlayerEvent
            {
                Type = PlayerEventType.ComboHit, Move = attack.Move, AttackId = attack.AttackId, MoveInstanceId = attack.MoveInstanceId,
                AttackKind = attack.Kind, Branch = attack.Branch, ChainIndex = attack.ChainIndex, IsFinisher = attack.IsFinisher,
                Element = attack.Element, Grade = attack.Grade, Count = comboCount, Amount = comboTimeRemaining,
                Origin = GetStrikeOrigin(lastPosition), InAir = targetAirborne
            });
        }

        // Water restores you: per clean hit, capped per move (0 cap = one HealOnHit).
        void HealFromHit(in AttackRecord attack)
        {
            MoveData move = attack.Move;
            if (move == null || !(move.HealOnHit > 0f)) return;
            int owner = FindMoveRecord(attack.MoveInstanceId);
            float cap = move.HealPerMoveMax > 0f ? move.HealPerMoveMax : move.HealOnHit;
            float healedSoFar = owner >= 0 ? recentAttacks[owner].Healed : 0f;
            float amount = Math.Min(move.HealOnHit, Math.Max(0f, cap - healedSoFar));
            if (!(amount > 0f)) return;
            float before = health;
            health = Math.Min(MaxHealth, health + amount);
            if (owner >= 0) recentAttacks[owner].Healed = healedSoFar + amount;
            // Seen and heard (verify S-12): the HUD ticks the health bar, the body draws a mote in.
            if (health > before)
            {
                Emit(new PlayerEvent
                {
                    Type = PlayerEventType.HealedOnHit, Amount = health - before, AttackId = attack.AttackId, Element = attack.Element,
                    Move = move
                });
            }
        }

        // No other sub-hit of this move has landed yet.
        bool IsFirstLandedSubHit(in AttackRecord attack)
        {
            for (int i = 0; i < recentAttacks.Length; i++)
            {
                if (recentAttacks[i].MoveInstanceId == attack.MoveInstanceId && recentAttacks[i].AttackId != attack.AttackId
                    && recentAttacks[i].Rewarded) return false;
            }
            return true;
        }

        // The MIX tiers for a finisher's hit (the level is the MIX before this hit lands).
        void ApplyMixFinisher(bool isFinisher, ref float damageScale, ref float launchSpeed, ref bool poiseBreak)
        {
            int level = MixLevel;
            if (!isFinisher || level < 2) return;
            MixTuning rules = MixRules;
            damageScale *= Math.Max(0f, rules.FinisherDamage);
            if (level >= 3) launchSpeed = Math.Max(launchSpeed, rules.FinisherLaunchSpeed);
            if (level >= 4 && rules.FinisherPoiseBreak) poiseBreak = true;
        }

        void RefillMeters()
        {
            float share = Math.Max(0f, MixRules.FinisherMeterRefill);
            if (!(share > 0f)) return;
            for (ElementId element = ElementId.Fire; element <= ElementId.Air; element++)
            {
                MomentumSettings rules = MomentumRulesOf(element);
                if (rules.Enabled) MeterOf(element).Gain(rules.Max * share, rules);
            }
        }

        // A perfect dodge or a deflect keeps the combo going (no hit added).
        void RefreshCombo()
        {
            if (comboCount > 0) comboTimeRemaining = Math.Max(comboTimeRemaining, Math.Max(0f, ComboRules.ComboTimeout));
        }

        void UpdateComboTimer(float dt)
        {
            if (comboCount <= 0) return;
            comboTimeRemaining -= dt;
            if (comboTimeRemaining <= 0f) EndCombo(ComboEndReason.Timeout);
        }

        void EndCombo(ComboEndReason reason)
        {
            if (comboCount > 0) Emit(new PlayerEvent { Type = PlayerEventType.ComboEnded, Count = comboCount, EndReason = reason });
            bool keepString = reason == ComboEndReason.Timeout && IsStringMemoryLive;
            ResetCombo();
            if (!keepString) ClearStringMemory();
        }
    }
}
