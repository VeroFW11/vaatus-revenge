using UnityEngine;
using VaatusRevenge.Core;

namespace VaatusRevenge
{
    // Everything the player sees and feels that isn't a rule: grey-box poses, fire effects, flashes, screen
    // shake, gamepad rumble and the sprint FOV kick. Nothing here changes the fight.
    //
    // PlayerController owns one. It passes in the combat model's events (moments: a strike starts, a guard
    // goes up, a hit lands) and calls Tick every frame for looks that simply follow a state (i-frame tint,
    // stagger wobble, charge glow, death). Following the state for those means a look can never get stuck on,
    // whatever order things happened in. All the numbers come from PlayerFeedbackSettings, read live.
    public sealed class PlayerFeedback
    {
        // GreyboxRig.Strike needs a finite hold time, so a raised guard is re-held every half of this (seconds).
        const float GuardHoldChunk = 1f;

        readonly Transform body;
        readonly GreyboxRig rig;
        readonly Combatant fighter;
        readonly PlayerRumble rumble = new PlayerRumble();

        FireVfxHandle chargeGlow;
        FireVfxHandle strikeTrail;
        FireVfxHandle leftFootTrail;
        FireVfxHandle rightFootTrail;
        Limb poseLimb = Limb.RightFist;   // the limb(s) the current move's pose uses, eased back when it ends
        bool charging;
        bool readyCueDone;                // the "get ready" cue has played (or been skipped) for the charge in progress
        bool guardRaised;
        float guardHoldTimer;
        bool plungeLandedThisFrame;
        ThirdPersonCameraRig fovCamera;
        float fovBoost;

        public PlayerFeedback(Transform body, GreyboxRig rig, Combatant fighter)
        {
            this.body = body;
            this.rig = rig;
            this.fighter = fighter;
        }

        // Set every frame by PlayerController: false while the player is on keyboard and mouse, dead or paused.
        // Rumble is then skipped, and any buzz already running is stopped.
        public bool RumbleAllowed { get; set; }

        // Just the rumble (the game lost focus): the effects on screen carry on.
        public void StopRumble()
        {
            rumble.Stop();
        }

        public void BeginFrame()
        {
            plungeLandedThisFrame = false;
        }

        // ---------------------------------------------------------------- the model's events

        public void OnEvent(in PlayerEvent e, PlayerCombatModel model, PlayerFeedbackSettings s)
        {
            switch (e.Type)
            {
                case PlayerEventType.AttackStarted: AttackStarted(in e, model, s); break;
                case PlayerEventType.AttackActiveStart: ActiveStarted(in e, s); break;
                case PlayerEventType.AttackActiveEnd: StopStrikeTrail(); break;
                case PlayerEventType.ProjectileLaunched: FireVfx.Muzzle(e.Origin.ToUnity(), e.Direction.ToUnity()); break;
                case PlayerEventType.PlungeImpact: PlungeImpact(in e, s); break;
                case PlayerEventType.AttackEnded:
                    StopStrikeTrail();
                    Release(poseLimb, s);
                    break;
                case PlayerEventType.ChargeStarted: ChargeStarted(in e, model, s); break;
                case PlayerEventType.ChargeSweetSpot: Pulse(s.SweetSpot); break; // the rig flashes itself (see Tick)
                case PlayerEventType.ChargeCancelled: Release(poseLimb, s); break;
                case PlayerEventType.DodgeStarted: DodgeStarted(in e, model, s); break;
                case PlayerEventType.DodgeEnded: StopDodgeTrails(); break;
                case PlayerEventType.PerfectDodge: PerfectDodge(s); break;
                case PlayerEventType.Jumped: FireVfx.Burst(FootPosition(s), Vector3.down, s.JumpBurstScale); break;
                case PlayerEventType.Landed:
                    if (!plungeLandedThisFrame && e.Amount >= s.HardLandingSpeed) Pulse(s.HardLanding);
                    break;
                case PlayerEventType.GuardStarted: RaiseGuard(s); break;
                case PlayerEventType.GuardEnded: LowerGuard(s); break;
                case PlayerEventType.Blocked: Blocked(in e, s); break;
                case PlayerEventType.GuardBroken: GuardBroken(in e, s); break;
                case PlayerEventType.Deflected: Deflected(in e, s); break;
                case PlayerEventType.HealStarted: HealStarted(model, s); break;
                case PlayerEventType.HealApplied: HealApplied(s); break;
                case PlayerEventType.HealInterrupted: Release(Limb.BothFists, s); break;
                case PlayerEventType.HealFailed: Flash(s.EmptyFlaskFlashColor, s.EmptyFlaskFlashTime); break;
                case PlayerEventType.Damaged: Damaged(in e, s); break;
                case PlayerEventType.Parried: GotParried(s); break;
                case PlayerEventType.Died:
                    // A death reported after a respawn already happened is old news.
                    if (model.State == PlayerState.Dead) OnKilled();
                    break;
                case PlayerEventType.Respawned: ResetAll(); break;
                // Stagger, sprint (FOV), i-frames and charge level are drawn from the model's state in Tick.
            }
        }

