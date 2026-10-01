using System;
using System.Collections.Generic;
using System.Numerics;
using NUnit.Framework;
using VaatusRevenge.Core;

namespace VaatusRevenge.Tests
{
    // Buttons a test holds down on a frame (edges are worked out from the previous frame, like the reader).
    [Flags]
    public enum Pad
    {
        None = 0,
        Light = 1,
        Heavy = 2,
        Dodge = 4,
        Jump = 8,
        Guard = 16,
        Skill = 32,
        Heal = 64,
        Zip = 128,
        AbilityNorth = 256,
        AbilityEast = 512
    }

    // Drives a PlayerCombatModel the way PlayerController will: one Tick per frame at a fixed dt, then moves
    // a pretend CharacterController on flat ground at y = 0 by the returned velocity. Logs every event with
    // the frame it happened on. Frame 0 is the first Step.
    public sealed class PlayerDriver
    {
        public readonly float Dt;
        public readonly PlayerCombatModel Model;
        public PlayerWorldState World;
        public readonly List<PlayerEvent> Log = new List<PlayerEvent>();
        public readonly List<int> LogFrames = new List<int>();
        public int Frame = -1;
        public PlayerTickResult Last;
        public ElementId Select;    // sent as PlayerInputFrame.ElementSelect on the next Step only (RB + a face button)
        public PlayerCommand Retract;   // sent as PlayerInputFrame.RetractPress with Select (a face press upgraded late)
        Pad held;

        public PlayerDriver(PlayerTuning tuning = null, ElementMoveSet moveSet = null, float fps = 60f)
        {
            Dt = 1f / fps;
            Model = new PlayerCombatModel(tuning ?? PlayerTuning.CreateFluid(), moveSet ?? ElementMoveSet.CreateFireFluid(), 1);
            World = new PlayerWorldState { Grounded = true, SelfRadius = 0.4f, SelfHeight = 1.8f };
        }

        PlayerDriver(PlayerCombatModel model, float fps)
        {
            Dt = 1f / fps;
            Model = model;
            World = new PlayerWorldState { Grounded = true, SelfRadius = 0.4f, SelfHeight = 1.8f };
        }

        // All four elements (Build 05). Null = the Fluid preset with the four default sets.
        public static PlayerDriver Elements(PlayerTuning tuning = null, ElementLoadout loadout = null, float fps = 60f)
        {
            return new PlayerDriver(new PlayerCombatModel(tuning ?? PlayerTuning.CreateFluid(), loadout ?? ElementLoadout.CreateFluid(), 1), fps);
        }

        // Fire Fluid with the dodge and chain cancels it had before Build 05 (a 0.30 s dodge costing 6 stamina, i-frames
        // 0.02-0.24, attack cancel 0.14, next dodge 0.28; light chain dodge cancels 0.22-0.38; no chain cap, no exit carry).
        // Several older tests pin a general rule (the input buffer, stamina pauses, i-frame frame maths) with those numbers;
        // Build 05 made the Fluid dodge free and chain hits dodge-cancellable from their first frame, so these tests now
        // state the numbers they rely on instead of reading the new defaults.
        public static ElementMoveSet PreBuild05Fire()
        {
            ElementMoveSet set = ElementMoveSet.CreateFireFluid();
            DodgeProfile d = set.Dodge;
            d.Duration = 0.30f;
            d.DashEaseOut = 0.6f;
            d.EvadeOutDistance = 4.2f;
            d.StaminaCost = 6f;
            d.RequiresStamina = true;
            d.IFrameStart = 0.02f;
            d.IFrameEnd = 0.24f;
            d.ChainIFrameGap = 0.05f;
            d.EvadeAttackCancelAt = 0.14f;
            d.SlipInAttackCancelAt = 0.14f;
            d.NextDodgeAt = 0.28f;
            d.ChainMax = 1000;
            d.ExitSpeedCarry = 0f;
            d.AutoEvadeOnNeutral = false;
            float[] dodgeCancels = { 0.22f, 0.23f, 0.24f, 0.30f, 0.38f };
            for (int i = 0; i < set.LightChain.Length && i < dodgeCancels.Length; i++) set.LightChain[i].DodgeCancelAt = dodgeCancels[i];
            set.Aerial.AirDashesPerJump = 1;
            return set;
        }

