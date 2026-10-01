using UnityEngine;
using VaatusRevenge.Core;

namespace VaatusRevenge
{
    // Everything the player sees, hears and feels that isn't a rule or a pose: element effects, flashes, screen shake,
    // gamepad rumble, the rhythm's sounds and the sprint FOV kick. Nothing here changes the fight. (The body's
    // martial-arts motion comes from the procedural animator: PlayerController feeds it through PlayerAnimationFeed.)
    //
    // PlayerController owns one. It passes in the combat model's events (moments: a strike goes active, a guard
    // goes up, a hit lands) and calls Tick every frame for looks that simply follow a state (i-frame shimmer,
    // stagger dimming, charge glow, death). Following the state for those means a look can never get stuck on,
    // whatever order things happened in. All the numbers come from PlayerFeedbackSettings, read live.
    //
    // Every move is drawn in its own element (the event's Element: a Fire Blast in flight after a switch is still fire):
    // ElementMoveEffects picks the shape from the move's EffectKey (data), ElementVfx draws it as fire, water, earth or
    // air, sized from the move's Range and ArcDegrees, so what you see is what can hit. Fire looks exactly as it did
    // before Build 05. Build 05 adds the rhythm (a ring and a chime on the beat), the element switch (the new element
    // washes over the body), the MIX accent, slip-in afterimages and the danger sense's rumble ticks.
    public sealed class PlayerFeedback
    {
        readonly Transform body;
        readonly HumanoidBody rig;
        readonly Combatant fighter;
        readonly PlayerRumble rumble = new PlayerRumble();
        readonly ElementMoveEffects moveEffects = new ElementMoveEffects();
        RhythmAudio rhythmAudio;

        FireVfxHandle chargeGlow;
        FireVfxHandle strikeTrail;
        FireVfxHandle limbFlame;
        FireVfxHandle leftFootTrail;
        FireVfxHandle rightFootTrail;
        FireVfxHandle leftJet;
        FireVfxHandle rightJet;
        FireVfxHandle afterimage;
        FireVfxHandle leftAura;
        FireVfxHandle rightAura;
        Limb strikeLimb = Limb.RightFist;  // the current move's striking limb
        ElementId attackElement = ElementId.Fire;   // the current move's element
        int subHitsSeen;                  // active windows opened so far by the current move (multi-hit moves)
        int mixAccentInstance;            // the switch strike's MoveInstanceId: its first landed hit gets the MIX accent
        bool mixAccentAirborne;           // ...made off the ground: Earth's accent is then dust, never rock (J3-06)
        const float StationaryDodgeDistance = 0.3f;   // metres: a dodge travelling less than this draws no push or trails
        int finisherSoundFrame = -1;      // a perfect string and a MIX finisher in the same frame chime once
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

