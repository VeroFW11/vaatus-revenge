using System.Numerics;
using NUnit.Framework;
using VaatusRevenge.Core;

namespace VaatusRevenge.Tests
{
    // Element switching (Build 05, spec 2.6): instant when free; mid-string a switch strike that switches at the cancel
    // point and carries the string's slot into the new element; denied on cooldown (the string goes on), when not
    // learned or the same; buffered while busy; a running action finishes with the element it started with.
    public class ElementSwitchTests
    {
        static PlayerDriver Driver(ElementLoadout loadout = null, PlayerTuning tuning = null)
        {
            return PlayerDriver.Elements(tuning, loadout);
        }

        [Test]
        public void PlainSwitchWhenFreeIsInstant()
        {
            PlayerDriver d = Driver();
            d.Select = ElementId.Water;
            d.Step();
            Assert.AreEqual(ElementId.Water, d.Model.ActiveElement);
            Assert.AreSame(d.Model.Loadout.Water, d.Model.MoveSet);
            PlayerEvent e = d.LastOf(PlayerEventType.ElementSwitched);
            Assert.AreEqual(ElementId.Water, e.Element);
            Assert.AreEqual(ElementId.Fire, e.PreviousElement);
            Assert.IsFalse(e.IsSwitchStrike);
            Assert.Greater(d.Model.SwitchCooldown01, 0.9f, "the cooldown wipe starts full");
            Assert.AreEqual(d.Model.Tuning.ElementSwitch.Cooldown, d.Model.SwitchCooldownRemaining, 1e-4f);
        }

        [Test]
        public void SwitchStrikeSwitchesAtCancelPointNotAtPress()
        {
            PlayerDriver d = Driver();
            d.Step(Pad.Light);
            d.SwitchOnBeat(ElementId.Water);
            Assert.AreEqual(ElementId.Fire, d.Model.ActiveElement, "not at the press");
            Assert.AreEqual(PlayerCommand.SwitchStrike, d.Model.BufferedCommand);
            Assert.AreEqual(BeatGrade.OnBeat, d.LastOf(PlayerEventType.BeatJudged).Grade, "judged like a Light press");
            d.RunUntilStarted(2);
            Assert.AreEqual(ElementId.Water, d.Model.ActiveElement);
            Assert.AreEqual(d.FramesToReach(d.Model.Loadout.Fire.LightChain[0].ChainCancelAt), d.FirstFrame(PlayerEventType.ElementSwitched));
            PlayerEvent e = d.LastOf(PlayerEventType.ElementSwitched);
            Assert.IsTrue(e.IsSwitchStrike);
            Assert.AreEqual(ComboBranch.Main, e.Branch);
            Assert.AreEqual(ElementId.Water, d.LastStarted.Element);
            Assert.IsTrue(d.LastStarted.IsSwitchStrike);
        }

        [Test]
        public void ChainIndexCarriesToNewElement()
        {
            PlayerDriver d = Driver();
            d.OnBeatString(1);                                   // X X (Fire)
            d.SwitchOnBeat(ElementId.Water);                     // RB + X (Water)
            d.RunUntilStarted(3);
            Assert.AreEqual(2, d.LastStarted.ChainIndex);
            Assert.AreSame(d.Model.Loadout.Water.LightChain[2], d.LastStarted.Move);
            d.PressOnBeat();
            d.RunUntilStarted(4);
            d.PressOnBeat();
            d.RunUntilStarted(5);
            Assert.AreSame(d.Model.Loadout.Water.LightChain[4], d.LastStarted.Move, "X X RB+X X X ends on Water's finisher");
            Assert.IsTrue(d.LastStarted.IsFinisher);
        }

