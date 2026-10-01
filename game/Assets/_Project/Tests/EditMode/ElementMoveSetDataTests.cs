using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using VaatusRevenge.Core;

namespace VaatusRevenge.Tests
{
    // The Water, Earth and Air move sets (Build 05 spec 3.2-3.6, acceptance 6.2): the poise budgets that keep one
    // single-element string from staggering a fresh soldier, the frame-data rules every string move obeys, the beat
    // validation, multi-hit moves fitting their active frames, closed effect shapes, real animation keys, string dps in
    // range, the per-element dodge, guard and air settings, and Punishing being exactly ApplyPunishing(Fluid). The string
    // moves are also pinned to the spec tables, so a change to them is a deliberate one.
    public class ElementMoveSetDataTests
    {
        static readonly ElementId[] NewElements = { ElementId.Water, ElementId.Earth, ElementId.Air };

        static ElementMoveSet Fluid(ElementId element)
        {
            switch (element)
            {
                case ElementId.Water: return ElementMoveSet.CreateWaterFluid();
                case ElementId.Earth: return ElementMoveSet.CreateEarthFluid();
                case ElementId.Air: return ElementMoveSet.CreateAirFluid();
                default: return ElementMoveSet.CreateFireFluid();
            }
        }

        static ElementMoveSet Punishing(ElementId element)
        {
            switch (element)
            {
                case ElementId.Water: return ElementMoveSet.CreateWaterPunishing();
                case ElementId.Earth: return ElementMoveSet.CreateEarthPunishing();
                case ElementId.Air: return ElementMoveSet.CreateAirPunishing();
                default: return ElementMoveSet.CreateFirePunishing();
            }
        }

        static IEnumerable<ElementMoveSet> BothPresets(ElementId element)
        {
            yield return Fluid(element);
            yield return Punishing(element);
        }

        static IEnumerable<MoveData> StringMoves(ElementMoveSet set)
        {
            foreach (MoveData m in set.LightChain) yield return m;
            foreach (MoveData m in set.PauseChain) yield return m;
            foreach (MoveData m in set.AirChain) yield return m;
            yield return set.DodgeStrike;
        }

        static IEnumerable<MoveData> AllMoves(ElementMoveSet set)
        {
            foreach (MoveData m in StringMoves(set)) yield return m;
            yield return set.Launcher;
            yield return set.AbilityNorth;
            yield return set.AbilityEast;
            yield return set.Heavy;
            yield return set.SprintAttack;
            yield return set.PlungeAttack;
            yield return set.Skill;
            yield return set.ZipStrike;
        }

        static float Poise(MoveData m) => m.PoiseDamage * Math.Max(1, m.HitCount);

        static float SoldierPoise => EnemyTuning.CreateDaoSoldier().MaxPoise;

        // Build 05 verify round 4 (J4-03): Earth's thrown boulders (Boulder Toss, Boulder Hurl) are drawn inside their hit size
        // (the rock's longest side, 0.9 of Radius x 2 x VisualScale, no bigger than the hit diameter), in both presets.
        [Test]
        public void EarthBouldersAreDrawnInsideTheirHitSize([Values(false, true)] bool punishing)
        {
            ElementMoveSet earth = (punishing ? ElementLoadout.CreatePunishing() : ElementLoadout.CreateFluid()).Get(ElementId.Earth);
            int boulders = 0;
            foreach (MoveData move in AllMoves(earth))
            {
                if (move == null || !move.LaunchesProjectile || move.Projectile == null) continue;
                boulders++;
                float drawn = move.Projectile.Radius * 2f * move.Projectile.VisualScale * 0.9f;
                Assert.LessOrEqual(drawn, move.Projectile.Radius * 2f + 1e-4f, move.DisplayName + " is drawn bigger than it hits");
            }
            Assert.GreaterOrEqual(boulders, 2, "Boulder Toss and Boulder Hurl");
        }

        [Test]
        public void TheSoldierPoiseBudgetIsTheSpecs()
        {
            Assert.AreEqual(52f, SoldierPoise, "the budgets below are written against a 52-poise soldier");
        }