        public static PlayerDriver PreBuild05(PlayerTuning tuning = null, float fps = 60f)
        {
            return new PlayerDriver(tuning, PreBuild05Fire(), fps);
        }

        public static PlayerDriver Punishing(float fps = 60f)
        {
            return new PlayerDriver(PlayerTuning.CreatePunishing(), ElementMoveSet.CreateFirePunishing(), fps);
        }

        // Fire with a held block (GuardSettings.Style = BlockAndParry), for the tests of blocking itself: Fire is
        // parry-only, but other elements may block, so the block rules stay covered.
        public static PlayerDriver Blocking(float fps = 60f)
        {
            return Blocking(ElementMoveSet.CreateFireFluid(), fps);
        }

        public static PlayerDriver Blocking(ElementMoveSet set, float fps = 60f)
        {
            set.Guard.Style = DefenseStyle.BlockAndParry;
            return new PlayerDriver(null, set, fps);
        }

        public PlayerInputFrame MakeInput(Pad now, Vector2 move)
        {
            var input = new PlayerInputFrame { Move = move, ElementSelect = Select, RetractPress = Retract };
            input.Light = ButtonState.From((now & Pad.Light) != 0, (held & Pad.Light) != 0);
            input.Heavy = ButtonState.From((now & Pad.Heavy) != 0, (held & Pad.Heavy) != 0);
            input.Dodge = ButtonState.From((now & Pad.Dodge) != 0, (held & Pad.Dodge) != 0);
            input.Jump = ButtonState.From((now & Pad.Jump) != 0, (held & Pad.Jump) != 0);
            input.Guard = ButtonState.From((now & Pad.Guard) != 0, (held & Pad.Guard) != 0);
            input.Skill = ButtonState.From((now & Pad.Skill) != 0, (held & Pad.Skill) != 0);
            input.Heal = ButtonState.From((now & Pad.Heal) != 0, (held & Pad.Heal) != 0);
            input.ZipStrike = ButtonState.From((now & Pad.Zip) != 0, (held & Pad.Zip) != 0);
            input.AbilityNorth = ButtonState.From((now & Pad.AbilityNorth) != 0, (held & Pad.AbilityNorth) != 0);
            input.AbilityEast = ButtonState.From((now & Pad.AbilityEast) != 0, (held & Pad.AbilityEast) != 0);
            return input;
        }

        public PlayerTickResult Step(Pad now = Pad.None, Vector2 move = default, float? dt = null)
        {
            PlayerInputFrame input = MakeInput(now, move);
            held = now;
            return StepFrame(input, dt);
        }

        // One frame with a ready-made input (a test that builds its frames from raw pad buttons, e.g. through
        // PadChordReader, the way PlayerInputReader does).
        public PlayerTickResult StepFrame(PlayerInputFrame input, float? dt = null)
        {
            Select = ElementId.None;
            Retract = PlayerCommand.None;
            float stepDt = dt ?? Dt;
            Frame++;
            Last = Model.Tick(stepDt, input, World);
            for (int i = 0; i < Last.Events.Count; i++)
            {
                Log.Add(Last.Events[i]);
                LogFrames.Add(Frame);
            }
            AssertFinite(Last.Velocity, "velocity");
            Assert.IsFalse(float.IsNaN(Last.FacingYaw) || float.IsInfinity(Last.FacingYaw), "facing yaw");

            if (stepDt > 0f)
            {
                Vector3 p = World.Position + Last.Velocity * stepDt;
                World.Grounded = p.Y <= 0f;
                if (p.Y < 0f) p.Y = 0f;
                World.Position = p;
            }
            return Last;
        }

