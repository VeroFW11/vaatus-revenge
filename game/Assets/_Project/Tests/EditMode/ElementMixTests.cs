using NUnit.Framework;
using VaatusRevenge.Core;

namespace VaatusRevenge.Tests
{
    // MIX (Build 05, spec 2.5): every distinct element that LANDS a clean hit in the combo raises the level; all damage is
    // multiplied while the combo lives; finishers at MIX 2/3/4 hit harder / launch / break poise. Poise is never multiplied.
    public class ElementMixTests
    {
        // Lands one hit in each element in order (plain switches between them, the cooldown waited out).
        static PlayerDriver HitsIn(params ElementId[] elements)
        {
            PlayerDriver d = PlayerDriver.Elements();
            foreach (ElementId element in elements)
            {
                if (d.Model.ActiveElement != element)
                {
                    d.Run(d.FramesToReach(d.Model.Tuning.ElementSwitch.Cooldown) + 1);
                    d.Select = element;
                    d.Step();
                }
                d.Step(Pad.Light);
                d.RunUntil(x => x.Model.Phase == AttackPhase.Active, 60);
                d.LandStrikes();
                d.RunUntil(x => x.Model.State == PlayerState.Locomotion, 90);
                d.Run(2);                                        // the jab's string is over (no grace): each hit a fresh jab
            }
            return d;
        }

        [Test]
        public void MixCountsOnlyElementsThatLanded()
        {
            PlayerDriver d = HitsIn(ElementId.Fire);
            Assert.AreEqual(1, d.Model.MixLevel);
            d.Run(d.FramesToReach(d.Model.Tuning.ElementSwitch.Cooldown));
            d.Select = ElementId.Water;
            d.Step();
            d.Step(Pad.Light);                                   // a Water jab that whiffs
            d.RunUntil(x => x.Model.State == PlayerState.Locomotion, 90);
            Assert.AreEqual(1, d.Model.MixLevel, "switching alone (and whiffing) earns nothing");
            Assert.AreEqual(1 << (int)ElementId.Fire, d.Model.MixElementsMask);
            Assert.AreEqual(1, d.Count(PlayerEventType.MixChanged));
        }

        [Test]
        public void MixCapsAtDistinctCount()
        {
            PlayerDriver d = HitsIn(ElementId.Fire, ElementId.Water, ElementId.Fire, ElementId.Water);
            Assert.AreEqual(4, d.Model.ComboCount);
            Assert.AreEqual(2, d.Model.MixLevel, "Fire, Water, Fire, Water is MIX 2");
            Assert.AreEqual(2, d.Count(PlayerEventType.MixChanged));
            PlayerDriver four = HitsIn(ElementId.Fire, ElementId.Water, ElementId.Earth, ElementId.Air);
            Assert.AreEqual(4, four.Model.MixLevel);
        }

        [Test]
        public void MixMultiplierApplied()
        {
            PlayerDriver d = HitsIn(ElementId.Fire, ElementId.Water, ElementId.Earth);
            Assert.AreEqual(3, d.Model.MixLevel);
            d.Step(Pad.Light);
            float expected = d.Model.CurrentMove.Damage * d.Model.MomentumMultiplier * d.Model.Tuning.Mix.DamageFor(3);
            Assert.AreEqual(expected, d.Model.BuildCurrentDamage().Damage, 1e-3f);
            Assert.AreEqual(d.Model.Tuning.Mix.DamageFor(3), d.LastOf(PlayerEventType.MixChanged).Amount, 1e-5f);
        }

        // Runs a full on-beat string in the active element after the given MIX was built, and returns the finisher's damage.
        static DamageInfo FinisherAfter(params ElementId[] mixed)
        {
            PlayerDriver d = HitsIn(mixed);
            d.OnBeatString(4);
            Assert.IsTrue(d.LastStarted.IsFinisher);
            return d.Model.BuildCurrentDamage();
        }

        [Test]
        public void MixFinisherTiers()
        {
            MixTuning mix = PlayerTuning.CreateFluid().Mix;
            DamageInfo one = FinisherAfter(ElementId.Fire);
            DamageInfo two = FinisherAfter(ElementId.Water, ElementId.Fire);
            DamageInfo three = FinisherAfter(ElementId.Water, ElementId.Earth, ElementId.Fire);
            DamageInfo four = FinisherAfter(ElementId.Water, ElementId.Earth, ElementId.Air, ElementId.Fire);
            Assert.AreEqual(one.Damage * mix.DamageFor(2) / mix.DamageFor(1) * mix.FinisherDamage, two.Damage, 0.05f, "MIX 2: harder");
            Assert.Less(two.LaunchSpeed, mix.FinisherLaunchSpeed - 0.5f + 1f);
            Assert.AreEqual(mix.FinisherLaunchSpeed, three.LaunchSpeed, 1e-4f, "MIX 3: launches");
            Assert.AreEqual(float.MaxValue, four.PoiseDamage, "MIX 4: breaks poise");
            Assert.Less(three.PoiseDamage, 100f);

            // The poise break still respects stagger immunity (the enemy decides).
            EnemyBrain soldier = EnemyBrain.Create(EnemyTuning.CreateDaoSoldier(), null, 50, 1);
            var world = new EnemyWorldState { Grounded = true, SelfRadius = 0.4f, HasTarget = true, TargetPosition = new System.Numerics.Vector3(0f, 0f, 2f) };
            soldier.Tick(1f / 60f, world);
            four.SourceTeam = Team.Player;
            four.LaunchSpeed = 0f;                               // (a launch is its own reaction; this checks the poise break)
            Assert.IsTrue(soldier.ReceiveHit(four, soldier.Forward).PoiseBroken, "a fresh soldier staggers");
            for (int i = 0; i < 70; i++) soldier.Tick(1f / 60f, world);   // the stagger ends; immunity starts
            Assert.IsTrue(soldier.IsStaggerImmune);
            four.AttackId = CombatIds.Next();
            Assert.IsFalse(soldier.ReceiveHit(four, soldier.Forward).PoiseBroken, "immune: no second stagger");
        }

