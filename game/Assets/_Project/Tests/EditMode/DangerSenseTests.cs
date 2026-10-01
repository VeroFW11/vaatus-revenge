using System;
using System.Collections.Generic;
using System.Numerics;
using NUnit.Framework;
using VaatusRevenge.Core;

namespace VaatusRevenge.Tests
{
    // Danger sense (Build 05, spec 2.7): the player model raises DangerWarning WarningLead before a registered strike
    // lands, DangerNow NowLead before it, and DangerCleared once it has landed, missed or been called off. Cues follow the
    // strike, not the wind-up. EnemyDangerRelayTests drives real enemy brains through DangerSenseRelay.
    public class DangerSenseTests
    {
        internal static IncomingStrike Strike(PlayerDriver d, int attacker, float inSeconds, int hit = 0, bool parryable = true,
            bool unblockable = false, bool hidden = false)
        {
            return new IncomingStrike
            {
                AttackerId = attacker, AttackKey = attacker * 100, HitIndex = hit, ImpactClock = d.Model.Clock + inSeconds,
                AttackerFeet = new Vector3(0f, 0f, 3f), StrikeForward = new Vector3(0f, 0f, -1f), Parryable = parryable,
                Unblockable = unblockable, Hidden = hidden
            };
        }

        static double FirstClock(PlayerDriver d, PlayerEventType type, List<double> clocks)
        {
            for (int i = 0; i < d.Log.Count; i++) if (d.Log[i].Type == type) return clocks[d.LogFrames[i]];
            return double.NaN;
        }

        // Steps 'seconds' recording each frame's clock (index = frame).
        static void RunRecording(PlayerDriver d, float seconds, List<double> clocks)
        {
            int frames = (int)Math.Ceiling(seconds / d.Dt);
            for (int i = 0; i < frames; i++)
            {
                d.Step();
                while (clocks.Count <= d.Frame) clocks.Add(0.0);
                clocks[d.Frame] = d.Model.Clock;
            }
        }

        static void CheckLead(PlayerEventType type, Func<DangerSenseSettings, float> lead)
        {
            foreach (float fps in new[] { 30f, 60f, 144f })
            {
                PlayerDriver d = PlayerDriver.Elements(null, null, fps);
                var clocks = new List<double>();
                RunRecording(d, 0.1f, clocks);
                IncomingStrike s = Strike(d, 5, 1.0f);
                d.Model.NotifyIncomingStrike(s);
                RunRecording(d, 1.3f, clocks);
                double at = FirstClock(d, type, clocks);
                double wanted = s.ImpactClock - lead(d.Model.Tuning.DangerSense);
                Assert.That(at - wanted, Is.InRange(-1e-6, d.Dt + 1e-6), type + " at " + fps + " fps: within one frame");
                Assert.AreEqual(1, d.Count(type), "once");
            }
        }

        [Test]
        public void WarningFiresWarningLeadBeforeImpact()
        {
            CheckLead(PlayerEventType.DangerWarning, s => s.WarningLead);
        }

        [Test]
        public void NowFiresNowLeadBeforeImpact()
        {
            CheckLead(PlayerEventType.DangerNow, s => s.NowLead);
        }

        [Test]
        public void ShortTelegraphWarnsImmediately()
        {
            PlayerDriver d = PlayerDriver.Elements();
            d.Step();
            d.Model.NotifyIncomingStrike(Strike(d, 5, 0.2f));     // less time left than both leads
            d.Step();
            Assert.AreEqual(0, d.FirstFrame(PlayerEventType.DangerWarning) - 1);
            Assert.AreEqual(1, d.FirstFrame(PlayerEventType.DangerNow), "both cues at once, on the next frame");
            PlayerEvent warning = d.LastOf(PlayerEventType.DangerWarning);
            Assert.AreEqual(0.2f, warning.Duration, d.Dt + 1e-3f);
            Assert.AreEqual(new Vector3(0f, 0f, 3f), warning.Origin);
            Assert.AreEqual(-1f, warning.Direction.Z, 1e-4f, "points from the attacker to the player");
        }

        [Test]
        public void InterruptedAttackEmitsCleared()
        {
            PlayerDriver d = PlayerDriver.Elements();
            d.Model.NotifyIncomingStrike(Strike(d, 5, 0.5f));
            d.Run(3);
            Assert.AreEqual(1, d.Count(PlayerEventType.DangerWarning));
            d.Model.CancelIncomingStrikes(5);
            d.Step();
            PlayerEvent cleared = d.LastOf(PlayerEventType.DangerCleared);
            Assert.AreEqual(5, cleared.AttackerId);
            Assert.AreEqual(0, d.Model.PendingThreatCount);

            PlayerDriver quiet = PlayerDriver.Elements();
            quiet.Model.NotifyIncomingStrike(Strike(quiet, 6, 2f));
            quiet.Model.CancelIncomingStrikes(6);
            quiet.Step();
            Assert.AreEqual(0, quiet.Count(PlayerEventType.DangerCleared), "never warned: nothing to clear");
        }

