using NUnit.Framework;
using VaatusRevenge.Core;

namespace VaatusRevenge.Tests
{
    // Build 05 verify round 2: the pure-C# halves of the fixes (the Unity halves are in PlayerFeedback, the HUD and the
    // input reader; PadChordReaderTests covers the chord reader).
    public class VerifyRound2Tests
    {
        // R2-03: Earth off the ground is dust, never rock: one rule for every effect path.
        [Test]
        public void EarthInTheAirIsDustOnly()
        {
            Assert.IsTrue(ElementFxRules.DustOnly(ElementId.Earth, true));
            Assert.IsFalse(ElementFxRules.DustOnly(ElementId.Earth, false), "on the ground Earth throws rock");
            foreach (ElementId other in new[] { ElementId.Fire, ElementId.Water, ElementId.Air })
                Assert.IsFalse(ElementFxRules.DustOnly(other, true), other + " has no rock to drop");
            Assert.IsTrue(ElementFxRules.IsAirborne(new PlayerEvent { Branch = ComboBranch.Air }), "an air-string strike");
            Assert.IsTrue(ElementFxRules.IsAirborne(new PlayerEvent { AttackKind = PlayerAttackKind.Air }));
            Assert.IsTrue(ElementFxRules.IsAirborne(new PlayerEvent { InAir = true }), "an air dash, an air switch");
            Assert.IsFalse(ElementFxRules.IsAirborne(new PlayerEvent()));
            Assert.IsTrue(ElementFxRules.IsAirborneAttacker(false, PlayerAttackKind.Light), "a hit spark from the air");
            Assert.IsTrue(ElementFxRules.IsAirborneAttacker(true, PlayerAttackKind.Air), "the air string's landing frame");
            Assert.IsFalse(ElementFxRules.IsAirborneAttacker(true, PlayerAttackKind.Light));
        }

        // R2-03: the switch event says it happened in the air, so the flourish can drop the rock.
        [Test]
        public void SwitchingToEarthInTheAirIsMarkedInAir()
        {
            PlayerDriver d = PlayerDriver.Elements();
            d.Step(Pad.Jump);
            d.Run(6);
            Assert.IsFalse(d.Model.IsGrounded);
            d.Select = ElementId.Earth;
            d.Step();
            PlayerEvent e = d.LastOf(PlayerEventType.ElementSwitched);
            Assert.AreEqual(ElementId.Earth, e.Element);
            Assert.IsTrue(e.InAir);
            Assert.IsTrue(ElementFxRules.DustOnly(e.Element, e.InAir));
        }

        // R2-06: How-To-Play's Punishing three-element recipe really launches: X X, RB + X, X X, then X X, RB + A, X X. Fire
        // and Water land in the first string, Earth before the second string's finisher, so that finisher is MIX 3.
        [Test]
        public void PunishingTwoStringThreeElementRecipeLaunches()
        {
            PlayerDriver d = PlayerDriver.Elements(PlayerTuning.CreatePunishing(), ElementLoadout.CreatePunishing());
            int started = 0;
            foreach (ElementId[] presses in new[]
            {
                new[] { ElementId.None, ElementId.None, ElementId.Water, ElementId.None, ElementId.None },
                new[] { ElementId.None, ElementId.None, ElementId.Earth, ElementId.None, ElementId.None },
            })
            {
                d.RunUntil(x => x.Model.State == PlayerState.Locomotion, 120);
                for (int i = 0; i < presses.Length; i++)
                {
                    if (i == 0) d.Step(Pad.Light);
                    else if (presses[i] == ElementId.None) d.PressOnBeat();
                    else d.SwitchOnBeat(presses[i]);
                    if (i > 0) d.LandStrikes();                      // the previous hit, landing on its beat
                    d.RunUntilStarted(++started);
                }
                d.RunUntil(x => x.Model.Phase == AttackPhase.Active, 60);
                d.LandStrikes();                                     // the finisher
                d.Step();                                            // events raised by a landing arrive with the next tick
                Assert.IsTrue(d.LastStarted.IsFinisher);
            }
            Assert.AreEqual(3, d.Model.MixLevel);
            PlayerEvent finisher = d.LastOf(PlayerEventType.MixFinisher);
            Assert.AreEqual(3, finisher.Count, "the second string's finisher is a MIX 3 finisher (it launches)");
            Assert.AreEqual(ElementId.Earth, finisher.Element);
        }
    }
}
