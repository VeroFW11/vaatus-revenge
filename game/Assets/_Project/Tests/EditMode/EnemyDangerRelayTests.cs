using System.Collections.Generic;
using System.Numerics;
using NUnit.Framework;
using VaatusRevenge.Core;

namespace VaatusRevenge.Tests
{
    // Danger sense with real enemy brains (Build 05, spec 2.7): every brain event goes through DangerSenseRelay (as
    // EnemyStrikes and the harness's SimEnemy do), and the player's warnings line up with the strikes that follow.
    public class EnemyDangerRelayTests
    {
        const float Dt = 1f / 60f;

        sealed class Fight
        {
            public readonly PlayerDriver Player = PlayerDriver.Elements();
            public readonly EnemyBrain Enemy;
            public EnemyWorldState World;
            public readonly List<(int frame, EnemyEvent e)> EnemyLog = new List<(int, EnemyEvent)>();

            public Fight(EnemyTuning tuning, Vector3 enemyFeet)
            {
                Enemy = EnemyBrain.Create(tuning, null, 42, 7, 180f);
                World = new EnemyWorldState
                {
                    Position = enemyFeet, Grounded = true, SelfRadius = 0.4f, HasTarget = true, TargetPosition = Vector3.Zero, TargetRadius = 0.4f,
                    TargetAimPoint = new Vector3(0f, 1.2f, 0f)
                };
            }

            // Player first, then the enemy (Unity's execution order); the enemy stays where it is.
            public void Run(int frames)
            {
                for (int i = 0; i < frames; i++)
                {
                    Player.Step();
                    EnemyTickResult r = Enemy.Tick(Dt, World);
                    for (int k = 0; k < r.Events.Count; k++)
                    {
                        EnemyEvent e = r.Events[k];
                        EnemyLog.Add((Player.Frame, e));
                        DangerSenseRelay.OnEnemyEvent(in e, Enemy, Enemy.OwnerId, World.Position, Player.World.Position, Player.Model);
                    }
                }
            }

            public List<int> EnemyFrames(EnemyEventType type)
            {
                var frames = new List<int>();
                foreach (var entry in EnemyLog) if (entry.e.Type == type) frames.Add(entry.frame);
                return frames;
            }
        }

        // Each strike that went active was warned WarningLead (+- a frame) before it and turned white NowLead before it;
        // a strike whose wind-up was shorter than the lead was warned as it started (the cue can't come before the glow).
        static void CheckStrikesWarned(Fight f)
        {
            DangerSenseSettings sense = f.Player.Model.Tuning.DangerSense;
            int telegraph = -1, strikes = 0;
            foreach ((int frame, EnemyEvent e) in f.EnemyLog)
            {
                if (e.Type == EnemyEventType.TelegraphStarted) telegraph = frame;
                if (e.Type != EnemyEventType.AttackActiveStart) continue;
                strikes++;
                int warned = LastCue(f.Player, PlayerEventType.DangerWarning, e.HitIndex, frame);
                int now = LastCue(f.Player, PlayerEventType.DangerNow, e.HitIndex, frame);
                float sinceWindUp = (frame - telegraph) * Dt;
                Assert.AreEqual(System.Math.Min(sense.WarningLead, sinceWindUp), (frame - warned) * Dt, 2 * Dt + 1e-4f,
                    "warning lead for strike " + e.HitIndex + " on frame " + frame);
                Assert.AreEqual(System.Math.Min(sense.NowLead, sinceWindUp), (frame - now) * Dt, 2 * Dt + 1e-4f,
                    "white cue for strike " + e.HitIndex + " on frame " + frame);
            }
            Assert.Greater(strikes, 0, "the enemy attacked");
        }

        // The last cue for this hit index on or before 'frame'.
        static int LastCue(PlayerDriver d, PlayerEventType type, int hitIndex, int frame)
        {
            int found = -1;
            for (int i = 0; i < d.Log.Count && d.LogFrames[i] <= frame; i++)
            {
                if (d.Log[i].Type == type && d.Log[i].Count == hitIndex) found = d.LogFrames[i];
            }
            Assert.GreaterOrEqual(found, 0, type + " for hit " + hitIndex + " before frame " + frame);
            return found;
        }

        [Test]
        public void DaoSoldier()
        {
            var f = new Fight(EnemyTuning.CreateDaoSoldier(), new Vector3(0f, 0f, 1.8f));
            f.Run(60 * 15);
            CheckStrikesWarned(f);
            Assert.AreEqual(f.Player.Count(PlayerEventType.DangerWarning), f.Player.Count(PlayerEventType.DangerCleared), "all resolved");
        }