        // Our attack touched somebody: a spark where it connected, coloured by what happened.
        public void OnHitReport(in HitReport report, PlayerFeedbackSettings s)
        {
            switch (report.Result.Outcome)
            {
                case HitOutcome.Hit:
                    FireVfx.HitSpark(report.Point, s.HitSparkColor);
                    break;
                case HitOutcome.Blocked:
                case HitOutcome.GuardBroken:
                    FireVfx.HitSpark(report.Point, s.BlockSparkColor);
                    break;
                case HitOutcome.Parried:
                    FireVfx.HitSpark(report.Point, s.DeflectSparkColor);
                    break;
            }
        }

        // Once per swing that landed at least one clean hit (not once per enemy, so a kick through three
        // enemies doesn't shake three times as hard).
        public void OnCleanHit(bool heavy, bool faJin, PlayerFeedbackSettings s)
        {
            Pulse(faJin ? s.FaJinHit : (heavy ? s.HeavyHit : s.LightHit));
        }

        // ---------------------------------------------------------------- every frame

        public void Tick(PlayerCombatModel model, PlayerFeedbackSettings s, float dt, float unscaledDt)
        {
            rumble.Tick(unscaledDt);
            if (!RumbleAllowed) rumble.Stop();

            bool dead = model.State == PlayerState.Dead;
            if (rig != null)
            {
                rig.SetDead(dead);
                rig.SetStaggered(model.State == PlayerState.Staggered);
                rig.SetInvulnerableLook(model.IsInvulnerable);
                // After a perfect dodge the fists glow while the counter (bonus damage) is ready.
                rig.SetTelegraph(s.CounterGlowColor, !dead && model.IsCounterWindowOpen ? s.CounterGlowIntensity : 0f);
            }
            UpdateCharge(model, s);
            UpdateGuardHold(dt, s);
            UpdateFov(!dead && s.SprintFovBoost && model.IsSprinting);
        }

        // ---------------------------------------------------------------- death, respawn, shutdown

        public void OnKilled()
        {
            rumble.Stop();
            StopEffects();
            guardRaised = false;
            ClearFov();
            if (rig != null) rig.SetDead(true);
        }

        public void ResetAll()
        {
            rumble.Stop();
            StopEffects();
            guardRaised = false;
            guardHoldTimer = 0f;
            poseLimb = Limb.RightFist;
            ClearFov();
            if (rig != null) rig.ResetPose();
        }

        // Disabled or quitting: stop everything that could outlive us (rumble above all). Poses are left alone:
        // if the player is switched back on mid-guard, the guard carries on. A running charge glow comes back
        // by itself through Tick.
        public void Shutdown()
        {
            rumble.Stop();
            StopEffects();
            ClearFov();
        }

        // ---------------------------------------------------------------- attacks

