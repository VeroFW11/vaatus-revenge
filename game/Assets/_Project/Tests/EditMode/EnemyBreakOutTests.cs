using System.Numerics;
using NUnit.Framework;
using VaatusRevenge.Core;

namespace VaatusRevenge.Tests
{
    // The anti-mash break-out (EnemyBreakOutRule, playtest report 02 round 3): enough clean hits in a short window
    // make the soldier answer with an armoured, telegraphed counter.
    public class EnemyBreakOutTests
    {
        static DamageInfo PlayerHit(float poise = 1f, float damage = 1f)
        {
            DamageInfo hit = PlayerDriver.EnemyHit(damage, poise, new Vector3(0f, 0f, 1f));
            hit.SourceTeam = Team.Player;
            return hit;
        }

        // A soldier that has noticed a target 1.8 m away and won't start a normal attack on its own for a long time,
        // so any attack it makes is the break-out.
        static EnemyDriver CirclingSoldier(EnemyTuning t = null, AttackTokenPool pool = null, int id = 500)
        {
            t = t ?? EnemyTuning.CreateDaoSoldier();
            t.AttackIntervalMin = 100f;
            t.AttackIntervalMax = 100f;
            var d = new EnemyDriver(t, pool, id);
            d.SetTarget(new Vector3(0f, 0f, 1.8f));
            d.Run(30);
            Assert.IsTrue(d.Brain.IsAggro, "noticed the target");
            Assert.AreNotEqual(EnemyState.Attacking, d.Brain.State, "still circling");
            return d;
        }

        // Steps the brain with the target kept 1.8 m in front of it, like a player pressing in on it.
        static void Close(EnemyDriver d, int frames)
        {
            for (int i = 0; i < frames; i++)
            {
                d.SetTarget(d.World.Position + d.Brain.Forward * 1.8f);
                d.Step();
            }
        }

        static void CloseUntil(EnemyDriver d, System.Func<EnemyDriver, bool> done, int maxFrames)
        {
            for (int i = 0; i < maxFrames; i++)
            {
                Close(d, 1);
                if (done(d)) return;
            }
            Assert.Fail("condition not reached in " + maxFrames + " frames (state " + d.Brain.State + ")");
        }

        // 'count' light hits, 'gap' seconds apart, stepping the brain in between.
        static void Hits(EnemyDriver d, int count, float gap)
        {
            for (int i = 0; i < count; i++)
            {
                if (i > 0) Close(d, (int)System.Math.Round(gap / d.Dt));
                d.Brain.ReceiveHit(PlayerHit(), Vector3.Zero);
            }
        }

        static bool BreakingOut(EnemyDriver d) => d.Brain.IsBreakingOut;

        [Test]
        public void OnlyTheSoldierBreaksOutAndItsCounterIsFair()
        {
            EnemyBreakOutRule rule = EnemyTuning.CreateDaoSoldier().BreakOut;
            Assert.IsTrue(rule.Enabled);
            Assert.IsFalse(EnemyTuning.CreateCrossbowman().BreakOut.Enabled, "crossbowman");
            Assert.IsFalse(EnemyTuning.CreateSparringDummy().BreakOut.Enabled, "the dummy is for practising combos");

            MoveData move = rule.Attack.Move;
            Assert.AreEqual(TelegraphKind.BreakOut, rule.Attack.Telegraph, "its own glow colour");
            Assert.GreaterOrEqual(move.Startup, 0.4f, "a readable telegraph");
            Assert.IsTrue(move.HyperArmor, "armoured...");
            Assert.AreEqual(0f, move.HyperArmorFrom, 1e-6f, "...from its first frame");
            Assert.IsTrue(move.Parryable, "deflectable");
            Assert.IsFalse(move.Unblockable, "blockable");
            Assert.Greater(move.Knockback, 0f, "shoves");
            foreach (EnemyAttackData a in EnemyTuning.CreateDaoSoldier().Attacks)
                Assert.AreNotSame(rule.Attack, a, "not part of the normal attack rhythm");
        }

