using System;
using System.Numerics;

namespace VaatusRevenge.Core
{
    // Reads an enemy brain each frame and says what its body should show (FighterAnimInput). Used by the Unity
    // EnemyFighter and by the headless CombatSim alike.
    //
    // Per frame: call OnEvent for each of the brain's events, then Build. The brain doesn't expose its attack
    // clock, so this keeps its own: 0 on the frame the telegraph starts, + dt every frame after (the same rule
    // the brain uses), which keeps each swing landing on its hit window, strike after strike in a combo.
    public sealed class EnemyAnimationFeed
    {
        public float HurtDuration = 0.36f;
        public float GetUpTime = 0.65f;                    // the last this-many seconds of a knockdown are the kip-up
        public float HitStrengthPerDamage = 1f / 18f;

        FighterAnimInput input;
        bool hasFacing;
        float lastFacing;
        int serial;
        EnemyState lastState = EnemyState.Idle;
        float stateTime;

        EnemyAttackData attack;
        float attackTime;
        bool attackStartedThisFrame;

        bool knockedDown;
        float knockdownTime;
        float knockdownDuration;
        bool gettingUp;

        string reactionKey = "";
        float reactionTime;
        bool reactionStarted;

        bool hitThisFrame;
        Vector3 hitDirection;
        float hitStrength;

        public void Reset()
        {
            input = default;
            hasFacing = false;
            serial++;
            lastState = EnemyState.Idle;
            stateTime = 0f;
            attack = null;
            knockedDown = false;
            gettingUp = false;
            reactionKey = "";
            hitThisFrame = false;
        }

        public void OnEvent(in EnemyEvent e)
        {
            switch (e.Type)
            {
                case EnemyEventType.TelegraphStarted:
                    attack = e.Attack;
                    attackTime = 0f;
                    attackStartedThisFrame = true;
                    serial++;
                    reactionKey = "";
                    break;
                case EnemyEventType.AttackEnded:
                    attack = null;
                    break;
                case EnemyEventType.Damaged:
                    hitThisFrame = true;
                    hitDirection = e.Direction;
                    hitStrength = Math.Max(hitStrength, e.Amount * HitStrengthPerDamage);
                    reactionKey = AnimationKeys.Hurt;
                    reactionTime = 0f;
                    reactionStarted = false;
                    serial++;
                    break;
                case EnemyEventType.Staggered:
                    attack = null;
                    serial++;
                    break;
                case EnemyEventType.Launched:
                    attack = null;
                    knockedDown = false;
                    serial++;
                    break;
                case EnemyEventType.Juggled:
                    hitThisFrame = true;
                    hitDirection = new Vector3(0f, e.Amount >= 0f ? 1f : -1f, 0f);
                    hitStrength = Math.Max(hitStrength, 0.6f);
                    break;
                case EnemyEventType.KnockedDown:
                    knockedDown = true;
                    gettingUp = false;
                    knockdownTime = 0f;
                    knockdownDuration = Math.Max(0f, e.Duration);
                    serial++;
                    break;
                case EnemyEventType.StaggerEnded:
                    knockedDown = false;
                    gettingUp = false;
                    serial++;
                    break;
                case EnemyEventType.Died:
                case EnemyEventType.Reset:
                    attack = null;
                    knockedDown = false;
                    gettingUp = false;
                    reactionKey = "";
                    serial++;
                    break;
            }
        }

        // dt: scaled delta time. grounded: the body is standing on something (CharacterController.isGrounded, or
        // always for a planted dummy). aimPitch: degrees up toward the target, for aiming clips (crossbow).
        public FighterAnimInput Build(EnemyBrain brain, float dt, bool grounded, float aimPitch = 0f)
        {
            if (brain == null) return default;
            if (!AnimMath.IsFinite(dt) || dt < 0f) dt = 0f;
            float facing = brain.FacingYaw;
            float yawDelta = hasFacing ? Wrap180(facing - lastFacing) : 0f;
            hasFacing = true;
            lastFacing = facing;

            EnemyState state = brain.State;
            if (state != lastState)
            {
                stateTime = 0f;
                lastState = state;
            }
            else
            {
                stateTime += dt;
            }
            if (attack != null)
            {
                if (attackStartedThisFrame) attackStartedThisFrame = false;
                else attackTime += dt;
            }

            Vector3 velocity = brain.Velocity;
            input = new FighterAnimInput
            {
                DeltaTime = dt,
                Grounded = grounded && state != EnemyState.Launched,
                LocalVelocity = PlayerAnimationFeed.ToLocal(velocity, facing),
                YawDelta = yawDelta,
                Dead = state == EnemyState.Dead,
                ActionKey = "",
                AimPitch = aimPitch,
            };
            if (hitThisFrame)
            {
                input.HitReaction = true;
                input.HitDirectionLocal = PlayerAnimationFeed.ToLocal(hitDirection, facing);
                input.HitStrength = AnimMath.Clamp(hitStrength, 0.2f, 1.2f);
                hitThisFrame = false;
                hitStrength = 0f;
            }

            switch (state)
            {
                case EnemyState.Dead:
                    SetState(AnimationKeys.Death, stateTime, 0f);
                    break;
                case EnemyState.Launched:
                    SetState(AnimationKeys.Launched, brain.LaunchedTime, 0f);
                    break;
                case EnemyState.Staggered:
                    if (knockedDown)
                    {
                        knockdownTime += dt;
                        float getUp = Math.Min(GetUpTime, knockdownDuration * 0.6f);
                        float lying = Math.Max(0f, knockdownDuration - getUp);
                        if (knockdownTime < lying) SetState(AnimationKeys.Knockdown, knockdownTime, 0f);
                        else
                        {
                            if (!gettingUp)
                            {
                                gettingUp = true;
                                serial++;
                            }
                            SetState(AnimationKeys.GetUp, knockdownTime - lying, getUp);
                        }
                    }
                    else
                    {
                        SetState(AnimationKeys.Stagger, stateTime, stateTime + brain.StaggerRemaining);
                    }
                    break;
                case EnemyState.Attacking:
                    if (attack != null && attack.Move != null)
                    {
                        MoveData move = attack.Move;
                        input.ActionKey = string.IsNullOrEmpty(move.AnimationKey) ? AnimationKeys.SwordSlash : move.AnimationKey;
                        input.ActionTime = attackTime;
                        input.HasFrameData = true;
                        input.Timing = new ClipTiming
                        {
                            Startup = move.Startup, Active = move.Active, Recovery = move.Recovery,
                            HitCount = Math.Max(1, attack.HitCount), HitInterval = attack.HitInterval
                        };
                    }
                    break;
            }

            // A flinch shows when the enemy is otherwise just moving about (an attack's own lean wins).
            if (reactionKey.Length > 0)
            {
                if (reactionStarted) reactionTime += dt;
                reactionStarted = true;
                bool free = state == EnemyState.Idle || state == EnemyState.Approach || state == EnemyState.Circle || state == EnemyState.Retreat;
                if (reactionTime >= HurtDuration || !free) reactionKey = "";
                else
                {
                    input.ActionKey = reactionKey;
                    input.ActionTime = reactionTime;
                    input.ActionDuration = HurtDuration;
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