        void AttackStarted(in PlayerEvent e, PlayerCombatModel model, PlayerFeedbackSettings s)
        {
            MoveData move = e.Move;
            if (move == null) return;
            if (e.IsCounter) Flash(s.CounterFlashColor, s.CounterFlashTime);
            PlayerStrikePoses p = s.Poses;
            if (rig == null || p == null) return;

            // The limb shoots out during startup, holds through the active frames and comes back during recovery,
            // so the pose shows the move's real timing.
            Limb limb = move.Limb;
            switch (e.AttackKind)
            {
                case PlayerAttackKind.Plunge:
                {
                    PlungeSettings plunge = model.MoveSet != null ? model.MoveSet.Plunge : null;
                    float hang = plunge != null ? plunge.HangTime : 0f;
                    float fall = plunge != null ? plunge.MaxFallTime : 0f;
                    poseLimb = IsFoot(limb) ? limb : Limb.RightFoot;
                    // Foot up above the head for the drop; PlungeImpact slams it down.
                    rig.Strike(poseLimb, PoseFor(p.AxeKickRaise, poseLimb), hang, fall, p.InterruptRetractTime);
                    StartStrikeTrail(poseLimb, fall, s);
                    break;
                }
                case PlayerAttackKind.Heavy:
                    // A heavy thrown with the hands is a two-handed palm strike.
                    poseLimb = IsFoot(limb) ? limb : Limb.BothFists;
                    rig.Strike(poseLimb, PoseFor(IsFoot(limb) ? p.Kick : p.Palm, poseLimb), move.Startup, Hold(move, p), move.Recovery);
                    rig.Lean(p.HeavyLeanDegrees, move.TotalDuration);
                    break;
                case PlayerAttackKind.Skill:
                    poseLimb = limb;
                    rig.Strike(limb, PoseFor(IsFoot(limb) ? p.Kick : p.Blast, limb), move.Startup, Hold(move, p), move.Recovery);
                    rig.Lean(p.StrikeLeanDegrees, move.TotalDuration);
                    break;
                default: // light chain and sprint attack
                {
                    poseLimb = limb;
                    Vector3 target = p.Punch;
                    if (IsFoot(limb))
                    {
                        if (move.ArcDegrees >= p.SpinKickMinArc)
                        {
                            // A wide kick is a spinning kick: the body turns under the foot as it sweeps round.
                            target = p.SpinKick;
                            rig.Spin(IsLeft(limb) ? -p.SpinDegrees : p.SpinDegrees, move.Startup + move.Active);
                        }
                        else
                        {
                            target = e.AttackKind == PlayerAttackKind.Sprint ? p.HighKick : p.Kick;
                        }
                    }
                    rig.Strike(limb, PoseFor(target, limb), move.Startup, Hold(move, p), move.Recovery);
                    rig.Lean(e.AttackKind == PlayerAttackKind.Sprint ? p.HeavyLeanDegrees : p.StrikeLeanDegrees, move.TotalDuration);
                    break;
                }
            }
        }

        // The strike can hit now: flame bursts from the strike along the facing, sized by the move's reach.
        void ActiveStarted(in PlayerEvent e, PlayerFeedbackSettings s)
        {
            MoveData move = e.Move;
            if (move == null) return;
            bool faJin = e.ChargeTier == ChargeTier.FaJin;
            float scale = move.Range * s.BurstScalePerMetre * (faJin ? s.FaJinBurstMultiplier : 1f);
            FireVfx.Burst(e.Origin.ToUnity(), e.Direction.ToUnity(), scale);
            PlayerStrikePoses p = s.Poses;
            if (p != null && move.ArcDegrees >= p.SpinKickMinArc && s.WideArcRingShare > 0f)
            {
                FireVfx.Ring(body.position, move.Range * s.WideArcRingShare);
            }
            StartStrikeTrail(poseLimb, move.Active, s);
            if (faJin) Pulse(s.FaJinRelease);
        }

        void PlungeImpact(in PlayerEvent e, PlayerFeedbackSettings s)
        {
            plungeLandedThisFrame = true;
            StopStrikeTrail();
            Vector3 feet = e.Origin.ToUnity();
            FireVfx.Ring(feet, e.Radius);
            if (s.PlungeExplosionShare > 0f) FireVfx.Explosion(feet + Vector3.up * s.EffectFootHeight, e.Radius * s.PlungeExplosionShare);
            Pulse(s.PlungeLanding);
            PlayerStrikePoses p = s.Poses;
            if (rig == null || p == null || e.Move == null) return;
            rig.Strike(poseLimb, PoseFor(p.AxeKickDrop, poseLimb), p.AxeKickDropTime, p.AxeKickDropHold, e.Move.Recovery);
            rig.Lean(p.HeavyLeanDegrees, e.Move.Recovery);
        }

        void ChargeStarted(in PlayerEvent e, PlayerCombatModel model, PlayerFeedbackSettings s)
        {
            MoveData move = e.Move;
            PlayerStrikePoses p = s.Poses;
            if (move == null || rig == null || p == null) return;
            // Pull the striking fist back to the hip while the power builds; the release shoots it out from there.
            poseLimb = ChamberLimb(move.Limb);
            ChargeSettings charge = model.MoveSet != null ? model.MoveSet.Charge : null;
            float hold = charge != null ? charge.MaxChargeTime : 0f;
            rig.Strike(poseLimb, PoseFor(p.Chamber, poseLimb), p.ChamberTime, hold, p.InterruptRetractTime);
        }

