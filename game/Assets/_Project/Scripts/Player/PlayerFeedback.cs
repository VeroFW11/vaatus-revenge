using UnityEngine;
using VaatusRevenge.Core;

namespace VaatusRevenge
{
    // Everything the player sees and feels that isn't a rule or a pose: fire effects, flashes, screen shake,
    // gamepad rumble and the sprint FOV kick. Nothing here changes the fight. (The body's martial-arts motion
    // comes from the procedural animator: PlayerController feeds it through PlayerAnimationFeed.)
    //
    // PlayerController owns one. It passes in the combat model's events (moments: a strike goes active, a guard
    // goes up, a hit lands) and calls Tick every frame for looks that simply follow a state (i-frame shimmer,
    // stagger dimming, charge glow, death). Following the state for those means a look can never get stuck on,
    // whatever order things happened in. All the numbers come from PlayerFeedbackSettings, read live.
    //
    // Fire follows each move's EffectKey (data): a burst from the fist, a cone for Phoenix Palm, a column for
    // the launcher, a lash for the Fire Whip, a ring for the Flame Wheel, a slam for the tornado and axe kicks,
    // with flames on the striking limb and a trail through the active frames. Every effect is sized from the
    // move's Range and ArcDegrees, so what you see is what can hit.
    public sealed class PlayerFeedback
    {
        readonly Transform body;
        readonly HumanoidBody rig;
        readonly Combatant fighter;
        readonly PlayerRumble rumble = new PlayerRumble();

        FireVfxHandle chargeGlow;
        FireVfxHandle strikeTrail;
        FireVfxHandle limbFlame;
        FireVfxHandle whip;
        FireVfxHandle leftFootTrail;
        FireVfxHandle rightFootTrail;
        FireVfxHandle leftJet;
        FireVfxHandle rightJet;
        Limb strikeLimb = Limb.RightFist;  // the current move's striking limb
        bool charging;
        bool readyCueDone;                // the "get ready" cue has played (or been skipped) for the charge in progress
        bool plungeLandedThisFrame;
        ThirdPersonCameraRig fovCamera;
        float fovBoost;

        public PlayerFeedback(Transform body, HumanoidBody rig, Combatant fighter)
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
                    StopLimbFlame();
                    StopJets();
                    break;
                case PlayerEventType.ChargeSweetSpot: Pulse(s.SweetSpot); break; // the body flashes itself (see Tick)
                case PlayerEventType.DodgeStarted: DodgeStarted(in e, model, s); break;
                case PlayerEventType.DodgeEnded:
                    StopDodgeTrails();
                    StopJets();
                    break;
                case PlayerEventType.PerfectDodge: PerfectDodge(s); break;
                case PlayerEventType.Jumped: FireVfx.Burst(FootPosition(s), Vector3.down, s.JumpBurstScale); break;
                case PlayerEventType.Landed:
                    if (!plungeLandedThisFrame && e.Amount >= s.HardLandingSpeed) Pulse(s.HardLanding);
                    break;
                case PlayerEventType.Blocked: Blocked(in e, s); break;
                case PlayerEventType.GuardBroken: GuardBroken(in e, s); break;
                case PlayerEventType.Deflected: Deflected(in e, s); break;
                case PlayerEventType.HealApplied: HealApplied(s); break;
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
            UpdateFov(!dead && s.SprintFovBoost && model.IsSprinting);
        }

        // ---------------------------------------------------------------- death, respawn, shutdown

        public void OnKilled()
        {
            rumble.Stop();
            StopEffects();
            ClearFov();
            if (rig != null) rig.SetDead(true);
        }

        public void ResetAll()
        {
            rumble.Stop();
            StopEffects();
            strikeLimb = Limb.RightFist;
            ClearFov();
            if (rig != null) rig.ResetLook();
        }