        [Test]
        public void OneStringPlusAJabNeverStaggersAFreshSoldier()
        {
            foreach (ElementId element in NewElements)
            foreach (ElementMoveSet set in BothPresets(element))
            {
                float hit1 = Poise(set.LightChain[0]);
                float main = set.LightChain.Sum(Poise) + hit1;
                Assert.Less(main, SoldierPoise, element + ": light string + hit 1");

                float pause = hit1 + set.PauseChain.Sum(Poise);
                for (int i = 0; i <= set.Rhythm.PauseAfterIndex; i++) pause += Poise(set.LightChain[i]);
                Assert.Less(pause, SoldierPoise, element + ": the pause path + hit 1");

                // The dodge strike takes a slot of the string (never the finisher's: a dodge never skips your finisher).
                for (int slot = 0; slot < set.LightChain.Length - 1; slot++)
                {
                    float substituted = main - Poise(set.LightChain[slot]) + Poise(set.DodgeStrike);
                    Assert.Less(substituted, SoldierPoise, element + ": the dodge strike in slot " + slot + " + hit 1");
                }
            }
        }

        [Test]
        public void StringMovesCancelAfterTheyHitAndOpenTheirWindowByTheCancel()
        {
            foreach (ElementId element in NewElements)
            foreach (ElementMoveSet set in BothPresets(element))
            foreach (MoveData m in StringMoves(set))
            {
                Assert.GreaterOrEqual(m.ChainCancelAt, m.ActiveEnd - 1e-4f, element + " " + m.DisplayName + ": ChainCancelAt >= ActiveEnd");
                Assert.LessOrEqual(m.ComboWindowStart, m.ChainCancelAt + 1e-4f, element + " " + m.DisplayName + ": ComboWindowStart <= ChainCancelAt");
            }
        }

        [Test]
        public void MultiHitMovesFitTheirSubHitsInsideTheActiveFrames()
        {
            foreach (ElementId element in NewElements)
            foreach (ElementMoveSet set in BothPresets(element))
            foreach (MoveData m in AllMoves(set))
            {
                Assert.GreaterOrEqual(m.HitCount, 1, m.DisplayName);
                Assert.GreaterOrEqual(m.Active, (m.HitCount - 1) * m.HitInterval - 1e-4f, element + " " + m.DisplayName + ": Active >= (HitCount - 1) x HitInterval");
                if (m.HitCount > 1) Assert.Greater(m.HitInterval, 0f, element + " " + m.DisplayName + ": sub-hits need an interval");
            }
        }

        // Spec 2.4.3, with the element's own beat deltas and on-beat rate, and the switch strike's extra late window.
        [Test]
        public void EveryStringMovePassesTheBeatValidation()
        {
            foreach (bool punishing in new[] { false, true })
            {
                PlayerTuning tuning = punishing ? PlayerTuning.CreatePunishing() : PlayerTuning.CreateFluid();
                RhythmTuning r = tuning.Rhythm;
                foreach (ElementId element in NewElements)
                {
                    ElementMoveSet set = punishing ? Punishing(element) : Fluid(element);
                    ElementRhythm er = set.Rhythm;
                    float rOn = er.OnBeatPlaybackRate > 0f ? er.OnBeatPlaybackRate : r.OnBeatPlaybackRate;
                    float early = r.BeatEarly + er.BeatEarlyDelta;
                    float late = r.BeatLate + er.BeatLateDelta + tuning.ElementSwitch.SwitchStrikeBeatLateBonus;
                    var moves = new List<(MoveData move, bool chains)>();
                    moves.AddRange(set.LightChain.Select(m => (m, true)));
                    moves.AddRange(set.PauseChain.Select(m => (m, true)));
                    moves.AddRange(set.AirChain.Select((m, i) => (m, i < set.AirChain.Length - 1)));
                    moves.Add((set.DodgeStrike, true));
                    foreach ((MoveData m, bool chains) in moves)
                    {
                        string what = (punishing ? "Punishing " : "Fluid ") + element + " " + m.DisplayName;
                        Assert.GreaterOrEqual(m.ActiveStart / rOn - early, 0f, what + ": the beat window opens before the move starts");
                        if (chains) Assert.LessOrEqual(m.ActiveStart + late, m.ComboWindowEnd + 1e-4f, what + ": the late beat runs past the combo window");
                        Assert.Less(m.ComboWindowEnd, m.TotalDuration + r.PauseGrace, what + ": combo window vs the pause band");
                    }
                }
            }
        }