        [Test]
        public void MixFinisherEventAndMeterRefill()
        {
            PlayerDriver d = HitsIn(ElementId.Water, ElementId.Earth, ElementId.Air, ElementId.Fire);
            Assert.AreEqual(0f, d.Model.MeterFraction(ElementId.Fire), 0.3f);
            float before = d.Model.MeterFraction(ElementId.Fire);
            d.OnBeatString(4);
            d.RunUntil(x => x.Model.Phase == AttackPhase.Active, 60);
            MoveData move = d.Model.CurrentMove;
            d.LandStrikes();
            d.Step();                                            // events raised between frames arrive with the next one
            PlayerEvent finisher = d.LastOf(PlayerEventType.MixFinisher);
            Assert.AreEqual(4, finisher.Count);
            Assert.AreSame(move, finisher.Move);
            Assert.GreaterOrEqual(d.Model.MeterFraction(ElementId.Fire), before + d.Model.Tuning.Mix.FinisherMeterRefill - 1e-3f);
        }

        [Test]
        public void MixClearedOnComboEnd()
        {
            PlayerDriver d = HitsIn(ElementId.Fire, ElementId.Water);
            Assert.AreEqual(2, d.Model.MixLevel);
            d.HitFromFront(5f);
            Assert.AreEqual(0, d.Model.ComboCount);
            Assert.AreEqual(0, d.Model.MixLevel);
            Assert.AreEqual(0, d.Model.MixElementsMask);
            d.Step(Pad.Light);
            d.RunUntil(x => x.Model.Phase == AttackPhase.Active, 60);
            d.LandStrikes();
            Assert.AreEqual(1, d.Model.MixLevel, "a new combo starts from scratch");
        }

        [Test]
        public void SwitchStrikeBonusesApplyToOneHitOnly()
        {
            PlayerDriver d = PlayerDriver.Elements();
            d.OnBeatString(1);
            d.SwitchOnBeat(ElementId.Water);
            d.RunUntilStarted(3);
            Assert.IsTrue(d.LastStarted.IsSwitchStrike);
            ElementSwitchTuning s = d.Model.Tuning.ElementSwitch;
            float onBeat = d.Model.Tuning.Rhythm.OnBeatDamageMultiplier;
            DamageInfo strike = d.Model.BuildCurrentDamage();
            Assert.AreEqual(d.Model.CurrentMove.Damage * d.Model.MomentumMultiplier * onBeat * s.SwitchStrikeDamageMultiplier, strike.Damage, 1e-3f);
            Assert.AreEqual(d.Model.CurrentMove.PoiseDamage * s.SwitchStrikePoiseMultiplier, strike.PoiseDamage, 1e-4f);
            d.PressOnBeat();
            d.RunUntilStarted(4);
            Assert.IsFalse(d.LastStarted.IsSwitchStrike);
            DamageInfo next = d.Model.BuildCurrentDamage();
            Assert.AreEqual(d.Model.CurrentMove.Damage * d.Model.MomentumMultiplier * onBeat, next.Damage, 1e-3f, "the next hit is ordinary");
            Assert.AreEqual(d.Model.CurrentMove.PoiseDamage, next.PoiseDamage, 1e-4f);
        }

        [Test]
        public void MixNeverScalesPoise()
        {
            PlayerDriver d = HitsIn(ElementId.Fire, ElementId.Water, ElementId.Earth);
            d.Step(Pad.Light);
            Assert.AreEqual(d.Model.CurrentMove.PoiseDamage, d.Model.BuildCurrentDamage().PoiseDamage, 1e-5f);
        }
        // Round 7, S7-12: the switch flash / MIX ring colours are equally bright and at least MinHueGap degrees apart in
        // hue (Fire and Earth were 12 apart, Water and Air 14).
        [Test]
        public void SwitchFlashColoursAreDistinctAndEquallyBright()
        {
            var elements = new[] { ElementId.Fire, ElementId.Water, ElementId.Earth, ElementId.Air };
            for (int a = 0; a < elements.Length; a++)
            {
                System.Numerics.Vector3 ca = ElementSwitchPalette.For(elements[a]);
                Assert.That(ElementSwitchPalette.Luminance(ca), Is.InRange(0.55f, 0.7f), elements[a] + " brightness");
                for (int b = a + 1; b < elements.Length; b++)
                {
                    float gap = ElementSwitchPalette.HueGap(ElementSwitchPalette.Hue(ca), ElementSwitchPalette.Hue(ElementSwitchPalette.For(elements[b])));
                    Assert.GreaterOrEqual(gap, ElementSwitchPalette.MinHueGap, elements[a] + " vs " + elements[b] + " hue gap");
                }
            }
            Assert.AreEqual(0f, ElementSwitchPalette.Hue(new System.Numerics.Vector3(1f, 0f, 0f)), 1e-3f);
            Assert.AreEqual(120f, ElementSwitchPalette.Hue(new System.Numerics.Vector3(0f, 1f, 0f)), 1e-3f);
            Assert.AreEqual(240f, ElementSwitchPalette.Hue(new System.Numerics.Vector3(0f, 0f, 1f)), 1e-3f);
        }
    }
}