        [Test]
        public void ThreeQuickCleanHitsTriggerAnArmouredBreakOut()
        {
            EnemyDriver d = CirclingSoldier();
            Hits(d, 3, 0.3f);
            Assert.IsTrue(d.Brain.IsBreakOutArmed || d.Brain.IsBreakingOut);
            CloseUntil(d, BreakingOut, 5);
            EnemyEvent telegraph = d.All(EnemyEventType.TelegraphStarted)[0];
            Assert.AreEqual(TelegraphKind.BreakOut, telegraph.Telegraph);
            Assert.AreEqual(d.Brain.Tuning.BreakOut.Attack.Move.Startup, telegraph.Duration, 1e-5f);

            // A big hit during its wind-up doesn't stagger it.
            Close(d, 5);
            Assert.IsTrue(d.Brain.IsTelegraphing);
            Assert.IsFalse(d.Brain.ReceiveHit(PlayerHit(1000f), Vector3.Zero).PoiseBroken);
            CloseUntil(d, x => x.FrameHas(EnemyEventType.AttackActiveStart), 60);
        }

        [Test]
        public void FewerHitsOrHitsSpreadOutDoNotTriggerIt()
        {
            EnemyDriver two = CirclingSoldier();
            Hits(two, 2, 0.3f);
            two.Run(120);
            Assert.AreEqual(0, two.Count(EnemyEventType.TelegraphStarted), "two hits");

            EnemyDriver slow = CirclingSoldier();
            float window = slow.Brain.Tuning.BreakOut.HitWindow;
            Hits(slow, 6, window * 0.6f);   // any three span more than the window
            slow.Run(120);
            Assert.AreEqual(0, slow.Count(EnemyEventType.TelegraphStarted), "spread out");
        }

        [Test]
        public void TheSwitchTurnsItOff()
        {
            EnemyTuning t = EnemyTuning.CreateDaoSoldier();
            t.BreakOut.Enabled = false;
            EnemyDriver d = CirclingSoldier(t);
            Hits(d, 6, 0.1f);
            d.Run(120);
            Assert.AreEqual(0, d.Count(EnemyEventType.TelegraphStarted));
        }

        [Test]
        public void AStaggerWipesTheCountAndHitsDuringItDoNotCount()
        {
            EnemyDriver d = CirclingSoldier();
            Hits(d, 2, 0.2f);
            Assert.IsTrue(d.Brain.ReceiveHit(PlayerHit(1000f), Vector3.Zero).PoiseBroken, "earned stagger");
            Assert.IsFalse(d.Brain.IsBreakOutArmed);
            Hits(d, 4, 0.1f);    // piling on while it's stunned
            Assert.AreEqual(EnemyState.Staggered, d.Brain.State);
            Assert.IsFalse(d.Brain.IsBreakOutArmed);
            d.RunUntil(x => x.Brain.State != EnemyState.Staggered, 180);
            d.Run(60);
            Assert.AreEqual(0, d.Count(EnemyEventType.TelegraphStarted), "no break-out for an earned stagger");
        }

        [Test]
        public void ItWaitsForAnAttackTokenAndForOtherAttackers()
        {
            var pool = new AttackTokenPool(1);
            Assert.IsTrue(pool.TryAcquire(999), "another enemy is attacking");
            EnemyDriver d = CirclingSoldier(null, pool);
            Hits(d, 3, 0.2f);
            Close(d, 10);
            Assert.IsFalse(d.Brain.IsBreakingOut, "no token, no break-out");
            Assert.IsTrue(d.Brain.IsBreakOutArmed, "still waiting");
            pool.Release(999);
            CloseUntil(d, BreakingOut, 5);
            Assert.IsTrue(d.Brain.HoldsToken);

            // With a token free but someone else swinging, WaitsForOtherAttackers holds it back; past MaxWait it gives up.
            var shared = new AttackTokenPool(2);
            Assert.IsTrue(shared.TryAcquire(999));
            EnemyDriver e = CirclingSoldier(null, shared, 501);
            Hits(e, 3, 0.2f);
            Close(e, (int)(e.Brain.Tuning.BreakOut.MaxWait / e.Dt) + 5);
            Assert.AreEqual(0, e.Count(EnemyEventType.TelegraphStarted));
            Assert.IsFalse(e.Brain.IsBreakOutArmed, "gave up");
        }

