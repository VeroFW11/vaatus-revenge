using System;
using System.Numerics;

namespace VaatusRevenge.Core
{
    // Reads the player's combat model each frame and says what the body should show (FighterAnimInput).
    // PlayerController (Unity) and the headless CombatSim both use this one class, so the game and the offline
    // renders animate exactly the same way.
    //
    // Per frame: call OnEvent for each of the model's events, then Build. Attacks are timed straight from the
    // model's own action clock, so the animation can never drift from the frame data.
    public sealed class PlayerAnimationFeed
    {
        // Short reactions that the model has no state for: how long they show.
        public float HurtDuration = 0.4f;
        public float ParrySuccessDuration = 0.42f;
        public float HitStrengthPerDamage = 1f / 20f;     // a 20-damage hit is a full-strength flinch

        FighterAnimInput input;
        bool hasFacing;
        float lastFacing;
        int serial;
        int lastAttackId;
        PlayerState lastState = PlayerState.Locomotion;
        float stateTime;

        // the running dodge
        bool dodgeInAir;
        bool dodgeBackstep;
        Vector3 dodgeDirection;

        // a transient reaction (hurt, parry success)
        string reactionKey = "";
        float reactionTime;
        float reactionDuration;
        bool reactionStarted;
        bool reactionShown;

        // plunge (falling axe kick)
        bool plungeLanded;
        float plungeLandTime;

        // this frame's events
        bool hitThisFrame;
        Vector3 hitDirection;
        float hitStrength;

        public void Reset()
        {
            input = default;
            hasFacing = false;
            serial++;
            lastAttackId = 0;
            lastState = PlayerState.Locomotion;
            stateTime = 0f;
            reactionKey = "";
            reactionStarted = false;
            plungeLanded = false;
            hitThisFrame = false;
        }

        public void OnEvent(in PlayerEvent e)
        {
            switch (e.Type)
            {
                case PlayerEventType.DodgeStarted:
                    dodgeInAir = e.InAir;
                    dodgeBackstep = e.IsBackstep;
                    dodgeDirection = e.Direction;
                    serial++;
                    ClearReaction();
                    break;
                case PlayerEventType.AttackStarted:
                case PlayerEventType.ChargeStarted:
                case PlayerEventType.HealStarted:
                    plungeLanded = false;
                    serial++;
                    ClearReaction();
                    break;
                case PlayerEventType.PlungeImpact:
                    plungeLanded = true;
                    plungeLandTime = -1f;   // stamped with the model's clock in Build
                    break;
                case PlayerEventType.Damaged:
                    hitThisFrame = true;
                    hitDirection = e.Direction;
                    hitStrength = Math.Max(hitStrength, e.Amount * HitStrengthPerDamage);
                    StartReaction(AnimationKeys.Hurt, HurtDuration);
                    break;
                case PlayerEventType.Blocked:
                    hitThisFrame = true;
                    hitDirection = e.Direction;
                    hitStrength = Math.Max(hitStrength, 0.35f);
                    break;
                case PlayerEventType.Deflected:
                    StartReaction(AnimationKeys.ParrySuccess, ParrySuccessDuration);
                    break;
                case PlayerEventType.Staggered:
                case PlayerEventType.Died:
                case PlayerEventType.Respawned:
                    serial++;
                    ClearReaction();
                    break;
            }
        }

        // dt: this frame's scaled delta time (0 during hitstop: everything holds).
        public FighterAnimInput Build(PlayerCombatModel model, float dt)
        {
            return Build(model, dt, false, Vector3.Zero, Vector3.Zero);
        }