        [Test]
        public void ShorterChainClampsToFinisher()
        {
            ElementLoadout loadout = ElementLoadout.CreateFluid();
            loadout.Water.LightChain = new[] { loadout.Water.LightChain[0], loadout.Water.LightChain[1], loadout.Water.LightChain[4] };
            PlayerDriver d = Driver(loadout);
            d.OnBeatString(2);                                   // Fire hit 3 (index 2)
            d.SwitchOnBeat(ElementId.Water);                     // index 3 doesn't exist in a 3-hit chain
            d.RunUntilStarted(4);
            Assert.AreEqual(2, d.LastStarted.ChainIndex, "the new element's finisher");
            Assert.IsTrue(d.LastStarted.IsFinisher);
        }

        [Test]
        public void AfterFinisherSwitchStartsAtZero()
        {
            PlayerDriver d = Driver();
            d.OnBeatString(4);
            Assert.IsTrue(d.LastStarted.IsFinisher);
            d.SwitchOnBeat(ElementId.Earth);
            d.RunUntilStarted(6, 90);
            Assert.AreEqual(ElementId.Earth, d.LastStarted.Element);
            Assert.AreEqual(0, d.LastStarted.ChainIndex);
        }

        [Test]
        public void PauseBranchSwitchContinuesPauseChain()
        {
            PlayerDriver d = Driver();
            d.OnBeatString(1);
            PlayerEvent second = d.LastStarted;
            double until = d.Model.Clock + (second.Move.ComboWindowEnd + 0.03f) / second.PlaybackRate;
            d.RunUntil(x => x.Model.Clock >= until, 120);
            d.Step(Pad.Light);
            d.RunUntilStarted(3);
            Assert.AreEqual(ComboBranch.Pause, d.LastStarted.Branch);
            d.SwitchOnBeat(ElementId.Water);
            d.RunUntilStarted(4);
            Assert.AreEqual(ComboBranch.Pause, d.LastStarted.Branch);
            Assert.AreEqual(1, d.LastStarted.ChainIndex);
            Assert.AreSame(d.Model.Loadout.Water.PauseChain[1], d.LastStarted.Move);
        }

        [Test]
        public void AirSwitchSharesAirAttackCap()
        {
            ElementLoadout loadout = ElementLoadout.CreateFluid();
            loadout.Fire.Aerial.AirAttacksPerJump = 3;
            loadout.Water.Aerial.AirAttacksPerJump = 2;
            loadout.Water.AirChain = new[] { loadout.Water.AirChain[0], loadout.Water.AirChain[1], loadout.Water.AirChain[0] };
            PlayerDriver d = Driver(loadout);
            d.Step(Pad.Jump);
            d.Run(5);
            d.Step(Pad.Light);
            Assert.AreEqual(1, d.Model.AirAttacksUsed);
            d.SwitchOnBeat(ElementId.Water);
            d.RunUntilStarted(2, 60);
            Assert.AreEqual(ElementId.Water, d.LastStarted.Element);
            Assert.AreEqual(1, d.LastStarted.ChainIndex, "the air string's slot carries over");
            Assert.AreEqual(2, d.Model.AirAttacksUsed, "shared across elements");
            d.PressOnBeat();
            d.Run(30);
            Assert.AreEqual(2, d.Started, "Water's cap (2) is reached: switching can't stretch a juggle");
        }

        [Test]
        public void CooldownDeniedStrikeStillContinuesStringInOldElement()
        {
            PlayerDriver d = Driver();
            d.Select = ElementId.Water;
            d.Step();
            d.Step(Pad.Light);                                   // Water jab
            d.Select = ElementId.Earth;                          // still cooling down
            d.Step();
            PlayerEvent denied = d.LastOf(PlayerEventType.ElementSwitchDenied);
            Assert.AreEqual(SwitchDeniedReason.Cooldown, denied.DenyReason);
            Assert.Greater(denied.Duration, 0f);
            Assert.AreEqual(PlayerCommand.Light, d.Model.BufferedCommand, "played as X: the string never drops");
            d.RunUntilStarted(2);
            Assert.AreEqual(ElementId.Water, d.LastStarted.Element);
            Assert.AreEqual(1, d.LastStarted.ChainIndex);
        }

