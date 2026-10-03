using System;

namespace VaatusRevenge.Core
{
    // Element switching (hold RB, then a face button; 1-4 on the keyboard). PlayerInputFrame.ElementSelect names the element;
    // the loadout says which are learned. One rule: "RB + an element's button on the beat = your next hit in that element".
    //
    //   A. String live (a string move running; or dodging / free with the string remembered or the pause band open; or
    //      airborne in an air string): the chord is a SWITCH STRIKE, buffered and beat-judged exactly like a Light press
    //      (with SwitchStrikeBeatLateBonus more late window). It switches element as its move starts (the normal cancel
    //      point, never at the press), and that move comes from the new element in the same slot (see .Actions); it hits
    //      harder (SwitchStrikeDamageMultiplier) and breaks more poise (SwitchStrikePoiseMultiplier), that one hit only.
    //   B. String not live: a plain switch. Instant when free, airborne, in a non-string attack, dodging, or guarding with
    //      no deflect armed; while busy (charging, plunging, healing, staggered, a deflect window open) it waits up to
    //      SwitchBufferWindow (a stagger drops it).
    //   C. On cooldown: a switch strike is played as a normal Light press in the current element (the string never drops)
    //      and ElementSwitchDenied{Cooldown} is raised; a plain switch is just denied.
    //   D. NotLearned and SameElement are denied (the HUD wheel shakes). A SameElement press with the attack button (X,
    //      Water's) while the string is live is played as a normal Light press, like C, so keeping RB down for the next X
    //      never drops a hit; with another face (PlayerInputFrame.ElementSelectOffAttack) it's denied, never an attack.
    // A running action always finishes with the element it started with (actionSet). Switching from a held block into a
    // parry-only element drops the guard; into a blocking element with the guard button held, it rises when free.
    // Switching costs no stamina: the cooldown is the limiter.
    public sealed partial class PlayerCombatModel
    {
        readonly ElementSwitchTuning fallbackSwitch = new ElementSwitchTuning();

        double switchCooldownUntil = double.NegativeInfinity;
        ElementId pendingSwitchElement;           // the switch strike's element (the buffered SwitchStrike command)
        bool switchStrikeQueuedAloft;             // that switch strike was pressed in the air (an air string's next hit)
        ElementId pendingSwitch;                  // a plain switch waiting for the player to be free (B, busy)
        double pendingSwitchUntil = double.NegativeInfinity;

        ElementSwitchTuning SwitchRules => tuning.ElementSwitch ?? fallbackSwitch;

        // Points the model at a loadout and picks the active element: the current one if still learned, else the
        // loadout's starting element (or the first learned one).
        void SetLoadout(ElementLoadout newLoadout)
        {
            loadout = newLoadout ?? ElementLoadout.FromSingle(ElementMoveSet.CreateFireFluid());
            ElementId element = loadout.IsLearned(activeElement) ? activeElement : loadout.FirstUsable();
            if (element == ElementId.None)
            {
                // Nothing learned at all (an empty asset): fall back to Fire so the player can still fight.
                loadout = ElementLoadout.FromSingle(ElementMoveSet.CreateFireFluid());
                element = ElementId.Fire;
            }
            activeElement = element;
            moveSet = loadout.Get(element);
            if (!IsInAction || actionSet == null) actionSet = moveSet;
        }

        // An action is running that keeps the set it started with.
        bool IsInAction => state == PlayerState.Attacking || state == PlayerState.Charging || state == PlayerState.Plunging
            || state == PlayerState.Dodging || state == PlayerState.Guarding || state == PlayerState.Healing
            || state == PlayerState.Staggered;

        // While these run, a plain switch waits (B).
        bool IsSwitchBusy => state == PlayerState.Charging || state == PlayerState.Plunging || state == PlayerState.Healing
            || state == PlayerState.Staggered || (state == PlayerState.Guarding && deflectArmed);

        // A string the next X would continue (A).
        bool IsStringLive => IsStringMoveRunning || IsStringMemoryLive || IsPauseBandLive;

        // The air string the running air move belongs to has nothing left to play (its finisher is running, or the
        // per-jump cap is reached, in 'element's rules): an X now would be dropped, so RB + a face is a plain switch at
        // once, never a switch strike that waits and then vanishes or turns into a ground hit on landing (J6-04).
        bool IsAirStringSpent(ElementId element)
        {
            if (!(IsStringMoveRunning && attackKind == PlayerAttackKind.Air)) return false;
            NextSlot(ComboBranch.Air, chainIndex, moveIsChainFinisher, out _, out int next);
            if (next < 0) return true;
            ElementMoveSet set = loadout.Get(element) ?? moveSet;
            AerialSettings aerial = set.Aerial ?? FallbackAerial;
            return airAttacksUsed >= Math.Max(0, aerial.AirAttacksPerJump);
        }

