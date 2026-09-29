using System;
using System.Numerics;

namespace VaatusRevenge.Core
{
    // What the animator needs to know about a fighter this frame. Filled by PlayerAnimationFeed or
    // EnemyAnimationFeed from the combat rules, so the Unity game and the headless sim animate identically.
    public struct FighterAnimInput
    {
        public float DeltaTime;             // scaled game time: 0 during hitstop or pause (the pose freezes)
        public bool Grounded;
        public Vector3 LocalVelocity;       // m/s in the fighter's own frame (x right, y up, z forward)
        public float YawDelta;              // degrees the fighter turned this frame (+ = right)
        public bool Sprinting;
        public bool Dead;                   // no secondary motion on a corpse

        public string ActionKey;            // AnimationKeys id of the action or state to show ("" = walk/run/idle)
        public int ActionSerial;            // changes whenever a new action starts, even one with the same key
        public float ActionTime;            // seconds since that action started
        public bool HasFrameData;           // Timing is the move's frame data (a strike), not a state length
        public ClipTiming Timing;           // frame data; ignored when HasFrameData is false
        public float ActionDuration;        // length of a state without frame data (<= 0: the clip's default)
        public float ActionDirectionYaw;    // degrees, fighter frame: which way a dodge or a flinch goes (0 = forward)
        public float ChargeLevel;           // 0..1 while charging a heavy
        public float AimPitch;              // degrees up toward the target (crossbow)

        public bool HasTarget;              // who the fighter is striking at (for aiming a strike at it)
        public Vector3 TargetLocal;         // the target's chest, in the fighter's frame, measured from its feet
        public Limb StrikeLimb;             // which fist or foot the current strike uses

        public bool HitReaction;            // took a hit this frame: flinch away from it
        public Vector3 HitDirectionLocal;   // direction the blow travelled (attacker -> us), fighter frame
        public float HitStrength;           // 0..1 (a light jab ~0.4, a heavy ~1)
    }

    // The procedural martial-arts animator: turns "what the fighter is doing" into a full-body pose every frame.
    //
    //   1. Locomotion: the idle stance (breathing, weight shifting), or a walk/run/sprint cycle generated from
    //      speed (GaitGenerator), or the jump/fall poses in the air, plus a knee-bending landing.
    //   2. Actions: an attack or state clip from the PoseLibrary, timed by the move's own frame data.
    //   3. Crossfades: whenever what's showing changes, the old pose blends into the new one over a few
    //      hundredths of a second, so nothing ever pops.
    //   4. Secondary motion: springs make the body lean into acceleration and turns, flinch away from hits and
    //      sway back, and the head trail fast turns. These are what make procedural motion feel alive.
    //   5. The PoseSolver turns the resulting dials into joint rotations (a BodyPose).
    //
    // Pure C#, deterministic (same inputs, same poses) and allocation-free per frame. One per fighter.
    public sealed class FighterAnimator
    {
        const float MaxStep = 1f / 120f;    // springs are integrated in steps no longer than this
        const float MaxDelta = 0.25f;       // a frame longer than this (a hitch) is treated as this long

        readonly PoseLibrary library;
        readonly System.Collections.Generic.Dictionary<string, PoseClip> styleClips;
        readonly PoseSolver solver;
        readonly BodyPose pose = new BodyPose();

        readonly PoseSpec stanceSpec = new PoseSpec();
        readonly PoseSpec gaitSpec = new PoseSpec();
        readonly PoseSpec airSpec = new PoseSpec();
        readonly PoseSpec fallSpec = new PoseSpec();
        readonly PoseSpec landSpec = new PoseSpec();
        readonly PoseSpec locoSpec = new PoseSpec();
        readonly PoseSpec actionSpec = new PoseSpec();
        readonly PoseSpec targetSpec = new PoseSpec();
        readonly PoseSpec fadeFrom = new PoseSpec();
        readonly PoseSpec blended = new PoseSpec();
        readonly PoseSpec output = new PoseSpec();
        readonly PoseSpec lastShown = new PoseSpec();
        readonly AnimatorSettings fallbackSettings = new AnimatorSettings();
        readonly GaitSettings fallbackGait = new GaitSettings();

        // what's showing
        string showingKey = "";
        int showingSerial = int.MinValue;
        bool showingAction;
        float fadeTime;
        float fadeDuration;
        float travel;
        float lastActionTime;

        // locomotion
        float gaitPhase;
        float smoothSpeed;
        Vector2 smoothDir = new Vector2(0f, 1f);
        float idleTime;
        bool wasGrounded = true;
        bool hasHistory;
        float landTime = float.MaxValue;
        float landDepth = 1f;
        float airTime;
        string locoKey = AnimationKeys.Idle;
        float locoKeyTime;

