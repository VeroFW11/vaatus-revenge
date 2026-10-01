using System;
using System.Numerics;
using NUnit.Framework;
using VaatusRevenge.Core;

namespace VaatusRevenge.Tests
{
    // The Spider-Man 2 dodge (Build 05, spec 2.3): you never turn your back on the fight; the stick against the
    // direction to the enemy picks the dodge (slip in, evade out, side-slip, automatic side-step, backstep); three quick
    // dodges then a breather; the dodge strike takes the string's next slot; you run on out of a dodge.
    public class CombatDodgeFlowTests
    {
        static readonly Vector2 Up = new Vector2(0f, 1f), Down = new Vector2(0f, -1f), Left = new Vector2(-1f, 0f), Right = new Vector2(1f, 0f);
        static readonly Vector3 Ahead = new Vector3(0f, 0f, 4f);

        static float YawTo(PlayerDriver d, Vector3 target)
        {
            return Directions.YawOf(Directions.Flatten(target - d.World.Position), 0f);
        }

        static Vector3 DodgeOnce(PlayerDriver d, Vector2 stick)
        {
            Vector3 start = d.World.Position;
            d.Step(Pad.Dodge, stick);
            Assert.AreEqual(PlayerState.Dodging, d.Model.State);
            // The stick only matters at the start; letting go keeps the exit carry (run speed) out of the distance.
            d.RunUntil(x => x.Model.State != PlayerState.Dodging, 60);
            return Directions.Flatten(d.World.Position - start);
        }

        [Test]
        public void DodgeKeepsFacingEngagedEnemy()
        {
            foreach (Vector2 stick in new[] { Left, Right, Down })
            {
                PlayerDriver d = PlayerDriver.Elements();
                d.Target(Ahead);
                d.Step(Pad.Dodge, stick);
                for (int i = 0; i < 12 && d.Model.State == PlayerState.Dodging; i++)
                {
                    Assert.AreEqual(0f, Angles.Delta(d.Model.FacingYaw, YawTo(d, Ahead)), 12f, "facing the enemy, stick " + stick);
                    d.Step(Pad.None, stick);
                }
            }
        }

        [Test]
        public void DodgeWithNoEnemyFacesDash()
        {
            PlayerDriver d = PlayerDriver.Elements();
            d.Step(Pad.Dodge, Right);
            Assert.AreEqual(DodgeKind.Traverse, d.LastOf(PlayerEventType.DodgeStarted).DodgeKind);
            Assert.AreEqual(90f, d.Model.FacingYaw, 1f, "faces where it dashes");
            Vector3 moved = DodgeOnce(PlayerDriver.Elements(), Right);
            Assert.AreEqual(d.Model.MoveSet.Dodge.EvadeOutDistance, moved.X, 0.05f);
        }

        [Test]
        public void FocusTrackedDuringDodge()
        {
            PlayerDriver d = PlayerDriver.Elements();
            d.LockOn(Ahead);
            d.Step(Pad.Dodge, Left);
            for (int i = 0; i < 12 && d.Model.State == PlayerState.Dodging; i++)
            {
                d.World.LockTargetPosition += new Vector3(0.15f, 0f, 0f);    // the enemy circles too
                d.Step(Pad.None, Left);
            }
            Assert.AreEqual(0f, Angles.Delta(d.Model.FacingYaw, YawTo(d, d.World.LockTargetPosition)), 15f, "turned with it");
        }

        [Test]
        public void SlipInStopsAtSlipInStopGap()
        {
            PlayerDriver d = PlayerDriver.Elements();
            d.Target(Ahead);
            d.Step(Pad.Dodge, Up);
            Assert.AreEqual(DodgeKind.SlipIn, d.LastOf(PlayerEventType.DodgeStarted).DodgeKind);
            d.RunUntil(x => x.Model.State != PlayerState.Dodging, 60);
            float gap = Directions.Flatten(Ahead - d.World.Position).Length() - d.World.SelfRadius - d.World.SoftTargetRadius;
            Assert.AreEqual(d.Model.MoveSet.Dodge.SlipInStopGap, gap, 0.05f, "slips in close, never through");

            PlayerDriver far = PlayerDriver.Elements();
            far.Target(new Vector3(0f, 0f, 6.5f));
            Vector3 moved = DodgeOnce(far, Up);
            Assert.AreEqual(far.Model.MoveSet.Dodge.SlipInMaxDistance, moved.Length(), 0.05f, "at most SlipInMaxDistance");
        }

