using System;
using System.Collections.Generic;
using System.Numerics;
using NUnit.Framework;
using VaatusRevenge.Core;

namespace VaatusRevenge.Tests
{
    // Drives one enemy brain on flat ground against a scripted target position.
    public sealed class EnemyDriver
    {
        public readonly float Dt;
        public readonly EnemyBrain Brain;
        public EnemyWorldState World;
        public readonly List<EnemyEvent> Log = new List<EnemyEvent>();
        public readonly List<int> LogFrames = new List<int>();
        public int Frame = -1;
        public EnemyTickResult Last;

        public EnemyDriver(EnemyTuning tuning, AttackTokenPool pool = null, int id = 500, int seed = 1, float fps = 60f)
        {
            Dt = 1f / fps;
            Brain = EnemyBrain.Create(tuning, pool, id, seed);
            World = new EnemyWorldState { Grounded = true, SelfRadius = 0.4f, HasTarget = true, TargetRadius = 0.4f };
        }

        public void SetTarget(Vector3 position)
        {
            World.TargetPosition = position;
            World.TargetAimPoint = position + new Vector3(0f, 1.2f, 0f);
        }

        public EnemyTickResult Step()
        {
            Frame++;
            Last = Brain.Tick(Dt, World);
            for (int i = 0; i < Last.Events.Count; i++)
            {
                Log.Add(Last.Events[i]);
                LogFrames.Add(Frame);
                PlayerDriver.AssertFinite(Last.Events[i].Origin, "event origin");
                PlayerDriver.AssertFinite(Last.Events[i].Direction, "event direction");
            }
            PlayerDriver.AssertFinite(Last.Velocity, "enemy velocity");
            Assert.IsFalse(float.IsNaN(Last.FacingYaw), "enemy facing");
            Vector3 p = World.Position + Last.Velocity * Dt;
            if (p.Y < 0f) p.Y = 0f;
            World.Grounded = p.Y <= 0f;
            World.Position = p;
            return Last;
        }

        public void Run(int frames)
        {
            for (int i = 0; i < frames; i++) Step();
        }

        public int RunUntil(Func<EnemyDriver, bool> done, int maxFrames)
        {
            for (int i = 0; i < maxFrames; i++)
            {
                Step();
                if (done(this)) return Frame;
            }
            Assert.Fail("condition not reached in " + maxFrames + " frames (state " + Brain.State + ")");
            return -1;
        }

        public int Count(EnemyEventType type)
        {
            int n = 0;
            foreach (EnemyEvent e in Log) if (e.Type == type) n++;
            return n;
        }

        public List<int> FramesOf(EnemyEventType type)
        {
            var frames = new List<int>();
            for (int i = 0; i < Log.Count; i++) if (Log[i].Type == type) frames.Add(LogFrames[i]);
            return frames;
        }

        public List<EnemyEvent> All(EnemyEventType type)
        {
            var list = new List<EnemyEvent>();
            foreach (EnemyEvent e in Log) if (e.Type == type) list.Add(e);
            return list;
        }

        public bool FrameHas(EnemyEventType type)
        {
            for (int i = 0; i < Last.Events.Count; i++) if (Last.Events[i].Type == type) return true;
            return false;
        }

        public static EnemyTuning OnlyAttack(EnemyTuning tuning, int index)
        {
            tuning.Attacks = new[] { tuning.Attacks[index] };
            tuning.Attacks[0].Cooldown = 0f;
            return tuning;
        }
    }