        // Spec 3.6: each element's string damage over the time to the end of its finisher at 1.0x, each hit chained at its
        // cancel point, stays in 22-32 dps (Fire, the reference, is 28.6).
        [Test]
        public void StringDpsStaysInTheSpecBand()
        {
            var expected = new Dictionary<ElementId, float> { { ElementId.Water, 23.4f }, { ElementId.Earth, 29.5f }, { ElementId.Air, 27.2f } };
            foreach (ElementId element in NewElements)
            {
                MoveData[] chain = Fluid(element).LightChain;
                float damage = chain.Sum(m => m.Damage * Math.Max(1, m.HitCount));
                float seconds = chain.Take(chain.Length - 1).Sum(m => m.ChainCancelAt) + chain[chain.Length - 1].TotalDuration;
                float dps = damage / seconds;
                Assert.That(dps, Is.InRange(22f, 32f), element + " string dps");
                Assert.AreEqual(expected[element], dps, 0.05f, element + " string dps as in the spec's summary");
            }
        }

        [Test]
        public void MovesUseOnlyTheClosedSetOfEffectShapes()
        {
            var shapes = new HashSet<string>(typeof(EffectKeys).GetFields().Where(f => f.IsLiteral).Select(f => (string)f.GetRawConstantValue()));
            Assert.AreEqual(13, shapes.Count, "the spec's 13 shapes");
            foreach (ElementId element in NewElements)
            foreach (MoveData m in AllMoves(Fluid(element)))
                Assert.IsTrue(shapes.Contains(m.EffectKey), element + " " + m.DisplayName + " uses effect '" + m.EffectKey + "'");
        }

        [Test]
        public void EveryMoveHasARealAnimationKey()
        {
            var keys = new HashSet<string>(typeof(AnimationKeys).GetFields().Where(f => f.IsLiteral).Select(f => (string)f.GetRawConstantValue()));
            var seen = new HashSet<string>();
            foreach (ElementId element in NewElements)
            foreach (MoveData m in AllMoves(Fluid(element)))
            {
                Assert.IsFalse(string.IsNullOrEmpty(m.AnimationKey), element + " " + m.DisplayName + " has no animation key");
                Assert.IsTrue(keys.Contains(m.AnimationKey), element + " " + m.DisplayName + ": '" + m.AnimationKey + "' is not an AnimationKeys constant");
                Assert.IsTrue(seen.Add(m.AnimationKey), m.AnimationKey + " is used by two moves");
                Assert.IsFalse(string.IsNullOrEmpty(m.DisplayName), "names live in DisplayName");
            }
            Assert.AreEqual(57, seen.Count, "19 moves for each of Water, Earth and Air");
        }

        [Test]
        public void SetsKnowTheirElementStyleAndDataVersion()
        {
            var styles = new Dictionary<ElementId, string> { { ElementId.Water, "water" }, { ElementId.Earth, "earth" }, { ElementId.Air, "air" } };
            foreach (ElementId element in NewElements)
            foreach (ElementMoveSet set in BothPresets(element))
            {
                Assert.AreEqual(element, set.Element);
                Assert.AreEqual(element.ToString(), set.DisplayName);
                Assert.AreEqual(styles[element], set.AnimationStyle);
                Assert.AreEqual(ElementMoveSet.CurrentDataVersion, set.DataVersion, element + " DataVersion");
                Assert.IsFalse(set.Momentum.Enabled, element + ": only Fire has an identity meter in this build");
                Assert.AreEqual(1, set.Rhythm.PauseAfterIndex, element + ": two hits, wait, hit");
            }
        }

        // C13: in Fluid you can dodge the start of every string hit but the finisher, which commits until 0.30.
        [Test]
        public void FluidStringHitsCanBeDodgedFromTheirFirstFrame()
        {
            foreach (ElementId element in NewElements)
            {
                MoveData[] chain = Fluid(element).LightChain;
                for (int i = 0; i < chain.Length - 1; i++) Assert.AreEqual(0f, chain[i].DodgeCancelAt, element + " hit " + (i + 1));
                Assert.AreEqual(0.30f, chain[chain.Length - 1].DodgeCancelAt, 1e-6f, element + " finisher");
            }
        }