        [Test]
        public void EvadeOutFullDistance()
        {
            PlayerDriver d = PlayerDriver.Elements();
            d.Target(Ahead);
            Vector3 moved = DodgeOnce(d, Down);
            Assert.AreEqual(DodgeKind.EvadeOut, d.LastOf(PlayerEventType.DodgeStarted).DodgeKind);
            Assert.AreEqual(d.Model.MoveSet.Dodge.EvadeOutDistance, moved.Length(), 0.05f);
            Assert.Less(moved.Z, 0f, "away from the enemy");
            Assert.AreEqual(0f, Angles.Delta(d.Model.FacingYaw, YawTo(d, Ahead)), 10f, "still facing it");
        }

        [Test]
        public void SideSlipDistance()
        {
            PlayerDriver d = PlayerDriver.Elements();
            d.Target(Ahead);
            Vector3 moved = DodgeOnce(d, Right);
            PlayerEvent started = d.LastOf(PlayerEventType.DodgeStarted);
            Assert.AreEqual(DodgeKind.SideSlip, started.DodgeKind);
            Assert.AreEqual(d.Model.MoveSet.Dodge.SideSlipDistance, moved.Length(), 0.05f);
            Assert.Greater(moved.X, 0f, "to the side the stick points");
            Assert.AreEqual(1f, started.LocalDirection.X, 1e-3f, "straight to the right in facing space");
        }

        static IncomingStrike StrikeFrom(PlayerDriver d, int attacker, Vector3 feet, float inSeconds)
        {
            return new IncomingStrike
            {
                AttackerId = attacker, AttackKey = attacker * 10, HitIndex = 0, ImpactClock = d.Model.Clock + inSeconds, AttackerFeet = feet,
                StrikeForward = Directions.SafeNormalize(Directions.Flatten(d.World.Position - feet), Vector3.Zero), Parryable = true
            };
        }

        [Test]
        public void NeutralWithThreatAutoEvadesPerpendicularAwayFromGroup()
        {
            foreach (float otherX in new[] { -3f, 3f })
            {
                PlayerDriver d = PlayerDriver.Elements();
                Vector3 attacker = new Vector3(0f, 0f, 2.5f);
                d.Model.NotifyIncomingStrike(StrikeFrom(d, 7, attacker, 0.3f));
                d.Model.NotifyIncomingStrike(StrikeFrom(d, 8, new Vector3(otherX, 0f, 2f), 1.5f));   // another enemy, later
                Vector3 moved = DodgeOnce(d, Vector2.Zero);
                Assert.AreEqual(DodgeKind.AutoEvade, d.LastOf(PlayerEventType.DodgeStarted).DodgeKind);
                Assert.AreEqual(d.Model.MoveSet.Dodge.SideSlipDistance, moved.Length(), 0.05f);
                Assert.AreEqual(0f, moved.Z, 0.05f, "across the strike's path");
                Assert.AreEqual(-Math.Sign(otherX), Math.Sign(moved.X), "away from the other enemy");
            }

            // Nobody else: the camera's right.
            PlayerDriver alone = PlayerDriver.Elements();
            alone.Model.NotifyIncomingStrike(StrikeFrom(alone, 7, new Vector3(0f, 0f, 2.5f), 0.3f));
            Vector3 right = DodgeOnce(alone, Vector2.Zero);
            Assert.Greater(right.X, 1f);
        }

        [Test]
        public void NeutralWithoutThreatBacksteps()
        {
            PlayerDriver d = PlayerDriver.Elements();
            d.Target(Ahead);
            Vector3 moved = DodgeOnce(d, Vector2.Zero);
            Assert.AreEqual(DodgeKind.Backstep, d.LastOf(PlayerEventType.DodgeStarted).DodgeKind);
            Assert.AreEqual(d.Model.MoveSet.Dodge.BackstepDistance, -moved.Z, 0.05f);
        }

        [Test]
        public void PunishingNeutralAlwaysBacksteps()
        {
            PlayerDriver d = PlayerDriver.Elements(PlayerTuning.CreatePunishing(), ElementLoadout.CreatePunishing());
            d.Target(Ahead);
            d.Model.NotifyIncomingStrike(StrikeFrom(d, 7, Ahead, 0.3f));
            d.Step(Pad.Dodge);
            d.Step();                                            // Punishing dodges on release
            Assert.AreEqual(DodgeKind.Backstep, d.LastOf(PlayerEventType.DodgeStarted).DodgeKind);
        }