    public class EnemyAiTests
    {
        [Test]
        public void EveryEnemyTelegraphLastsAtLeastFourTenthsOfASecond()
        {
            foreach (EnemyTuning tuning in new[] { EnemyTuning.CreateDaoSoldier(), EnemyTuning.CreateCrossbowman(), EnemyTuning.CreateSparringDummy() })
            {
                Assert.IsNotEmpty(tuning.Attacks, tuning.DisplayName);
                foreach (EnemyAttackData attack in tuning.Attacks)
                    Assert.GreaterOrEqual(attack.Move.Startup, 0.4f, tuning.DisplayName + ": " + attack.Move.DisplayName);
            }

            // And in play: from the telegraph event to the first strike or bolt.
            foreach (EnemyTuning tuning in new[] { EnemyTuning.CreateDaoSoldier(), EnemyTuning.CreateCrossbowman() })
            {
                var d = new EnemyDriver(tuning);
                d.SetTarget(new Vector3(0f, 0f, tuning.Archetype == EnemyArchetype.Ranged ? 10f : 2f));
                d.Run(60 * 30);
                List<int> telegraphs = d.FramesOf(EnemyEventType.TelegraphStarted);
                Assert.Greater(telegraphs.Count, 3, tuning.DisplayName + " attacks");
                var firstStrikes = new List<int>();
                for (int i = 0; i < d.Log.Count; i++)
                {
                    EnemyEvent e = d.Log[i];
                    bool strike = e.Type == EnemyEventType.AttackActiveStart || e.Type == EnemyEventType.ProjectileLaunched;
                    if (strike && e.HitIndex == 0) firstStrikes.Add(d.LogFrames[i]);
                }
                for (int i = 0; i < firstStrikes.Count; i++)
                    Assert.GreaterOrEqual((firstStrikes[i] - telegraphs[i]) * d.Dt, 0.4f - 1e-4f, tuning.DisplayName);
            }
        }

        [Test]
        public void EnemyTrackingStopsWhenTheActiveFramesStart()
        {
            var d = new EnemyDriver(EnemyTuning.CreateDaoSoldier());
            float angle = 0f;
            bool sawTelegraphTurn = false;
            int attacksChecked = 0;
            float committedYaw = 0f;
            bool committed = false;
            float telegraphYaw = 0f;
            for (int frame = 0; frame < 60 * 40; frame++)
            {
                angle += 50f * d.Dt;                             // the target keeps circling the enemy at 2 m
                d.SetTarget(d.World.Position + Directions.FromYaw(angle) * 2f);
                d.Step();
                if (d.FrameHas(EnemyEventType.TelegraphStarted)) telegraphYaw = d.Brain.FacingYaw;
                if (d.FrameHas(EnemyEventType.AttackActiveStart) && !committed)
                {
                    committed = true;
                    committedYaw = d.Brain.FacingYaw;
                    if (Math.Abs(Angles.Delta(telegraphYaw, committedYaw)) > 5f) sawTelegraphTurn = true;
                }
                if (d.FrameHas(EnemyEventType.AttackEnded) && committed)
                {
                    committed = false;                           // free again this frame: it may turn
                    attacksChecked++;
                }
                if (committed)
                {
                    Assert.AreEqual(committedYaw, d.Brain.FacingYaw, 1e-4f, "no turning once the swing is active");
                }
            }
            Assert.Greater(attacksChecked, 3);
            Assert.IsTrue(sawTelegraphTurn, "it does turn to follow you during the wind-up");
        }