        [Test]
        public void PunishingIsApplyPunishingOfFluid()
        {
            foreach (ElementId element in NewElements)
            {
                ElementMoveSet expected = Fluid(element);
                ElementMoveSet.ApplyPunishing(expected);
                AssertSameData(expected, Punishing(element), element.ToString());
            }
        }

        // Spec 3.2-3.5 and 3.6: what makes each element play differently.
        [Test]
        public void ElementSettingsMatchTheSpec()
        {
            ElementMoveSet water = Fluid(ElementId.Water), earth = Fluid(ElementId.Earth), air = Fluid(ElementId.Air);

            Assert.AreEqual(DefenseStyle.ParryOnly, water.Guard.Style);
            Assert.AreEqual(0.20f, water.Guard.DeflectWindow, 1e-6f, "Water has the widest parry");
            Assert.AreEqual(0.30f, water.Guard.DeflectWhiffLockout, 1e-6f);
            Assert.AreEqual(DefenseStyle.BlockAndParry, earth.Guard.Style, "Earth holds a real block");
            Assert.AreEqual(180f, earth.Guard.ArcDegrees, 1e-6f);
            Assert.AreEqual(0.35f, earth.Guard.MoveSpeedMultiplier, 1e-6f);
            Assert.AreEqual(1.2f, earth.Guard.GuardBreakStagger, 1e-6f);
            Assert.AreEqual(0.12f, earth.Guard.DeflectWindow, 1e-6f, "Earth has the tightest parry");
            Assert.AreEqual(0.40f, earth.Guard.DeflectWhiffLockout, 1e-6f);
            Assert.AreEqual(DefenseStyle.ParryOnly, air.Guard.Style);
            Assert.AreEqual(0.18f, air.Guard.DeflectWindow, 1e-6f);

            AssertAerial(water.Aerial, 6, 2, 0.28f, 3.6f);
            AssertAerial(earth.Aerial, 4, 1, 0.45f, 2.6f);
            AssertAerial(air.Aerial, 9, 2, 0.18f, 4.2f);

            AssertDodge(water.Dodge, 4.6f, 3.6f, 3.0f, 2.4f, 0.28f, 0.65f, 0.20f, 0.10f, 0.06f, 0.22f, 3, 0.16f, 1.0f, 1.4f);
            AssertDodge(earth.Dodge, 3.0f, 2.2f, 2.4f, 1.8f, 0.22f, 0.80f, 0.14f, 0.06f, 0.05f, 0.20f, 2, 0.10f, 0.8f, 1.8f);
            AssertDodge(air.Dodge, 4.8f, 3.6f, 3.4f, 2.6f, 0.28f, 0.65f, 0.20f, 0.08f, 0.05f, 0.16f, 4, 0.12f, 0.8f, 1.3f);
            foreach (ElementMoveSet set in new[] { water, earth, air })
            {
                Assert.AreEqual(0f, set.Dodge.StaminaCost, set.DisplayName + ": Fluid dodges are free");
                Assert.IsFalse(set.Dodge.RequiresStamina);
            }
            foreach (ElementId element in NewElements)
            {
                Assert.AreEqual(16f, Punishing(element).Dodge.StaminaCost, element + ": Punishing dodges cost 16");
                Assert.IsTrue(Punishing(element).Dodge.RequiresStamina);
            }

            AssertRhythm(water.Rhythm, 0f, 0.04f, 1.10f, 0f, 0f, 3f, false, false);
            AssertRhythm(earth.Rhythm, -0.02f, -0.03f, 1.10f, 0.10f, 0f, 0f, true, false);
            AssertRhythm(air.Rhythm, 0f, 0f, 1.20f, 0f, 0f, 0f, false, true);

            AssertZip(water.Zip, 16f, 50f, 3f);
            AssertZip(earth.Zip, 12f, 45f, 1f);
            AssertZip(air.Zip, 18f, 60f, 6f);

            AssertCharge(water.Charge, 1.4f, 0.70f, 1.10f, 1.6f, 2.0f);
            AssertCharge(earth.Charge, 1.5f, 0.90f, 1.15f, 2.0f, 2.0f);
            AssertCharge(air.Charge, 0.9f, 0.40f, 0.60f, 1.6f, 1.6f);

            AssertPlunge(water.Plunge, 0.10f, 16f, 2.8f);
            AssertPlunge(earth.Plunge, 0.06f, 22f, 2.6f);
            AssertPlunge(air.Plunge, 0.12f, 14f, 3.6f);

            // The element mechanics (spec 2.8): Water pulls and heals, Earth is rooted, Air spreads its hits and circles.
            Assert.AreEqual(5f, water.AbilityNorth.PullDistance, 1e-6f, "Water Whip hauls the foe in");
            Assert.AreEqual(8f, water.AbilityEast.HealPerMoveMax, 1e-6f, "Tide Ring heals at most 8 per use");
            Assert.IsTrue(earth.LightChain.Skip(2).All(m => m.HyperArmor), "Earth's later string hits are rooted");
            Assert.AreEqual(150f, air.LightChain[3].OrbitDegrees, 1e-6f, "the double palm change carries you half way round");
            Assert.AreEqual(4f, air.LightChain[4].Knockback, 1e-6f, "the gale palm has the biggest knockback of the strings");
            Assert.IsTrue(earth.PauseChain[1].LaunchesProjectile && earth.Skill.LaunchesProjectile, "Boulder Hurl and Boulder Toss fly");
            Assert.AreEqual(6f, earth.PauseChain[1].Projectile.Gravity, 1e-6f);
            Assert.AreEqual(2.5f, earth.PauseChain[1].Projectile.ExplosionRadius, 1e-6f);
        }