        // Disabled or quitting: stop everything that could outlive us (rumble above all). A running charge glow
        // comes back by itself through Tick.
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
            strikeLimb = move.Limb == Limb.Weapon ? Limb.RightFist : move.Limb;
            PlayerStrikePoses p = s.Poses;
            if (e.AttackKind == PlayerAttackKind.ZipStrike) ZipDashStarted(move, s);
            if (rig == null || p == null || !p.LimbFlames || e.AttackKind == PlayerAttackKind.Plunge) return;
            // The striking fist or foot catches fire as it winds up and burns until the strike is over. Moves with a
            // big effect of their own keep it shorter.
            float duration = move.Startup + move.Active;
            if (HasBigEffect(move)) duration *= p.BigEffectLimbFlameShare;
            StopLimbFlame();
            if (duration > 0f) limbFlame = FireVfx.LimbFlame(rig.GetAnchor(strikeLimb), duration);
        }

        // The strike can hit now: fire in the shape of the move's EffectKey, sized by its reach, and a flame trail
        // on the striking limb through the active frames.
        void ActiveStarted(in PlayerEvent e, PlayerFeedbackSettings s)
        {
            MoveData move = e.Move;
            if (move == null) return;
            bool faJin = e.ChargeTier == ChargeTier.FaJin;
            Vector3 origin = e.Origin.ToUnity();
            Vector3 direction = e.Direction.ToUnity();
            Transform limb = rig != null ? rig.GetAnchor(strikeLimb) : body;
            PlayerStrikePoses p = s.Poses;
            switch (move.EffectKey)
            {
                case EffectKeys.Cone:
                    // Drawn exactly as long as it hits: a fa jin cone is stronger, not longer.
                    FireVfx.Cone(origin, direction, move.Range, move.ArcDegrees);
                    break;
                case EffectKeys.Pillar:
                    // The rising kick throws a burst up along the kick; the column of fire itself rises under the
                    // enemy when it's actually launched (EnemyRigPresenter.OnLaunched), so there's only ever one.
                    FireVfx.Burst(limb.position, direction, move.Range * s.BurstScalePerMetre * 0.5f);
                    break;
                case EffectKeys.Whip:
                    StopWhip();
                    if (rig != null)
                        whip = FireVfx.Whip(rig.GetAnchor(Limb.RightFist), origin, direction, move.Range, move.ArcDegrees,
                            move.Active + (p != null ? p.WhipLinger : 0.1f));
                    break;
                case EffectKeys.Wheel:
                    FireVfx.Wheel(body.position, move.Range);
                    break;
                case EffectKeys.Slam:
                    // A downward burst from the kicking foot. The ring of fire on the floor appears where and when the
                    // enemy actually lands (EnemyRigPresenter.OnKnockedDown), not under the player.
                    FireVfx.Burst(limb.position, Vector3.down, move.Range * s.BurstScalePerMetre * 0.5f);
                    break;
                case EffectKeys.Trail:
                    FireVfx.Burst(limb.position, direction, move.Range * s.BurstScalePerMetre * 0.5f);
                    // A wide spinning kick also throws a low ring of fire.
                    if (move.ArcDegrees >= 180f && s.WideArcRingShare > 0f) FireVfx.Ring(body.position, move.Range * s.WideArcRingShare);
                    break;
                default:
                {
                    float scale = move.Range * s.BurstScalePerMetre * (faJin ? s.FaJinBurstMultiplier : 1f);
                    FireVfx.Burst(origin, direction, scale);
                    break;
                }
            }
            StartStrikeTrail(strikeLimb, move.Active, s);
            if (faJin) Pulse(s.FaJinRelease);
        }

        static bool HasBigEffect(MoveData move)
        {
            string key = move.EffectKey;
            return key == EffectKeys.Cone || key == EffectKeys.Whip || key == EffectKeys.Wheel;
        }

        void PlungeImpact(in PlayerEvent e, PlayerFeedbackSettings s)
        {
            plungeLandedThisFrame = true;
            StopStrikeTrail();
            Vector3 feet = e.Origin.ToUnity();
            FireVfx.Ring(feet, e.Radius);
            if (rig != null) FireVfx.Burst(rig.GetAnchor(Limb.RightFoot).position, Vector3.down, Mathf.Max(0.5f, e.Radius * 0.3f));
            if (s.PlungeExplosionShare > 0f) FireVfx.Explosion(feet + Vector3.up * s.EffectFootHeight, e.Radius * s.PlungeExplosionShare);
            Pulse(s.PlungeLanding);
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
                // The body flashes white-gold the moment InSweetSpot turns true: the sweet spot is open.
                if (rig != null) rig.SetCharge(level, model.InSweetSpot);
            }
            else if (charging)
            {
                StopChargeGlow();
            }
            charging = nowCharging;
        }

        // A faint glow and a light rumble tick Charge.ReadyCueLead seconds before the sweet spot opens. People need
        // about a fifth of a second to react, so the cue comes a little early: reacting to it lets you go around the
        // middle of the window (playtest reports 01 HUD-01 and 02 NEW-04). It's read from the
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

        // ---------------------------------------------------------------- defence and movement

        void DodgeStarted(in PlayerEvent e, PlayerCombatModel model, PlayerFeedbackSettings s)
        {
            Vector3 direction = e.Direction.ToUnity();
            // Flame Step: fire jets from the feet push you the other way.
            FireVfx.Burst(FootPosition(s), -direction, s.DodgeBurstScale);
            DodgeProfile dodge = model.MoveSet != null ? model.MoveSet.Dodge : null;
            float duration = dodge != null ? dodge.Duration : 0f;
            StopDodgeTrails();
            if (rig == null) return;
            if (e.InAir)
            {
                AerialSettings aerial = model.MoveSet != null ? model.MoveSet.Aerial : null;
                StartJets(-direction, aerial != null ? aerial.AirDashDuration : duration, s);
                return;
            }
            if (s.DodgeTrails)
            {
                leftFootTrail = FireVfx.Trail(rig.GetAnchor(Limb.LeftFoot), duration);
                rightFootTrail = FireVfx.Trail(rig.GetAnchor(Limb.RightFoot), duration);
            }
        }

        // Flame Step Strike: jets of fire from both feet carry you across the gap for the whole dash.
        void ZipDashStarted(MoveData move, PlayerFeedbackSettings s)
        {
            FireVfx.Burst(FootPosition(s), -body.forward, s.DodgeBurstScale);
            StopDodgeTrails();
            StartJets(-body.forward, move.Startup + move.Active, s);
        }

        void StartJets(Vector3 direction, float duration, PlayerFeedbackSettings s)
        {
            StopJets();
            PlayerStrikePoses p = s.Poses;
            if (rig == null || !(duration > 0f) || (p != null && !p.FootJets)) return;
            leftJet = FireVfx.FootJet(rig.GetAnchor(Limb.LeftFoot), direction, duration);
            rightJet = FireVfx.FootJet(rig.GetAnchor(Limb.RightFoot), direction, duration);
        }

        void PerfectDodge(PlayerFeedbackSettings s)
        {
            Flash(s.PerfectDodgeFlashColor, s.PerfectDodgeFlashTime);
            Vector3 chest = ChestPosition();
            FireVfx.Burst(chest, body.forward, s.PerfectDodgeBurstScale);
            FireVfx.HitSpark(chest, s.PerfectDodgeFlashColor);
            Pulse(s.PerfectDodge);
        }

        void Blocked(in PlayerEvent e, PlayerFeedbackSettings s)
        {
            FireVfx.HitSpark(ContactPoint(e.Direction), s.BlockSparkColor);
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
            Pulse(s.Hurt);
        }

        // An enemy deflected our strike (a stagger follows, drawn by Tick).
        void GotParried(PlayerFeedbackSettings s)
        {
            if (rig != null) FireVfx.HitSpark(rig.GetAnchor(strikeLimb).position, s.DeflectSparkColor);
            Pulse(s.GotParried);
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

        void StartStrikeTrail(Limb limb, float duration, PlayerFeedbackSettings s)
        {
            StopStrikeTrail();
            if (!s.StrikeTrails || !(duration > 0f) || rig == null) return;
            if (limb == Limb.BothFists)
            {
                // Both palms: a trail on each hand.
                strikeTrail = FireVfx.Trail(rig.GetAnchor(Limb.RightFist), duration);
                FireVfx.Trail(rig.GetAnchor(Limb.LeftFist), duration);
                return;
            }
            strikeTrail = FireVfx.Trail(rig.GetAnchor(limb), duration);
        }

        void StopStrikeTrail()
        {
            strikeTrail.Stop();
            strikeTrail = FireVfxHandle.None;
        }

        void StopLimbFlame()
        {
            limbFlame.Stop();
            limbFlame = FireVfxHandle.None;
        }

        void StopWhip()
        {
            whip.Stop();
            whip = FireVfxHandle.None;
        }

        void StopJets()
        {
            leftJet.Stop();
            rightJet.Stop();
            leftJet = FireVfxHandle.None;
            rightJet = FireVfxHandle.None;
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
            StopLimbFlame();
            StopWhip();
            StopJets();
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

        // The single limb that winds up a charged move: its foot, or the striking fist.
        static Limb ChamberLimb(Limb limb)
        {
            if (limb == Limb.RightFoot || limb == Limb.LeftFoot || limb == Limb.LeftFist) return limb;
            return Limb.RightFist;
        }
    }
}