        [Test]
        public void AttackTokensNeverExceedTheLimitAndAreAlwaysReturned()
        {
            var pool = new AttackTokenPool(2);
            var soldiers = new List<EnemyDriver>();
            for (int i = 0; i < 4; i++)
            {
                var s = new EnemyDriver(EnemyTuning.CreateDaoSoldier(), pool, 700 + i, 11 + i);
                s.World.Position = Directions.FromYaw(i * 90f) * 3f;
                s.SetTarget(Vector3.Zero);
                soldiers.Add(s);
            }
            int maxAttackers = 0;
            var holding = new int[4];
            for (int frame = 0; frame < 60 * 60; frame++)
            {
                int attackers = 0;
                for (int i = 0; i < soldiers.Count; i++)
                {
                    EnemyDriver s = soldiers[i];
                    s.Step();
                    if (s.Brain.State == EnemyState.Attacking) attackers++;
                    holding[i] = s.Brain.HoldsToken ? holding[i] + 1 : 0;
                    Assert.Less(holding[i] * s.Dt, 8f, "a token held for ages means it leaked");
                }
                maxAttackers = Math.Max(maxAttackers, attackers);
                Assert.LessOrEqual(pool.Count, 2);
                Assert.LessOrEqual(attackers, 2, "frame " + frame);

                if (frame % 300 == 150)
                {
                    foreach (EnemyDriver s in soldiers)
                    {
                        if (s.Brain.State != EnemyState.Attacking) continue;
                        s.Brain.OnParried();                     // interrupted mid-attack
                        Assert.IsFalse(s.Brain.HoldsToken, "stagger returns the token");
                        break;
                    }
                }
                if (frame == 60 * 30)
                {
                    DamageInfo kill = PlayerDriver.EnemyHit(9999f, 0f, new Vector3(0f, 0f, 1f));
                    kill.SourceTeam = Team.Player;
                    Assert.IsTrue(soldiers[0].Brain.ReceiveHit(kill, Vector3.Zero).Killed);
                    Assert.IsFalse(soldiers[0].Brain.HoldsToken, "death returns the token");
                }
            }
            Assert.AreEqual(2, maxAttackers, "the limit is actually reached (enemies do get to attack)");
            foreach (EnemyDriver s in soldiers)
            {
                s.Brain.Reset();
                Assert.AreEqual(s.Count(EnemyEventType.AttackActiveStart), s.Count(EnemyEventType.AttackActiveEnd), "windows paired");
            }
            Assert.AreEqual(0, pool.Count);
        }

        [Test]
        public void LosingTheTargetReturnsTheToken()
        {
            var pool = new AttackTokenPool(1);
            var d = new EnemyDriver(EnemyTuning.CreateDaoSoldier(), pool);
            d.SetTarget(new Vector3(0f, 0f, 2f));
            d.RunUntil(x => x.Brain.State == EnemyState.Attacking, 600);
            Assert.IsTrue(d.Brain.HoldsToken);
            d.World.HasTarget = false;                           // the player died
            d.Step();
            Assert.IsFalse(d.Brain.HoldsToken);
            Assert.AreEqual(EnemyState.Idle, d.Brain.State);
            Assert.AreEqual(d.Count(EnemyEventType.AttackActiveStart), d.Count(EnemyEventType.AttackActiveEnd));
        }

        [Test]
        public void ParriedEnemyStaggersForItsParryTimeThenRecovers()
        {
            var d = new EnemyDriver(EnemyTuning.CreateDaoSoldier());
            d.SetTarget(new Vector3(0f, 0f, 2f));
            d.RunUntil(x => x.Brain.IsAttackActive, 900);
            d.Brain.OnParried();
            Assert.AreEqual(EnemyState.Staggered, d.Brain.State);
            Assert.IsFalse(d.Brain.IsAttackActive);
            d.Step();
            Assert.AreEqual(1, d.Count(EnemyEventType.Staggered));
            Assert.AreEqual(d.Count(EnemyEventType.AttackActiveStart), d.Count(EnemyEventType.AttackActiveEnd));
            float stagger = d.Brain.Tuning.ParriedStaggerDuration;
            d.Run((int)(stagger / d.Dt) - 3);
            Assert.AreEqual(EnemyState.Staggered, d.Brain.State);
            d.Run(6);
            Assert.AreNotEqual(EnemyState.Staggered, d.Brain.State);
        }