        // The string moves of spec 3.2-3.4 (Fluid): name, S/A/R, combo window, chain cancel, damage, poise, sub-hits,
        // stamina. DodgeCancelAt is the C13 rule above.
        [Test]
        public void StringMovesMatchTheSpecTables()
        {
            // element, slot, name, S, A, R, CWstart, CWend, Ch, Dmg, Po, hits, interval, St
            var rows = new List<(ElementId, string, string, float, float, float, float, float, float, float, float, int, float, float)>
            {
                (ElementId.Water, "L1", "Ward Off", .14f, .14f, .30f, .18f, .52f, .30f, 7, 6, 1, 0, 8),
                (ElementId.Water, "L2", "Roll Back", .14f, .14f, .32f, .18f, .54f, .30f, 7, 6, 1, 0, 8),
                (ElementId.Water, "L3", "Press", .16f, .12f, .34f, .20f, .56f, .32f, 9, 8, 1, 0, 9),
                (ElementId.Water, "L4", "Push", .18f, .14f, .38f, .30f, .66f, .40f, 11, 9, 1, 0, 10),
                (ElementId.Water, "L5", "Single Whip", .22f, .18f, .46f, .50f, .84f, .62f, 17, 10, 1, 0, 13),
                (ElementId.Water, "P1", "Cloud Hands", .16f, .48f, .30f, .56f, .90f, .66f, 3, 2, 4, .12f, 12),
                (ElementId.Water, "P2", "Part the Wild Horse's Mane", .20f, .14f, .44f, .52f, .80f, .60f, 15, 14, 1, 0, 12),
                (ElementId.Water, "DS", "Return the Tide", .10f, .12f, .30f, .12f, .40f, .24f, 12, 6, 1, 0, 0),
                (ElementId.Earth, "L1", "Horse Stance Punch", .18f, .10f, .36f, .22f, .54f, .30f, 11, 7, 1, 0, 11),
                (ElementId.Earth, "L2", "Tiger Claw Rake", .18f, .12f, .38f, .24f, .58f, .32f, 12, 8, 1, 0, 11),
                (ElementId.Earth, "L3", "Rooted Stomp", .22f, .12f, .40f, .28f, .62f, .36f, 13, 9, 1, 0, 12),
                (ElementId.Earth, "L4", "Butterfly Palms", .24f, .12f, .44f, .36f, .72f, .46f, 15, 9, 1, 0, 13),
                (ElementId.Earth, "L5", "Mountain Quake", .30f, .14f, .56f, .58f, .94f, .72f, 21, 11, 1, 0, 16),
                (ElementId.Earth, "P1", "Raise the Boulder", .24f, .12f, .30f, .36f, .66f, .40f, 10, 10, 1, 0, 12),
                (ElementId.Earth, "P2", "Boulder Hurl", .26f, 0f, .48f, .52f, .80f, .56f, 20, 14, 1, 0, 14),
                (ElementId.Earth, "DS", "Pivot Elbow", .12f, .10f, .34f, .14f, .44f, .26f, 14, 7, 1, 0, 0),
                (ElementId.Air, "L1", "Piercing Palm", .09f, .10f, .22f, .10f, .36f, .19f, 3, 2, 2, .06f, 7),
                (ElementId.Air, "L2", "Turning Palm", .09f, .12f, .22f, .12f, .38f, .21f, 3, 2, 2, .08f, 7),
                (ElementId.Air, "L3", "Swimming Body Sweep", .10f, .14f, .26f, .14f, .44f, .24f, 3, 2, 3, .05f, 8),
                (ElementId.Air, "L4", "Double Palm Change", .12f, .18f, .28f, .22f, .52f, .30f, 3, 3, 3, .07f, 9),
                (ElementId.Air, "L5", "Gale Palm", .16f, .12f, .40f, .40f, .68f, .52f, 14, 12, 1, 0, 11),
                (ElementId.Air, "P1", "Circle Walk Flurry", .09f, .40f, .24f, .44f, .72f, .49f, 3, 2, 4, .10f, 10),
                (ElementId.Air, "P2", "Whirlwind", .18f, .30f, .40f, .52f, .80f, .70f, 3, 3, 3, .10f, 14),
                (ElementId.Air, "DS", "Circle Step Palm", .09f, .14f, .24f, .12f, .38f, .24f, 3, 2, 3, .05f, 0),
            };
            foreach (var (element, slot, name, s, a, r, cws, cwe, ch, dmg, po, hits, interval, st) in rows)
            {
                ElementMoveSet set = Fluid(element);
                MoveData m = slot[0] == 'L' ? set.LightChain[slot[1] - '1'] : slot[0] == 'P' ? set.PauseChain[slot[1] - '1'] : set.DodgeStrike;
                string what = element + " " + slot;
                Assert.AreEqual(name, m.DisplayName, what);
                Assert.AreEqual(s, m.Startup, 1e-6f, what + " startup");
                Assert.AreEqual(a, m.Active, 1e-6f, what + " active");
                Assert.AreEqual(r, m.Recovery, 1e-6f, what + " recovery");
                Assert.AreEqual(cws, m.ComboWindowStart, 1e-6f, what + " combo window start");
                Assert.AreEqual(cwe, m.ComboWindowEnd, 1e-6f, what + " combo window end");
                Assert.AreEqual(ch, m.ChainCancelAt, 1e-6f, what + " chain cancel");
                Assert.AreEqual(dmg, m.Damage, 1e-6f, what + " damage");
                Assert.AreEqual(po, m.PoiseDamage, 1e-6f, what + " poise");
                Assert.AreEqual(hits, m.HitCount, what + " hits");
                Assert.AreEqual(interval, m.HitInterval, 1e-6f, what + " hit interval");
                Assert.AreEqual(st, m.StaminaCost, 1e-6f, what + " stamina");
            }
        }