        public void Run(int frames, Pad now = Pad.None, Vector2 move = default)
        {
            for (int i = 0; i < frames; i++) Step(now, move);
        }

        // Steps until the condition holds (checked after each step). Fails after maxFrames.
        public int RunUntil(Func<PlayerDriver, bool> done, int maxFrames, Pad now = Pad.None, Vector2 move = default)
        {
            for (int i = 0; i < maxFrames; i++)
            {
                Step(now, move);
                if (done(this)) return Frame;
            }
            Assert.Fail("condition not reached in " + maxFrames + " frames (state " + Model.State + ")");
            return -1;
        }

        public void Tap(Pad button, Vector2 move = default)
        {
            Step(button, move);
            Step(Pad.None, move);
        }

        public int FirstFrame(PlayerEventType type, int fromFrame = 0)
        {
            for (int i = 0; i < Log.Count; i++)
                if (Log[i].Type == type && LogFrames[i] >= fromFrame) return LogFrames[i];
            return -1;
        }

        public int Count(PlayerEventType type)
        {
            int n = 0;
            for (int i = 0; i < Log.Count; i++) if (Log[i].Type == type) n++;
            return n;
        }

        public List<PlayerEvent> All(PlayerEventType type)
        {
            var list = new List<PlayerEvent>();
            for (int i = 0; i < Log.Count; i++) if (Log[i].Type == type) list.Add(Log[i]);
            return list;
        }

        public void ClearLog()
        {
            Log.Clear();
            LogFrames.Clear();
        }

        // Frame on which an action started on frame 0 first reaches 'mark' seconds (same float sums as the model).
        public int FramesToReach(float mark)
        {
            float t = 0f;
            int frames = 0;
            while (t < mark)
            {
                t += Dt;
                frames++;
            }
            return frames;
        }

        public void LockOn(Vector3 target, float radius = 0.4f)
        {
            World.HasLockTarget = true;
            World.LockTargetPosition = target;
            World.LockTargetAimPoint = target + new Vector3(0f, 1.2f, 0f);
            World.LockTargetRadius = radius;
        }

        public static void AssertFinite(Vector3 v, string what)
        {
            Assert.IsFalse(float.IsNaN(v.X) || float.IsNaN(v.Y) || float.IsNaN(v.Z), what + " is NaN");
            Assert.IsFalse(float.IsInfinity(v.X) || float.IsInfinity(v.Y) || float.IsInfinity(v.Z), what + " is infinite");
        }

        // A hit from an enemy standing in 'fromDirection' (world, horizontal) of the player.
        public static DamageInfo EnemyHit(float damage, float poise, Vector3 fromDirection, bool parryable = true,
            float guardStamina = 10f, bool unblockable = false)
        {
            return new DamageInfo
            {
                Damage = damage, PoiseDamage = poise, GuardStaminaDamage = guardStamina, Knockback = 0.5f,
                Direction = -Vector3.Normalize(fromDirection), SourceTeam = Team.Enemy, SourceId = 99,
                AttackId = CombatIds.Next(), Parryable = parryable, Unblockable = unblockable, Kind = HitKind.Light
            };
        }

        public HitResult HitFromFront(float damage, float poise = 0f, bool parryable = true, float guardStamina = 10f)
        {
            return Model.ReceiveHit(EnemyHit(damage, poise, Model.Forward, parryable, guardStamina), Model.Forward);
        }

        // ---------------------------------------------------------------- Build 05 helpers

        // An enemy to fight: the soft-lock target and the nearest enemy (feet at 'position').
        public void Target(Vector3 position, float radius = 0.4f)
        {
            World.HasSoftTarget = true;
            World.SoftTargetPosition = position;
            World.SoftTargetAimPoint = position + new Vector3(0f, 1.2f, 0f);
            World.SoftTargetRadius = radius;
            World.HasNearestEnemy = true;
            World.NearestEnemyPosition = position;
            World.NearestEnemyRadius = radius;
        }