        [Test]
        public void RangedEnemyBacksOffWhenCrowdedAndHoldsItsBand()
        {
            EnemyTuning t = EnemyTuning.CreateCrossbowman();
            t.LeashRadius = 0f;                                  // free-roaming archer (the default one keeps to a 3 m leash)
            var close = new EnemyDriver(t);
            for (int i = 0; i < 90; i++)
            {
                close.SetTarget(close.World.Position + new Vector3(0f, 0f, 3f));   // the player keeps crowding it
                close.Step();
            }
            Vector3 toTarget = new Vector3(0f, 0f, 1f);
            Assert.AreEqual(EnemyState.Retreat, close.Brain.State);
            Assert.That(Vector3.Dot(close.Last.Velocity, toTarget), Is.EqualTo(-t.RetreatSpeed).Within(0.1f));
            Assert.AreEqual(0, close.Count(EnemyEventType.TelegraphStarted), "busy escaping, not shooting");

            var inBand = new EnemyDriver(t);
            inBand.SetTarget(new Vector3(0f, 0f, 11f));
            inBand.Run(20);
            Vector3 dir = Vector3.Normalize(inBand.World.TargetPosition - inBand.World.Position);
            Assert.AreEqual(EnemyState.Circle, inBand.Brain.State);
            Assert.Less(Math.Abs(Vector3.Dot(inBand.Last.Velocity, dir)), 0.3f, "strafes, neither closing nor fleeing");

            var far = new EnemyDriver(t);
            far.SetTarget(new Vector3(0f, 0f, 20f));
            far.Run(30);
            Assert.AreEqual(EnemyState.Approach, far.Brain.State);
            Assert.Greater(far.Last.Velocity.Z, 1f);
        }

        [Test]
        public void CornerednRangedEnemyShootsAnyway()
        {
            EnemyTuning t = EnemyTuning.CreateCrossbowman();
            t.LeashRadius = 0f;
            var d = new EnemyDriver(t);
            d.SetTarget(new Vector3(0f, 0f, 3f));
            for (int i = 0; i < 60 * 8; i++)
            {
                d.World.Position = Vector3.Zero;                 // against a wall: can't actually back away
                d.Step();
            }
            Assert.Greater(d.Count(EnemyEventType.TelegraphStarted), 0);
        }

        [Test]
        public void RangedEnemyFiresAimedShotsAndThreeBoltBurstsWithACommittedAim()
        {
            EnemyTuning t = EnemyTuning.CreateCrossbowman();
            var d = new EnemyDriver(t);
            d.SetTarget(new Vector3(0f, 2f, 11f));               // player up on a ledge
            d.Run(60 * 40);
            List<EnemyEvent> launches = d.All(EnemyEventType.ProjectileLaunched);
            Assert.Greater(launches.Count, 4);
            int bursts = 0;
            for (int i = 0; i < launches.Count; i++)
            {
                EnemyEvent shot = launches[i];
                Assert.AreEqual(1f, shot.Direction.Length(), 1e-3f);
                Assert.Greater(shot.Direction.Y, 0f, "aims up at the ledge");
                if (shot.HitIndex != 2) continue;
                bursts++;
                EnemyEvent first = launches[i - 2], second = launches[i - 1];
                Assert.AreEqual(0, first.HitIndex);
                Assert.AreEqual(3, first.Attack.HitCount);
                Assert.AreNotEqual(first.AttackId, second.AttackId);
                Assert.AreNotEqual(second.AttackId, shot.AttackId);
                Assert.AreEqual(first.Direction, shot.Direction, "the burst doesn't re-aim once it starts");
            }
            Assert.Greater(bursts, 0);
            List<int> frames = d.FramesOf(EnemyEventType.ProjectileLaunched);
            for (int i = 0; i < launches.Count; i++)
            {
                if (launches[i].HitIndex == 0) continue;
                Assert.AreEqual(12f, frames[i] - frames[i - 1], 1f, "bolts 0.2 s apart");
            }
        }