        void UpdateCharge(PlayerCombatModel model, PlayerFeedbackSettings s)
        {
            bool nowCharging = model.State == PlayerState.Charging;
            if (nowCharging)
            {
                float level = model.ChargeLevel;
                if (!charging)
                {
                    readyCueDone = false;
                    if (rig != null)
                    {
                        MoveData heavy = model.CurrentMove;
                        chargeGlow = FireVfx.ChargeGlow(rig.GetAnchor(ChamberLimb(heavy != null ? heavy.Limb : Limb.RightFist)));
                    }
                }
                chargeGlow.SetLevel(level);
                UpdateReadyCue(model, s);
                // The rig flashes white-gold the moment InSweetSpot turns true: the sweet spot is open.
                if (rig != null) rig.SetCharge(level, model.InSweetSpot);
            }
            else if (charging)
            {
                StopChargeGlow();
            }
            charging = nowCharging;
        }

        // A faint glow and a light rumble tick Charge.ReadyCueLead seconds before the sweet spot opens. People need
        // about a fifth of a second to react, so releasing on the sweet-spot flash itself is usually too late; this
        // earlier "get ready" cue lets you let go inside the window (playtest report HUD-01). It's read from the
        // model's charge time every frame rather than from an event, so it plays once per charge and can't be missed.
        void UpdateReadyCue(PlayerCombatModel model, PlayerFeedbackSettings s)
        {
            if (readyCueDone) return;
            ChargeSettings charge = model.MoveSet != null ? model.MoveSet.Charge : null;
            if (charge == null || !(charge.ReadyCueLead > 0f))
            {
                readyCueDone = true; // no lead set: no cue this charge
                return;
            }
            float readyAt = charge.SweetSpotStart - charge.ReadyCueLead;
            if (model.ChargeTime < readyAt) return;
            readyCueDone = true;
            // Skip it when it would come too early to mean anything (a lead longer than the wait for the sweet spot)
            // or too late (the charge began at or past the sweet spot, which then gives its own cue).
            if (!(readyAt > 0f) || model.ChargeTime >= charge.SweetSpotStart) return;
            Pulse(s.ReadyCue);
            Flash(s.ReadyCueFlashColor, s.ReadyCueFlashTime);
        }

        // ---------------------------------------------------------------- defence

        void DodgeStarted(in PlayerEvent e, PlayerCombatModel model, PlayerFeedbackSettings s)
        {
            Vector3 direction = e.Direction.ToUnity();
            // Flame Step: fire jets from the feet push you the other way.
            FireVfx.Burst(FootPosition(s), -direction, s.DodgeBurstScale);
            DodgeProfile dodge = model.MoveSet != null ? model.MoveSet.Dodge : null;
            float duration = dodge != null ? dodge.Duration : 0f;
            StopDodgeTrails();
            if (rig == null) return;
            if (s.DodgeTrails)
            {
                leftFootTrail = FireVfx.Trail(rig.GetAnchor(Limb.LeftFoot), duration);
                rightFootTrail = FireVfx.Trail(rig.GetAnchor(Limb.RightFoot), duration);
            }
            PlayerStrikePoses p = s.Poses;
            if (p != null && duration > 0f) rig.Lean(Vector3.Dot(direction, body.forward) * p.DodgeLeanDegrees, duration);
        }

        void PerfectDodge(PlayerFeedbackSettings s)
        {
            Flash(s.PerfectDodgeFlashColor, s.PerfectDodgeFlashTime);
            Vector3 chest = ChestPosition();
            FireVfx.Burst(chest, body.forward, s.PerfectDodgeBurstScale);
            FireVfx.HitSpark(chest, s.PerfectDodgeFlashColor);
            Pulse(s.PerfectDodge);
        }

        void RaiseGuard(PlayerFeedbackSettings s)
        {
            guardRaised = true;
            guardHoldTimer = 0f;
            PlayerStrikePoses p = s.Poses;
            if (rig != null && p != null)
            {
                rig.Strike(Limb.BothFists, PoseFor(p.Guard, Limb.BothFists), p.GuardRaiseTime, GuardHoldChunk, p.GuardLowerTime);
            }
        }