        // secondary springs
        Spring leanPitch, leanRoll, headLag, flinchPitch, flinchRoll, flinchYaw;
        Vector3 lastVelocity;
        float clock;

        // style: "" for the player, or a clip style such as "sword" (see PoseLibrary): "sword:idle" then
        // replaces "idle" for this fighter.
        public FighterAnimator(PoseLibrary library, HumanoidSkeleton skeleton, string style = "")
        {
            this.library = library ?? PoseLibrary.Default;
            Style = style ?? "";
            styleClips = this.library.StyleOverrides(Style);
            solver = new PoseSolver(skeleton ?? HumanoidSkeleton.Create());
            Reset();
        }

        public string Style { get; }

        // The clip this fighter plays for a key: its style's version if there is one. No allocation.
        public PoseClip Clip(string key)
        {
            if (string.IsNullOrEmpty(key)) return null;
            if (styleClips.Count > 0 && styleClips.TryGetValue(key, out PoseClip styled)) return styled;
            return library.Get(key);
        }

        public BodyPose Pose => pose;
        public PoseSpec CurrentSpec => output;
        public PoseSolver Solver => solver;
        public HumanoidSkeleton Skeleton => solver.Skeleton;
        public PoseLibrary Library => library;
        public AnimationCue ActionCue { get; private set; }
        public AnimationCue LocomotionCue { get; private set; }

        // True if this key can be shown (a library clip, or one of the generated gaits).
        public bool CanPlay(string key)
        {
            return Clip(key) != null || IsGaitKey(key);
        }

        public static bool IsGaitKey(string key)
        {
            return key == AnimationKeys.Walk || key == AnimationKeys.Run || key == AnimationKeys.Sprint || key == AnimationKeys.Strafe;
        }

        // Back to the idle stance, springs at rest (spawn, respawn, reset).
        public void Reset()
        {
            showingKey = "";
            showingSerial = int.MinValue;
            showingAction = false;
            fadeTime = 0f;
            fadeDuration = 0f;
            travel = 0f;
            gaitPhase = 0f;
            smoothSpeed = 0f;
            smoothDir = new Vector2(0f, 1f);
            idleTime = 0f;
            wasGrounded = true;
            hasHistory = false;
            landTime = float.MaxValue;
            airTime = 0f;
            locoKey = AnimationKeys.Idle;
            locoKeyTime = 0f;
            leanPitch = leanRoll = headLag = flinchPitch = flinchRoll = flinchYaw = default;
            lastVelocity = Vector3.Zero;
            clock = 0f;
            EvaluateClip(AnimationKeys.Idle, 0f, default, false, 0f, output);
            fadeFrom.CopyFrom(output);
            lastShown.CopyFrom(output);
            blended.CopyFrom(output);
            locoSpec.CopyFrom(output);
            solver.Solve(output, pose);
            ActionCue = default;
            LocomotionCue = new AnimationCue { Key = AnimationKeys.Idle, Loop = true, Weight = 1f };
        }

        public void Update(in FighterAnimInput input)
        {
            float dt = input.DeltaTime;
            if (!AnimMath.IsFinite(dt) || dt < 0f) dt = 0f;
            dt = Math.Min(dt, MaxDelta);
            clock += dt;
            AnimatorSettings settings = library.Settings ?? fallbackSettings;
            Vector3 velocity = AnimMath.IsFinite(input.LocalVelocity) ? input.LocalVelocity : Vector3.Zero;

            // ---- 1. locomotion layer (always evaluated, so it's ready the moment an action ends)
            UpdateLocomotion(in input, velocity, dt, settings);

            // ---- 2. action layer
            string key = input.ActionKey ?? "";
            bool action = key.Length > 0 && CanPlay(key);
            if (action != showingAction || (action && (key != showingKey || input.ActionSerial != showingSerial)))
            {
                BeginFade(key, action, in input, settings);
            }

            if (action)
            {
                if (dt > 0f) travel += Math.Max(0f, velocity.Z) * dt;
                EvaluateAction(key, in input, actionSpec, out bool upperOnly);
                ApplyStrikeAim(in input, actionSpec, settings);
                if (upperOnly) PoseSpec.Merge(actionSpec, locoSpec, targetSpec);
                else targetSpec.CopyFrom(actionSpec);
                lastActionTime = input.ActionTime;
            }
            else
            {
                targetSpec.CopyFrom(locoSpec);
            }

            // ---- 3. crossfade from whatever was showing before
            float weight = 1f;
            if (fadeDuration > 0f && fadeTime < fadeDuration)
            {
                // Time advances first, so the very first frame of a blend already moves (no held frame, then a jump).
                fadeTime += dt;
                weight = AnimMath.SmoothStep(fadeTime / fadeDuration);
                UnwrapTurnsToward(fadeFrom, targetSpec);
                PoseSpec.Lerp(fadeFrom, targetSpec, weight, blended);
            }
            else
            {
                blended.CopyFrom(targetSpec);
            }

            lastShown.CopyFrom(blended);

            // ---- 4. secondary motion
            output.CopyFrom(blended);
            ApplySecondary(in input, velocity, dt, settings);
            ApplyLeap(in input, velocity, action, settings);
            UpdateFootLocks(in input, velocity, dt, settings);

            // ---- 5. solve
            PoseClip shown = action ? Clip(key) : null;
            float aim = shown != null && shown.Aims ? input.AimPitch : 0f;
            solver.Solve(output, pose, action ? travel : 0f, aim);
            if (!pose.IsFinite())
            {
                // Never hand Unity a broken pose: fall back to the stance (and say nothing: this is a safety net).
                EvaluateClip(AnimationKeys.Idle, 0f, default, false, 0f, output);
                solver.Solve(output, pose);
            }
            KeepPropAboveFloor(in input, settings);

            UpdateCues(key, action, in input, weight);
            lastVelocity = velocity;
        }

