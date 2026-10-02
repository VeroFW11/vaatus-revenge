using System;
using System.Numerics;

namespace VaatusRevenge.Core
{
    // Reads the player's combat model each frame and says what the body should show (FighterAnimInput).
    // PlayerController (Unity) and the headless CombatSim both use this one class, so the game and the offline
    // renders animate exactly the same way.
    //
    // Per frame: call OnEvent for each of the model's events, then Build. Attacks are timed straight from the
    // model's own action clock (which runs at the move's rhythm playback rate), so the animation can never drift from the
    // frame data, sped up or not.
    //
    // Elements: the body takes the active element's style (ElementMoveSet.AnimationStyle: Water's upright Tai Chi stance,
    // Earth's horse stance, Air's turned-in Bagua stance), a dodge shows its kind (slip in, side-step left or right, evade
    // out), and a plain element switch made while standing free gets a short flourish (never mid-combo).
    public sealed class PlayerAnimationFeed
    {
        // Short reactions that the model has no state for: how long they show.
        public float HurtDuration = 0.4f;
        public float ParrySuccessDuration = 0.42f;
        public float ElementSwitchDuration = 0.4f;
        public float HitStrengthPerDamage = 1f / 20f;     // a 20-damage hit is a full-strength flinch
        // The plunge clips (PoseClip.LandsItself) put their landing pose (heel or feet on the floor, knees bent) at this
        // share of the move's recovery. With the floor known, that much of the recovery plays during the last of the fall,
        // timed to reach the landing pose on the impact frame, so the impact freeze shows the landing, not the air pose,
        // and nothing snaps to the floor in one frame (J6-01).
        public float PlungeLandShare = 0.25f;
        public float PlungeLandMaxRate = 2f;               // ...played at most this much faster than authored (a short drop
                                                          // finishes the rest of the chop after the impact, not in one frame)

        FighterAnimInput input;
        bool hasFacing;
        float lastFacing;
        int serial;
        int lastAttackId;
        PlayerState lastState = PlayerState.Locomotion;
        float stateTime;

        // the running dodge
        Vector3 dodgeDirection;
        string dodgeKey = AnimationKeys.Dodge;
        ElementId dodgeElement;

        // a plain element switch waiting to show its flourish (decided in Build, where the model's state is known)
        bool switchFlourishPending;

        // a transient reaction (hurt, parry success)
        string reactionKey = "";
        float reactionTime;
        float reactionDuration;
        bool reactionStarted;
        bool reactionShown;

        // plunge (falling axe kick)
        bool plungeLanded;
        float plungeLandTime;
        float plungePreLand;           // seconds of the plunge's recovery already played on the way down
        float floorBelow = -1f;        // this frame's distance from the feet to the floor (< 0 = unknown)

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
            plungePreLand = 0f;
            hitThisFrame = false;
            switchFlourishPending = false;
            dodgeKey = AnimationKeys.Dodge;
        }

