using System.Numerics;
using NUnit.Framework;
using VaatusRevenge.Core;

namespace VaatusRevenge.Tests
{
    // Pins the Spider-Man-style aerial game: the held-attack launcher, the air string, the air dash, the ability
    // slots, and enemies being launched, juggled, slammed and knocked down.
    public class AerialCombatTests
    {
        static readonly Vector2 Forward = new Vector2(0f, 1f);

        // ---------------------------------------------------------------- player

        [Test]
        public void FireHasAFiveHitStringWithCloseMidAndLongRange()
        {
            ElementMoveSet fire = ElementMoveSet.CreateFireFluid();
            Assert.AreEqual(5, fire.LightChain.Length);
            foreach (MoveData move in fire.LightChain) Assert.IsNotEmpty(move.AnimationKey, move.DisplayName);
            Assert.Less(fire.LightChain[0].Range, 3f, "close: the string opens with a jab");
            Assert.GreaterOrEqual(fire.AbilityNorth.Range, 6f, "mid: the fire whip reaches 6 m and more");
            Assert.AreEqual(360f, fire.AbilityEast.ArcDegrees, "close all round: the flame wheel");
            Assert.IsTrue(fire.Skill.LaunchesProjectile, "long: fire blast");
            Assert.GreaterOrEqual(fire.Zip.Range, 12f, "long gap closer: the zip strike");
        }

        [Test]
        public void HoldingAttackTurnsTheJabIntoTheLauncherWhichLiftsYouWhenItLands()
        {
            var d = new PlayerDriver();
            MoveData launcher = d.Model.MoveSet.Launcher;
            d.RunUntil(x => x.Model.CurrentAttackKind == PlayerAttackKind.Launcher, 60, Pad.Light);
            float held = (d.Frame + 1) * d.Dt;
            Assert.AreEqual(d.Model.MoveSet.Aerial.LauncherHoldTime, held, 2f * d.Dt, "after the hold time");
            Assert.IsTrue(d.Model.IsGrounded, "still on the ground during the launcher's wind-up");
            d.RunUntil(x => x.Model.IsAttackActive, 30);
            d.Step();
            Assert.IsFalse(d.Model.IsGrounded, "the kick carries you up");
            Assert.Greater(d.Last.Velocity.Y, launcher.SelfLift * 0.8f);
        }

        [Test]
        public void TappingAttackNeverLaunches()
        {
            var d = new PlayerDriver();
            for (int i = 0; i < 4; i++)
            {
                d.Tap(Pad.Light);
                d.Run(14);
            }
            foreach (PlayerEvent e in d.All(PlayerEventType.AttackStarted))
                Assert.AreNotEqual(PlayerAttackKind.Launcher, e.AttackKind);
        }

        [Test]
        public void AttackInTheAirIsTheAirStringAndYouHangWhileStriking()
        {
            var d = new PlayerDriver();
            d.Step(Pad.Jump);
            d.Run(6);
            var falling = new PlayerDriver();
            falling.Step(Pad.Jump);
            falling.Run(6);

            d.Step(Pad.Light);
            Assert.AreEqual(PlayerAttackKind.Air, d.Model.CurrentAttackKind);
            PlayerEvent started = d.All(PlayerEventType.AttackStarted)[0];
            Assert.IsTrue(started.InAir);
            Assert.AreEqual(d.Model.MoveSet.AirChain[0].AnimationKey, started.Move.AnimationKey);
            d.Run(12);
            falling.Run(13);
            Assert.Greater(d.World.Position.Y, falling.World.Position.Y, "hanging in the air while the air strike runs");
        }