        // ------------------------------------------------------------------ locomotion

        void UpdateLocomotion(in FighterAnimInput input, Vector3 velocity, float dt, AnimatorSettings settings)
        {
            GaitSettings gait = library.Gait ?? fallbackGait;
            var horizontal = new Vector2(velocity.X, velocity.Z);
            float speed = horizontal.Length();
            if (!hasHistory)
            {
                smoothSpeed = speed;
                hasHistory = true;
                wasGrounded = input.Grounded;
            }
            float k = AnimMath.ExpBlend(gait.SpeedSmoothing, dt);
            smoothSpeed += (speed - smoothSpeed) * k;
            if (speed > 0.05f)
            {
                Vector2 dir = horizontal / speed;
                smoothDir = Vector2.Normalize(Vector2.Lerp(smoothDir, dir, AnimMath.Clamp01(k * 1.5f)) + dir * 1e-3f);
            }
            idleTime += dt;
            if (smoothSpeed > 0.01f) gaitPhase = Wrap01(gaitPhase + gait.Cadence(smoothSpeed) * dt);

            // Landing: knees absorb the impact for a moment, deeper for a hard landing.
            if (input.Grounded && !wasGrounded && airTime > 0.12f)
            {
                landTime = 0f;
                landDepth = AnimMath.Clamp(-lastVelocity.Y / Math.Max(1f, settings.HardLandingSpeed), 0.5f, 1.3f);
            }
            airTime = input.Grounded ? 0f : airTime + dt;
            wasGrounded = input.Grounded;

            EvaluateClip(AnimationKeys.Idle, idleTime, default, false, 0f, stanceSpec);

            string key;
            if (!input.Grounded && airTime > 0.05f)
            {
                // Rising: tucked jump. Falling: legs reach down, arms out. Blended by vertical speed.
                EvaluateClip(AnimationKeys.Jump, airTime, default, false, 0f, airSpec);
                EvaluateClip(AnimationKeys.Fall, airTime, default, false, 0f, fallSpec);
                float falling = AnimMath.SmoothStep((2f - velocity.Y) / 6f);
                PoseSpec.Lerp(airSpec, fallSpec, falling, locoSpec);
                key = falling > 0.5f ? AnimationKeys.Fall : AnimationKeys.Jump;
            }
            else
            {
                float moveWeight = AnimMath.SmoothStep((smoothSpeed - gait.MoveThreshold * 0.5f) / Math.Max(0.05f, gait.WalkSpeed * 0.6f));
                if (moveWeight > 0f)
                {
                    GaitGenerator.Evaluate(gaitPhase, smoothSpeed, smoothDir, gait, stanceSpec, gaitSpec, IdleArmSwing());
                    PoseSpec.Lerp(stanceSpec, gaitSpec, moveWeight, locoSpec);
                }
                else
                {
                    locoSpec.CopyFrom(stanceSpec);
                }
                key = GaitKey(smoothSpeed, smoothDir, input.Sprinting, gait);

                float landDuration = Math.Max(0.05f, settings.LandDuration);
                if (landTime < landDuration)
                {
                    EvaluateClip(AnimationKeys.Land, landTime, default, false, landDuration, landSpec);
                    float w = (1f - AnimMath.SmoothStep(landTime / landDuration)) * AnimMath.Clamp01(landDepth);
                    PoseSpec.Lerp(locoSpec, landSpec, w, locoSpec);
                    key = AnimationKeys.Land;
                    landTime += dt;
                }
            }
            if (key != locoKey)
            {
                locoKey = key;
                locoKeyTime = 0f;
            }
            else
            {
                locoKeyTime += dt;
            }
        }

        float IdleArmSwing()
        {
            PoseClip idle = Clip(AnimationKeys.Idle);
            return idle != null ? idle.ArmSwing : 1f;
        }