        // X X, dodge, X (the dodge strike in slot 3), X: the string carries on at slot 4.
        [Test]
        public void DodgeStrikeTakesNextSlotAndStringContinues()
        {
            PlayerDriver d = PlayerDriver.Elements();
            d.Target(new Vector3(0f, 0f, 2.2f));
            d.OnBeatString(1);
            d.RunUntil(x => x.Model.Phase == AttackPhase.Recovery, 60);
            d.Step(Pad.Dodge, Down);
            d.Step(Pad.Light, Down);
            d.RunUntilStarted(3, 60);
            PlayerEvent strike = d.LastStarted;
            Assert.AreEqual(PlayerAttackKind.DodgeStrike, strike.AttackKind);
            Assert.AreEqual(ComboBranch.DodgeStrike, strike.Branch);
            Assert.AreEqual(2, strike.ChainIndex);
            Assert.AreSame(d.Model.MoveSet.DodgeStrike, strike.Move);
            Assert.AreEqual(BeatGrade.None, strike.Grade, "outside a counter window it isn't beat-judged");
            d.PressOnBeat();                                     // the press after it is
            d.RunUntilStarted(4, 60);
            Assert.AreEqual(3, d.LastStarted.ChainIndex);
            Assert.AreEqual(ComboBranch.Main, d.LastStarted.Branch);
            Assert.AreEqual(BeatGrade.OnBeat, d.LastStarted.Grade);
        }

        [Test]
        public void DodgeStrikeAfterAnEvadeOutClosesTheGap()
        {
            PlayerDriver d = PlayerDriver.Elements();
            var target = new Vector3(0f, 0f, 2.0f);
            d.Target(target);
            d.OnBeatString(1);
            d.RunUntil(x => x.Model.Phase == AttackPhase.Recovery, 60);
            d.Step(Pad.Dodge, Down);                             // evade out, about 4 m
            d.RunUntil(x => x.Model.State != PlayerState.Dodging, 60, Pad.None, Down);
            float far = Directions.Flatten(target - d.World.Position).Length();
            Assert.Greater(far, 4f);
            d.Step(Pad.Light);
            Assert.AreEqual(PlayerAttackKind.DodgeStrike, d.LastStarted.AttackKind);
            MoveData strike = d.LastStarted.Move;
            d.RunUntil(x => x.Model.Phase == AttackPhase.Active, 60);
            float gap = Directions.Flatten(target - d.World.Position).Length() - d.World.SelfRadius - d.World.SoftTargetRadius;
            Assert.LessOrEqual(gap, strike.Range, "the dodge strike dashes back in and reaches");
        }

        [Test]
        public void DodgeStrikeBeforeFinisherPlaysFinisher()
        {
            PlayerDriver d = PlayerDriver.Elements();
            d.Target(new Vector3(0f, 0f, 2.2f));
            d.OnBeatString(3);                                   // hit 4
            d.RunUntil(x => x.Model.Phase == AttackPhase.Recovery, 60);
            d.Step(Pad.Dodge, Down);
            d.Step(Pad.Light, Down);
            d.RunUntilStarted(5, 60);
            Assert.AreEqual(4, d.LastStarted.ChainIndex);
            Assert.AreEqual(PlayerAttackKind.Light, d.LastStarted.AttackKind, "a dodge never skips your finisher");
            Assert.IsTrue(d.LastStarted.IsFinisher);
        }

        [Test]
        public void DodgeStrikeInsideCounterWindowIsCounterAndAutoBeat()
        {
            PlayerDriver d = PlayerDriver.Elements();
            d.Target(Ahead);
            d.Step(Pad.Dodge, Left);
            d.Run(2, Pad.None, Left);
            Assert.AreEqual(HitOutcome.PerfectEvade, d.HitFromFront(10f).Outcome);
            d.Step(Pad.Light, Left);
            d.RunUntilStarted(1, 60);
            PlayerEvent counter = d.LastStarted;
            Assert.AreEqual(PlayerAttackKind.DodgeStrike, counter.AttackKind);
            Assert.IsTrue(counter.IsCounter);
            Assert.AreEqual(BeatGrade.Auto, counter.Grade);
            Assert.Greater(counter.PlaybackRate, 1f, "plays as an on-beat hit");
        }

        [Test]
        public void DodgeStrikeNeedsTarget()
        {
            PlayerDriver d = PlayerDriver.Elements();
            d.Step(Pad.Dodge, Left);
            d.Step(Pad.Light, Left);
            d.RunUntilStarted(1, 60);
            Assert.AreEqual(PlayerAttackKind.Light, d.LastStarted.AttackKind);
            Assert.AreEqual(ComboBranch.Main, d.LastStarted.Branch);

            PlayerDriver far = PlayerDriver.Elements();
            far.Target(new Vector3(0f, 0f, 12f));               // further than the soft-lock range
            far.World.HasSoftTarget = false;
            far.Step(Pad.Dodge, Left);
            far.Step(Pad.Light, Left);
            far.RunUntilStarted(1, 60);
            Assert.AreEqual(PlayerAttackKind.Light, far.LastStarted.AttackKind);
        }