        [Test]
        public void TheAirStringDoesNotLoopAndIsSpentUntilYouLand()
        {
            var d = new PlayerDriver();
            d.Step(Pad.Jump);
            d.Run(4);
            int length = d.Model.MoveSet.AirChain.Length;
            for (int press = 0; press < length + 2; press++)
            {
                int started = d.Count(PlayerEventType.AttackStarted);
                d.Step(Pad.Light);
                d.RunUntil(x => x.Count(PlayerEventType.AttackStarted) > started || x.Model.State != PlayerState.Attacking, 60);
                if (d.Model.State != PlayerState.Attacking) break;
                d.RunUntil(x => x.Model.ActionTime >= x.Model.CurrentMove.ComboWindowStart, 60);
            }
            Assert.LessOrEqual(d.Count(PlayerEventType.AttackStarted), length, "never more than one pass of the air string");
            d.RunUntil(x => x.Model.IsGrounded && x.Model.State == PlayerState.Locomotion, 240);
            Assert.AreEqual(0, d.Model.AirAttacksUsed, "landing refills it");
        }

        [Test]
        public void OneAirDashPerJumpFlatAndInvincibleAtFirst()
        {
            // AirDashesPerJump = 1 here (Build 05 gave Fluid two; TwoAirDashesFluidZeroPunishing covers that): the per-jump limit.
            ElementMoveSet set = ElementMoveSet.CreateFireFluid();
            set.Aerial.AirDashesPerJump = 1;
            var d = new PlayerDriver(null, set);
            d.Step(Pad.Jump);
            d.Run(8);
            d.Step(Pad.Dodge, Forward);
            Assert.IsTrue(d.Model.IsAirDashing);
            Assert.IsTrue(d.Model.IsInvulnerable);
            Assert.IsTrue(d.All(PlayerEventType.DodgeStarted)[0].InAir);
            Assert.AreEqual(0f, d.Last.Velocity.Y, 1e-4f, "no gravity while dashing");
            d.RunUntil(x => !x.Model.IsAirDashing, 60, Pad.None, Forward);
            d.Step(Pad.Dodge, Forward);
            d.Step(Pad.None, Forward);
            Assert.AreEqual(1, d.Count(PlayerEventType.DodgeStarted), "only one per jump");
        }

        [Test]
        public void AbilitySlotsRunTheWhipAndTheWheel()
        {
            var whip = new PlayerDriver();
            whip.Step(Pad.AbilityNorth);
            Assert.AreEqual(PlayerAttackKind.Ability, whip.Model.CurrentAttackKind);
            Assert.AreSame(whip.Model.MoveSet.AbilityNorth, whip.Model.CurrentMove);

            var wheel = new PlayerDriver();
            wheel.Step(Pad.AbilityEast);
            Assert.AreSame(wheel.Model.MoveSet.AbilityEast, wheel.Model.CurrentMove);
        }

        [Test]
        public void ZipStrikeRisesToAJuggledTarget()
        {
            var d = new PlayerDriver();
            d.World.HasZipTarget = true;
            d.World.ZipTargetPosition = new Vector3(0f, 2.5f, 8f);
            d.World.ZipTargetAimPoint = new Vector3(0f, 3.7f, 8f);
            d.World.ZipTargetRadius = 0.4f;
            d.Step(Pad.Zip);
            d.RunUntil(x => !x.Model.IsAttackActive && x.Model.Phase == AttackPhase.Recovery, 60);
            Assert.AreEqual(2.5f, d.World.Position.Y, 0.25f, "arrives level with the target");
        }

        // ---------------------------------------------------------------- enemies

        static DamageInfo PlayerHit(MoveData move)
        {
            DamageInfo hit = PlayerDriver.EnemyHit(move.Damage, move.PoiseDamage, new Vector3(0f, 0f, 1f));
            hit.SourceTeam = Team.Player;
            hit.LaunchSpeed = move.LaunchSpeed;
            hit.AirLift = move.AirLift;
            hit.SlamSpeed = move.SlamSpeed;
            hit.Knockback = move.Knockback;
            return hit;
        }

        static EnemyDriver Soldier()
        {
            var d = new EnemyDriver(EnemyTuning.CreateDaoSoldier());
            d.World.HasTarget = false;
            return d;
        }