        static string GaitKey(float speed, Vector2 dir, bool sprinting, GaitSettings g)
        {
            if (speed < g.MoveThreshold) return AnimationKeys.Idle;
            if (sprinting || speed > (g.RunSpeed + g.SprintSpeed) * 0.5f) return AnimationKeys.Sprint;
            if (Math.Abs(dir.X) > 0.7f && speed < g.RunSpeed) return AnimationKeys.Strafe;
            return speed > (g.WalkSpeed + g.RunSpeed) * 0.5f ? AnimationKeys.Run : AnimationKeys.Walk;
        }

        // ------------------------------------------------------------------ actions

        void BeginFade(string key, bool action, in FighterAnimInput input, AnimatorSettings settings)
        {
            // Blend from exactly what was on screen last frame (before secondary motion, which carries on by itself).
            fadeFrom.CopyFrom(lastShown);
            aimValid = false;
            float fade = settings.DefaultFade;
            if (action)
            {
                PoseClip clip = Clip(key);
                if (clip != null) fade = clip.FadeIn;
                if (input.HasFrameData && input.Timing.Startup > 0f) fade = Math.Min(fade, input.Timing.Startup * settings.StrikeFadeShare);
            }
            else
            {
                fade = settings.LocomotionFade;
            }
            fadeDuration = AnimMath.Clamp(fade, settings.MinFade, settings.MaxFade);
            if (action && input.HasFrameData && input.Timing.Startup > 0f)
                fadeDuration = Math.Min(fadeDuration, input.Timing.Startup * settings.StrikeFadeShare);
            fadeTime = 0f;
            showingKey = key;
            showingSerial = input.ActionSerial;
            showingAction = action;
            travel = 0f;
        }

        // Whole-body turns are blended the short way round: the pose we blend from is re-expressed within half a
        // turn of where we're blending to (a finished 360 spin doesn't unwind backwards, a tumble at -217 degrees
        // lands on its back at -90 by going through -180, never up through 0 = standing upright).
        static void UnwrapTurnsToward(PoseSpec from, PoseSpec to)
        {
            Unwrap(from, to, PoseChannel.RootYaw);
            Unwrap(from, to, PoseChannel.PelvisPitch);
            Unwrap(from, to, PoseChannel.PelvisYaw);
            Unwrap(from, to, PoseChannel.PelvisRoll);
        }

        static void Unwrap(PoseSpec from, PoseSpec to, PoseChannel c)
        {
            from[c] = to[c] + Wrap180(from[c] - to[c]);
        }

        // ------------------------------------------------------------------ aiming strikes

        readonly PoseSpec aimSpec = new PoseSpec();
        readonly BodyPose aimPose = new BodyPose();
        PoseSolver aimSolver;
        bool aimValid;
        float aimYaw, aimPitch;

        // The strike's fist or foot points at the target on the first active frame: the authored pose is checked at
        // that moment and the body turned (and the limb tilted) by however much it would miss. Measured until the
        // strike commits, like the combat rules' tracking, then held; eased in over the wind-up, out over recovery.
        void ApplyStrikeAim(in FighterAnimInput input, PoseSpec spec, AnimatorSettings s)
        {
            if (!s.AimStrikes || !input.HasFrameData || !input.HasTarget) return;
            PoseClip clip = Clip(input.ActionKey);
            if (clip == null) return;
            ClipTiming timing = input.Timing;
            float t = input.ActionTime;
            if (!AnimMath.IsFinite(t)) return;
            if (!aimValid || t <= timing.Startup)
            {
                aimValid = true;
                aimYaw = 0f;
                aimPitch = 0f;
                if (aimSolver == null) aimSolver = new PoseSolver(solver.Skeleton);
                clip.Evaluate(timing.Startup, in timing, aimSpec);
                aimSolver.Solve(aimSpec, aimPose);
                LimbEnds(aimSolver, input.StrikeLimb, out Vector3 root, out Vector3 end);
                Vector3 dir = end - root;
                Vector3 to = input.TargetLocal - root;
                if (AnimMath.IsFinite(to) && dir.LengthSquared() > 1e-4f && to.LengthSquared() > 1e-4f && to.Length() <= s.StrikeAimMaxDistance)
                {
                    float yawError = Wrap180(YawOf(to) - YawOf(dir));
                    if (Math.Abs(yawError) <= s.StrikeAimGiveUpYaw)
                    {
                        aimYaw = AnimMath.Clamp(yawError, -s.StrikeAimMaxYaw, s.StrikeAimMaxYaw);
                        aimPitch = AnimMath.Clamp(PitchOf(to) - PitchOf(dir), -s.StrikeAimMaxPitch, s.StrikeAimMaxPitch);
                    }
                }
            }
            float w;
            if (t < timing.Startup) w = AnimMath.SmoothStep(timing.Startup > 0f ? t / timing.Startup : 1f);
            else if (t <= timing.LastActiveEnd) w = 1f;
            else w = 1f - AnimMath.SmoothStep((t - timing.LastActiveEnd) / Math.Max(0.05f, timing.Recovery * 0.6f));
            if (!(w > 0f)) return;
            spec[PoseChannel.RootYaw] += aimYaw * w;
            switch (input.StrikeLimb)
            {
                case Limb.LeftFoot:
                case Limb.RightFoot:
                {
                    BodySide side = input.StrikeLimb == Limb.LeftFoot ? BodySide.Left : BodySide.Right;
                    if (spec[PoseSpec.Leg(side, 7)] > 0.5f) spec[PoseSpec.Leg(side, 9)] += aimPitch * w;
                    break;
                }
                case Limb.LeftFist:
                    spec[PoseSpec.Arm(BodySide.Left, 1)] += aimPitch * w;
                    break;
                case Limb.BothFists:
                    spec[PoseSpec.Arm(BodySide.Left, 1)] += aimPitch * w;
                    spec[PoseSpec.Arm(BodySide.Right, 1)] += aimPitch * w;
                    break;
                default:
                    spec[PoseSpec.Arm(BodySide.Right, 1)] += aimPitch * w;
                    break;
            }
        }