        void LowerGuard(PlayerFeedbackSettings s)
        {
            if (!guardRaised) return;
            guardRaised = false;
            PlayerStrikePoses p = s.Poses;
            Release(Limb.BothFists, p != null ? p.GuardLowerTime : 0f);
        }

        void UpdateGuardHold(float dt, PlayerFeedbackSettings s)
        {
            if (!guardRaised || rig == null || !(dt > 0f)) return;
            guardHoldTimer += dt;
            if (guardHoldTimer < GuardHoldChunk * 0.5f) return;
            guardHoldTimer = 0f;
            PlayerStrikePoses p = s.Poses;
            // The fists are already up: an instant re-strike to the same spot simply keeps them there.
            if (p != null) rig.Strike(Limb.BothFists, PoseFor(p.Guard, Limb.BothFists), 0f, GuardHoldChunk, p.GuardLowerTime);
        }

        void Blocked(in PlayerEvent e, PlayerFeedbackSettings s)
        {
            FireVfx.HitSpark(ContactPoint(e.Direction), s.BlockSparkColor);
            PlayerStrikePoses p = s.Poses;
            if (rig != null && p != null) rig.Lean(-p.BlockLeanDegrees, p.ReactionLeanTime);
            Pulse(s.GuardBlock);
        }

        void GuardBroken(in PlayerEvent e, PlayerFeedbackSettings s)
        {
            FireVfx.HitSpark(ContactPoint(e.Direction), s.BlockSparkColor);
            Flash(s.GuardBreakFlashColor, s.GuardBreakFlashTime);
            Pulse(s.GuardBreak);
        }

        void Deflected(in PlayerEvent e, PlayerFeedbackSettings s)
        {
            Vector3 toAttacker = ToAttacker(e.Direction);
            Vector3 contact = ChestPosition() + toAttacker * BodyRadius;
            FireVfx.HitSpark(contact, s.DeflectSparkColor);
            FireVfx.Burst(contact, toAttacker, s.DeflectBurstScale);
            Flash(s.DeflectFlashColor, s.DeflectFlashTime);
            Pulse(s.Deflect);
        }

        void Damaged(in PlayerEvent e, PlayerFeedbackSettings s)
        {
            FireVfx.HitSpark(ContactPoint(e.Direction), s.HurtSparkColor);
            Flash(s.HurtFlashColor, s.HurtFlashTime);
            PlayerStrikePoses p = s.Poses;
            if (rig != null && p != null) rig.Lean(-p.HurtLeanDegrees, p.ReactionLeanTime);
            Pulse(s.Hurt);
        }

        // An enemy deflected our strike (a stagger follows, drawn by Tick).
        void GotParried(PlayerFeedbackSettings s)
        {
            if (rig != null) FireVfx.HitSpark(rig.GetAnchor(poseLimb).position, s.DeflectSparkColor);
            Pulse(s.GotParried);
        }

        // ---------------------------------------------------------------- healing

        void HealStarted(PlayerCombatModel model, PlayerFeedbackSettings s)
        {
            PlayerStrikePoses p = s.Poses;
            if (rig == null || p == null) return;
            float duration = model.Tuning != null ? model.Tuning.HealDuration : 0f;
            float hold = Mathf.Max(0f, duration - p.DrinkRaiseTime - p.DrinkLowerTime);
            rig.Strike(Limb.BothFists, PoseFor(p.Drink, Limb.BothFists), p.DrinkRaiseTime, hold, p.DrinkLowerTime);
        }

        void HealApplied(PlayerFeedbackSettings s)
        {
            Flash(s.HealFlashColor, s.HealFlashTime);
            FireVfx.HitSpark(ChestPosition(), s.HealFlashColor);
            Pulse(s.HealApplied);
        }

        // ---------------------------------------------------------------- helpers

        void Pulse(FeedbackPulse pulse)
        {
            if (pulse == null) return;
            if (pulse.ShakeAmplitude > 0f && pulse.ShakeDuration > 0f) CameraShake.Add(pulse.ShakeAmplitude, pulse.ShakeDuration);
            if (RumbleAllowed) rumble.Play(pulse);
        }

        void Flash(Color color, float duration)
        {
            if (rig != null) rig.Flash(color, duration);
        }