        public void OnEvent(in PlayerEvent e)
        {
            switch (e.Type)
            {
                case PlayerEventType.DodgeStarted:
                    dodgeDirection = e.Direction;
                    dodgeKey = DodgeKeyFor(e.DodgeKind, e.LocalDirection, e.InAir, e.IsBackstep);
                    dodgeElement = e.Element;
                    serial++;
                    ClearReaction();
                    switchFlourishPending = false;
                    break;
                case PlayerEventType.ElementSwitched:
                    // A switch strike is its own attack; only a plain switch can flourish.
                    switchFlourishPending = !e.IsSwitchStrike;
                    break;
                case PlayerEventType.AttackStarted:
                case PlayerEventType.ChargeStarted:
                case PlayerEventType.HealStarted:
                    plungeLanded = false;
                    plungePreLand = 0f;
                    serial++;
                    ClearReaction();
                    switchFlourishPending = false;
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
        // player's own position, targetChest = the target's aim point, both in world space. floorBelow: metres from the
        // feet down to the floor (a ray down; < 0 = unknown): airborne, the legs reach for the floor as it comes and a
        // plunge starts its landing in time (J6-01).
        public FighterAnimInput Build(PlayerCombatModel model, float dt, bool hasTarget, Vector3 feet, Vector3 targetChest,
            float floorBelow = -1f)
        {
            if (model == null) return default;
            this.floorBelow = AnimMath.IsFinite(floorBelow) ? floorBelow : -1f;
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
                DashIn = model.CurrentAttackKind == PlayerAttackKind.DodgeStrike,
                Dead = state == PlayerState.Dead,
                ActionKey = "",
                ActionSerial = serial,
                Style = StyleOf(model),
            };
            if (!model.IsGrounded && this.floorBelow >= 0f)
            {
                input.HasFloorBelow = true;
                input.FloorBelow = this.floorBelow;
            }

            // A plain switch while standing free, with no string to carry on: a short flourish of the new element (the legs
            // keep doing what locomotion wants). Mid-combo, airborne or busy: no flourish, the stance just blends over.
            if (switchFlourishPending)
            {
                switchFlourishPending = false;
                if (state == PlayerState.Locomotion && model.IsGrounded && model.StringNextIndex < 0 && model.ComboCount == 0)
                    StartReaction(AnimationKeys.ElementSwitch, ElementSwitchDuration);
            }

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
                    // The dodge keeps the profile it started with (a switch mid-dodge doesn't change a running action).
                    ElementMoveSet set = model.Loadout != null ? model.Loadout.Get(dodgeElement) : null;
                    DodgeProfile dodge = set != null ? set.Dodge : model.MoveSet != null ? model.MoveSet.Dodge : null;
                    string key = model.IsAirDashing ? AnimationKeys.AirDash : dodgeKey;
                    // Timed by the dodge's own clock: a chained dodge stays Dodging but restarts its clip (DodgeStarted
                    // bumps the serial and the model's DodgeTime starts over), instead of playing on from the last one.
                    float duration = model.IsAirDashing ? AirDashDuration(set, model) : dodge != null ? dodge.TotalDuration : 0f;
                    SetState(key, model.DodgeTime, duration);
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
                    if (move != null) SetPlunge(model, move, dt);
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

        static float AirDashDuration(ElementMoveSet set, PlayerCombatModel model)
        {
            AerialSettings aerial = set != null ? set.Aerial : model.MoveSet != null ? model.MoveSet.Aerial : null;
            return aerial != null ? aerial.AirDashDuration : 0f;
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
        // landing is the recovery, when the leg chops down. With the floor known, the recovery up to its landing pose
        // (PlungeLandShare) plays over the last of the fall, timed to reach that pose on the impact frame (squeezed into a
        // shorter fall): the chop lands with the body and the impact freeze holds the landing pose (J6-01).
        void SetPlunge(PlayerCombatModel model, MoveData move, float dt)
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
            float lead = Math.Max(0f, PlungeLandShare) * Math.Max(0f, move.Recovery);
            float fallSpeed = -model.Velocity.Y;
            if (!plungeLanded && t > hang && floorBelow >= 0f && fallSpeed > 1f && dt > 0f && plungePreLand < lead)
            {
                // Seconds to the floor at this speed; the recovery runs fast enough to reach its landing pose by then.
                float remaining = lead - plungePreLand;
                float toFloor = floorBelow / fallSpeed;
                if (toFloor <= remaining)
                {
                    float rate = Math.Min(Math.Max(1f, PlungeLandMaxRate), remaining / Math.Max(toFloor, dt));
                    plungePreLand = Math.Min(lead, plungePreLand + dt * rate);
                }
            }
            float time = plungeLanded ? hang + fallHold + plungePreLand + Math.Max(0f, t - plungeLandTime)
                : plungePreLand > 0f ? hang + fallHold + plungePreLand
                : Math.Min(t, hang + fallHold * 0.99f);
            input.ActionKey = string.IsNullOrEmpty(move.AnimationKey) ? AnimationKeys.AxeKick : move.AnimationKey;
            input.ActionTime = time;
            input.HasFrameData = true;
            input.Timing = new ClipTiming { Startup = hang, Active = fallHold, Recovery = move.Recovery, HitCount = 1 };
        }

        // The clip for a dodge (Build 05 spec 4.5): slip in, side-step left or right (also the automatic side-step, by
        // which way it went), evade out still facing the foe, backstep, a plain dash with nobody near, or an air dash.
        public static string DodgeKeyFor(DodgeKind kind, Vector3 localDirection, bool inAir, bool backstep)
        {
            if (inAir || kind == DodgeKind.AirDash) return AnimationKeys.AirDash;
            switch (kind)
            {
                case DodgeKind.SlipIn: return AnimationKeys.DodgeSlip;
                case DodgeKind.SideSlip:
                case DodgeKind.AutoEvade: return localDirection.X < 0f ? AnimationKeys.DodgeSideLeft : AnimationKeys.DodgeSideRight;
                case DodgeKind.EvadeOut: return AnimationKeys.DodgeEvade;
                case DodgeKind.Backstep: return AnimationKeys.Backstep;
            }
            return backstep ? AnimationKeys.Backstep : AnimationKeys.Dodge;
        }

        // The body's style: the active element's (Fire = "", the base clips).
        static string StyleOf(PlayerCombatModel model)
        {
            ElementMoveSet set = model.Loadout != null ? model.Loadout.Get(model.ActiveElement) : null;
            if (set == null) set = model.MoveSet;
            return set != null && set.AnimationStyle != null ? set.AnimationStyle : "";
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