        public void ClearTargets()
        {
            World.HasSoftTarget = false;
            World.HasLockTarget = false;
            World.HasNearestEnemy = false;
            World.HasZipTarget = false;
        }

        public int Started => Count(PlayerEventType.AttackStarted);

        public PlayerEvent LastOf(PlayerEventType type)
        {
            for (int i = Log.Count - 1; i >= 0; i--) if (Log[i].Type == type) return Log[i];
            Assert.Fail("no " + type + " event");
            return default;
        }

        public PlayerEvent LastStarted => LastOf(PlayerEventType.AttackStarted);

        // Steps until the n-th attack has started (n counts every AttackStarted since the driver was made).
        public void RunUntilStarted(int n, int maxFrames = 120, Pad now = Pad.None, Vector2 move = default)
        {
            if (Started >= n) return;
            RunUntil(x => x.Started >= n, maxFrames, now, move);
        }

        // Every strike that has gone active since the last call lands a clean hit (the hit system's report).
        public void LandStrikes(bool targetAirborne = false)
        {
            for (int i = landedUpTo; i < Log.Count; i++)
            {
                PlayerEventType type = Log[i].Type;
                if (type == PlayerEventType.AttackActiveStart || type == PlayerEventType.ProjectileLaunched)
                    Model.OnAttackLanded(new HitResult { Outcome = HitOutcome.Hit }, Log[i].AttackId, targetAirborne);
            }
            landedUpTo = Log.Count;
        }

        int landedUpTo;

        // Steps, landing every strike as it goes active.
        public void RunLanding(int frames, Pad now = Pad.None)
        {
            for (int i = 0; i < frames; i++)
            {
                Step(now);
                LandStrikes();
            }
        }

        // Presses on the running string move's beat (the frame that crosses it).
        public void PressOnBeat(Pad pad = Pad.Light, Vector2 move = default)
        {
            for (int i = 0; i < 120 && Model.Rhythm.Active && Model.Rhythm.TimeToBeat > Dt * 0.5f; i++) Step(Pad.None, move);
            Step(pad, move);
        }

        // RB + an element's button on the next frame, on the running string move's beat.
        public void SwitchOnBeat(ElementId element)
        {
            for (int i = 0; i < 120 && Model.Rhythm.Active && Model.Rhythm.TimeToBeat > Dt * 0.5f; i++) Step();
            Select = element;
            Step();
        }

        // A string of on-beat presses: the first press, then 'followUps' more, each on the beat.
        // Ends with the last move just started.
        public void OnBeatString(int followUps)
        {
            int n = Started + 1;
            Step(Pad.Light);
            for (int i = 0; i < followUps; i++)
            {
                RunUntilStarted(n);
                PressOnBeat();
                n++;
            }
            RunUntilStarted(n);
        }
    }

    // Sanity checks for the driver itself, so the other tests can trust it.
    public class CombatDriverTests
    {
        [Test]
        public void IdlePlayerStaysPutOnTheGround()
        {
            var d = new PlayerDriver();
            d.Run(120);
            Assert.AreEqual(PlayerState.Locomotion, d.Model.State);
            Assert.That(d.World.Position.X, Is.EqualTo(0f).Within(1e-5f));
            Assert.That(d.World.Position.Z, Is.EqualTo(0f).Within(1e-5f));
            Assert.IsTrue(d.World.Grounded);
            Assert.Less(d.Last.Velocity.Y, 0f, "grounded characters get a small push down to hug the floor");
        }

        [Test]
        public void FramesToReachMatchesSixtyFps()
        {
            var d = new PlayerDriver();
            Assert.AreEqual(8, d.FramesToReach(0.12f));
            Assert.AreEqual(14, d.FramesToReach(0.22f));
        }
    }
}