        // ---------------------------------------------------------------- helpers

        static void AssertAerial(AerialSettings a, int attacks, int dashes, float gravity, float dash)
        {
            Assert.AreEqual(attacks, a.AirAttacksPerJump);
            Assert.AreEqual(dashes, a.AirDashesPerJump);
            Assert.AreEqual(gravity, a.AirAttackGravityScale, 1e-6f);
            Assert.AreEqual(dash, a.AirDashDistance, 1e-6f);
        }

        static void AssertDodge(DodgeProfile d, float evade, float side, float slip, float back, float duration, float ease, float iEnd,
            float evadeCancel, float slipCancel, float next, int chain, float perfect, float counter, float counterDamage)
        {
            string n = d.DisplayName;
            Assert.AreEqual(evade, d.EvadeOutDistance, 1e-6f, n);
            Assert.AreEqual(side, d.SideSlipDistance, 1e-6f, n);
            Assert.AreEqual(slip, d.SlipInMaxDistance, 1e-6f, n);
            Assert.AreEqual(back, d.BackstepDistance, 1e-6f, n);
            Assert.AreEqual(duration, d.Duration, 1e-6f, n);
            Assert.AreEqual(ease, d.DashEaseOut, 1e-6f, n);
            Assert.AreEqual(0f, d.IFrameStart, 1e-6f, n);
            Assert.AreEqual(iEnd, d.IFrameEnd, 1e-6f, n);
            Assert.AreEqual(evadeCancel, d.EvadeAttackCancelAt, 1e-6f, n);
            Assert.AreEqual(slipCancel, d.SlipInAttackCancelAt, 1e-6f, n);
            Assert.AreEqual(next, d.NextDodgeAt, 1e-6f, n);
            Assert.AreEqual(chain, d.ChainMax, n);
            Assert.AreEqual(perfect, d.PerfectWindow, 1e-6f, n);
            Assert.AreEqual(counter, d.CounterWindow, 1e-6f, n);
            Assert.AreEqual(counterDamage, d.CounterDamageMultiplier, 1e-6f, n);
        }