        [Test]
        public void PunishingItsRecoveryCountsOnlyAfterATrade()
        {
            // Quick Slash only, then hits on its recovery.
            foreach (bool traded in new[] { false, true })
            {
                EnemyTuning t = EnemyDriver.OnlyAttack(EnemyTuning.CreateDaoSoldier(), 0);
                var d = new EnemyDriver(t);
                d.SetTarget(new Vector3(0f, 0f, 1.8f));
                d.RunUntil(x => x.FrameHas(EnemyEventType.AttackActiveStart), 600);
                if (traded) d.Brain.OnStrikeLanded();   // the swing hit the player: they traded instead of defending
                d.RunUntil(x => x.Brain.Phase == AttackPhase.Recovery, 60);
                Hits(d, 3, 0.1f);
                Assert.AreEqual(traded, d.Brain.IsBreakOutArmed, traded ? "a trade" : "an earned punish");
            }
        }

        [Test]
        public void ItCutsAnUnarmouredWindUpShort()
        {
            EnemyTuning t = EnemyDriver.OnlyAttack(EnemyTuning.CreateDaoSoldier(), 0);   // Quick Slash: no armour
            var d = new EnemyDriver(t);
            d.SetTarget(new Vector3(0f, 0f, 1.8f));
            d.RunUntil(x => x.Brain.IsTelegraphing, 600);
            Hits(d, 3, 0.05f);
            CloseUntil(d, BreakingOut, 5);
            Assert.AreEqual(1, d.Count(EnemyEventType.AttackEnded), "the dropped Quick Slash still gets its end event");
            Assert.AreEqual(0, d.Count(EnemyEventType.AttackActiveStart), "it never struck");
        }

        [Test]
        public void ALandedBreakOutIsFollowedUpAndADodgedOneIsNot()
        {
            foreach (bool landed in new[] { false, true })
            {
                EnemyDriver d = CirclingSoldier();
                Hits(d, 3, 0.2f);
                CloseUntil(d, BreakingOut, 5);
                CloseUntil(d, x => x.FrameHas(EnemyEventType.AttackActiveStart), 60);
                if (landed) d.Brain.OnStrikeLanded();
                CloseUntil(d, x => x.FrameHas(EnemyEventType.AttackEnded), 120);
                float expected = landed ? d.Brain.Tuning.BreakOut.FollowUpDelay : 100f;   // CirclingSoldier's normal pause
                Assert.AreEqual(expected, d.Brain.AttackCooldownRemaining, 0.05f, landed ? "landed" : "dodged");
            }
        }

        // Frames on which a break-out telegraph started.
        static System.Collections.Generic.List<int> BreakOutStarts(EnemyDriver d)
        {
            var frames = new System.Collections.Generic.List<int>();
            for (int i = 0; i < d.Log.Count; i++)
                if (d.Log[i].Type == EnemyEventType.TelegraphStarted && d.Log[i].Telegraph == TelegraphKind.BreakOut) frames.Add(d.LogFrames[i]);
            return frames;
        }

        [Test]
        public void HitsIntoTheBreakOutItselfDoNotArmTheNextOne()
        {
            // The seed-4 masher replay: hits landed into the violet wind-up re-armed the next shove, so break-outs
            // chained ~1.5 s apart. Now nothing on the break-out counts: wind-up, shove or recovery.
            EnemyDriver d = CirclingSoldier();
            Hits(d, 3, 0.2f);
            CloseUntil(d, BreakingOut, 5);
            Hits(d, 3, 0.1f);                                                      // into the wind-up
            Assert.IsTrue(d.Brain.IsTelegraphing);
            CloseUntil(d, x => x.Brain.Phase == AttackPhase.Recovery, 120);
            Hits(d, 3, 0.05f);                                                     // into its recovery
            Assert.IsTrue(d.Brain.IsBreakingOut, "still the same break-out");
            Assert.IsFalse(d.Brain.IsBreakOutArmed);
            Close(d, (int)((d.Brain.Tuning.BreakOut.Cooldown + 1f) / d.Dt));
            Assert.AreEqual(1, BreakOutStarts(d).Count, "no second break-out from hits on the first");
        }