        [Test]
        public void NotLearnedDeniedWithEvent()
        {
            ElementLoadout loadout = ElementLoadout.CreateFluid();
            loadout.LearnedAir = false;
            PlayerDriver d = Driver(loadout);
            Assert.IsFalse(d.Model.IsLearned(ElementId.Air));
            d.Select = ElementId.Air;
            d.Step();
            Assert.AreEqual(ElementId.Fire, d.Model.ActiveElement);
            PlayerEvent e = d.LastOf(PlayerEventType.ElementSwitchDenied);
            Assert.AreEqual(SwitchDeniedReason.NotLearned, e.DenyReason);
            Assert.AreEqual(ElementId.Air, e.Element);
        }

        [Test]
        public void SameElementDeniedWithoutToast()
        {
            PlayerDriver d = Driver();
            d.Select = ElementId.Fire;
            d.Step();
            Assert.AreEqual(SwitchDeniedReason.SameElement, d.LastOf(PlayerEventType.ElementSwitchDenied).DenyReason);
            Assert.AreEqual(0, d.Count(PlayerEventType.ElementSwitched));
        }

        // Build 05 verify J-04: RB still held for the next X mid-string (X is also Water's button, so in Water that's a
        // same-element pick): the press carries the string on in the current element instead of being swallowed.
        [Test]
        public void SameElementMidStringContinuesString()
        {
            PlayerDriver d = Driver();
            d.Target(new Vector3(0f, 0f, 1.5f));
            d.OnBeatString(1);                                   // X X (Fire)
            d.SwitchOnBeat(ElementId.Water);                     // RB + X: hit 3 in Water
            d.RunUntilStarted(3);
            Assert.AreEqual(ElementId.Water, d.Model.ActiveElement);
            d.SwitchOnBeat(ElementId.Water);                     // RB still down, X again: same element
            Assert.AreEqual(0, d.Count(PlayerEventType.ElementSwitchDenied), "the string going on is no denial (J3-S01: no wheel shake)");
            Assert.AreEqual(PlayerCommand.Light, d.Model.BufferedCommand, "played as X: the string never drops");
            d.RunUntilStarted(4);
            Assert.AreEqual(ElementId.Water, d.LastStarted.Element);
            Assert.AreEqual(3, d.LastStarted.ChainIndex, "hit 4 of the string");
            Assert.AreEqual(BeatGrade.OnBeat, d.LastStarted.Grade);
        }

        [Test]
        public void SameElementWhenFreeStillDoesNothing()
        {
            PlayerDriver d = Driver();
            d.Select = ElementId.Fire;
            d.Step();
            Assert.AreEqual(PlayerCommand.None, d.Model.BufferedCommand);
            d.Run(10);
            Assert.AreEqual(0, d.Started);
        }

        // Build 05 verify J-05: "put one X between two switches" holds in every element order on Fluid, including Air
        // (its quick hit) in the middle: X, RB+a, X, RB+b, X all on the beat gets both switch strikes.
        [Test]
        public void OneXBetweenSwitchesWorksForEveryElementOrder()
        {
            Assert.IsEmpty(SwitchOrderFailures(false, 1), "Fluid, one X between: the second switch was denied");
        }

        // Punishing's longer cooldown asks for two X between switches (How-To-Play says so), in every order.
        [Test]
        public void PunishingTwoXBetweenSwitchesWorksForEveryElementOrder()
        {
            Assert.IsEmpty(SwitchOrderFailures(true, 2), "Punishing, two X between: the second switch was denied");
        }