        // Where a strike limb starts (shoulder / hip) and ends (wrist / ankle) in the last solve.
        static void LimbEnds(PoseSolver s, Limb limb, out Vector3 root, out Vector3 end)
        {
            switch (limb)
            {
                case Limb.LeftFist:
                    root = s.ModelPosition(BodyJoint.LeftUpperArm);
                    end = s.ModelPosition(BodyJoint.LeftHand);
                    return;
                case Limb.RightFoot:
                    root = s.ModelPosition(BodyJoint.RightUpperLeg);
                    end = s.ModelPosition(BodyJoint.RightFoot);
                    return;
                case Limb.LeftFoot:
                    root = s.ModelPosition(BodyJoint.LeftUpperLeg);
                    end = s.ModelPosition(BodyJoint.LeftFoot);
                    return;
                case Limb.BothFists:
                    root = (s.ModelPosition(BodyJoint.LeftUpperArm) + s.ModelPosition(BodyJoint.RightUpperArm)) * 0.5f;
                    end = (s.ModelPosition(BodyJoint.LeftHand) + s.ModelPosition(BodyJoint.RightHand)) * 0.5f;
                    return;
                default:
                    root = s.ModelPosition(BodyJoint.RightUpperArm);
                    end = s.ModelPosition(BodyJoint.RightHand);
                    return;
            }
        }

        static float YawOf(Vector3 v)
        {
            return MathF.Atan2(v.X, v.Z) * AnimMath.Rad2Deg;
        }

        static float PitchOf(Vector3 v)
        {
            return MathF.Atan2(v.Y, MathF.Sqrt(v.X * v.X + v.Z * v.Z)) * AnimMath.Rad2Deg;
        }

        // ------------------------------------------------------------------ feet: leaps and locks

        struct FootLock
        {
            public bool Locked;
            public Vector2 Position;      // where the planted foot stands (fighter frame, this frame)
            public bool Stepping;
            public Vector2 From;
            public float StepTime;
            public float StepDuration;
            public float StepHeight;
            public Vector2 Offset;        // after a release: how far the foot still is from the pose (decays)
        }

        readonly FootLock[] footLocks = new FootLock[2];

        // A grounded action covering ground faster than a person can step (a stretched lunge, a flying kick) becomes
        // a leap: both feet leave the floor for the rush and land as it slows.
        void ApplyLeap(in FighterAnimInput input, Vector3 velocity, bool action, AnimatorSettings s)
        {
            if (!action || !input.Grounded || input.Dead) return;
            float speed = new Vector2(velocity.X, velocity.Z).Length();
            float lift = AnimMath.Clamp((speed - s.LeapSpeed) * s.LeapLiftPerSpeed, 0f, s.MaxLeapLift);
            if (!(lift > 0f)) return;
            float k = 1f / Math.Max(0.1f, solver.Skeleton.Scale);
            output[PoseChannel.HipsY] += lift * 0.6f * k;
            output[PoseChannel.LFootY] += lift * k;
            output[PoseChannel.RFootY] += lift * k;
        }

