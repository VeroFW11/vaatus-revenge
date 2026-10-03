using System;
using System.Numerics;
using NUnit.Framework;
using VaatusRevenge.Core;

namespace VaatusRevenge.Tests
{
    // Enemy-side fixes from Playtest Report 01 (docs/Prototype/Playtest-Report-01.md).
    public class EnemyAiPlaytestFixTests
    {
        static DamageInfo PlayerHit(float poise, float damage = 1f)
        {
            DamageInfo hit = PlayerDriver.EnemyHit(damage, poise, new Vector3(0f, 0f, 1f));
            hit.SourceTeam = Team.Player;
            return hit;
        }

        // ---------------------------------------------------------------- ENEMY-02: mashing

        [Test]
        public void OneLightChainNoLongerStaggersASoldierFromFullPoise()
        {
            EnemyTuning t = EnemyTuning.CreateDaoSoldier();
            Assert.AreEqual(180f, t.MaxHealth, 1e-5f);
            Assert.AreEqual(52f, t.MaxPoise, 1e-5f);
            ElementMoveSet fire = ElementMoveSet.CreateFireFluid();
            var d = new EnemyDriver(t);
            d.World.HasTarget = false;
            foreach (MoveData move in fire.LightChain)
                Assert.IsFalse(d.Brain.ReceiveHit(PlayerHit(move.PoiseDamage), Vector3.Zero).PoiseBroken, move.DisplayName);
            Assert.IsFalse(d.Brain.ReceiveHit(PlayerHit(fire.LightChain[0].PoiseDamage), Vector3.Zero).PoiseBroken,
                "nor a string and a jab: the break-out gets its turn on that 6th hit (report 04, W-01)");
            Assert.IsTrue(d.Brain.IsBreakOutArmed, "that 6th hit arms the break-out...");
            Assert.IsFalse(d.Brain.ReceiveHit(PlayerHit(fire.LightChain[1].PoiseDamage), Vector3.Zero).PoiseBroken,
                "...which braces it: a 7th hit can't stagger it and wipe the counter");

            EnemyTuning noBreakOut = EnemyTuning.CreateDaoSoldier();
            noBreakOut.BreakOut.Enabled = false;
            var plain = new EnemyDriver(noBreakOut);
            plain.World.HasTarget = false;
            foreach (MoveData move in fire.LightChain) plain.Brain.ReceiveHit(PlayerHit(move.PoiseDamage), Vector3.Zero);
            plain.Brain.ReceiveHit(PlayerHit(fire.LightChain[0].PoiseDamage), Vector3.Zero);
            Assert.IsTrue(plain.Brain.ReceiveHit(PlayerHit(fire.LightChain[1].PoiseDamage), Vector3.Zero).PoiseBroken,
                "without a break-out, a string and two more hits staggers");
        }

        [Test]
        public void BigWindUpsAreArmouredFromHalfwayAndTheQuickSlashIsNot()
        {
            EnemyAttackData[] attacks = EnemyTuning.CreateDaoSoldier().Attacks;
            foreach (EnemyAttackData attack in attacks)
            {
                MoveData move = attack.Move;
                bool big = move.DisplayName == "Heavy Overhead" || move.DisplayName == "Delayed Thrust";
                Assert.AreEqual(big, move.HyperArmor, move.DisplayName);
                if (big) Assert.AreEqual(move.Startup * 0.5f, move.HyperArmorFrom, 1e-5f, move.DisplayName);
            }

            // Late in the Delayed Thrust's wind-up: armoured. Late in the Quick Slash's: still interruptible.
            Assert.IsFalse(HitLateInTheWindUp(3).PoiseBroken, "Delayed Thrust");
            Assert.IsTrue(HitLateInTheWindUp(0).PoiseBroken, "Quick Slash");
        }

        static HitResult HitLateInTheWindUp(int attackIndex)
        {
            EnemyTuning t = EnemyDriver.OnlyAttack(EnemyTuning.CreateDaoSoldier(), attackIndex);
            t.Attacks[0].MinRange = 0f;
            var d = new EnemyDriver(t);
            d.SetTarget(new Vector3(0f, 0f, 1.8f));
            d.RunUntil(x => x.Brain.IsTelegraphing, 900);
            d.Run((int)(t.Attacks[0].Move.Startup * 0.8f / d.Dt));
            Assert.IsTrue(d.Brain.IsTelegraphing, "still winding up");
            return d.Brain.ReceiveHit(PlayerHit(100f), Vector3.Zero);
        }

        // ---------------------------------------------------------------- enemy reach = the drawn weapon