        [Test]
        public void MultiHitAttackWarnsEachHit()
        {
            PlayerDriver d = PlayerDriver.Elements();
            EnemyAttackData doubleSlash = EnemyTuning.CreateDaoSoldier().Attacks[2];
            Assert.AreEqual(2, doubleSlash.HitCount);
            var e = new EnemyEvent
            {
                Type = EnemyEventType.TelegraphStarted, Attack = doubleSlash, Move = doubleSlash.Move, AttackId = CombatIds.Next()
            };
            DangerSenseRelay.OnEnemyEvent(in e, null, 9, new Vector3(0f, 0f, 2f), Vector3.Zero, d.Model);
            Assert.AreEqual(2, d.Model.PendingThreatCount);
            d.Run(90);
            List<PlayerEvent> warnings = d.All(PlayerEventType.DangerWarning);
            Assert.AreEqual(2, warnings.Count);
            Assert.AreEqual(0, warnings[0].Count);
            Assert.AreEqual(1, warnings[1].Count);
            Assert.AreEqual(doubleSlash.HitInterval, (d.FirstFrame(PlayerEventType.DangerNow, d.FirstFrame(PlayerEventType.DangerNow) + 1)
                                                      - d.FirstFrame(PlayerEventType.DangerNow)) * d.Dt, d.Dt + 1e-4f);
        }

        [Test]
        public void BoltImpactRefreshedOnLaunch()
        {
            PlayerDriver d = PlayerDriver.Elements();
            d.Step();                                // one frame in: the model has seen its body
            EnemyAttackData shot = EnemyTuning.CreateCrossbowman().Attacks[0];
            var archer = new Vector3(0f, 0f, 16f);
            var telegraph = new EnemyEvent { Type = EnemyEventType.TelegraphStarted, Attack = shot, Move = shot.Move, AttackId = CombatIds.Next() };
            DangerSenseRelay.OnEnemyEvent(in telegraph, null, 11, archer, Vector3.Zero, d.Model);
            Assert.IsTrue(d.Model.TryGetThreat(0, out IncomingStrike estimate));
            Assert.IsTrue(estimate.Ranged);
            // A bolt lands when it touches the body: the gap less the player's and the bolt's radii.
            float touch = d.Model.BodyRadius + shot.Move.Projectile.Radius;
            Assert.Greater(d.Model.BodyRadius, 0f, "the model knows its body from the world state");
            float flight = (16f - shot.Move.OriginForward - touch) / shot.Move.Projectile.Speed;
            Assert.AreEqual(d.Model.Clock + shot.Move.Startup + flight, estimate.ImpactClock, 1e-4);
            d.Run(30);
            // The bolt really flies from closer in (the archer stepped forward): the impact comes sooner.
            var launch = new EnemyEvent
            {
                Type = EnemyEventType.ProjectileLaunched, Attack = shot, Move = shot.Move, AttackId = CombatIds.Next(), HitIndex = 0,
                Origin = new Vector3(0f, 1.4f, 8f)
            };
            DangerSenseRelay.OnEnemyEvent(in launch, null, 11, new Vector3(0f, 0f, 8.5f), Vector3.Zero, d.Model);
            Assert.AreEqual(1, d.Model.PendingThreatCount, "the same strike, refreshed");
            d.Model.TryGetThreat(0, out IncomingStrike refreshed);
            Assert.AreEqual(d.Model.Clock + (8f - touch) / shot.Move.Projectile.Speed, refreshed.ImpactClock, 1e-4);
            Assert.AreEqual(launch.AttackId, refreshed.AttackKey, "follows the bolt");
        }

        [Test]
        public void MustDodgeForUnparryableOrUnblockable()
        {
            PlayerDriver d = PlayerDriver.Elements();
            d.Model.NotifyIncomingStrike(Strike(d, 1, 0.1f));
            d.Model.NotifyIncomingStrike(Strike(d, 2, 0.1f, parryable: false));
            d.Model.NotifyIncomingStrike(Strike(d, 3, 0.1f, unblockable: true));
            d.Step();
            foreach (PlayerEvent e in d.All(PlayerEventType.DangerWarning))
                Assert.AreEqual(e.AttackerId != 1, e.MustDodge, "attacker " + e.AttackerId + ": gold only when it can be parried");
            Assert.AreEqual(3, d.Count(PlayerEventType.DangerWarning));
        }

        [Test]
        public void HiddenAttackNoWarning()
        {
            PlayerDriver d = PlayerDriver.Elements();
            d.Model.NotifyIncomingStrike(Strike(d, 1, 0.5f, hidden: true));
            Assert.AreEqual(1, d.Model.PendingThreatCount, "tracked...");
            Assert.IsFalse(d.Model.TryGetMostImminentThreat(out _), "...but never shown");
            d.Run(60);
            Assert.AreEqual(0, d.Count(PlayerEventType.DangerWarning));
            Assert.AreEqual(0, d.Count(PlayerEventType.DangerNow));
            Assert.AreEqual(0, d.Count(PlayerEventType.DangerCleared));
            Assert.AreEqual(0, d.Model.PendingThreatCount, "forgotten after it lands");
        }