        [Test]
        public void DodgeChainMaxThenCooldown()
        {
            PlayerDriver d = PlayerDriver.Elements();
            DodgeProfile p = d.Model.MoveSet.Dodge;
            for (int f = 0; f < 120; f++) d.Step(f % 2 == 0 ? Pad.Dodge : Pad.None, Left);   // spam
            var starts = d.All(PlayerEventType.DodgeStarted);
            Assert.AreEqual(1, d.Count(PlayerEventType.DodgeChainLimited) > 0 ? 1 : 0, "the chain hit its limit");
            int limited = d.FirstFrame(PlayerEventType.DodgeChainLimited);
            int third = -1, fourth = -1, n = 0;
            for (int i = 0; i < d.Log.Count; i++)
            {
                if (d.Log[i].Type != PlayerEventType.DodgeStarted) continue;
                n++;
                if (n == p.ChainMax) third = d.LogFrames[i];
                if (n == p.ChainMax + 1) fourth = d.LogFrames[i];
            }
            Assert.Greater(limited, third);
            Assert.AreEqual(p.ChainCooldown, d.LastOf(PlayerEventType.DodgeChainLimited).Duration, 1e-5f);
            Assert.GreaterOrEqual((fourth - limited) * d.Dt, p.ChainCooldown - d.Dt, "no dodge during the breather");
            Assert.Greater(starts.Count, p.ChainMax, "and dodging comes back after it");
        }

        [Test]
        public void FluidDodgeFreeAndAllowedAtZeroStamina()
        {
            PlayerTuning t = PlayerTuning.CreateFluid();
            t.StaminaRegen = 0f;
            t.MaxStamina = 9f;
            PlayerDriver d = PlayerDriver.Elements(t);
            d.Tap(Pad.Light);                                    // the jab empties the bar
            d.RunUntil(x => x.Model.State == PlayerState.Locomotion, 90);
            Assert.AreEqual(0f, d.Model.Stamina);
            d.Step(Pad.Dodge, Left);
            Assert.AreEqual(PlayerState.Dodging, d.Model.State, "an empty bar still dodges");
            Assert.AreEqual(0f, d.Model.Stamina);
            d.RunUntil(x => x.Model.State == PlayerState.Locomotion, 60, Pad.None, Left);
            d.Tap(Pad.Light);
            Assert.AreEqual(1, d.Started, "but can't attack");
        }

        [Test]
        public void PunishingDodgeCostsAndNeedsStamina()
        {
            PlayerTuning t = PlayerTuning.CreatePunishing();
            t.StaminaRegen = 0f;
            PlayerDriver d = PlayerDriver.Elements(t, ElementLoadout.CreatePunishing());
            float before = d.Model.Stamina;
            d.Tap(Pad.Dodge, Left);
            Assert.AreEqual(before - d.Model.MoveSet.Dodge.StaminaCost, d.Model.Stamina, 1e-3f);
            d.RunUntil(x => x.Model.State == PlayerState.Locomotion, 90, Pad.None, Left);

            PlayerTuning empty = PlayerTuning.CreatePunishing();
            empty.StaminaRegen = 0f;
            empty.MaxStamina = 13f;
            PlayerDriver e = PlayerDriver.Elements(empty, ElementLoadout.CreatePunishing());
            e.Tap(Pad.Light);
            e.RunUntil(x => x.Model.State == PlayerState.Locomotion, 120);
            Assert.AreEqual(0f, e.Model.Stamina);
            e.Tap(Pad.Dodge, Left);
            e.Run(10, Pad.None, Left);
            Assert.AreEqual(0, e.Count(PlayerEventType.DodgeStarted), "no stamina, no dodge");
        }

        [Test]
        public void DodgeExitCarriesRunSpeed()
        {
            PlayerDriver d = PlayerDriver.Elements();
            d.Step(Pad.Dodge, Right);
            d.RunUntil(x => x.Model.State != PlayerState.Dodging, 60, Pad.None, Right);
            d.Step(Pad.None, Right);
            Assert.AreEqual(d.Model.Tuning.RunSpeed, Directions.Flatten(d.Last.Velocity).Length(), 0.05f,
                "already at run speed the frame after the dodge ends (no acceleration from a standstill)");

            PlayerDriver punishing = PlayerDriver.Elements(PlayerTuning.CreatePunishing(), ElementLoadout.CreatePunishing());
            punishing.Step(Pad.Dodge, Right);
            punishing.Step(Pad.None, Right);
            punishing.RunUntil(x => x.Model.State != PlayerState.Dodging, 60, Pad.None, Right);
            punishing.Step(Pad.None, Right);
            Assert.Less(Directions.Flatten(punishing.Last.Velocity).Length(), punishing.Model.Tuning.RunSpeed * 0.75f, "Punishing carries half");
        }