        [Test]
        public void EnemyReachMatchesTheWeaponTheyAreDrawnWith()
        {
            var expected = new System.Collections.Generic.Dictionary<string, float>
            {
                { "Quick Slash", 2.1f }, { "Double Slash", 2.1f }, { "Heavy Overhead", 2.1f }, { "Delayed Thrust", 2.6f },
                { "Practice Swing", 2.1f }
            };
            int checkedAttacks = 0;
            foreach (EnemyTuning t in new[] { EnemyTuning.CreateDaoSoldier(), EnemyTuning.CreateSparringDummy() })
            {
                foreach (EnemyAttackData attack in t.Attacks)
                {
                    MoveData move = attack.Move;
                    float reach = expected[move.DisplayName];
                    Assert.AreEqual(reach, move.OriginForward + move.Range, 1e-4f, move.DisplayName);
                    Assert.LessOrEqual(attack.MaxRange, reach + 1e-4f, move.DisplayName + ": only swings when the tip can reach");

                    // The hit test adds the target's radius: a body just inside the tip is hit, just outside is not.
                    Vector3 origin = new Vector3(0f, move.OriginHeight, move.OriginForward);
                    Vector3 forward = new Vector3(0f, 0f, 1f);
                    const float radius = 0.4f;
                    Assert.IsTrue(HitGeometry.InArc(origin, forward, move.Range, move.ArcDegrees, move.VerticalReach,
                        new Vector3(0f, 0f, reach + radius - 0.05f), 1.8f, radius), move.DisplayName + " inside");
                    Assert.IsFalse(HitGeometry.InArc(origin, forward, move.Range, move.ArcDegrees, move.VerticalReach,
                        new Vector3(0f, 0f, reach + radius + 0.05f), 1.8f, radius), move.DisplayName + " outside");
                    checkedAttacks++;
                }
            }
            Assert.AreEqual(5, checkedAttacks);
        }

        // ---------------------------------------------------------------- ENEMY-03: the platform archer's leash

        [Test]
        public void LeashedArcherNeverStraysFromHomeAndStillShoots()
        {
            EnemyTuning t = EnemyTuning.CreateCrossbowman();
            Assert.AreEqual(2f, t.LeashRadius, 1e-5f);
            Assert.Less(t.LeashRadius + 0.4f, 3f, "leash plus body radius fits inside the 6 m platform (report 02, NEW-06)");
            var d = new EnemyDriver(t);
            Vector3 home = new Vector3(4f, 2.5f, -3f);           // up on a platform
            d.World.Position = home;
            float furthest = 0f;
            Vector3[] playerSpots = { new Vector3(4f, 0f, 20f), new Vector3(6f, 0f, -1f), new Vector3(-8f, 0f, -3f), new Vector3(4f, 0f, 7f) };
            for (int frame = 0; frame < 60 * 80; frame++)
            {
                Vector3 player = playerSpots[(frame / (60 * 20)) % playerSpots.Length];
                d.SetTarget(player);
                d.World.TargetHidden = frame / (60 * 10) % 4 == 3;   // sometimes out of sight: it would walk to find you
                d.Step();
                d.World.Position = new Vector3(d.World.Position.X, home.Y, d.World.Position.Z);   // stays on the platform top
                d.World.Grounded = true;
                furthest = Math.Max(furthest, Directions.Flatten(d.World.Position - home).Length());
            }
            Assert.AreEqual(home, d.Brain.Home);
            Assert.LessOrEqual(furthest, t.LeashRadius + 1e-3f);
            Assert.Greater(d.Count(EnemyEventType.TelegraphStarted), 5, "shoots from inside its leash");
        }

        [Test]
        public void CrowdedLeashedArcherShootsInsteadOfFleeingForever()
        {
            EnemyTuning t = EnemyTuning.CreateCrossbowman();
            var d = new EnemyDriver(t);
            for (int i = 0; i < 60 * 4; i++)
            {
                d.SetTarget(d.World.Position + new Vector3(0f, 0f, 3f));   // the player keeps crowding it
                d.Step();
            }
            Assert.LessOrEqual(Directions.Flatten(d.World.Position).Length(), t.LeashRadius + 1e-3f);
            Assert.Greater(d.Count(EnemyEventType.TelegraphStarted), 0, "cornered by its leash, it fights back");
        }

        [Test]
        public void LeashHomeIsTakenOnTheFirstTickAndAgainAfterReset()
        {
            var d = new EnemyDriver(EnemyTuning.CreateCrossbowman());
            d.World.Position = new Vector3(5f, 0f, 5f);
            d.SetTarget(new Vector3(5f, 0f, 30f));
            d.Step();
            Assert.AreEqual(new Vector3(5f, 0f, 5f), d.Brain.Home);

            d.World.Position = new Vector3(-3f, 0f, 2f);          // the Unity side teleports it to its spawn, then resets
            d.Brain.Reset();
            d.Step();
            Assert.AreEqual(new Vector3(-3f, 0f, 2f), d.Brain.Home);
        }