        [Test]
        public void DoubleSlashStrikesTwiceWithSeparateAttackIds()
        {
            EnemyTuning t = EnemyDriver.OnlyAttack(EnemyTuning.CreateDaoSoldier(), 2);
            var d = new EnemyDriver(t);
            d.SetTarget(new Vector3(0f, 0f, 2f));
            d.RunUntil(x => x.Count(EnemyEventType.AttackActiveStart) >= 2, 900);
            List<EnemyEvent> strikes = d.All(EnemyEventType.AttackActiveStart);
            List<int> frames = d.FramesOf(EnemyEventType.AttackActiveStart);
            Assert.AreEqual(0, strikes[0].HitIndex);
            Assert.AreEqual(1, strikes[1].HitIndex);
            Assert.AreNotEqual(strikes[0].AttackId, strikes[1].AttackId);
            Assert.AreEqual(21f, frames[1] - frames[0], 1f, "0.35 s apart");
            Assert.AreEqual(strikes[0].Direction, strikes[1].Direction);
        }

        // Updated in the playtest fix round (ENEMY-02): hyper armour now starts halfway through the wind-up, so an
        // early hit still interrupts it but mashing into the second half of the telegraph gets you hit.
        [Test]
        public void HeavyOverheadIsArmouredOnlyFromHalfwayThroughItsWindUp()
        {
            DamageInfo Jab()
            {
                DamageInfo hit = PlayerDriver.EnemyHit(8f, 100f, new Vector3(0f, 0f, 1f));
                hit.SourceTeam = Team.Player;
                return hit;
            }

            EnemyTuning t = EnemyDriver.OnlyAttack(EnemyTuning.CreateDaoSoldier(), 1);
            var early = new EnemyDriver(t);
            early.SetTarget(new Vector3(0f, 0f, 2f));
            early.RunUntil(x => x.Brain.IsTelegraphing, 900);
            Assert.AreEqual(TelegraphKind.Heavy, early.All(EnemyEventType.TelegraphStarted)[0].Telegraph);
            Assert.IsTrue(early.Brain.ReceiveHit(Jab(), Vector3.Zero).PoiseBroken, "the start of the wind-up can be interrupted");

            var late = new EnemyDriver(t);
            late.SetTarget(new Vector3(0f, 0f, 2f));
            late.RunUntil(x => x.Brain.IsTelegraphing, 900);
            late.Run((int)Math.Ceiling(t.Attacks[0].Move.HyperArmorFrom / late.Dt) + 1);
            Assert.IsTrue(late.Brain.IsTelegraphing);
            HitResult result = late.Brain.ReceiveHit(Jab(), Vector3.Zero);
            Assert.AreEqual(HitOutcome.Hit, result.Outcome, "armour still takes damage");
            Assert.IsFalse(result.PoiseBroken);
            Assert.AreEqual(EnemyState.Attacking, late.Brain.State);
        }

        [Test]
        public void EnemyWaitsUntilItNoticesYouOrGetsHit()
        {
            var d = new EnemyDriver(EnemyTuning.CreateDaoSoldier());
            d.SetTarget(new Vector3(0f, 0f, 30f));
            d.Run(120);
            Assert.AreEqual(EnemyState.Idle, d.Brain.State);
            Assert.AreEqual(Vector3.Zero, d.World.Position);
            DamageInfo blast = PlayerDriver.EnemyHit(5f, 0f, new Vector3(0f, 0f, 1f));
            blast.SourceTeam = Team.Player;
            d.Brain.ReceiveHit(blast, Vector3.Zero);
            d.Run(30);
            Assert.AreEqual(1, d.Count(EnemyEventType.Aggroed));
            Assert.AreEqual(EnemyState.Approach, d.Brain.State);
            Assert.Greater(d.World.Position.Z, 0.5f);
        }

        [Test]
        public void TargetStandingOnTheEnemyNeverProducesNaN()
        {
            foreach (EnemyTuning t in new[] { EnemyTuning.CreateDaoSoldier(), EnemyTuning.CreateCrossbowman(), EnemyTuning.CreateSparringDummy() })
            {
                var d = new EnemyDriver(t);
                if (d.Brain is SparringDummyBrain dummy) dummy.SwingEnabled = true;
                for (int i = 0; i < 60 * 15; i++)
                {
                    d.SetTarget(d.World.Position);
                    d.World.TargetAimPoint = Vector3.Zero;
                    d.Step();
                }
                Assert.Greater(d.Count(EnemyEventType.TelegraphStarted) + d.Count(EnemyEventType.Aggroed), 0, t.DisplayName);
            }
        }