        [Test]
        public void TheCooldownAlwaysHoldsAgainstNonStopMashing()
        {
            // A masher that eats every shove (so the soldier follows up at once) and never stops pressing light.
            EnemyTuning t = EnemyTuning.CreateDaoSoldier();
            t.MaxHealth = 1e6f;
            t.HealthRefillDelay = 0f;
            EnemyDriver d = CirclingSoldier(t);
            int gap = (int)System.Math.Round(0.25f / d.Dt);
            for (int f = 0; f < (int)(12f / d.Dt); f++)
            {
                if (f % gap == 0) d.Brain.ReceiveHit(PlayerHit(0f), Vector3.Zero);   // no poise damage: never staggers
                Close(d, 1);
                if (d.FrameHas(EnemyEventType.AttackActiveStart)) d.Brain.OnStrikeLanded();
            }
            System.Collections.Generic.List<int> starts = BreakOutStarts(d);
            Assert.GreaterOrEqual(starts.Count, 3, "the masher keeps getting shoved");
            float cooldown = t.BreakOut.Cooldown;
            for (int i = 1; i < starts.Count; i++)
                Assert.GreaterOrEqual((starts[i] - starts[i - 1]) * d.Dt, cooldown - 1e-3f, "break-out " + i + " came inside the cooldown");
        }

        [Test]
        public void HitsAfterTheShoveCountButArmOnlyWhenTheCooldownEnds()
        {
            EnemyDriver d = CirclingSoldier();
            float cooldown = d.Brain.Tuning.BreakOut.Cooldown;
            Hits(d, 3, 0.2f);
            CloseUntil(d, BreakingOut, 5);
            int start = d.Frame;
            CloseUntil(d, x => !x.Brain.IsBreakingOut, 120);
            int left = (int)(cooldown / d.Dt) - (d.Frame - start);
            Assert.Greater(left, 30, "the break-out is shorter than its cooldown");
            Close(d, left - 30);
            Hits(d, 3, 0.1f);                                                      // a full combo inside the cooldown
            Assert.IsFalse(d.Brain.IsBreakOutArmed, "not during the cooldown");
            CloseUntil(d, x => x.Brain.IsBreakOutArmed || x.Brain.IsBreakingOut, 30);
            Assert.GreaterOrEqual((d.Frame - start) * d.Dt, cooldown - 1e-3f, "armed right as the cooldown ended");

            // Stop pressing well before the cooldown ends and the banked hits age out instead. (With the default
            // 2 s cooldown a combo right after the shove is always still inside the window, so use a longer one.)
            EnemyTuning longer = EnemyTuning.CreateDaoSoldier();
            longer.BreakOut.Cooldown = 4f;
            cooldown = longer.BreakOut.Cooldown;
            EnemyDriver e = CirclingSoldier(longer);
            Hits(e, 3, 0.2f);
            CloseUntil(e, BreakingOut, 5);
            CloseUntil(e, x => !x.Brain.IsBreakingOut, 120);
            Hits(e, 3, 0.1f);
            Close(e, (int)((cooldown + e.Brain.Tuning.BreakOut.HitWindow) / e.Dt));
            Assert.AreEqual(1, BreakOutStarts(e).Count, "the combo had left the window by the time the cooldown ended");
        }

        [Test]
        public void ResetClearsABreakOutInTheMaking()
        {
            EnemyDriver d = CirclingSoldier();
            Hits(d, 3, 0.2f);
            d.Brain.Reset();
            Assert.IsFalse(d.Brain.IsBreakOutArmed);
            d.SetTarget(new Vector3(0f, 0f, 1.8f));
            d.Run(60);
            Assert.IsFalse(d.Brain.IsBreakingOut);
        }
    }
}