        [Test]
        public void Crossbowman()
        {
            var f = new Fight(EnemyTuning.CreateCrossbowman(), new Vector3(0f, 0f, 10f));
            f.Run(60 * 12);
            List<int> launches = f.EnemyFrames(EnemyEventType.ProjectileLaunched);
            Assert.Greater(launches.Count, 0);
            Assert.IsTrue(f.Player.All(PlayerEventType.DangerWarning).TrueForAll(e => e.IsRanged), "marked as ranged");
            // A bolt lands its flight time after it flies (until it touches the body): the warning came WarningLead before that.
            MoveData bolt = EnemyTuning.CreateCrossbowman().Attacks[0].Move;
            float flight = (10f - bolt.OriginForward - f.Player.Model.BodyRadius - bolt.Projectile.Radius) / bolt.Projectile.Speed;
            DangerSenseSettings sense = f.Player.Model.Tuning.DangerSense;
            int firstLaunch = launches[0];
            int firstWarning = f.Player.FirstFrame(PlayerEventType.DangerWarning);
            Assert.AreEqual(sense.WarningLead, (firstLaunch - firstWarning) * Dt + flight, 0.1f);
            Assert.AreEqual(f.Player.Count(PlayerEventType.DangerWarning), f.Player.Count(PlayerEventType.DangerCleared), "all resolved");
        }

        [Test]
        public void BreakOutShove()
        {
            EnemyTuning tuning = EnemyTuning.CreateDaoSoldier();
            tuning.AttackIntervalMin = tuning.AttackIntervalMax = 30f;   // nothing but the shove
            var f = new Fight(tuning, new Vector3(0f, 0f, 1.6f));
            f.Run(5);
            for (int i = 0; i < tuning.BreakOut.HitsToTrigger; i++)
            {
                var hit = new DamageInfo
                {
                    Damage = 1f, PoiseDamage = 1f, SourceTeam = Team.Player, SourceId = 1, AttackId = CombatIds.Next(), MoveInstanceId = CombatIds.Next()
                };
                f.Enemy.ReceiveHit(hit, f.Enemy.Forward);
                f.Run(5);
            }
            f.Run(90);
            Assert.IsTrue(f.EnemyLog.Exists(x => x.e.Type == EnemyEventType.TelegraphStarted && x.e.Telegraph == TelegraphKind.BreakOut), "the shove came");
            CheckStrikesWarned(f);
            Assert.IsFalse(f.Player.LastOf(PlayerEventType.DangerWarning).MustDodge, "the shove can be parried: gold");
        }
        // Round 7, J7-03: the sandbox's soldiers show BOTH colours of the mark (the Delayed Thrust can't be parried: red;
        // the rest gold), and every attack's wind-up glow agrees with the mark it raises (red glow <=> red mark).
        [Test]
        public void SandboxSoldiersShowRedAndGoldAndTheGlowAgrees([Values(false, true)] bool partner)
        {
            EnemyTuning tuning = partner ? EnemyTuning.CreateTutorialPartner() : EnemyTuning.CreateDaoSoldier();
            var f = new Fight(tuning, new Vector3(0f, 0f, 1.8f));
            f.Run(60 * 40);
            int red = 0, gold = 0;
            for (int i = 0; i < f.Player.Log.Count; i++)
            {
                PlayerEvent warning = f.Player.Log[i];
                if (warning.Type != PlayerEventType.DangerWarning) continue;
                int frame = f.Player.LogFrames[i];
                EnemyEvent telegraph = default(EnemyEvent);
                bool found = false;
                foreach (var entry in f.EnemyLog)
                {
                    if (entry.frame > frame) break;
                    if (entry.e.Type == EnemyEventType.TelegraphStarted)
                    {
                        telegraph = entry.e;
                        found = true;
                    }
                }
                Assert.IsTrue(found, "a wind-up came before the warning on frame " + frame);
                Assert.AreEqual(TelegraphLook.GlowsMustDodgeRed(telegraph.Telegraph, telegraph.Move), warning.MustDodge,
                    telegraph.Move.DisplayName + ": the glow and the mark agree");
                if (warning.MustDodge)
                {
                    red++;
                    Assert.AreEqual("Delayed Thrust", telegraph.Move.DisplayName, "only the thrust is red");
                }
                else gold++;
            }
            Assert.Greater(red, 0, "a red (must-dodge) mark appeared");
            Assert.Greater(gold, 0, "gold marks appeared");
        }
    }
}