        [Test]
        public void LeashedEnemyPushedOutsideWalksStraightBack()
        {
            EnemyTuning t = EnemyTuning.CreateCrossbowman();
            var d = new EnemyDriver(t);
            d.SetTarget(new Vector3(0f, 0f, 11f));
            d.Run(10);
            d.World.Position = new Vector3(6f, 0f, 0f);           // knocked 6 m from home
            d.Step();
            Assert.Less(d.Last.Velocity.X, -1f, "heading home");
            d.Run(60 * 4);
            Assert.LessOrEqual(Directions.Flatten(d.World.Position).Length(), t.LeashRadius + 1e-3f);
        }

        [Test]
        public void UnleashedEnemiesStillRoam()
        {
            var d = new EnemyDriver(EnemyTuning.CreateDaoSoldier());
            Assert.AreEqual(0f, d.Brain.Tuning.LeashRadius);
            d.SetTarget(new Vector3(0f, 0f, 12f));
            d.Run(120);
            Assert.Greater(d.World.Position.Z, 4f);
        }

        // ---------------------------------------------------------------- ENEMY-01: crossbowmen share the tokens
        // (default pending David's answer; EnemyTuning.UsesAttackToken flips it)

        [Test]
        public void CrossbowmanWaitsForAFreeAttackToken()
        {
            EnemyTuning t = EnemyTuning.CreateCrossbowman();
            Assert.IsTrue(t.UsesAttackToken);
            var pool = new AttackTokenPool(2);
            Assert.IsTrue(pool.TryAcquire(9001));                // two soldiers are mid-attack
            Assert.IsTrue(pool.TryAcquire(9002));
            var d = new EnemyDriver(t, pool);
            d.SetTarget(new Vector3(0f, 0f, 11f));
            d.Run(60 * 8);
            Assert.AreEqual(0, d.Count(EnemyEventType.TelegraphStarted), "holds fire while both tokens are out");
            pool.Release(9001);
            d.Run(60 * 5);
            Assert.Greater(d.Count(EnemyEventType.TelegraphStarted), 0, "shoots once one is free");
            Assert.LessOrEqual(pool.Count, 2);
        }

        // ---------------------------------------------------------------- tuning and reset

        [Test]
        public void CrossbowmanShootsLessOften()
        {
            EnemyTuning t = EnemyTuning.CreateCrossbowman();
            Assert.AreEqual(2.5f, t.AttackIntervalMin, 1e-5f);
            Assert.AreEqual(3.5f, t.AttackIntervalMax, 1e-5f);
        }

        [Test]
        public void ResetDropsEventsQueuedBeforeItButKeepsStrikeEnds()
        {
            var d = new EnemyDriver(EnemyTuning.CreateDaoSoldier());
            d.SetTarget(new Vector3(0f, 0f, 2f));
            d.RunUntil(x => x.Brain.IsAttackActive, 900);
            d.Brain.ReceiveHit(PlayerHit(100f, 5f), Vector3.Zero);   // Damaged + Staggered (+ the strike's end) queue up...
            d.Brain.Reset();                                     // ...then the enemy is reset before its next Tick
            d.Step();
            int activeEnds = 0, attackEnds = 0, resetAt = -1;
            for (int i = 0; i < d.Last.Events.Count; i++)
            {
                EnemyEvent e = d.Last.Events[i];
                Assert.AreNotEqual(EnemyEventType.Damaged, e.Type, "stale event from before the reset");
                Assert.AreNotEqual(EnemyEventType.Staggered, e.Type, "stale event from before the reset");
                if (e.Type == EnemyEventType.AttackActiveEnd) { activeEnds++; Assert.AreEqual(-1, resetAt, "strike ends come before Reset"); }
                if (e.Type == EnemyEventType.AttackEnded) { attackEnds++; Assert.AreEqual(-1, resetAt, "strike ends come before Reset"); }
                if (e.Type == EnemyEventType.Reset) resetAt = i;
            }
            Assert.GreaterOrEqual(resetAt, 0);
            Assert.AreEqual(1, activeEnds, "the open strike is closed exactly once (report 02, NEW-05)");
            Assert.AreEqual(1, attackEnds);
        }

        [Test]
        public void ResetMidSwingStillClosesTheStrike()
        {
            var d = new EnemyDriver(EnemyTuning.CreateDaoSoldier());
            d.SetTarget(new Vector3(0f, 0f, 2f));
            for (int round = 0; round < 3; round++)
            {
                d.RunUntil(x => x.Brain.IsAttackActive, 900);
                Assert.AreEqual(d.Count(EnemyEventType.AttackActiveStart), d.Count(EnemyEventType.AttackActiveEnd) + 1, "one strike open");
                d.Brain.Reset();                                 // mid-swing, nothing queued: the reset closes it
                d.Step();
                Assert.IsFalse(d.Brain.IsAttackActive);
                Assert.AreEqual(d.Count(EnemyEventType.AttackActiveStart), d.Count(EnemyEventType.AttackActiveEnd), "every strike that opened also closed");
                Assert.AreEqual(d.Count(EnemyEventType.TelegraphStarted), d.Count(EnemyEventType.AttackEnded));
            }
        }
    }
}