        // Planted feet stay where they touched down while the body moves and turns over them; when the pose wants a
        // foot somewhere else, it takes a quick step there. The fighter's own motion (velocity, turning) is how a
        // world-fixed spot is tracked without knowing where the fighter is in the world.
        void UpdateFootLocks(in FighterAnimInput input, Vector3 velocity, float dt, AnimatorSettings s)
        {
            float k = Math.Max(0.1f, solver.Skeleton.Scale);
            bool enabled = s.FootLocks && input.Grounded && !input.Dead;
            float rootYaw = output[PoseChannel.RootYaw];
            float turn = AnimMath.IsFinite(input.YawDelta) ? input.YawDelta : 0f;
            var move = new Vector2(velocity.X, velocity.Z) * dt;
            float decay = 1f - AnimMath.ExpBlend(s.ReleaseRate, dt);
            for (int i = 0; i < 2; i++)
            {
                BodySide side = i == 0 ? BodySide.Left : BodySide.Right;
                float sign = (float)side;
                ref FootLock f = ref footLocks[i];
                if (dt > 0f)
                {
                    // The body moved and turned: a spot fixed on the ground moves the other way in the body's frame.
                    f.Position = RotateXZ(f.Position - move, -turn);
                    f.From = RotateXZ(f.From - move, -turn);
                    f.Offset *= decay;
                }
                Vector2 key = RotateXZ(new Vector2(sign * output[PoseSpec.Leg(side, 0)], output[PoseSpec.Leg(side, 2)]) * k, rootYaw);
                bool planted = output[PoseSpec.Leg(side, 7)] < 0.5f && output[PoseSpec.Leg(side, 1)] * k < (0.08f + s.PlantHeight) * k;
                Vector2 shown;
                float lift = 0f;
                if (!enabled || !planted)
                {
                    if (f.Locked) f.Offset = f.Position - key;
                    f.Locked = false;
                    f.Stepping = false;
                    shown = key + f.Offset;
                }
                else if (!f.Locked)
                {
                    f.Locked = true;
                    f.Position = key + f.Offset;
                    f.Offset = Vector2.Zero;
                    shown = f.Position;
                }
                else
                {
                    if (f.Stepping && dt > 0f)
                    {
                        f.StepTime += dt;
                        if (f.StepTime >= f.StepDuration)
                        {
                            f.Stepping = false;
                            f.Position = key;
                        }
                    }
                    if (!f.Stepping)
                    {
                        float distance = Vector2.Distance(key, f.Position);
                        bool otherStepping = footLocks[1 - i].Stepping;
                        if (distance > s.StepDistance * k && (!otherStepping || distance > 2f * s.StepDistance * k))
                        {
                            f.Stepping = true;
                            f.From = f.Position;
                            f.StepTime = 0f;
                            f.StepDuration = AnimMath.Clamp(distance / Math.Max(0.1f, s.StepSpeed), s.StepMinTime, s.StepMaxTime);
                            f.StepHeight = Math.Min(s.StepMaxLift, distance * s.StepLiftPerMetre) * k;
                        }
                    }
                    if (f.Stepping)
                    {
                        float u = AnimMath.Clamp01(f.StepTime / Math.Max(1e-3f, f.StepDuration));
                        shown = Vector2.Lerp(f.From, key, AnimMath.SmoothStep(u));
                        lift = f.StepHeight * MathF.Sin(MathF.PI * u);
                        f.Position = shown;
                    }
                    else
                    {
                        shown = f.Position;
                    }
                }
                if (!f.Locked && f.Offset.LengthSquared() < 1e-8f) continue;
                Vector2 local = RotateXZ(shown, -rootYaw) / k;
                output[PoseSpec.Leg(side, 0)] = sign * local.X;
                output[PoseSpec.Leg(side, 2)] = local.Y;
                output[PoseSpec.Leg(side, 1)] += lift / k;
                if (f.Locked) output[PoseSpec.Leg(side, 11)] = 0f;   // the lock replaces the lunge anchor
            }
        }

        // Rotates a point on the floor (x right, y = forward) by yaw degrees (+ = to the right, like the body).
        static Vector2 RotateXZ(Vector2 p, float yawDegrees)
        {
            if (yawDegrees == 0f) return p;
            float a = yawDegrees * AnimMath.Deg2Rad;
            float c = MathF.Cos(a), sn = MathF.Sin(a);
            return new Vector2(p.X * c + p.Y * sn, -p.X * sn + p.Y * c);
        }

        // ------------------------------------------------------------------ props

        // Held weapon length (hand to tip); 0 = no prop. Set by whoever builds the body.
        public float PropLength { get; set; }