        // RB + a face button this frame. Returns the command to buffer: SwitchStrike (A), Light (C: the string goes on in
        // the current element) or None (a plain switch, done or waiting, or a denial).
        PlayerCommand ReadElementSelect(ElementId requested, bool offAttack = false)
        {
            if (requested == ElementId.None) return PlayerCommand.None;
            if (!loadout.IsLearned(requested))
            {
                EmitSwitchDenied(requested, SwitchDeniedReason.NotLearned);
                return PlayerCommand.None;
            }
            bool live = IsStringLive && !IsAirStringSpent(requested);
            if (requested == activeElement)
            {
                // Mid-string, RB still held for the next X (X is also Water's button): the press carries the string on in
                // the current element (C), instead of being swallowed. That's the string going on, not a refusal, so no
                // denial (the element wheel would shake on every hit with RB down, J3-S01). Only the attack button does
                // that: RB still held and B in Fire, A in Earth or Y in Air is no attack (J6-S02), just the wheel's shake.
                if (live && !offAttack) return PlayerCommand.Light;
                EmitSwitchDenied(requested, SwitchDeniedReason.SameElement);
                return PlayerCommand.None;
            }
            bool coolingDown = clock < switchCooldownUntil;
            if (live)
            {
                if (coolingDown)
                {
                    EmitSwitchDenied(requested, SwitchDeniedReason.Cooldown);
                    return PlayerCommand.Light;
                }
                pendingSwitchElement = requested;
                switchStrikeQueuedAloft = Aloft;
                return PlayerCommand.SwitchStrike;
            }
            if (coolingDown)
            {
                EmitSwitchDenied(requested, SwitchDeniedReason.Cooldown);
                return PlayerCommand.None;
            }
            if (IsSwitchBusy)
            {
                pendingSwitch = requested;
                pendingSwitchUntil = clock + Math.Max(0f, SwitchRules.SwitchBufferWindow);
                return PlayerCommand.None;
            }
            SwitchElement(requested, false, ComboBranch.Other);
            return PlayerCommand.None;
        }

        // A switch strike waited in the buffer and never got to run (pressed early in a long dodge on Punishing, whose
        // buffer is short; or replaced by a dodge, parry, jump or heal pressed after it): the switch still happens, as a plain switch (or waits, or is refused with the wheel's shake),
        // so RB + a face button is never dropped without a trace (J3-S03). The next X carries the string on in it.
        void SwitchStrikeExpired()
        {
            ElementId element = pendingSwitchElement;
            if (!IsSwitchUsable(element) || state == PlayerState.Dead) return;
            if (clock < switchCooldownUntil)
            {
                EmitSwitchDenied(element, SwitchDeniedReason.Cooldown);
                return;
            }
            if (IsSwitchBusy)
            {
                pendingSwitch = element;
                pendingSwitchUntil = clock + Math.Max(0f, SwitchRules.SwitchBufferWindow);
                return;
            }
            SwitchElement(element, false, ComboBranch.Other);
        }

        // Still worth switching to when the buffered switch strike finally runs.
        bool IsSwitchUsable(ElementId element)
        {
            return element != ElementId.None && element != activeElement && loadout.IsLearned(element);
        }

        // A plain switch that waited for the player to be free (B).
        void UpdatePendingSwitch()
        {
            if (pendingSwitch == ElementId.None) return;
            if (clock > pendingSwitchUntil + 1e-6)
            {
                ElementId expired = pendingSwitch;
                pendingSwitch = ElementId.None;
                EmitSwitchDenied(expired, SwitchDeniedReason.Busy);
                return;
            }
            if (IsSwitchBusy || state == PlayerState.Dead) return;
            ElementId element = pendingSwitch;
            pendingSwitch = ElementId.None;
            if (!IsSwitchUsable(element)) return;
            if (clock < switchCooldownUntil)
            {
                EmitSwitchDenied(element, SwitchDeniedReason.Cooldown);
                return;
            }
            SwitchElement(element, false, ComboBranch.Other);
        }

        void CancelPendingSwitch()
        {
            if (pendingSwitch != ElementId.None && state != PlayerState.Dead) EmitSwitchDenied(pendingSwitch, SwitchDeniedReason.Busy);
            pendingSwitch = ElementId.None;
        }

        // Puts the player straight into a learned element while nothing is running (the tutorial starts its lessons in its
        // StartElement, J3-01): no cooldown, no switch strike, no MIX. False when the element isn't learned or an action
        // is running (nothing changes then).
        public bool SetElementAtRest(ElementId element)
        {
            if (!loadout.IsLearned(element) || IsInAction) return false;
            CancelPendingSwitch();
            if (element != activeElement) SwitchElement(element, false, ComboBranch.Other);
            switchCooldownUntil = double.NegativeInfinity;
            return true;
        }

        // The switch strike's move is starting (StartAttack, after the previous move has ended): switch now.
        void ApplySwitchStrike(ElementId element, ComboBranch branch)
        {
            if (!IsSwitchUsable(element)) return;
            SwitchElement(element, true, branch);
            actionSet = moveSet;                  // between two moves: the new one is the new element's
        }

        void SwitchElement(ElementId element, bool switchStrike, ComboBranch branch)
        {
            ElementId previous = activeElement;
            activeElement = element;
            moveSet = loadout.Get(element);
            if (!IsInAction) actionSet = moveSet;
            switchCooldownUntil = clock + Math.Max(0f, SwitchRules.Cooldown);
            Emit(new PlayerEvent
            {
                Type = PlayerEventType.ElementSwitched, Element = element, PreviousElement = previous, IsSwitchStrike = switchStrike,
                Branch = branch, Count = MixLevel, InAir = !grounded
            });
            // A held block doesn't survive a switch to a parry-only element (a parry window would have kept it busy).
            if (state == PlayerState.Guarding && (moveSet.Guard ?? FallbackGuard).IsParryOnly && !(actionSet.Guard ?? FallbackGuard).IsParryOnly)
            {
                ExitAction(false);
                state = PlayerState.Locomotion;
            }
        }

        void EmitSwitchDenied(ElementId requested, SwitchDeniedReason reason)
        {
            Emit(new PlayerEvent
            {
                Type = PlayerEventType.ElementSwitchDenied, Element = requested, DenyReason = reason, Duration = SwitchCooldownRemaining
            });
        }
    }
}