        // Eases a limb back to rest from wherever it is right now (a strike cut short, a guard lowered).
        void Release(Limb limb, PlayerFeedbackSettings s)
        {
            PlayerStrikePoses p = s.Poses;
            Release(limb, p != null ? p.InterruptRetractTime : 0f);
        }

        void Release(Limb limb, float time)
        {
            if (rig == null || !rig.IsBuilt) return;
            if (limb == Limb.BothFists)
            {
                Release(Limb.RightFist, time);
                Release(Limb.LeftFist, time);
                return;
            }
            if (limb == Limb.Weapon) limb = Limb.RightFist; // the player is unarmed: the rig moves the right hand
            Transform root = rig.transform;
            Vector3 current = root.InverseTransformPoint(rig.GetAnchor(limb).position);
            rig.Strike(limb, current, 0f, 0f, time);
        }

        void StartStrikeTrail(Limb limb, float duration, PlayerFeedbackSettings s)
        {
            StopStrikeTrail();
            if (!s.StrikeTrails || !(duration > 0f) || rig == null) return;
            strikeTrail = FireVfx.Trail(rig.GetAnchor(limb), duration);
        }

        void StopStrikeTrail()
        {
            strikeTrail.Stop();
            strikeTrail = FireVfxHandle.None;
        }

        void StopDodgeTrails()
        {
            leftFootTrail.Stop();
            rightFootTrail.Stop();
            leftFootTrail = FireVfxHandle.None;
            rightFootTrail = FireVfxHandle.None;
        }

        void StopChargeGlow()
        {
            chargeGlow.Stop();
            chargeGlow = FireVfxHandle.None;
            charging = false;
            if (rig != null) rig.SetCharge(0f, false);
        }

        void StopEffects()
        {
            StopStrikeTrail();
            StopDodgeTrails();
            StopChargeGlow();
        }

        void UpdateFov(bool sprinting)
        {
            ThirdPersonCameraRig cam = ThirdPersonCameraRig.Instance;
            float wanted = cam != null && sprinting ? cam.SprintFovBoost : 0f;
            if (cam == fovCamera && wanted == fovBoost) return;
            if (cam != null) cam.SetFovBoost(wanted);
            fovCamera = cam;
            fovBoost = wanted;
        }

        void ClearFov()
        {
            if (fovCamera != null && fovBoost != 0f) fovCamera.SetFovBoost(0f);
            fovCamera = null;
            fovBoost = 0f;
        }

        Vector3 ChestPosition()
        {
            if (fighter != null) return fighter.AimPoint.position;
            return rig != null ? rig.ChestAnchor.position : body.position;
        }

        float BodyRadius => fighter != null ? fighter.Radius : 0f;

        Vector3 FootPosition(PlayerFeedbackSettings s)
        {
            return body.position + Vector3.up * s.EffectFootHeight;
        }

        // Hit directions point attacker -> defender. An unknown direction counts as from the front.
        Vector3 ToAttacker(System.Numerics.Vector3 hitDirection)
        {
            Vector3 toAttacker = -hitDirection.ToUnity();
            toAttacker.y = 0f;
            return toAttacker.sqrMagnitude > 1e-6f ? toAttacker.normalized : body.forward;
        }

        // Where on our body a hit from that direction lands (for sparks).
        Vector3 ContactPoint(System.Numerics.Vector3 hitDirection)
        {
            return ChestPosition() + ToAttacker(hitDirection) * BodyRadius;
        }

        static float Hold(MoveData move, PlayerStrikePoses p)
        {
            return Mathf.Max(move.Active, p.MinStrikeHold);
        }

        static bool IsFoot(Limb limb)
        {
            return limb == Limb.RightFoot || limb == Limb.LeftFoot;
        }

        static bool IsLeft(Limb limb)
        {
            return limb == Limb.LeftFist || limb == Limb.LeftFoot;
        }

        // The single limb that winds up a charged move: its foot, or the lead fist for hand moves.
        static Limb ChamberLimb(Limb limb)
        {
            if (IsFoot(limb) || limb == Limb.LeftFist) return limb;
            return Limb.RightFist;
        }

        // Poses are written for the right side: left limbs mirror across the body, both fists centre on it.
        static Vector3 PoseFor(Vector3 pose, Limb limb)
        {
            if (IsLeft(limb)) pose.x = -pose.x;
            else if (limb == Limb.BothFists) pose.x = 0f;
            return pose;
        }
    }
}