        // A sword or crossbow held by someone slumped, sprawled or dying mustn't stick through the floor: if the tip
        // would be under it, the prop swings up just enough to rest on it.
        void KeepPropAboveFloor(in FighterAnimInput input, AnimatorSettings s)
        {
            if (!(PropLength > 0f) || !input.Grounded) return;
            Quaternion hand = solver.ModelRotation(BodyJoint.RightHand);
            Vector3 grip = solver.ModelPosition(BodyJoint.RightHand) + AnimMath.Rotate(hand, new Vector3(solver.Skeleton.HandLength * 0.6f, 0f, 0f));
            Vector3 dir = AnimMath.Rotate(hand * pose.PropLocal, Vector3.UnitZ);
            float floor = s.PropFloorClearance;
            if (grip.Y + dir.Y * PropLength >= floor) return;
            float y = AnimMath.Clamp((floor - grip.Y) / PropLength, -1f, 1f);
            var flat = new Vector2(dir.X, dir.Z);
            if (flat.LengthSquared() < 1e-6f) flat = new Vector2(0f, 1f);
            flat = Vector2.Normalize(flat) * MathF.Sqrt(Math.Max(0f, 1f - y * y));
            var fixedDir = new Vector3(flat.X, y, flat.Y);
            Vector3 local = AnimMath.Rotate(Quaternion.Inverse(hand), fixedDir);
            Vector3 up = AnimMath.Rotate(Quaternion.Inverse(hand), Vector3.UnitY);
            if (Math.Abs(Vector3.Dot(up, local)) > 0.95f) up = Vector3.UnitX;
            pose.PropLocal = AnimMath.LookRotation(local, up);
        }

        void EvaluateAction(string key, in FighterAnimInput input, PoseSpec result, out bool upperOnly)
        {
            upperOnly = false;
            if (IsGaitKey(key) && Clip(key) == null)
            {
                // A gait asked for by name (the pose gallery): run the cycle at that gait's reference speed.
                GaitSettings g = library.Gait ?? fallbackGait;
                float speed = key == AnimationKeys.Walk ? g.WalkSpeed : key == AnimationKeys.Sprint ? g.SprintSpeed : key == AnimationKeys.Strafe ? g.WalkSpeed : g.RunSpeed;
                Vector2 dir = key == AnimationKeys.Strafe ? new Vector2(1f, 0f) : new Vector2(0f, 1f);
                float phase = Wrap01(input.ActionTime * g.Cadence(speed));
                GaitGenerator.Evaluate(phase, speed, dir, g, stanceSpec, result, IdleArmSwing());
                return;
            }
            PoseClip clip = Clip(key);
            upperOnly = clip.UpperBodyOnly;
            EvaluateClip(key, input.ActionTime, input.Timing, input.HasFrameData, input.ActionDuration, result);
            DirectionalWarp(key, in input, result);
        }

        void EvaluateClip(string key, float time, in ClipTiming frameData, bool hasFrameData, float duration, PoseSpec result)
        {
            PoseClip clip = Clip(key);
            if (clip == null)
            {
                result.CopyFrom(PoseLibrary.Neutral);
                return;
            }
            ClipTiming timing = hasFrameData ? frameData : clip.DefaultTiming(duration);
            clip.Evaluate(time, in timing, result);
        }

        // Dodges and flinches are authored going forward / hit from the front; this bends them toward the real
        // direction: a sidestep leans and turns into the dash, a backstep is its own clip.
        static void DirectionalWarp(string key, in FighterAnimInput input, PoseSpec s)
        {
            if (key != AnimationKeys.Dodge && key != AnimationKeys.AirDash) return;
            float yaw = Wrap180(input.ActionDirectionYaw);
            float side = MathF.Sin(yaw * AnimMath.Deg2Rad);
            float forward = MathF.Cos(yaw * AnimMath.Deg2Rad);
            s[PoseChannel.RootYaw] += AnimMath.Clamp(yaw, -70f, 70f) * 0.6f;
            s[PoseChannel.TorsoRoll] += -side * 18f;
            s[PoseChannel.TorsoPitch] *= Math.Max(0.2f, forward);
        }

        // ------------------------------------------------------------------ secondary motion