        // With a target (lock-on, soft lock or the nearest enemy): strikes are aimed at its chest. feet = the
        // player's own position, targetChest = the target's aim point, both in world space.
        public FighterAnimInput Build(PlayerCombatModel model, float dt, bool hasTarget, Vector3 feet, Vector3 targetChest)
        {
            if (model == null) return default;
            if (!AnimMath.IsFinite(dt) || dt < 0f) dt = 0f;
            float facing = model.FacingYaw;
            float yawDelta = hasFacing ? Wrap180(facing - lastFacing) : 0f;
            hasFacing = true;
            lastFacing = facing;

            PlayerState state = model.State;
            if (state != lastState)
            {
                stateTime = 0f;
                lastState = state;
            }
            else
            {
                stateTime += dt;
            }

            input = new FighterAnimInput
            {
                DeltaTime = dt,
                Grounded = model.IsGrounded,
                LocalVelocity = ToLocal(model.Velocity, facing),
                YawDelta = yawDelta,
                Sprinting = state == PlayerState.Sprinting,
                Dead = state == PlayerState.Dead,
                ActionKey = "",
                ActionSerial = serial,
            };

            if (hitThisFrame)
            {
                input.HitReaction = true;
                input.HitDirectionLocal = ToLocal(hitDirection, facing);
                input.HitStrength = AnimMath.Clamp(hitStrength, 0.2f, 1.2f);
                hitThisFrame = false;
                hitStrength = 0f;
            }

            MoveData move = model.CurrentMove;
            if (hasTarget && AnimMath.IsFinite(targetChest) && AnimMath.IsFinite(feet))
            {
                input.HasTarget = true;
                input.TargetLocal = ToLocal(targetChest - feet, facing);
            }
            if (move != null) input.StrikeLimb = move.Limb;
            switch (state)
            {
                case PlayerState.Dead:
                    SetState(AnimationKeys.Death, stateTime, 0f);
                    break;
                case PlayerState.Staggered:
                    SetState(AnimationKeys.Stagger, stateTime, stateTime + model.StaggerRemaining);
                    break;
                case PlayerState.Charging:
                    SetState(AnimationKeys.Charge, stateTime, 0f);
                    input.ChargeLevel = model.ChargeLevel;
                    break;
                case PlayerState.Healing:
                    SetState(AnimationKeys.Heal, model.ActionTime, model.Tuning != null ? model.Tuning.HealDuration : 0f);
                    break;
                case PlayerState.Dodging:
                {
                    DodgeProfile dodge = model.MoveSet != null ? model.MoveSet.Dodge : null;
                    string key = dodgeInAir || model.IsAirDashing ? AnimationKeys.AirDash
                        : dodgeBackstep ? AnimationKeys.Backstep : AnimationKeys.Dodge;
                    SetState(key, stateTime, dodge != null ? dodge.TotalDuration : 0f);
                    Vector3 local = ToLocal(dodgeDirection, facing);
                    input.ActionDirectionYaw = local.LengthSquared() > 1e-4f ? MathF.Atan2(local.X, local.Z) * AnimMath.Rad2Deg : 0f;
                    break;
                }
                case PlayerState.Guarding:
                    SetState(model.MoveSet != null && model.MoveSet.Guard != null && !model.MoveSet.Guard.IsParryOnly && !model.IsDeflectWindowOpen
                        ? AnimationKeys.Block : AnimationKeys.Parry, stateTime, 0f);
                    break;
                case PlayerState.Attacking:
                    if (move != null) SetAttack(model, move);
                    break;
                case PlayerState.Plunging:
                    if (move != null) SetPlunge(model, move);
                    break;
            }

            // A short reaction (flinch, parry flourish) shows when the model is otherwise free (or guarding).
            if (reactionKey.Length > 0)
            {
                if (reactionStarted) reactionTime += dt;
                reactionStarted = true;
                bool free = state == PlayerState.Locomotion || state == PlayerState.Sprinting || state == PlayerState.Airborne
                            || state == PlayerState.Guarding;
                if (reactionTime >= reactionDuration || !free) ClearReaction();
                else
                {
                    if (!reactionShown)
                    {
                        // Only a reaction that really shows starts a new blend (a hit taken mid-attack or while
                        // launched must not restart what's playing).
                        reactionShown = true;
                        serial++;
                    }
                    input.ActionKey = reactionKey;
                    input.ActionTime = reactionTime;
                    input.ActionDuration = reactionDuration;
                    input.HasFrameData = false;
                }
            }
            input.ActionSerial = serial;
            return input;
        }