        [Test]
        public void TheLauncherThrowsASoldierUpAndItLandsKnockedDownThenGetsUp()
        {
            ElementMoveSet fire = ElementMoveSet.CreateFireFluid();
            EnemyDriver d = Soldier();
            HitResult r = d.Brain.ReceiveHit(PlayerHit(fire.Launcher), Vector3.Zero);
            Assert.IsTrue(r.PoiseBroken);
            Assert.AreEqual(EnemyState.Launched, d.Brain.State);
            d.Run(10);
            Assert.Greater(d.World.Position.Y, 1f, "up in the air");
            int frames = 0;
            while (d.Brain.State == EnemyState.Launched && frames++ < 300) d.Step();
            Assert.AreEqual(EnemyState.Staggered, d.Brain.State, "knocked down on landing");
            Assert.IsTrue(d.Log.Exists(e => e.Type == EnemyEventType.KnockedDown));
            Assert.AreEqual(0f, d.World.Position.Y, 1e-3f);
            while (d.Brain.State == EnemyState.Staggered && frames++ < 600) d.Step();
            Assert.IsTrue(d.Log.Exists(e => e.Type == EnemyEventType.StaggerEnded), "gets up");
        }

        [Test]
        public void AirHitsKeepItUpTheSlamBringsItDownAndNoneCountTowardABreakOut()
        {
            ElementMoveSet fire = ElementMoveSet.CreateFireFluid();
            EnemyDriver d = Soldier();
            d.Brain.ReceiveHit(PlayerHit(fire.Launcher), Vector3.Zero);
            while (d.Brain.VerticalVelocity > 0f) d.Step();     // over the top and starting to fall
            float before = d.Brain.VerticalVelocity;
            d.Brain.ReceiveHit(PlayerHit(fire.AirChain[0]), Vector3.Zero);
            Assert.GreaterOrEqual(d.Brain.VerticalVelocity, fire.AirChain[0].AirLift - 1e-3f);
            Assert.Greater(d.Brain.VerticalVelocity, before);
            d.Brain.ReceiveHit(PlayerHit(fire.AirChain[1]), Vector3.Zero);
            d.Brain.ReceiveHit(PlayerHit(fire.AirChain[1]), Vector3.Zero);
            Assert.IsFalse(d.Brain.IsBreakOutArmed, "juggle hits never arm the anti-mash counter");
            d.Brain.ReceiveHit(PlayerHit(fire.AirChain[2]), Vector3.Zero);
            Assert.AreEqual(-fire.AirChain[2].SlamSpeed, d.Brain.VerticalVelocity, 1e-3f);
        }

        [Test]
        public void JugglesRunOutAndSomeFoesCantBeLaunched()
        {
            ElementMoveSet fire = ElementMoveSet.CreateFireFluid();
            EnemyDriver d = Soldier();
            d.Brain.ReceiveHit(PlayerHit(fire.Launcher), Vector3.Zero);
            int frames = (int)(d.Brain.Tuning.MaxJuggleTime / d.Dt) + 2;
            for (int i = 0; i < frames && d.Brain.IsLaunched; i++)
            {
                d.Step();
                if (i % 20 == 0) d.Brain.ReceiveHit(PlayerHit(fire.AirChain[0]), Vector3.Zero);
            }
            int landing = 0;
            while (d.Brain.IsLaunched && landing++ < 400) d.Step();
            Assert.IsFalse(d.Brain.IsLaunched, "it comes down even if you keep hitting it");

            var dummy = new EnemyDriver(EnemyTuning.CreateSparringDummy());
            dummy.Brain.ReceiveHit(PlayerHit(fire.Launcher), Vector3.Zero);
            Assert.AreNotEqual(EnemyState.Launched, dummy.Brain.State, "the dummy is a post in the ground");
        }
    }
}