        // The rhythm's sounds (created on first use in play mode; null in edit mode). The tutorial chimes with it too.
        public RhythmAudio Audio
        {
            get
            {
                if (rhythmAudio == null && Application.isPlaying && body != null) rhythmAudio = new RhythmAudio(body.gameObject);
                return rhythmAudio;
            }
        }

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
                case PlayerEventType.AttackActiveEnd: ActiveEnded(in e); break;
                case PlayerEventType.ProjectileLaunched:
                    ElementVfx.Muzzle(ElementOf(in e, model), e.Origin.ToUnity(), e.Direction.ToUnity());
                    break;
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
                    StopAfterimage();
                    break;
                case PlayerEventType.PerfectDodge: PerfectDodge(ElementOf(in e, model), e.InAir, s); break;
                case PlayerEventType.Jumped: ElementVfx.Burst(model.ActiveElement, FootPosition(s), Vector3.down, s.JumpBurstScale); break;
                case PlayerEventType.Landed:
                    if (!plungeLandedThisFrame && e.Amount >= s.HardLandingSpeed) Pulse(s.HardLanding);
                    break;
                case PlayerEventType.Blocked: Blocked(in e, s); break;
                case PlayerEventType.GuardBroken: GuardBroken(in e, s); break;
                case PlayerEventType.Deflected: Deflected(in e, ElementOf(in e, model), s); break;
                case PlayerEventType.HealApplied: HealApplied(s); break;
                case PlayerEventType.HealFailed: Flash(s.EmptyFlaskFlashColor, s.EmptyFlaskFlashTime); break;
                case PlayerEventType.Damaged: Damaged(in e, s); break;
                case PlayerEventType.Parried: GotParried(s); break;
                case PlayerEventType.Died:
                    // A death reported after a respawn already happened is old news.
                    if (model.State == PlayerState.Dead) OnKilled();
                    break;
                case PlayerEventType.Respawned: ResetAll(); break;
                // Build 05: rhythm, switching, MIX, danger sense.
                case PlayerEventType.BeatJudged: BeatJudged(in e, s); break;
                case PlayerEventType.ComboHit: ComboHit(in e, model, s); break;
                case PlayerEventType.PerfectString:
                    Flash(s.PerfectStringFlashColor, s.PerfectStringFlashTime);
                    PlayFinisherSound(s);
                    break;
                case PlayerEventType.MixFinisher: MixFinisher(in e, s); break;
                case PlayerEventType.HealedOnHit:
                    // Water's healing: a small blue-white mote at the chest and a soft flash.
                    ElementVfx.BeatAccent(ChestPosition(), s.BeatAccentScale * 0.6f, HealMoteColor, s.BeatAccentTime);
                    Flash(HealMoteColor, s.SwitchFlashTime * 0.6f);
                    break;
                case PlayerEventType.ElementSwitched: ElementSwitched(in e, s); break;
                case PlayerEventType.DangerWarning: Pulse(s.DangerTick); break;
                case PlayerEventType.DangerNow: Pulse(s.DangerNowTick); break;
                case PlayerEventType.DodgeChainLimited:
                    // Three dodges in a row, then a short breath (J4-S01): the refused press is felt, not silently eaten.
                    Flash(s.DodgeChainLimitedFlashColor, s.DodgeChainLimitedFlashTime);
                    Pulse(s.DodgeChainLimitedPulse);
                    break;
                // Stagger, sprint (FOV), i-frames and charge level are drawn from the model's state in Tick.
            }
        }

        // Our attack touched somebody: a spark where it connected, coloured by what happened (a clean hit in the
        // element's colour, with a little of the element thrown off).
        public void OnHitReport(in HitReport report, PlayerFeedbackSettings s)
        {
            OnHitReport(in report, ElementId.Fire, s);
        }

        public void OnHitReport(in HitReport report, ElementId element, PlayerFeedbackSettings s)
        {
            OnHitReport(in report, element, false, s);
        }

        // attackerAirborne: the hit came from the air (the air string): Earth's spark is then dust, never rock (canon).
        public void OnHitReport(in HitReport report, ElementId element, bool attackerAirborne, PlayerFeedbackSettings s)
        {
            switch (report.Result.Outcome)
            {
                case HitOutcome.Hit:
                    ElementVfx.HitSpark(element, report.Point, HitSparkColor(element, s), attackerAirborne);
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
            attackElement = ElementId.Fire;
            mixAccentInstance = 0;
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
            if (rhythmAudio != null) rhythmAudio.Stop();
        }

        // The player is being destroyed: free the sounds made in code.
        public void Dispose()
        {
            Shutdown();
            if (rhythmAudio == null) return;
            rhythmAudio.Dispose();
            rhythmAudio = null;
        }

        // ---------------------------------------------------------------- attacks

        void AttackStarted(in PlayerEvent e, PlayerCombatModel model, PlayerFeedbackSettings s)
        {
            MoveData move = e.Move;
            if (move == null) return;
            attackElement = ElementOf(in e, model);
            subHitsSeen = 0;
            if (e.IsSwitchStrike)
            {
                mixAccentInstance = e.MoveInstanceId;
                mixAccentAirborne = ElementFxRules.IsAirborne(in e) || (model != null && !model.IsGrounded);
            }
            if (e.IsCounter) Flash(s.CounterFlashColor, s.CounterFlashTime);
            strikeLimb = move.Limb == Limb.Weapon ? Limb.RightFist : move.Limb;
            PlayerStrikePoses p = s.Poses;
            if (e.AttackKind == PlayerAttackKind.ZipStrike) ZipDashStarted(move, ElementFxRules.IsAirborne(in e), s);
            if (e.AttackKind == PlayerAttackKind.DodgeStrike) DodgeStrikeDashStarted(move, ElementFxRules.IsAirborne(in e), s);
            if (move.LaunchesProjectile && move.Projectile != null && ElementFxRules.StoneFromFloor(attackElement)
                && !ElementFxRules.IsAirborne(in e) && (model == null || model.IsGrounded))
            {
                // Earth's boulder (Boulder Toss, Boulder Hurl) is drawn up out of the ground over the wind-up and is thrown
                // from where it rose (J4-03): the same launch point and size as the flying boulder (GetStrikeOrigin).
                Vector3 launch = body.position + Vector3.up * move.OriginHeight + body.forward * move.OriginForward;
                float size = move.Projectile.Radius * 2f * (move.Projectile.VisualScale > 0f ? move.Projectile.VisualScale : 1f);
                ElementVfx.RaiseStone(attackElement, launch, size, move.Startup / Mathf.Max(0.05f, e.PlaybackRate));
            }
            if (rig == null || p == null || !p.LimbFlames || e.AttackKind == PlayerAttackKind.Plunge) return;
            // The striking fist or foot takes on the element as it winds up and keeps it until the strike is over. Moves with
            // a big effect of their own keep it shorter.
            float duration = move.Startup + move.Active;
            if (ElementMoveEffects.HasBigEffect(move)) duration *= p.BigEffectLimbFlameShare;
            StopLimbFlame();
            if (!(duration > 0f)) return;
            // Earth in the air draws dust off the limb, never gravel (canon: no stone without ground).
            limbFlame = ElementMoveEffects.IsAirborneEarth(in e, attackElement)
                ? ElementVfx.LimbDust(attackElement, rig.GetAnchor(strikeLimb), duration)
                : ElementVfx.LimbAura(attackElement, rig.GetAnchor(strikeLimb), duration);
        }

        // The strike can hit now: the move's effect in its element, and a trail on the striking limb through the active
        // frames (a flurry's later sub-hits only burst: the trail already runs through all of them).
        void ActiveStarted(in PlayerEvent e, PlayerFeedbackSettings s)
        {
            MoveData move = e.Move;
            if (move == null) return;
            bool first = ElementMoveEffects.IsFirstSubHit(in e);
            subHitsSeen = first ? 1 : subHitsSeen + 1;
            ElementId element = e.Element != ElementId.None ? e.Element : attackElement;
            Transform limb = rig != null ? rig.GetAnchor(strikeLimb) : body;
            float burst = e.AttackKind == PlayerAttackKind.DodgeStrike ? Mathf.Max(0f, s.DodgeStrikeBurstMultiplier) : 1f;
            if (moveEffects.ActiveStarted(in e, element, body, limb, rig, s, burst)) Pulse(s.StompShake);
            if (!first) return;
            StartStrikeTrail(strikeLimb, move.Active, element, s);
            if (e.ChargeTier == ChargeTier.FaJin) Pulse(s.FaJinRelease);
        }

        // A single-hit move's trail stops with its active frames; a flurry's runs until its last sub-hit is over.
        void ActiveEnded(in PlayerEvent e)
        {
            MoveData move = e.Move;
            if (move != null && move.HitCount > 1 && subHitsSeen < move.HitCount) return;
            StopStrikeTrail();
        }

        void PlungeImpact(in PlayerEvent e, PlayerFeedbackSettings s)
        {
            plungeLandedThisFrame = true;
            StopStrikeTrail();
            ElementId element = e.Element != ElementId.None ? e.Element : attackElement;
            Vector3 feet = e.Origin.ToUnity();
            ElementVfx.Ring(element, feet, e.Radius);
            if (rig != null) ElementVfx.Burst(element, rig.GetAnchor(Limb.RightFoot).position, Vector3.down, Mathf.Max(0.5f, e.Radius * 0.3f));
            if (s.PlungeExplosionShare > 0f) ElementVfx.Explosion(element, feet + Vector3.up * s.EffectFootHeight, e.Radius * s.PlungeExplosionShare);
            Pulse(s.PlungeLanding);
            if (element == ElementId.Earth) Pulse(s.StompShake);   // an earthquake drop shakes the ground
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
                        Transform anchor = rig.GetAnchor(ChamberLimb(heavy != null ? heavy.Limb : Limb.RightFist));
                        chargeGlow = ElementVfx.ChargeGlow(model.ActiveElement, anchor);
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

        // ---------------------------------------------------------------- rhythm, switching, MIX

        // An on-beat press: the chime and a tick you can feel (the HUD's beat ring bursts too).
        void BeatJudged(in PlayerEvent e, PlayerFeedbackSettings s)
        {
            if (e.Grade != BeatGrade.OnBeat) return;
            PlaySound(SoundKind.Chime, s);
            Pulse(s.OnBeatTick);
        }

        // Every clean hit of the combo ticks (the metronome); a hit from an on-beat press flashes a white-gold ring; the
        // first hit of a switch strike bursts bigger in the new element (the MIX accent).
        void ComboHit(in PlayerEvent e, PlayerCombatModel model, PlayerFeedbackSettings s)
        {
            PlaySound(SoundKind.Tick, s);
            Vector3 at = e.Origin.ToUnity();
            ElementId element = ElementOf(in e, model);
            if (e.Grade == BeatGrade.OnBeat || e.Grade == BeatGrade.Auto)
            {
                ElementVfx.BeatAccent(at, s.BeatAccentScale, s.BeatAccentColor, s.BeatAccentTime);
                Flash(s.OnBeatFlashColor, s.OnBeatFlashTime);
            }
            if (mixAccentInstance == 0 || e.MoveInstanceId != mixAccentInstance) return;
            mixAccentInstance = 0;
            float scale = Mathf.Max(0f, s.MixAccentScale);
            // An airborne switch strike into Earth (the air string) bursts dust, never rock (spec 8.1 item 2, J3-06).
            if (ElementFxRules.MixAccentIsDust(element, mixAccentAirborne)) ElementVfx.Dust(element, at, body.forward, scale * 0.6f);
            else ElementVfx.Burst(element, at, body.forward, scale * 0.6f);
            ElementVfx.BeatAccent(at, s.BeatAccentScale * scale, Bright(ElementVfx.SwitchFlashColor(element)), s.BeatAccentTime * scale);
        }

        static readonly Color HealMoteColor = new Color(0.75f, 0.92f, 1f, 1f);

        void MixFinisher(in PlayerEvent e, PlayerFeedbackSettings s)
        {
            PlayFinisherSound(s);
            Pulse(s.MixFinisherPulse);
            ElementVfx.BeatAccent(ChestPosition(), s.BeatAccentScale * (1f + 0.25f * e.Count), Bright(ElementVfx.SwitchFlashColor(e.Element)),
                s.BeatAccentTime * 2f);
        }

        // The new element washes over the body from a ring at the feet and clings to both fists a moment; the body flashes
        // its colour. (The stance change is the animator's; the HUD wheel flashes on the same frame.)
        void ElementSwitched(in PlayerEvent e, PlayerFeedbackSettings s)
        {
            ElementId element = e.Element;
            // Off the ground (mid air string) Earth washes over the body as dust, never rock (ElementFxRules.DustOnly).
            bool dustOnly = ElementFxRules.DustOnly(element, e.InAir);
            ElementVfx.Switch(e.PreviousElement, element, ChestPosition(), body.position, e.InAir);
            StopAuras();
            if (rig != null && s.SwitchAuraTime > 0f)
            {
                leftAura = dustOnly ? ElementVfx.LimbDust(element, rig.GetAnchor(Limb.LeftFist), s.SwitchAuraTime)
                                    : ElementVfx.LimbAura(element, rig.GetAnchor(Limb.LeftFist), s.SwitchAuraTime);
                rightAura = dustOnly ? ElementVfx.LimbDust(element, rig.GetAnchor(Limb.RightFist), s.SwitchAuraTime)
                                     : ElementVfx.LimbAura(element, rig.GetAnchor(Limb.RightFist), s.SwitchAuraTime);
            }
            Flash(ElementVfx.SwitchFlashColor(element), s.SwitchFlashTime);   // the element's colour, every element equally bright (J3-S09)
            PlaySound(SoundKind.Switch, s);
            Pulse(s.SwitchPulse);
        }

        // ---------------------------------------------------------------- defence and movement

        void DodgeStarted(in PlayerEvent e, PlayerCombatModel model, PlayerFeedbackSettings s)
        {
            ElementId element = ElementOf(in e, model);
            Vector3 direction = e.Direction.ToUnity();
            DodgeProfile dodge = model.MoveSet != null ? model.MoveSet.Dodge : null;
            float duration = dodge != null ? dodge.Duration : 0f;
            StopDodgeTrails();
            StopAfterimage();
            // A slip-in from contact range has no room to travel (Amount ~ 0): it's a sway in place, so no push from the
            // feet, no trails and no afterimages streaking the wrong way (J3-S04).
            if (!e.InAir && e.Amount < StationaryDodgeDistance) return;
            // A push from the feet the other way (Flame Step's jets of fire; a splash, a scuff of dust, a gust). An Earth air
            // dash pushes dust, not rock (ElementFxRules.DustOnly).
            ElementVfx.BurstOrDust(element, FootPosition(s), -direction, s.DodgeBurstScale, e.InAir);
            if (rig == null) return;
            if (e.InAir)
            {
                AerialSettings aerial = model.MoveSet != null ? model.MoveSet.Aerial : null;
                StartJets(-direction, aerial != null ? aerial.AirDashDuration : duration, element, s);
                return;
            }
            if (s.DodgeTrails)
            {
                leftFootTrail = ElementVfx.Trail(element, rig.GetAnchor(Limb.LeftFoot), duration);
                rightFootTrail = ElementVfx.Trail(element, rig.GetAnchor(Limb.RightFoot), duration);
            }
            // Slipping in toward the enemy leaves copies of the body behind: you closed the gap faster than the eye.
            if (e.DodgeKind == DodgeKind.SlipIn && s.SlipInAfterimages && fighter != null && duration > 0f)
                afterimage = ElementVfx.Afterimage(element, fighter.AimPoint, duration);
        }

        // The zip strike's dash: a push from both feet carries you across the gap for the whole dash.
        void ZipDashStarted(MoveData move, bool inAir, PlayerFeedbackSettings s)
        {
            ElementVfx.BurstOrDust(attackElement, FootPosition(s), -body.forward, s.DodgeBurstScale, inAir);
            StopDodgeTrails();
            StartJets(-body.forward, move.Startup + move.Active, attackElement, s);
        }

        // The dodge strike's dash back in (J4-02): a push off the floor and the element trailing from both feet for the run
        // in (Water a surf trail, Earth a scuff of dust, Air a gust, Fire its Flame Step flames), so the approach reads as
        // bending carrying the body in, not a crouched pose sliding along. In the air it's the air dash's jets.
        void DodgeStrikeDashStarted(MoveData move, bool inAir, PlayerFeedbackSettings s)
        {
            float duration = Mathf.Max(0f, move.Startup);
            if (!(duration > 0f)) return;
            ElementVfx.BurstOrDust(attackElement, FootPosition(s), -body.forward, s.DodgeBurstScale, inAir);
            StopDodgeTrails();
            if (rig == null) return;
            if (inAir)
            {
                StartJets(-body.forward, duration, attackElement, s);
                return;
            }
            if (!s.DodgeTrails) return;
            leftFootTrail = ElementVfx.Trail(attackElement, rig.GetAnchor(Limb.LeftFoot), duration);
            rightFootTrail = ElementVfx.Trail(attackElement, rig.GetAnchor(Limb.RightFoot), duration);
        }

        void StartJets(Vector3 direction, float duration, ElementId element, PlayerFeedbackSettings s)
        {
            StopJets();
            PlayerStrikePoses p = s.Poses;
            if (rig == null || !(duration > 0f) || (p != null && !p.FootJets)) return;
            leftJet = ElementVfx.FootJet(element, rig.GetAnchor(Limb.LeftFoot), direction, duration);
            rightJet = ElementVfx.FootJet(element, rig.GetAnchor(Limb.RightFoot), direction, duration);
        }

        void PerfectDodge(ElementId element, bool inAir, PlayerFeedbackSettings s)
        {
            Flash(s.PerfectDodgeFlashColor, s.PerfectDodgeFlashTime);
            Vector3 chest = ChestPosition();
            ElementVfx.BurstOrDust(element, chest, body.forward, s.PerfectDodgeBurstScale, inAir);
            ElementVfx.HitSpark(element, chest, s.PerfectDodgeFlashColor, inAir);
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

        void Deflected(in PlayerEvent e, ElementId element, PlayerFeedbackSettings s)
        {
            Vector3 toAttacker = ToAttacker(e.Direction);
            Vector3 contact = ChestPosition() + toAttacker * BodyRadius;
            ElementVfx.HitSpark(element, contact, s.DeflectSparkColor);
            ElementVfx.Burst(element, contact, toAttacker, s.DeflectBurstScale);
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

        enum SoundKind { Chime, Tick, Finisher, Switch }

        void PlaySound(SoundKind kind, PlayerFeedbackSettings s)
        {
            if (!s.RhythmSounds) return;
            RhythmAudio audio = Audio;
            if (audio == null) return;
            switch (kind)
            {
                case SoundKind.Chime: audio.PlayChime(s.ChimeVolume); break;
                case SoundKind.Tick: audio.PlayTick(s.HitTickVolume); break;
                case SoundKind.Finisher: audio.PlayFinisher(s.FinisherVolume); break;
                default: audio.PlaySwitch(s.SwitchVolume); break;
            }
        }

        void PlayFinisherSound(PlayerFeedbackSettings s)
        {
            int frame = Time.frameCount;
            if (frame == finisherSoundFrame) return;
            finisherSoundFrame = frame;
            PlaySound(SoundKind.Finisher, s);
        }

        void Pulse(FeedbackPulse pulse)
        {
            if (pulse == null) return;
            if (pulse.ShakeAmplitude > 0f && pulse.ShakeDuration > 0f) CameraShake.Add(pulse.ShakeAmplitude, pulse.ShakeDuration);
            if (RumbleAllowed) rumble.Play(pulse);
        }

        void Flash(Color color, float duration)
        {
            if (rig != null && duration > 0f) rig.Flash(color, duration);
        }

        // The move's element from the event, else the element in hand (events from before Build 05 carry none).
        static ElementId ElementOf(in PlayerEvent e, PlayerCombatModel model)
        {
            if (e.Element != ElementId.None) return e.Element;
            return model != null ? model.ActiveElement : ElementId.Fire;
        }

        static Color HitSparkColor(ElementId element, PlayerFeedbackSettings s)
        {
            return element == ElementId.Fire || element == ElementId.None ? s.HitSparkColor : ElementVfx.StyleOf(element).HitSparkColor;
        }

        // A colour lifted for bloom (accent rings).
        static Color Bright(Color color)
        {
            return new Color(color.r * 2.2f, color.g * 2.2f, color.b * 2.2f, 1f);
        }

        void StartStrikeTrail(Limb limb, float duration, ElementId element, PlayerFeedbackSettings s)
        {
            StopStrikeTrail();
            if (!s.StrikeTrails || !(duration > 0f) || rig == null) return;
            if (limb == Limb.BothFists)
            {
                // Both palms: a trail on each hand.
                strikeTrail = ElementVfx.Trail(element, rig.GetAnchor(Limb.RightFist), duration);
                ElementVfx.Trail(element, rig.GetAnchor(Limb.LeftFist), duration);
                return;
            }
            strikeTrail = ElementVfx.Trail(element, rig.GetAnchor(limb), duration);
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

        void StopAfterimage()
        {
            afterimage.Stop();
            afterimage = FireVfxHandle.None;
        }

        void StopAuras()
        {
            leftAura.Stop();
            rightAura.Stop();
            leftAura = FireVfxHandle.None;
            rightAura = FireVfxHandle.None;
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
            moveEffects.Stop();
            StopJets();
            StopDodgeTrails();
            StopAfterimage();
            StopAuras();
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
