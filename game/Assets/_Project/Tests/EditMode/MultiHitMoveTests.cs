using System;
using System.Collections.Generic;
using System.Numerics;
using System.Reflection;
using NUnit.Framework;
using VaatusRevenge.Core;

namespace VaatusRevenge.Tests
{
    // The four per-move mechanics Build 05 adds (spec 2.8): multi-hit moves (each sub-hit its own AttackId, one shared
    // MoveInstanceId; the break-out counts per move), pull (stops short), orbit (the lunge curves round the target) and
    // heal on hit (capped per move). Plus the Punishing factory staying as it was for every field that existed before.
    public class MultiHitMoveTests
    {
        const float Dt = 1f / 60f;

        static ElementLoadout WithJab(Action<MoveData> edit)
        {
            ElementLoadout loadout = ElementLoadout.CreateFluid();
            edit(loadout.Fire.LightChain[0]);
            return loadout;
        }

        [Test]
        public void SubHitsGoLiveAtInterval()
        {
            PlayerDriver d = PlayerDriver.Elements(null, WithJab(m =>
            {
                m.HitCount = 3;
                m.HitInterval = 0.05f;
                m.Active = 0.12f;
            }));
            MoveData jab = d.Model.Loadout.Fire.LightChain[0];
            d.Step(Pad.Light);
            d.Run(40);
            List<PlayerEvent> starts = d.All(PlayerEventType.AttackActiveStart);
            Assert.AreEqual(3, starts.Count);
            Assert.AreEqual(3, d.Count(PlayerEventType.AttackActiveEnd), "one Active pair per sub-hit");
            int k = 0;
            for (int i = 0; i < d.Log.Count; i++)
            {
                if (d.Log[i].Type != PlayerEventType.AttackActiveStart) continue;
                Assert.AreEqual(d.FramesToReach(jab.ActiveStart + k * jab.HitInterval), d.LogFrames[i], "sub-hit " + k);
                k++;
            }
            Assert.AreEqual(3, new HashSet<int> { starts[0].AttackId, starts[1].AttackId, starts[2].AttackId }.Count, "each its own AttackId");
        }

        [Test]
        public void SubHitsShareMoveInstanceId()
        {
            PlayerDriver d = PlayerDriver.Elements(null, WithJab(m =>
            {
                m.HitCount = 3;
                m.HitInterval = 0.04f;
                m.Active = 0.10f;
            }));
            d.Step(Pad.Light);
            d.Run(40);
            List<PlayerEvent> starts = d.All(PlayerEventType.AttackActiveStart);
            int instance = d.LastOf(PlayerEventType.AttackStarted).MoveInstanceId;
            Assert.AreEqual(starts[0].AttackId, instance, "the first sub-hit's AttackId");
            foreach (PlayerEvent e in starts)
            {
                Assert.AreEqual(instance, e.MoveInstanceId);
                Assert.AreEqual(instance, d.Model.BuildDamage(e.Move, e.AttackId).MoveInstanceId);
            }
        }

        static EnemyBrain FreshSoldier(out EnemyWorldState world)
        {
            EnemyTuning tuning = EnemyTuning.CreateDaoSoldier();
            tuning.MaxPoise = 1000f;                             // never staggers: only the break-out count matters here
            EnemyBrain soldier = EnemyBrain.Create(tuning, null, 77, 3);
            world = new EnemyWorldState { Grounded = true, SelfRadius = 0.4f, HasTarget = true, TargetPosition = new Vector3(0f, 0f, 1.5f), TargetRadius = 0.4f };
            soldier.Tick(Dt, world);
            return soldier;
        }

        static DamageInfo PlayerHit(int moveInstance)
        {
            return new DamageInfo
            {
                Damage = 1f, PoiseDamage = 1f, SourceTeam = Team.Player, SourceId = 1, AttackId = CombatIds.Next(), Parryable = true,
                MoveInstanceId = moveInstance, Direction = new Vector3(0f, 0f, -1f)
            };
        }

        [Test]
        public void BreakOutCountsPerMoveInstance()
        {
            EnemyBrain soldier = FreshSoldier(out EnemyWorldState world);
            int flurry = CombatIds.Next();
            for (int i = 0; i < 6; i++) soldier.ReceiveHit(PlayerHit(flurry), soldier.Forward);   // one move, six sub-hits
            Assert.IsFalse(soldier.IsBreakOutArmed, "a multi-hit move is one hit for the anti-mash rule");

            EnemyBrain other = FreshSoldier(out world);
            int hitsNeeded = other.Tuning.BreakOut.HitsToTrigger;
            for (int i = 0; i < hitsNeeded; i++) other.ReceiveHit(PlayerHit(CombatIds.Next()), other.Forward);
            Assert.IsTrue(other.IsBreakOutArmed, "separate moves still count");
        }

