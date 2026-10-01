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

        public string Style;                // the clip style to use from now on (the player's element: "", "water", "earth",
                                            // "air"); null = keep the animator's current style (enemies never change theirs)
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
        readonly System.Collections.Generic.Dictionary<string, PoseClip> styleClips =
            new System.Collections.Generic.Dictionary<string, PoseClip>(StringComparer.Ordinal);
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
        readonly PoseSpec locoFrom = new PoseSpec();
        float locoFadeTime = float.MaxValue;
        bool wasAirborne;
        readonly AnimatorSettings fallbackSettings = new AnimatorSettings();
        readonly GaitSettings fallbackGait = new GaitSettings();

        // what's showing
        string showingKey = "";
        int showingSerial = int.MinValue;
        bool showingAction;
        bool showingFrameData;
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
            this.library.FillStyleOverrides(Style, styleClips);
            solver = new PoseSolver(skeleton ?? HumanoidSkeleton.Create());
            Reset();
        }

        public string Style { get; private set; }

        // Changes the clip style while running (the player switching element: Water's stance, Earth's charge...). Nothing
        // pops: the whole body blends from what was on screen into the new style over AnimatorSettings.StyleFade, and the
        // legs of an upper-body action (a parry, the switch flourish) blend into the new stance with the locomotion layer.
        // A strike already playing keeps its own timing (strike clips are the move's own, never styled). Builds the
        // override table, so call it on a change only (Update does, from FighterAnimInput.Style).
        public void SetStyle(string style)
        {
            style = style ?? "";
            if (style == Style) return;
            Style = style;
            library.FillStyleOverrides(Style, styleClips);
            AnimatorSettings settings = library.Settings ?? fallbackSettings;
            locoFrom.CopyFrom(locoSpec);
            locoFadeTime = 0f;
            fadeFromAir = false;
            if (!showingAction || !showingFrameData)
            {
                fadeFrom.CopyFrom(lastShown);
                fadeTime = 0f;
                fadeDuration = AnimMath.Clamp(settings.StyleFade, settings.MinFade, Math.Max(settings.MaxFade, settings.StyleFade));
            }
        }

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
            showingFrameData = false;
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
            footLocks[0] = footLocks[1] = default;
            lastRootYaw = 0f;
            fadeFromAir = false;
            groundSpeed = 0f;
            turnCarry = turnCarryFrom = turnCarryTime = turnExcess = 0f;
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
            if (input.Style != null && input.Style != Style) SetStyle(input.Style);

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
            ApplyTurnCarry(in input, dt, settings);
            ApplySecondary(in input, velocity, dt, settings);
            ApplyLeap(in input, velocity, action, settings);
            UpdateFootLocks(in input, velocity, dt, settings);

            // ---- 5. solve
            PoseClip shown = action ? Clip(key) : null;
            float aim = shown != null && shown.Aims ? input.AimPitch : 0f;
            solver.Solve(output, pose, action ? travel : 0f, aim, input.Grounded);
            if (!pose.IsFinite())
            {
                // Never hand Unity a broken pose: fall back to the stance (and say nothing: this is a safety net).
                EvaluateClip(AnimationKeys.Idle, 0f, default, false, 0f, output);
                solver.Solve(output, pose);
            }
            KeepPropAboveFloor(in input, settings);
            for (int i = 0; i < 2; i++)
            {
                Vector3 ankle = solver.ModelPosition(i == 0 ? BodyJoint.LeftFoot : BodyJoint.RightFoot);
                footLocks[i].Shown = new Vector2(ankle.X, ankle.Z);
            }

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
                smoothSpeed = groundSpeed = speed;
                hasHistory = true;
                wasGrounded = input.Grounded;
            }
            float k = AnimMath.ExpBlend(gait.SpeedSmoothing, dt);
            smoothSpeed += (speed - smoothSpeed) * k;
            groundSpeed += (speed - groundSpeed) * AnimMath.ExpBlend(gait.GroundSpeedSmoothing, dt);
            if (speed > 0.05f)
            {
                Vector2 dir = horizontal / speed;
                smoothDir = Vector2.Normalize(Vector2.Lerp(smoothDir, dir, AnimMath.Clamp01(k * 1.5f)) + dir * 1e-3f);
            }
            idleTime += dt;
            if (smoothSpeed > 0.01f) gaitPhase = Wrap01(gaitPhase + gait.Cadence(groundSpeed) * dt);

            // Landing: knees absorb the impact for a moment, deeper for a hard landing.
            if (input.Grounded && !wasGrounded && airTime > 0.12f)
            {
                landTime = 0f;
                landDepth = AnimMath.Clamp(-lastVelocity.Y / Math.Max(1f, settings.HardLandingSpeed), 0.5f, 1.3f);
            }
            airTime = input.Grounded ? 0f : airTime + dt;
            wasGrounded = input.Grounded;

            EvaluateClip(AnimationKeys.Idle, idleTime, default, false, 0f, stanceSpec);

            // Switching between the air poses and the ground ones blends too (a landing never snaps).
            bool airborne = !input.Grounded && airTime > 0.05f;
            if (airborne != wasAirborne)
            {
                locoFrom.CopyFrom(locoSpec);
                locoFadeTime = 0f;
                fadeFromAir = wasAirborne;
                wasAirborne = airborne;
            }

            string key;
            if (airborne)
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
                    GaitGenerator.Evaluate(gaitPhase, smoothSpeed, smoothDir, gait, stanceSpec, gaitSpec, IdleArmSwing(), groundSpeed);
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
                    float w = (1f - AnimMath.SmoothStep(landTime / landDuration)) * AnimMath.Clamp01(landDepth)
                              * AnimMath.SmoothStep((landTime + dt) / Math.Max(0.01f, settings.LandBlendIn));
                    PoseSpec.Lerp(locoSpec, landSpec, w, locoSpec);
                    key = AnimationKeys.Land;
                    landTime += dt;
                }
            }
            if (locoFadeTime < settings.LocomotionFade)
            {
                locoFadeTime += dt;
                UnwrapTurnsToward(locoFrom, locoSpec);
                // Touching down: the feet are already at the floor, so the legs take the ground pose at once and only
                // the body above them blends out of the air pose. (Blending the legs too would draw the feet back up
                // toward the tucked fall pose for a few frames: the landing would float.)
                bool plantNow = fadeFromAir && input.Grounded;
                if (plantNow) SaveLegs(locoSpec);
                PoseSpec.Lerp(locoFrom, locoSpec, AnimMath.SmoothStep(locoFadeTime / Math.Max(0.01f, settings.LocomotionFade)), locoSpec);
                if (plantNow) RestoreLegs(locoSpec);
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

        // ------------------------------------------------------------------ instant turns

        float turnCarry;        // degrees the drawn body still lags behind the fighter's facing
        float turnCarryFrom;    // the lag when the last snap happened, eased away from there
        float turnCarryTime;
        float turnExcess;       // this frame's facing change the body didn't show at once

        // The combat rules may snap the facing round in one frame (an attack turning to a target behind you). The
        // body can't teleport-rotate like that: a facing change faster than a human turn is carried as a lag, which
        // the body then whips through, eased in and out over TurnCatchUpTime (quick, so the strike still lands facing
        // its target). The planted feet stay put and pivot or step as the body comes round over them.
        void ApplyTurnCarry(in FighterAnimInput input, float dt, AnimatorSettings s)
        {
            float turn = AnimMath.IsFinite(input.YawDelta) ? input.YawDelta : 0f;
            turnExcess = Math.Abs(turn) > s.VisualTurnRate * dt ? turn : 0f;
            if (turnExcess != 0f)
            {
                turnCarryFrom = Wrap180(turnCarry - turnExcess);
                turnCarryTime = 0f;
            }
            if (turnCarryFrom != 0f)
            {
                turnCarryTime += dt;
                float u = turnCarryTime / Math.Max(0.01f, s.TurnCatchUpTime);
                turnCarry = turnCarryFrom * (1f - AnimMath.SmoothStep(u));
                if (u >= 1f) turnCarryFrom = turnCarry = 0f;
            }
            output[PoseChannel.RootYaw] += turnCarry;
        }

        float groundSpeed;      // the body's speed over the ground, barely smoothed (sizes the stride: see GaitGenerator)
        bool fadeFromAir;
        readonly float[] savedLegs = new float[2 * PoseSpec.LegChannels + 1];

        void SaveLegs(PoseSpec spec)
        {
            for (int c = 0; c < PoseSpec.LegChannels; c++)
            {
                savedLegs[c] = spec[PoseSpec.Leg(BodySide.Left, c)];
                savedLegs[PoseSpec.LegChannels + c] = spec[PoseSpec.Leg(BodySide.Right, c)];
            }
            savedLegs[2 * PoseSpec.LegChannels] = spec[PoseChannel.LegFrame];
        }

        void RestoreLegs(PoseSpec spec)
        {
            for (int c = 0; c < PoseSpec.LegChannels; c++)
            {
                spec[PoseSpec.Leg(BodySide.Left, c)] = savedLegs[c];
                spec[PoseSpec.Leg(BodySide.Right, c)] = savedLegs[PoseSpec.LegChannels + c];
            }
            spec[PoseChannel.LegFrame] = savedLegs[2 * PoseSpec.LegChannels];
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
            showingFrameData = action && input.HasFrameData;
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
                clip.Evaluate(timing.Startup + s.StrikeAimLead, in timing, aimSpec);
                // Three passes: turning the body also moves the shoulder or hip the limb swings from, so later
                // passes correct what's left after the first.
                for (int pass = 0; pass < 3; pass++)
                {
                    aimSolver.Solve(aimSpec, aimPose);
                    LimbEnds(aimSolver, input.StrikeLimb, out Vector3 root, out Vector3 end);
                    Vector3 dir = end - root;
                    Vector3 to = input.TargetLocal - root;
                    if (!AnimMath.IsFinite(to) || dir.LengthSquared() < 1e-4f || to.LengthSquared() < 1e-4f || to.Length() > s.StrikeAimMaxDistance) break;
                    float yawError = Wrap180(YawOf(to) - YawOf(dir));
                    if (Math.Abs(yawError) > s.StrikeAimGiveUpYaw) break;
                    float newYaw = AnimMath.Clamp(aimYaw + yawError, -s.StrikeAimMaxYaw, s.StrikeAimMaxYaw);
                    float newPitch = AnimMath.Clamp(aimPitch + PitchOf(to) - PitchOf(dir), -s.StrikeAimMaxPitch, s.StrikeAimMaxPitch);
                    AddAim(aimSpec, input.StrikeLimb, newYaw - aimYaw, newPitch - aimPitch);
                    aimYaw = newYaw;
                    aimPitch = newPitch;
                }
            }
            float w;
            if (t < timing.Startup) w = AnimMath.SmoothStep(timing.Startup > 0f ? t / timing.Startup : 1f);
            else if (t <= timing.LastActiveEnd) w = 1f;
            else w = 1f - AnimMath.SmoothStep((t - timing.LastActiveEnd) / Math.Max(0.05f, timing.Recovery * 0.6f));
            if (!(w > 0f)) return;
            AddAim(spec, input.StrikeLimb, aimYaw * w, aimPitch * w);
        }

        static void AddAim(PoseSpec spec, Limb limb, float yaw, float pitch)
        {
            spec[PoseChannel.RootYaw] += yaw;
            float aimPitch = pitch, w = 1f;
            switch (limb)
            {
                case Limb.LeftFoot:
                case Limb.RightFoot:
                {
                    BodySide side = limb == Limb.LeftFoot ? BodySide.Left : BodySide.Right;
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

        // Where a strike limb starts (shoulder / hip) and ends (wrist / ball of the foot) in the last solve.
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
                    end = s.ModelPosition(BodyJoint.RightToes);    // the ball of the foot is what lands
                    return;
                case Limb.LeftFoot:
                    root = s.ModelPosition(BodyJoint.LeftUpperLeg);
                    end = s.ModelPosition(BodyJoint.LeftToes);
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
            public Vector2 To;            // where the step lands (a fixed spot on the ground, predicted from the body's motion)
            public float StepTime;
            public float StepDuration;
            public float StepHeight;
            public Vector2 Offset;        // after a release: how far the foot still is from the pose (decays)
            // Which way the foot points, in degrees in the fighter frame: a planted foot keeps pointing the same way
            // on the ground while the body turns over it (otherwise the toes would swing round the ankle and skate).
            public float Yaw;
            public float FromYaw;
            public float ToYaw;
            public float YawOffset;       // after a release: how far the foot's angle still is from the pose (decays)
            public Vector2 Shown;         // where the ankle was drawn last frame (fighter frame)
            public bool Airborne;         // the foot was in the air: it plants where it touches down, not where the pose says
        }

        readonly FootLock[] footLocks = new FootLock[2];
        float lastRootYaw;

        // A grounded action covering ground faster than a person can step (a stretched lunge, a flying kick) becomes
        // a leap: both feet leave the floor for the rush and land as it slows (a dodge or backstep hops the same way).
        void ApplyLeap(in FighterAnimInput input, Vector3 velocity, bool action, AnimatorSettings s)
        {
            if (!action || !input.Grounded || input.Dead) return;
            float speed = new Vector2(velocity.X, velocity.Z).Length();
            float lift = AnimMath.Clamp((speed - s.LeapSpeed) * s.LeapLiftPerSpeed, 0f, s.MaxLeapLift);
            if (!(lift > 0f)) return;
            float k = 1f / Math.Max(0.1f, solver.Skeleton.Scale);
            output[PoseChannel.HipsY] += lift * 0.6f * k;
            for (int i = 0; i < 2; i++)
            {
                BodySide side = i == 0 ? BodySide.Left : BodySide.Right;
                // A foot up on its toes is already raised by its tip (see PoseSolver.FootTipRise): lift from there, or
                // the leap would vanish into that rise and the toes would drag along the floor.
                float y = output[PoseSpec.Leg(side, 1)];
                float pitch = output[PoseSpec.Leg(side, 5)];
                if (output[PoseSpec.Leg(side, 6)] > 0.5f && pitch > 0f && y < 0.1f)
                    y = Math.Max(y, 0.08f + PoseSolver.FootTipRise(pitch));
                output[PoseSpec.Leg(side, 1)] = y + lift * k;
            }
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
            float turnRate = dt > 0f ? turn / dt : 0f;
            // A spin the clip itself makes (RootYaw: a spinning kick) pivots a planted foot on the ball with the body;
            // only the fighter's own turning (YawDelta) leaves the foot pointing where it was.
            // (A facing snap the body is lagging behind isn't a spin: that part of RootYaw's change is left out.)
            float spin = Wrap180(rootYaw - lastRootYaw + turnExcess);
            lastRootYaw = rootYaw;
            float footLength = solver.Skeleton.Proportions.FootLength * k;
            var move = new Vector2(velocity.X, velocity.Z) * dt;
            float decay = 1f - AnimMath.ExpBlend(s.ReleaseRate, dt);
            for (int i = 0; i < 2; i++)
            {
                BodySide side = i == 0 ? BodySide.Left : BodySide.Right;
                float sign = (float)side;
                ref FootLock f = ref footLocks[i];
                if (dt > 0f)
                {
                    f.Shown = RotateXZ(f.Shown, -turn) - move;
                    // The body turned and moved: a spot fixed on the ground turns and moves the other way in the
                    // body's frame (the velocity is already in the new, turned frame, so turn first, then move).
                    f.Position = RotateXZ(f.Position, -turn) - move;
                    f.From = RotateXZ(f.From, -turn) - move;
                    f.To = RotateXZ(f.To, -turn) - move;
                    if (f.Locked && !f.Stepping && spin != 0f)
                    {
                        // Pivot on the ball of the foot, not the ankle: the toes stay put and the heel swings round.
                        Vector2 ball = f.Position + RotateXZ(new Vector2(0f, footLength), f.Yaw - turn);
                        f.Position = ball - RotateXZ(new Vector2(0f, footLength), f.Yaw - turn + spin);
                    }
                    f.Yaw += spin - turn;
                    f.FromYaw += spin - turn;
                    f.ToYaw += spin - turn;
                    f.Offset *= decay;
                    f.YawOffset *= decay;
                }
                Vector2 key = RotateXZ(new Vector2(sign * output[PoseSpec.Leg(side, 0)], output[PoseSpec.Leg(side, 2)]) * k, rootYaw);
                float keyYaw = rootYaw + sign * output[PoseSpec.Leg(side, 4)];
                bool planted = output[PoseSpec.Leg(side, 7)] < 0.5f && output[PoseSpec.Leg(side, 1)] * k < (0.08f + s.PlantHeight) * k;
                Vector2 shown;
                float shownYaw;
                float lift = 0f;
                if (!input.Grounded) f.Airborne = true;
                if (!enabled || !planted)
                {
                    if (f.Locked)
                    {
                        f.Offset = f.Position - key;
                        f.YawOffset = Wrap180(f.Yaw - keyYaw);
                    }
                    f.Locked = false;
                    f.Stepping = false;
                    shown = key + f.Offset;
                    shownYaw = keyYaw + f.YawOffset;
                }
                else if (!f.Locked)
                {
                    f.Locked = true;
                    // Landing from the air: the foot plants where it came down (the pose then steps it into place if it
                    // wants it far from there), so touchdown never slides the feet across the floor.
                    f.Position = f.Airborne ? f.Shown : key + f.Offset;
                    f.Airborne = false;
                    f.Yaw = keyYaw + f.YawOffset;
                    f.Offset = Vector2.Zero;
                    f.YawOffset = 0f;
                    shown = f.Position;
                    shownYaw = f.Yaw;
                }
                else
                {
                    if (f.Stepping && dt > 0f)
                    {
                        f.StepTime += dt;
                        if (f.StepTime >= f.StepDuration)
                        {
                            f.Stepping = false;
                            f.Position = f.To;
                            f.Yaw = f.ToYaw;
                        }
                    }
                    if (!f.Stepping)
                    {
                        float distance = Vector2.Distance(key, f.Position);
                        float twist = Math.Abs(Wrap180(keyYaw - f.Yaw));
                        // Keep a foot on the floor: a foot waits while the other is stepping, unless the pose has run
                        // far away from it, and even then only once the other foot is coming down.
                        // (The other foot is also "stepping" while the pose itself has it off the floor: a walk's swing.)
                        ref FootLock other = ref footLocks[1 - i];
                        bool otherStepping = (other.Stepping && other.StepTime < 0.6f * other.StepDuration) || !other.Locked;
                        bool wantsStep = distance > s.StepDistance * k || twist > s.StepTurn;
                        bool urgent = distance > 2f * s.StepDistance * k || twist > 2f * s.StepTurn;
                        if (wantsStep && (!otherStepping || urgent))
                        {
                            f.Stepping = true;
                            f.From = f.Position;
                            f.FromYaw = f.Yaw;
                            f.StepTime = 0f;
                            float length = Math.Max(distance, twist * AnimMath.Deg2Rad * 0.1f * k);   // a pivot counts as a short step
                            f.StepDuration = AnimMath.Clamp(length / Math.Max(0.1f, s.StepSpeed), s.StepMinTime, s.StepMaxTime);
                            // Land where (and pointing how) the pose will want the foot when the step ends (the body keeps
                            // moving and turning meanwhile), so the foot touches down on a fixed spot and doesn't skid.
                            f.To = RotateXZ(key, turnRate * f.StepDuration) + new Vector2(velocity.X, velocity.Z) * f.StepDuration;
                            f.ToYaw = keyYaw + turnRate * f.StepDuration;
                            f.StepHeight = AnimMath.Clamp(length * s.StepLiftPerMetre, s.StepMinLift, s.StepMaxLift) * k;
                        }
                    }
                    if (f.Stepping)
                    {
                        float u = AnimMath.Clamp01(f.StepTime / Math.Max(1e-3f, f.StepDuration));
                        if (u < s.StepRetargetShare)
                        {
                            // Early in the step the foot is still in the air: if the pose keeps moving (a clip drawing
                            // the foot back to guard), the landing spot follows it, so one step covers the whole move
                            // instead of a shuffle of short ones.
                            float remaining = f.StepDuration - f.StepTime;
                            f.To = RotateXZ(key, turnRate * remaining) + new Vector2(velocity.X, velocity.Z) * remaining;
                            f.ToYaw = keyYaw + turnRate * remaining;
                        }
                        float e = AnimMath.SmoothStep(u);
                        shown = Vector2.Lerp(f.From, f.To, e);
                        f.Yaw = f.FromYaw + Wrap180(f.ToYaw - f.FromYaw) * e;
                        lift = f.StepHeight * MathF.Sin(MathF.PI * u);
                        f.Position = shown;
                    }
                    else
                    {
                        shown = f.Position;
                    }
                    shownYaw = f.Yaw;
                }
                if (!f.Locked && f.Offset.LengthSquared() < 1e-8f && Math.Abs(f.YawOffset) < 0.01f) continue;
                Vector2 local = RotateXZ(shown, -rootYaw) / k;
                output[PoseSpec.Leg(side, 0)] = sign * local.X;
                output[PoseSpec.Leg(side, 2)] = local.Y;
                output[PoseSpec.Leg(side, 1)] += lift / k;
                output[PoseSpec.Leg(side, 4)] = sign * Wrap180(shownYaw - rootYaw);
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
                float yawRate = AnimMath.IsFinite(input.YawDelta) ? (input.YawDelta - turnExcess) / dt : 0f;   // the turn the body shows
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