        static System.Collections.Generic.List<string> SwitchOrderFailures(bool punishing, int xBetween)
        {
            var failures = new System.Collections.Generic.List<string>();
            for (ElementId start = ElementId.Fire; start <= ElementId.Air; start++)
            for (ElementId a = ElementId.Fire; a <= ElementId.Air; a++)
            for (ElementId b = ElementId.Fire; b <= ElementId.Air; b++)
            {
                if (a == start || b == a) continue;
                PlayerDriver d = punishing ? Driver(ElementLoadout.CreatePunishing(), PlayerTuning.CreatePunishing()) : Driver();
                d.Target(new Vector3(0f, 0f, 1.5f));
                if (start != ElementId.Fire)
                {
                    d.Select = start;
                    d.Step();
                }
                d.Run(45);                                       // the setup switch's cooldown is over
                d.ClearLog();
                int n = d.Started;
                d.Step(Pad.Light);
                d.RunUntilStarted(n + 1);
                d.SwitchOnBeat(a);
                n += 2;
                d.RunUntilStarted(n);
                for (int x = 0; x < xBetween; x++)
                {
                    d.PressOnBeat();
                    d.RunUntilStarted(++n);
                }
                d.SwitchOnBeat(b);
                d.RunUntilStarted(++n);
                int strikes = 0;
                foreach (PlayerEvent e in d.All(PlayerEventType.ElementSwitched)) if (e.IsSwitchStrike) strikes++;
                if (strikes != 2 || d.Model.ActiveElement != b) failures.Add(start + ">" + a + ">" + b);
            }
            return failures;
        }

        [Test]
        public void BusyWhileChargingBufferedThenApplied()
        {
            PlayerDriver d = Driver();
            d.Step(Pad.Heavy);
            Assert.AreEqual(PlayerState.Charging, d.Model.State);
            d.Select = ElementId.Water;
            d.Step(Pad.Heavy);
            Assert.AreEqual(ElementId.Fire, d.Model.ActiveElement, "waits while charging");
            d.Run(3, Pad.None);                                  // release: the heavy fires (an attack: not busy)
            Assert.AreEqual(ElementId.Water, d.Model.ActiveElement, "applied once free of the charge");
            Assert.AreEqual(ElementId.Fire, d.LastStarted.Element, "the heavy itself stays Fire's");
        }

        [Test]
        public void BufferedSwitchClearedByStagger()
        {
            PlayerDriver d = Driver();
            d.Step(Pad.Heavy);
            d.Select = ElementId.Water;
            d.Step(Pad.Heavy);
            d.HitFromFront(5f, 999f);
            d.Step(Pad.Heavy);
            Assert.AreEqual(PlayerState.Staggered, d.Model.State);
            Assert.AreEqual(SwitchDeniedReason.Busy, d.LastOf(PlayerEventType.ElementSwitchDenied).DenyReason);
            d.RunUntil(x => x.Model.State == PlayerState.Locomotion, 120);
            Assert.AreEqual(ElementId.Fire, d.Model.ActiveElement);
        }

        [Test]
        public void GuardDroppedWhenSwitchingToParryOnly()
        {
            ElementLoadout loadout = ElementLoadout.CreateFluid();
            loadout.Earth.Guard.Style = DefenseStyle.BlockAndParry;
            PlayerDriver d = Driver(loadout);
            d.Select = ElementId.Earth;
            d.Step();
            d.Run(20, Pad.Guard);                                // holding a block, past the deflect window
            Assert.AreEqual(PlayerState.Guarding, d.Model.State);
            d.Run(20, Pad.Guard);                                // past the switch cooldown
            d.Select = ElementId.Fire;
            d.Step(Pad.Guard);
            Assert.AreEqual(ElementId.Fire, d.Model.ActiveElement);
            Assert.AreNotEqual(PlayerState.Guarding, d.Model.State, "Fire can't hold a block");
            Assert.AreEqual(1, d.Count(PlayerEventType.GuardEnded));

            // And back into a blocking element with LB still held: the block rises.
            d.Run(20, Pad.Guard);
            d.Select = ElementId.Earth;
            d.Step(Pad.Guard);
            d.Step(Pad.Guard);
            Assert.AreEqual(PlayerState.Guarding, d.Model.State);
        }