        void ApplySecondary(in FighterAnimInput input, Vector3 velocity, float dt, AnimatorSettings s)
        {
            if (dt > 0f)
            {
                float live = input.Dead ? 0f : 1f;
                float accelZ = (velocity.Z - lastVelocity.Z) / dt;
                float yawRate = AnimMath.IsFinite(input.YawDelta) ? input.YawDelta / dt : 0f;
                float speed = new Vector2(velocity.X, velocity.Z).Length();
                float pitchTarget = live * AnimMath.Clamp(accelZ * s.AccelLeanPerMs2, -s.MaxAccelLean, s.MaxAccelLean);
                float rollTarget = live * AnimMath.Clamp(-yawRate * speed * s.TurnBankPerDegPerSec, -s.MaxTurnBank, s.MaxTurnBank);
                float headTarget = live * AnimMath.Clamp(-yawRate * s.HeadLagPerDegPerSec, -s.MaxHeadLag, s.MaxHeadLag);

                if (input.HitReaction && !input.Dead)
                {
                    // Knocked the way the blow travelled: from the front the chest snaps back, from the side it
                    // bends away. The spring then rocks back past neutral once and settles.
                    Vector3 dir = AnimMath.SafeNormalize(new Vector3(input.HitDirectionLocal.X, 0f, input.HitDirectionLocal.Z), -Vector3.UnitZ);
                    float strength = AnimMath.Clamp(input.HitStrength, 0.2f, 1.5f) * s.FlinchDegrees;
                    float kick = strength * s.FlinchSpringFrequency * 2f * MathF.PI;
                    flinchPitch.Velocity += dir.Z * kick;
                    flinchRoll.Velocity += -dir.X * kick;
                    flinchYaw.Velocity += dir.X * kick * 0.4f;
                }

                float remaining = dt;
                while (remaining > 1e-6f)
                {
                    float step = Math.Min(MaxStep, remaining);
                    remaining -= step;
                    leanPitch.Step(pitchTarget, s.LeanSpringFrequency, s.LeanSpringDamping, step);
                    leanRoll.Step(rollTarget, s.LeanSpringFrequency, s.LeanSpringDamping, step);
                    headLag.Step(headTarget, s.LeanSpringFrequency * 1.5f, 0.8f, step);
                    flinchPitch.Step(0f, s.FlinchSpringFrequency, s.FlinchSpringDamping, step);
                    flinchRoll.Step(0f, s.FlinchSpringFrequency, s.FlinchSpringDamping, step);
                    flinchYaw.Step(0f, s.FlinchSpringFrequency, s.FlinchSpringDamping, step);
                }
            }

            output[PoseChannel.TorsoPitch] += leanPitch.Value + flinchPitch.Value;
            output[PoseChannel.TorsoRoll] += leanRoll.Value + flinchRoll.Value;
            output[PoseChannel.PelvisRoll] += leanRoll.Value * 0.3f;
            output[PoseChannel.TorsoYaw] += flinchYaw.Value;
            output[PoseChannel.HeadYaw] += headLag.Value;
            output[PoseChannel.HeadPitch] += flinchPitch.Value * 0.7f;

            if (input.ChargeLevel > 0f && !input.Dead)
            {
                float level = AnimMath.Clamp01(input.ChargeLevel);
                float tremble = MathF.Sin(clock * s.ChargeTrembleRate * 2f * MathF.PI) * s.ChargeTremble * level;
                output[PoseChannel.TorsoRoll] += tremble;
                output[PoseChannel.HeadRoll] -= tremble * 0.5f;
            }
        }

        // ------------------------------------------------------------------ cues (for a pack-clip player)

        void UpdateCues(string key, bool action, in FighterAnimInput input, float weight)
        {
            if (action)
            {
                PoseClip clip = Clip(key);
                float duration;
                float impact = 0f;
                if (input.HasFrameData)
                {
                    duration = input.Timing.Total;
                    impact = input.Timing.Startup;
                }
                else if (clip != null && clip.Mode != ClipMode.Action)
                {
                    duration = 0f;   // held or looping state: open-ended
                }
                else
                {
                    duration = input.ActionDuration > 0f ? input.ActionDuration : (clip != null ? clip.DefaultDuration : 0f);
                }
                ActionCue = new AnimationCue
                {
                    Key = key, Time = Math.Max(0f, input.ActionTime), Duration = duration, ImpactTime = impact,
                    Loop = clip != null && clip.Mode == ClipMode.Loop, Speed = 0f, Weight = weight
                };
            }
            else
            {
                ActionCue = default;
            }
            GaitSettings g = library.Gait ?? fallbackGait;
            bool cycling = IsGaitKey(locoKey);
            LocomotionCue = new AnimationCue
            {
                Key = locoKey, Time = cycling ? gaitPhase / g.Cadence(smoothSpeed) : locoKeyTime,
                Duration = cycling ? 1f / g.Cadence(smoothSpeed) : 0f,
                Loop = locoKey != AnimationKeys.Land, Speed = smoothSpeed, Weight = action ? 1f - weight : 1f
            };
        }

        static float Wrap01(float x)
        {
            x %= 1f;
            return x < 0f ? x + 1f : x;
        }

        static float Wrap180(float degrees)
        {
            if (!AnimMath.IsFinite(degrees)) return 0f;
            degrees %= 360f;
            if (degrees > 180f) degrees -= 360f;
            else if (degrees <= -180f) degrees += 360f;
            return degrees;
        }

        // A damped spring (value pulled toward a target, with momentum): used for everything that should lag,
        // overshoot a little and settle, like a real body's weight.
        struct Spring
        {
            public float Value;
            public float Velocity;

            public void Step(float target, float frequency, float damping, float dt)
            {
                float omega = 2f * MathF.PI * Math.Max(0.01f, frequency);
                float accel = omega * omega * (target - Value) - 2f * Math.Max(0f, damping) * omega * Velocity;
                Velocity += accel * dt;       // semi-implicit Euler: stable for small steps
                Value += Velocity * dt;
                if (!AnimMath.IsFinite(Value) || !AnimMath.IsFinite(Velocity))
                {
                    Value = 0f;
                    Velocity = 0f;
                }
            }
        }
    }
}