        void SetState(string key, float time, float duration)
        {
            input.ActionKey = key;
            input.ActionTime = time;
            input.ActionDuration = duration;
            input.HasFrameData = false;
        }

        void SetAttack(PlayerCombatModel model, MoveData move)
        {
            int id = model.CurrentAttackId;
            if (id != lastAttackId)
            {
                lastAttackId = id;
                serial++;
            }
            input.ActionKey = KeyFor(move, model.CurrentAttackKind);
            input.ActionTime = model.ActionTime;
            input.HasFrameData = true;
            input.Timing = ClipTiming.FromMove(move);
        }

        // Falling axe kick: the hang is the "startup", the fall holds the raised leg (as long as it takes), and the
        // landing is the recovery, when the leg chops down.
        void SetPlunge(PlayerCombatModel model, MoveData move)
        {
            int id = model.CurrentAttackId;
            if (id != lastAttackId)
            {
                lastAttackId = id;
                serial++;
            }
            PlungeSettings plunge = model.MoveSet != null ? model.MoveSet.Plunge : null;
            float hang = plunge != null ? Math.Max(0.01f, plunge.HangTime) : 0.1f;
            const float fallHold = 10f;
            float t = model.ActionTime;
            if (plungeLanded && plungeLandTime < 0f) plungeLandTime = t;
            float time = plungeLanded ? hang + fallHold + Math.Max(0f, t - plungeLandTime) : Math.Min(t, hang + fallHold * 0.99f);
            input.ActionKey = string.IsNullOrEmpty(move.AnimationKey) ? AnimationKeys.AxeKick : move.AnimationKey;
            input.ActionTime = time;
            input.HasFrameData = true;
            input.Timing = new ClipTiming { Startup = hang, Active = fallHold, Recovery = move.Recovery, HitCount = 1 };
        }

        // The move's own key; a move with none gets a sensible default from what it is.
        static string KeyFor(MoveData move, PlayerAttackKind kind)
        {
            if (!string.IsNullOrEmpty(move.AnimationKey)) return move.AnimationKey;
            switch (kind)
            {
                case PlayerAttackKind.Heavy: return AnimationKeys.FaJinPalm;
                case PlayerAttackKind.Skill: return AnimationKeys.FireBlast;
                case PlayerAttackKind.Sprint: return AnimationKeys.SprintKick;
                case PlayerAttackKind.ZipStrike: return AnimationKeys.ZipKick;
                case PlayerAttackKind.Launcher: return AnimationKeys.Launcher;
                case PlayerAttackKind.Air: return AnimationKeys.AirJab;
                case PlayerAttackKind.Plunge: return AnimationKeys.AxeKick;
            }
            return move.Limb == Limb.LeftFist ? AnimationKeys.Jab
                : move.Limb == Limb.RightFoot || move.Limb == Limb.LeftFoot ? AnimationKeys.SnapKick : AnimationKeys.Cross;
        }

        void StartReaction(string key, float duration)
        {
            reactionKey = key;
            reactionTime = 0f;
            reactionDuration = Math.Max(0.05f, duration);
            reactionStarted = false;
            reactionShown = false;
        }

        void ClearReaction()
        {
            reactionKey = "";
            reactionTime = 0f;
            reactionStarted = false;
        }

        // World direction -> the fighter's own frame (x right, z forward).
        public static Vector3 ToLocal(Vector3 world, float facingYaw)
        {
            if (!AnimMath.IsFinite(world)) return Vector3.Zero;
            return AnimMath.Rotate(AnimMath.Yaw(-facingYaw), world);
        }

        static float Wrap180(float degrees)
        {
            if (!AnimMath.IsFinite(degrees)) return 0f;
            degrees %= 360f;
            if (degrees > 180f) degrees -= 360f;
            else if (degrees <= -180f) degrees += 360f;
            return degrees;
        }
    }
}