        [Test]
        public void RunningActionFinishesWithStartElement()
        {
            PlayerDriver d = Driver();
            d.Step(Pad.AbilityNorth);                            // Fire Whip
            d.Select = ElementId.Water;
            d.Step();
            Assert.AreEqual(ElementId.Water, d.Model.ActiveElement, "a non-string attack doesn't block a plain switch");
            d.RunUntil(x => x.Model.State == PlayerState.Locomotion, 90);
            foreach (PlayerEvent e in d.Log)
            {
                if (e.Type == PlayerEventType.AttackStarted || e.Type == PlayerEventType.AttackActiveStart || e.Type == PlayerEventType.AttackEnded)
                    Assert.AreEqual(ElementId.Fire, e.Element, e.Type.ToString());
            }
        }

        [Test]
        public void FireMomentumSurvivesSwitchAwayAndDecaysNormally()
        {
            PlayerDriver d = Driver();
            d.Target(new Vector3(0f, 0f, 1.5f));
            d.OnBeatString(2);
            for (int i = 0; i < d.Log.Count; i++)
            {
                if (d.Log[i].Type == PlayerEventType.AttackStarted) d.Model.OnAttackLanded(new HitResult { Outcome = HitOutcome.Hit }, d.Log[i].AttackId);
            }
            float fire = d.Model.MeterFraction(ElementId.Fire);
            Assert.Greater(fire, 0f);
            d.RunUntil(x => x.Model.State == PlayerState.Locomotion, 90);
            d.Select = ElementId.Water;
            d.Step();
            Assert.AreEqual(0f, d.Model.Momentum, "Water has no meter");
            Assert.AreEqual(fire, d.Model.MeterFraction(ElementId.Fire), 0.02f, "Fire's isn't emptied by the switch");
            d.Run(d.FramesToReach(d.Model.Loadout.Fire.Momentum.DecayDelay + 1f));
            Assert.Less(d.Model.MeterFraction(ElementId.Fire), fire, "and drains with Fire's own rules");
        }

        [Test]
        public void InFlightProjectileKeepsElementMultiplier()
        {
            PlayerDriver d = Driver();
            d.Target(new Vector3(0f, 0f, 1.5f));
            d.OnBeatString(3);
            for (int i = 0; i < d.Log.Count; i++)
            {
                if (d.Log[i].Type == PlayerEventType.AttackStarted) d.Model.OnAttackLanded(new HitResult { Outcome = HitOutcome.Hit }, d.Log[i].AttackId);
            }
            d.RunUntil(x => x.Model.State == PlayerState.Locomotion, 90);
            d.Step(Pad.Skill);
            d.RunUntil(x => x.Count(PlayerEventType.ProjectileLaunched) > 0, 60);
            PlayerEvent blast = d.LastOf(PlayerEventType.ProjectileLaunched);
            d.RunUntil(x => x.Model.State == PlayerState.Locomotion, 60);
            float fireMultiplier = d.Model.MomentumMultiplier;
            Assert.Greater(fireMultiplier, 1f);
            d.Select = ElementId.Water;
            d.Step();
            Assert.AreEqual(1f, d.Model.MomentumMultiplier, "Water's own (none)");
            Assert.AreEqual(ElementId.Fire, blast.Element);
            DamageInfo damage = d.Model.BuildDamage(in blast);
            Assert.AreEqual(ElementId.Fire, damage.Element);
            Assert.AreEqual(blast.Move.Damage * fireMultiplier, damage.Damage, 1e-3f, "the Fire Blast in flight keeps Fire's Momentum");
        }

        [Test]
        public void OldSingleSetConstructorStillWorks()
        {
            var model = new PlayerCombatModel(PlayerTuning.CreateFluid(), ElementMoveSet.CreateFireFluid());
            Assert.AreEqual(ElementId.Fire, model.ActiveElement);
            Assert.IsTrue(model.IsLearned(ElementId.Fire));
            Assert.IsFalse(model.IsLearned(ElementId.Water));
            var d = new PlayerDriver();
            d.Select = ElementId.Water;
            d.Step();
            Assert.AreEqual(SwitchDeniedReason.NotLearned, d.LastOf(PlayerEventType.ElementSwitchDenied).DenyReason);
            d.Tap(Pad.Light);
            Assert.AreEqual(1, d.Started);
        }
    }
}