        [Test]
        public void SameSeedGivesTheSameFightAndSeedsMatter()
        {
            var a = new EnemyDriver(EnemyTuning.CreateDaoSoldier(), null, 1, 42);
            var b = new EnemyDriver(EnemyTuning.CreateDaoSoldier(), null, 1, 42);
            var c = new EnemyDriver(EnemyTuning.CreateDaoSoldier(), null, 1, 43);
            bool differs = false;
            for (int i = 0; i < 60 * 30; i++)
            {
                Vector3 target = new Vector3((float)Math.Sin(i * 0.01) * 4f, 0f, 3f);
                a.SetTarget(target);
                b.SetTarget(target);
                c.SetTarget(target);
                a.Step();
                b.Step();
                c.Step();
                Assert.AreEqual(a.Last.Velocity, b.Last.Velocity, "frame " + i);
                Assert.AreEqual(a.Last.FacingYaw, b.Last.FacingYaw);
                Assert.AreEqual(a.Last.Events.Count, b.Last.Events.Count);
                if (a.Last.Velocity != c.Last.Velocity) differs = true;
            }
            Assert.IsTrue(differs);

            a.Brain.Reset();
            var fresh = new EnemyDriver(EnemyTuning.CreateDaoSoldier(), null, 1, 42);
            a.World = fresh.World;
            for (int i = 0; i < 600; i++)
            {
                a.SetTarget(new Vector3(0f, 0f, 3f));
                fresh.SetTarget(new Vector3(0f, 0f, 3f));
                a.Step();
                fresh.Step();
                Assert.AreEqual(fresh.Last.Velocity, a.Last.Velocity, "a reset brain replays like a new one");
            }
        }

        [Test]
        public void SparringDummyCantDieRefillsAndSwingsOnABeat()
        {
            EnemyTuning t = EnemyTuning.CreateSparringDummy();
            var d = new EnemyDriver(t);
            var dummy = (SparringDummyBrain)d.Brain;
            d.SetTarget(new Vector3(0f, 0f, 2f));
            d.Run(60 * 6);
            Assert.AreEqual(0, d.Count(EnemyEventType.TelegraphStarted), "swinging is off by default");
            Assert.AreEqual(Vector3.Zero, d.World.Position, "never moves");

            for (int i = 0; i < 3; i++)
            {
                DamageInfo hit = PlayerDriver.EnemyHit(90f, 0f, new Vector3(0f, 0f, 1f));
                hit.SourceTeam = Team.Player;
                HitResult result = d.Brain.ReceiveHit(hit, Vector3.Zero);
                Assert.IsFalse(result.Killed);
                Assert.AreEqual(90f, result.DamageDealt);
                d.Run(30);
            }
            Assert.IsTrue(d.Brain.IsAlive);
            Assert.AreEqual(1f, d.Brain.Health, 1e-4f);
            Assert.AreEqual(3, dummy.ComboHits);
            Assert.AreEqual(270f, dummy.ComboDamage, 1e-3f);
            Assert.AreEqual(270f / 1f, dummy.ComboDps, 1f);
            d.Run((int)(t.HealthRefillDelay / d.Dt) + 2);
            Assert.AreEqual(d.Brain.MaxHealth, d.Brain.Health);
            Assert.AreEqual(1, d.Count(EnemyEventType.HealthRefilled));
            Assert.AreEqual(0, dummy.ComboHits);

            dummy.SwingEnabled = true;
            d.Run(60 * 12);
            List<int> swings = d.FramesOf(EnemyEventType.TelegraphStarted);
            Assert.Greater(swings.Count, 3);
            for (int i = 1; i < swings.Count; i++)
                Assert.AreEqual(t.AttackIntervalMin / d.Dt, swings[i] - swings[i - 1], 1.5f, "steady beat");
        }
    }
}