        // Moves the enemy by its velocity for 'frames' frames; returns its feet.
        static Vector3 Settle(EnemyBrain brain, ref EnemyWorldState world, int frames)
        {
            for (int i = 0; i < frames; i++)
            {
                EnemyTickResult r = brain.Tick(Dt, world);
                world.Position += Directions.Flatten(r.Velocity) * Dt;
            }
            return world.Position;
        }

        [Test]
        public void PullStopsShort()
        {
            foreach (float pull in new[] { 1.5f, 8f })
            {
                EnemyTuning tuning = EnemyTuning.CreateDaoSoldier();
                tuning.MaxPoise = 1000f;
                tuning.WalkSpeed = tuning.ChaseSpeed = tuning.StrafeSpeed = tuning.RetreatSpeed = 0f;
                EnemyBrain brain = EnemyBrain.Create(tuning, null, 78, 4);
                brain.Passive = true;
                var world = new EnemyWorldState { Grounded = true, SelfRadius = 0.4f, HasTarget = true, Position = new Vector3(0f, 0f, 6f),
                    TargetPosition = Vector3.Zero, TargetRadius = 0.4f };
                brain.Tick(Dt, world);
                DamageInfo hit = PlayerHit(CombatIds.Next());
                hit.PullDistance = pull;
                brain.ReceiveHit(hit, brain.Forward);
                Vector3 end = Settle(brain, ref world, 40);
                float expected = Math.Max(tuning.PullStopDistance, 6f - pull);
                Assert.AreEqual(expected, end.Length(), 0.05f, "pulled " + pull + " m toward the attacker, never closer than PullStopDistance");
            }
        }

        [Test]
        public void OrbitEndsAtDegrees()
        {
            foreach (Vector2 stick in new[] { Vector2.Zero, new Vector2(-1f, 0f) })
            {
                PlayerDriver d = PlayerDriver.Elements(null, WithJab(m => m.OrbitDegrees = 90f));
                var target = new Vector3(0f, 0f, 2.5f);
                d.Target(target);
                d.Step(Pad.Light, stick);
                d.RunUntil(x => x.Model.Phase == AttackPhase.Recovery, 60, Pad.None, stick);
                Vector3 offset = Directions.Flatten(d.World.Position - target);
                float angle = Angles.Delta(180f, Directions.YawOf(offset, 0f));
                // Neutral: round to the camera's right (+X); stick left: round to the left (-X). A quarter turn either way.
                float expected = stick == Vector2.Zero ? -90f : 90f;
                Assert.AreEqual(expected, angle, 3f, "stick " + stick);
                Assert.AreEqual(stick == Vector2.Zero ? 1 : -1, Math.Sign(d.World.Position.X));
                Assert.AreEqual(0f, Angles.Delta(d.Model.FacingYaw, Directions.YawOf(-offset, 0f)), 5f, "still facing it");
            }
        }

        [Test]
        public void HealOnHitCappedPerMove()
        {
            PlayerDriver d = PlayerDriver.Elements(null, WithJab(m =>
            {
                m.HitCount = 4;
                m.HitInterval = 0.025f;
                m.Active = 0.10f;
                m.HealOnHit = 2f;
                m.HealPerMoveMax = 5f;
            }));
            d.HitFromFront(40f);
            float hurt = d.Model.Health;
            d.Run(40);
            d.Step(Pad.Light);
            d.RunLanding(40);
            Assert.AreEqual(4, d.Model.ComboCount);
            Assert.AreEqual(hurt + 5f, d.Model.Health, 1e-3f, "2 per sub-hit, at most 5 for the move");
            // Verify S-12: every heal is announced (the HUD and the body show it), with what it really restored.
            float announced = 0f;
            foreach (PlayerEvent e in d.All(PlayerEventType.HealedOnHit)) announced += e.Amount;
            Assert.AreEqual(3, d.Count(PlayerEventType.HealedOnHit), "2 + 2 + 1, then the cap");
            Assert.AreEqual(5f, announced, 1e-3f);
            d.Step(Pad.Light);
            d.RunLanding(40);
            Assert.AreEqual(hurt + 10f, d.Model.Health, 1e-3f, "the cap is per move");

            PlayerDriver one = PlayerDriver.Elements(null, WithJab(m => m.HealOnHit = 3f));
            one.HitFromFront(40f);
            float before = one.Model.Health;
            one.Run(40);
            one.Step(Pad.Light);
            one.RunLanding(40);
            Assert.AreEqual(before + 3f, one.Model.Health, 1e-3f, "no cap set: one HealOnHit");
        }