        [Test]
        public void PunishingHasNoNowCue()
        {
            PlayerDriver d = PlayerDriver.Elements(PlayerTuning.CreatePunishing(), ElementLoadout.CreatePunishing());
            Assert.AreEqual(0f, d.Model.Tuning.DangerSense.NowLead);
            d.Model.NotifyIncomingStrike(Strike(d, 1, 1f));
            d.Run(90);
            Assert.AreEqual(1, d.Count(PlayerEventType.DangerWarning));
            Assert.AreEqual(0, d.Count(PlayerEventType.DangerNow));
            Assert.AreEqual(1, d.Count(PlayerEventType.DangerCleared));
        }

        [Test]
        public void DelayedThrustWarningTracksStrikeNotWindup()
        {
            PlayerDriver d = PlayerDriver.Elements();
            EnemyAttackData thrust = EnemyTuning.CreateDaoSoldier().Attacks[3];
            Assert.AreEqual(TelegraphKind.Delayed, thrust.Telegraph);
            d.Step();
            int telegraphFrame = d.Frame;
            var e = new EnemyEvent { Type = EnemyEventType.TelegraphStarted, Attack = thrust, Move = thrust.Move, AttackId = CombatIds.Next() };
            DangerSenseRelay.OnEnemyEvent(in e, null, 4, new Vector3(0f, 0f, 2.5f), Vector3.Zero, d.Model);
            d.Run(90);
            float warnedAfter = (d.FirstFrame(PlayerEventType.DangerWarning) - telegraphFrame) * d.Dt;
            Assert.AreEqual(thrust.Move.Startup - d.Model.Tuning.DangerSense.WarningLead, warnedAfter, d.Dt + 1e-4f,
                "the long wind-up glows at once, but the mark waits for the strike");
            Assert.Greater(warnedAfter, 0.5f);
        }

        // Random strikes, refreshes and cancels for a minute: every warning ends in a DangerCleared, by the strike's
        // impact (+ ClearAfterImpact) or by being called off, and nothing is left over.
        [Test]
        public void EveryWarningResolvedByImpactOrCleared()
        {
            var random = new DeterministicRandom(17);
            PlayerDriver d = PlayerDriver.Elements();
            var impacts = new Dictionary<(int, int), double>();
            for (int frame = 0; frame < 3600; frame++)
            {
                if (random.NextFloat() < 0.05f)
                {
                    int attacker = 1 + (int)(random.NextFloat() * 4f);
                    int hit = (int)(random.NextFloat() * 3f);
                    IncomingStrike s = Strike(d, attacker, 0.05f + random.NextFloat() * 1.2f, hit);
                    d.Model.NotifyIncomingStrike(s);
                    impacts[(attacker, hit)] = s.ImpactClock;
                }
                if (random.NextFloat() < 0.01f) d.Model.CancelIncomingStrikes(1 + (int)(random.NextFloat() * 4f));
                d.Step();
                Assert.LessOrEqual(d.Model.PendingThreatCount, d.Model.Tuning.DangerSense.MaxTracked);
            }
            d.Run(120);
            var open = new Dictionary<(int, int), int>();
            foreach (PlayerEvent e in d.Log)
            {
                var key = (e.AttackerId, e.Count);
                if (e.Type == PlayerEventType.DangerWarning)
                {
                    Assert.IsFalse(open.ContainsKey(key) && open[key] > 0, "one warning per strike until it clears");
                    open[key] = 1;
                }
                else if (e.Type == PlayerEventType.DangerCleared)
                {
                    Assert.IsTrue(open.ContainsKey(key) && open[key] > 0, "a clear always follows a warning");
                    open[key] = 0;
                }
            }
            foreach (var pair in open) Assert.AreEqual(0, pair.Value, "warning for " + pair.Key + " never resolved");
            Assert.AreEqual(0, d.Model.PendingThreatCount);
        }

        [Test]
        public void MostImminentThreatOrdering()
        {
            PlayerDriver d = PlayerDriver.Elements();
            d.Model.NotifyIncomingStrike(Strike(d, 1, 0.8f));
            d.Model.NotifyIncomingStrike(Strike(d, 2, 0.3f));
            d.Model.NotifyIncomingStrike(Strike(d, 3, 0.5f));
            Assert.IsTrue(d.Model.TryGetMostImminentThreat(out IncomingStrike first));
            Assert.AreEqual(2, first.AttackerId);
            d.Run(d.FramesToReach(0.3f + d.Model.Tuning.DangerSense.ClearAfterImpact) + 1);
            Assert.IsTrue(d.Model.TryGetMostImminentThreat(out IncomingStrike second));
            Assert.AreEqual(3, second.AttackerId, "once it has landed, the next one");
            Assert.AreEqual(2, d.Model.PendingThreatCount);
        }
    }
}