        static void AssertRhythm(ElementRhythm r, float early, float late, float rate, float damage, float momentum, float refund, bool armour, bool dodgeKeeps)
        {
            Assert.AreEqual(early, r.BeatEarlyDelta, 1e-6f);
            Assert.AreEqual(late, r.BeatLateDelta, 1e-6f);
            Assert.AreEqual(rate, r.OnBeatPlaybackRate, 1e-6f);
            Assert.AreEqual(damage, r.OnBeatDamageBonus, 1e-6f);
            Assert.AreEqual(momentum, r.OnBeatMomentumBonus, 1e-6f);
            Assert.AreEqual(refund, r.OnBeatStaminaRefund, 1e-6f);
            Assert.AreEqual(armour, r.OnBeatHyperArmor);
            Assert.AreEqual(dodgeKeeps, r.DodgeKeepsBeat);
        }

        static void AssertZip(ZipStrikeSettings z, float range, float angle, float height)
        {
            Assert.AreEqual(range, z.Range, 1e-6f);
            Assert.AreEqual(angle, z.AngleDegrees, 1e-6f);
            Assert.AreEqual(height, z.MaxHeightDifference, 1e-6f);
        }

        static void AssertCharge(ChargeSettings c, float max, float sweetStart, float sweetEnd, float faJin, float poise)
        {
            Assert.AreEqual(max, c.MaxChargeTime, 1e-6f);
            Assert.AreEqual(sweetStart, c.SweetSpotStart, 1e-6f);
            Assert.AreEqual(sweetEnd, c.SweetSpotEnd, 1e-6f);
            Assert.AreEqual(faJin, c.FaJinDamageMultiplier, 1e-6f);
            Assert.AreEqual(poise, c.FaJinPoiseMultiplier, 1e-6f);
            Assert.Less(c.ReadyCueLead, c.SweetSpotStart, "the ready cue comes before the sweet spot opens");
            Assert.Less(c.QuickReleaseTime, c.SweetSpotStart);
        }

        static void AssertPlunge(PlungeSettings p, float hang, float fall, float ring)
        {
            Assert.AreEqual(hang, p.HangTime, 1e-6f);
            Assert.AreEqual(fall, p.FallSpeed, 1e-6f);
            Assert.AreEqual(ring, p.RingRadius, 1e-6f);
        }

        // Field-by-field equality of two tuning objects (public fields, recursing into tuning classes and arrays).
        static void AssertSameData(object expected, object actual, string path)
        {
            if (expected == null || actual == null)
            {
                Assert.AreEqual(expected == null, actual == null, path + " null");
                return;
            }
            Type type = expected.GetType();
            Assert.AreEqual(type, actual.GetType(), path);
            if (type.IsPrimitive || type.IsEnum || type == typeof(string) || type == typeof(decimal))
            {
                Assert.AreEqual(expected, actual, path);
                return;
            }
            if (type.IsArray)
            {
                var a = (Array)expected;
                var b = (Array)actual;
                Assert.AreEqual(a.Length, b.Length, path + " length");
                for (int i = 0; i < a.Length; i++) AssertSameData(a.GetValue(i), b.GetValue(i), path + "[" + i + "]");
                return;
            }
            foreach (FieldInfo f in type.GetFields(BindingFlags.Public | BindingFlags.Instance))
                AssertSameData(f.GetValue(expected), f.GetValue(actual), path + "." + f.Name);
        }
    }
}