        // Build 05 rebuilt CreateFirePunishing as ApplyPunishing(CreateFireFluid()); every field that existed before keeps
        // its old Punishing value.
        [Test]
        public void PunishingFactoryUnchangedForExistingFields()
        {
            ElementMoveSet p = ElementMoveSet.CreateFirePunishing();
            ElementMoveSet viaRule = ElementMoveSet.CreateFireFluid();
            ElementMoveSet.ApplyPunishing(viaRule);
            AssertSameData(p, viaRule, "set");

            foreach (MoveData m in p.LightChain)
            {
                Assert.AreEqual(13f, m.StaminaCost);
                Assert.AreEqual(m.ActiveEnd + m.Recovery * 0.6f, m.DodgeCancelAt, 1e-5f);
            }
            foreach (MoveData m in p.AirChain) Assert.AreEqual(11f, m.StaminaCost);
            Assert.AreEqual(28f, p.Heavy.StaminaCost);
            Assert.AreEqual(20f, p.ZipStrike.StaminaCost);
            Assert.AreEqual(18f, p.Launcher.StaminaCost);
            Assert.AreEqual(26f, p.AbilityNorth.StaminaCost);
            Assert.AreEqual(28f, p.AbilityEast.StaminaCost);
            Assert.AreEqual(16f, p.SprintAttack.StaminaCost, "unchanged by Punishing");
            Assert.AreEqual(22f, p.Skill.StaminaCost, "unchanged by Punishing");
            foreach (MoveData m in new[] { p.Heavy, p.SprintAttack, p.Skill, p.ZipStrike, p.Launcher, p.AbilityNorth, p.AbilityEast })
                Assert.AreEqual(m.ActiveEnd + m.Recovery * 0.6f, m.DodgeCancelAt, 1e-5f, m.DisplayName);
            Assert.AreEqual(4, p.Aerial.AirAttacksPerJump);
            Assert.AreEqual(0, p.Aerial.AirDashesPerJump);
            Assert.IsFalse(p.Charge.CanDodgeCancelCharge);
            Assert.AreEqual(p.PlungeAttack.Recovery, p.PlungeAttack.DodgeCancelAt, 1e-6f);
            DodgeProfile d = p.Dodge;
            Assert.AreEqual(16f, d.StaminaCost);
            Assert.AreEqual(0.36f, d.Duration, 1e-6f);
            Assert.AreEqual(0.04f, d.IFrameStart, 1e-6f);
            Assert.AreEqual(0.30f, d.IFrameEnd, 1e-6f);
            Assert.AreEqual(0.12f, d.EndRecovery, 1e-6f);
            Assert.AreEqual(0.48f, d.AttackCancelAt, 1e-6f);
            Assert.AreEqual(0.48f, d.NextDodgeAt, 1e-6f);
            Assert.AreEqual(4.2f, d.Distance, 1e-6f);
            Assert.AreEqual(2.2f, d.BackstepDistance, 1e-6f);
            Assert.AreEqual(0.6f, d.DashEaseOut, 1e-6f);
            Assert.AreEqual(0.05f, d.ChainIFrameGap, 1e-6f);
            Assert.IsFalse(d.PerfectDodgeEnabled);
            // The pre-existing moves' own numbers (spot checks: names, damage, poise, frame data).
            string[] names = { "Flame Jab", "Flame Cross", "Rising Snap Kick", "Dragon Tail Kick", "Phoenix Palm" };
            float[] poise = { 8f, 9f, 7f, 8f, 11f };
            for (int i = 0; i < 5; i++)
            {
                Assert.AreEqual(names[i], p.LightChain[i].DisplayName);
                Assert.AreEqual(poise[i], p.LightChain[i].PoiseDamage);
            }
            Assert.AreEqual(0.22f, p.LightChain[4].Startup, 1e-6f);
            Assert.AreEqual(18f, p.LightChain[4].Damage);
            Assert.AreEqual(ElementMoveSet.CurrentDataVersion, p.DataVersion);
        }

        static void AssertSameData(object a, object b, string path)
        {
            if (a == null || b == null)
            {
                Assert.AreEqual(a == null, b == null, path);
                return;
            }
            Type type = a.GetType();
            if (type.IsPrimitive || type.IsEnum || type == typeof(string))
            {
                Assert.AreEqual(a, b, path);
                return;
            }
            if (a is Array arrayA)
            {
                var arrayB = (Array)b;
                Assert.AreEqual(arrayA.Length, arrayB.Length, path);
                for (int i = 0; i < arrayA.Length; i++) AssertSameData(arrayA.GetValue(i), arrayB.GetValue(i), path + "[" + i + "]");
                return;
            }
            foreach (FieldInfo f in type.GetFields(BindingFlags.Public | BindingFlags.Instance)) AssertSameData(f.GetValue(a), f.GetValue(b), path + "." + f.Name);
        }
    }
}