        [Test]
        public void AirDashKeepsAirString()
        {
            PlayerDriver d = PlayerDriver.Elements();
            d.Target(new Vector3(0f, 1.2f, 2.0f));
            d.Step(Pad.Jump);
            d.Run(6);
            d.Step(Pad.Light);
            Assert.AreEqual(PlayerAttackKind.Air, d.LastStarted.AttackKind);
            d.PressOnBeat();
            d.RunUntilStarted(2, 60);
            Assert.AreEqual(1, d.LastStarted.ChainIndex);
            d.RunUntil(x => x.Model.Phase == AttackPhase.Recovery, 60);
            d.Step(Pad.Dodge, Left);
            Assert.IsTrue(d.Model.IsAirDashing);
            Assert.AreEqual(2, d.Model.StringNextIndex);
            Assert.AreEqual(ComboBranch.Air, d.Model.StringBranch);
            d.Step(Pad.Light, Left);
            d.RunUntilStarted(3, 60);
            Assert.AreEqual(PlayerAttackKind.Air, d.LastStarted.AttackKind);
            Assert.AreEqual(2, d.LastStarted.ChainIndex, "the air string goes on after the dash");
        }

        [Test]
        public void TwoAirDashesFluidZeroPunishing()
        {
            foreach (bool punishing in new[] { false, true })
            {
                PlayerDriver d = punishing ? PlayerDriver.Elements(PlayerTuning.CreatePunishing(), ElementLoadout.CreatePunishing())
                    : PlayerDriver.Elements();
                d.Step(Pad.Jump);
                d.Run(5);
                for (int f = 0; f < 40; f++) d.Step(f % 4 == 0 ? Pad.Dodge : Pad.None, Left);
                int airDashes = 0;
                foreach (PlayerEvent e in d.All(PlayerEventType.DodgeStarted)) if (e.InAir) airDashes++;
                Assert.AreEqual(punishing ? 0 : 2, airDashes);
            }
        }

        [Test]
        public void FluidChainHitsDodgeCancelableFromStart()
        {
            for (int hit = 0; hit < 4; hit++)
            {
                PlayerDriver d = PlayerDriver.Elements();
                d.OnBeatString(hit);
                Assert.AreEqual(AttackPhase.Startup, d.Model.Phase);
                d.Step(Pad.Dodge, Left);
                Assert.AreEqual(PlayerState.Dodging, d.Model.State, "hit " + (hit + 1) + " dodged out of on its first frame");
            }
            PlayerDriver finisher = PlayerDriver.Elements();
            finisher.OnBeatString(4);
            finisher.Step(Pad.Dodge, Left);
            Assert.AreEqual(PlayerState.Attacking, finisher.Model.State, "the finisher commits for a moment");
            ElementMoveSet punishing = ElementMoveSet.CreateFirePunishing();
            foreach (MoveData m in punishing.LightChain) Assert.Greater(m.DodgeCancelAt, m.ActiveEnd, "Punishing unchanged");
        }

        [Test]
        public void SnapshotDodgeProfileSurvivesMidDodgeSwitch()
        {
            ElementLoadout loadout = ElementLoadout.CreateFluid();
            loadout.Water.Dodge.Duration = 0.4f;
            loadout.Water.Dodge.EvadeOutDistance = 6f;
            PlayerDriver d = PlayerDriver.Elements(null, loadout);
            d.Step(Pad.Dodge, Right);
            d.Select = ElementId.Water;
            d.Step(Pad.None, Right);
            Assert.AreEqual(ElementId.Water, d.Model.ActiveElement, "switched at once (a dodge isn't busy)");
            Vector3 start = Vector3.Zero;
            d.RunUntil(x => x.Model.State != PlayerState.Dodging, 60);
            Assert.AreEqual(loadout.Fire.Dodge.EvadeOutDistance, d.World.Position.X - start.X, 0.05f, "the dodge finished as Fire's");
            Assert.AreEqual(d.FramesToReach(loadout.Fire.Dodge.Duration), d.FirstFrame(PlayerEventType.DodgeEnded), 1);
            d.Step(Pad.Dodge, Right);
            Assert.AreEqual(ElementId.Water, d.LastOf(PlayerEventType.DodgeStarted).Element, "the next one is Water's");
        }
    }
}
