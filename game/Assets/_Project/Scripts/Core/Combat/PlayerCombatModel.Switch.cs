using System;

namespace VaatusRevenge.Core
{
    // Element switching (hold RB + a face button; 1-4 on the keyboard). PlayerInputFrame.ElementSelect names the
    // element; the loadout says which are learned.
    public sealed partial class PlayerCombatModel
    {
        double switchCooldownUntil = double.NegativeInfinity;

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

        void ReadElementSelect(ElementId requested)
        {
            if (requested == ElementId.None) return;
            if (!loadout.IsLearned(requested))
            {
                EmitSwitchDenied(requested, SwitchDeniedReason.NotLearned);
                return;
            }
            if (requested == activeElement)
            {
                EmitSwitchDenied(requested, SwitchDeniedReason.SameElement);
                return;
            }
            if (clock < switchCooldownUntil)
            {
                EmitSwitchDenied(requested, SwitchDeniedReason.Cooldown);
                return;
            }
            SwitchElement(requested, false, ComboBranch.Other);
        }

        void SwitchElement(ElementId element, bool switchStrike, ComboBranch branch)
        {
            ElementId previous = activeElement;
            activeElement = element;
            moveSet = loadout.Get(element);
            if (!IsInAction) actionSet = moveSet;
            switchCooldownUntil = clock + Math.Max(0f, tuning.ElementSwitch != null ? tuning.ElementSwitch.Cooldown : 0f);
            Emit(new PlayerEvent
            {
                Type = PlayerEventType.ElementSwitched, Element = element, PreviousElement = previous, IsSwitchStrike = switchStrike,
                Branch = branch, Count = MixLevel, InAir = !grounded
            });
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
