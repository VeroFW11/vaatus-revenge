using System;
using System.Collections.Generic;
using System.Globalization;
using System.Numerics;

namespace VaatusRevenge.Core
{
    // The player's whole rulebook: movement, attacks, dodging, guarding, healing, stamina, poise,
    // Momentum, damage and death. Pure C#, deterministic (same inputs = same result), so the offline
    // harness can play it with bot inputs and the Unity PlayerController is only an adapter.
    //
    // Split over four files: this one (state, public API, the order of one frame), .Actions (attacks,
    // charge, plunge, skill, heal, stagger), .Defense (dodge, guard, deflect, incoming hits) and
    // .Locomotion (walking, sprinting, jumping, facing and velocity).
    //
    // HOW TO DRIVE IT (Unity side, once per Update):
    //   1. result = model.Tick(Time.deltaTime, input, world)   (dt <= 0 is safe: nothing advances)
    //   2. characterController.Move(result.Velocity * Time.deltaTime); turn the body to result.FacingYaw
    //   3. handle result.Events in order (hit queries, projectiles, effects...). They're valid until the next Tick
    //   4. while model.IsAttackActive, run the melee arc query each frame (BuildCurrentDamage gives the DamageInfo)
    //   5. incoming hits: model.ReceiveHit(hit, transform.forward); your hits: model.OnAttackLanded(result, attackId);
    //      a HitOutcome.Parried from a target: model.OnParried(). Events these raise arrive with the next Tick.
    //
    // Tuning: keeps references to the PlayerTuning / ElementMoveSet it's given and reads them every frame,
    // so Inspector edits apply live. It never writes to them (they may be shared ScriptableObject data).
    public sealed partial class PlayerCombatModel
    {
        const float Epsilon = 1e-4f;

        // Used when a move set has no settings object for something (only possible for code-built data).
        static readonly DodgeProfile FallbackDodge = new DodgeProfile();
        static readonly GuardSettings FallbackGuard = new GuardSettings();
        static readonly ChargeSettings FallbackCharge = new ChargeSettings();
        static readonly PlungeSettings FallbackPlunge = new PlungeSettings();
        static readonly MomentumSettings FallbackMomentum = new MomentumSettings { Enabled = false };

        PlayerTuning tuning;
        ElementMoveSet moveSet;

        readonly StaminaMeter stamina = new StaminaMeter();
        readonly PoiseMeter poise = new PoiseMeter();
        readonly MomentumMeter momentum = new MomentumMeter();
        readonly InputBuffer buffer = new InputBuffer();
        readonly TapHoldResolver dodgeButton = new TapHoldResolver();
        readonly ActionTimeline action = new ActionTimeline();
        readonly List<PlayerEvent> events = new List<PlayerEvent>(32);
        readonly List<PlayerEvent> pendingEvents = new List<PlayerEvent>(16);
        readonly AttackRecord[] recentAttacks = new AttackRecord[8];
        readonly int[] recentHitsTaken = new int[8];
        int recentAttackCursor;
        int recentHitCursor;

        struct AttackRecord
        {
            public int AttackId;
            public float MomentumGain;
            public bool Rewarded;
        }

        bool insideTick;
        double clock;                 // seconds of game time this model has lived (double: stays precise for hours)
        PlayerState state;
        float health;
        int healCharges;

        // Buttons: last frame's held state, so a press is never missed even if a Pressed flag was.
        bool lightHeld, heavyHeld, jumpHeld, guardHeld, skillHeld, healHeld;
        bool guardPressPending;       // a guard press waiting to be handled this frame
        Vector2 moveStick;

        // Current action (attack, charge, plunge, dodge, heal, stagger).
        MoveData currentMove;
        PlayerAttackKind attackKind;
        int currentAttackId;
        int lastAttackId;
        int chainIndex = -1;          // light chain move running now (-1 = none)
        double chainGraceUntil = double.NegativeInfinity;   // see UpdateAttack: chain survives a short recovery
        int chainGraceNext;
        bool activeOpen;
        bool isCounter;
        bool releasingCharge;
        ChargeTier chargeTier;
        float chargeTime;
        bool sweetSpotAnnounced;
        bool plungeFalling;
        bool plungeLanded;
        float landingTime;            // action time when the plunge landed
        float staggerDuration;
        bool healApplied;
        double counterWindowUntil = double.NegativeInfinity;

        public PlayerCombatModel(PlayerTuning tuning, ElementMoveSet moveSet, int ownerId = 0, float facingYaw = 0f)
        {
            this.tuning = tuning ?? PlayerTuning.CreateFluid();
            this.moveSet = moveSet ?? ElementMoveSet.CreateFireFluid();
            OwnerId = ownerId;
            ResetState(facingYaw);
        }

        // ---------------------------------------------------------------- read-only state (HUD, harness)

        public PlayerTuning Tuning => tuning;
        public ElementMoveSet MoveSet => moveSet;
        public int OwnerId { get; set; }             // CombatIds id of the player's Combatant, used as DamageInfo.SourceId
        public double Clock => clock;
        public PlayerState State => state;
        public bool IsAlive => state != PlayerState.Dead;

        public float Health => health;
        public float MaxHealth => Math.Max(1f, tuning.MaxHealth);
        public float Stamina => stamina.Current;
        public float MaxStamina => Math.Max(0f, tuning.MaxStamina);
        public float Poise => poise.Current;
        public float Momentum => momentum.Current;
        public float MaxMomentum => MomentumRules.Enabled ? MomentumRules.Max : 0f;
        public float MomentumMultiplier => momentum.DamageMultiplier(MomentumRules);
        public int HealCharges => healCharges;
        public int MaxHealCharges => Math.Max(0, tuning.HealCharges);

        public MoveData CurrentMove => IsInMove ? currentMove : null;
        public PlayerAttackKind CurrentAttackKind => IsInMove ? attackKind : PlayerAttackKind.None;
        public int ChainIndex => state == PlayerState.Attacking ? chainIndex : -1;
        public int CurrentAttackId => IsInMove ? currentAttackId : 0;
        public bool IsAttackActive => state == PlayerState.Attacking && activeOpen;
        public int ActiveAttackId => IsAttackActive ? currentAttackId : 0;
        public ChargeTier CurrentChargeTier => IsInMove ? chargeTier : ChargeTier.None;
        public bool IsCounterAttack => IsInMove && isCounter;
        public float ActionTime => action.IsRunning ? action.Time : 0f;
        public AttackPhase Phase => ComputePhase(out _);
        public float PhaseTime
        {
            get
            {
                ComputePhase(out float phaseTime);
                return phaseTime;
            }
        }

        public float ChargeTime => state == PlayerState.Charging ? chargeTime : 0f;
        public float ChargeLevel => state == PlayerState.Charging && Charge.MaxChargeTime > 0f
            ? Angles.Clamp(chargeTime / Charge.MaxChargeTime, 0f, 1f) : 0f;
        public bool InSweetSpot => state == PlayerState.Charging && chargeTime >= Charge.SweetSpotStart && chargeTime <= Charge.SweetSpotEnd;

        public bool IsGuarding => state == PlayerState.Guarding;
        public bool IsSprinting => state == PlayerState.Sprinting;
        public bool IsGrounded => grounded;
        public float FacingYaw => facingYaw;
        public Vector3 Forward => Directions.FromYaw(facingYaw);
        public Vector3 Velocity => lastVelocity;
        public float SprintTime => state == PlayerState.Sprinting ? sprintTime : 0f;
        public bool IsCounterWindowOpen => clock <= counterWindowUntil;
        public float CounterWindowRemaining => IsCounterWindowOpen ? (float)(counterWindowUntil - clock) : 0f;
        public float StaggerRemaining => state == PlayerState.Staggered ? Math.Max(0f, staggerDuration - action.Time) : 0f;

        public PlayerCommand BufferedCommand => buffer.Command;
        public bool BufferedCommandQueued => buffer.Locked;    // accepted as the next light chain move
        public float BufferedCommandAge => buffer.Age(clock);

        bool IsInMove => state == PlayerState.Attacking || state == PlayerState.Charging || state == PlayerState.Plunging;
        int ChainLength => moveSet.LightChain == null ? 0 : moveSet.LightChain.Length;
        DodgeProfile Dodge => moveSet.Dodge ?? FallbackDodge;
        GuardSettings Guard => moveSet.Guard ?? FallbackGuard;
        ChargeSettings Charge => moveSet.Charge ?? FallbackCharge;
        PlungeSettings Plunge => moveSet.Plunge ?? FallbackPlunge;
        MomentumSettings MomentumRules => moveSet.Momentum ?? FallbackMomentum;

        // ---------------------------------------------------------------- one frame

        public PlayerTickResult Tick(float dt, in PlayerInputFrame input, in PlayerWorldState world)
        {
            events.Clear();
            insideTick = true;
            try
            {
                // Events raised between frames (ReceiveHit, OnParried...) are delivered first.
                events.AddRange(pendingEvents);
                pendingEvents.Clear();

                if (!(dt > 0f) || float.IsInfinity(dt))
                {
                    // Frozen frame (paused, or a full hitstop freeze): nothing advances, but presses are still
                    // remembered so a button mashed during a freeze-frame comes out right after it.
                    ReadInput(input, 0f);
                    return MakeResult();
                }

                clock += dt;
                ReadInput(input, dt);
                buffer.Expire(clock, tuning.InputBufferWindow);
                UpdateGrounding(dt, world);
                UpdateTimers(dt);
                AdvanceAction(dt, world);
                TryRunBufferedCommand(world);
                UpdateHeldStates(dt);
                ComputeMotion(dt, world);
                UpdateMomentum(dt, world);
                ClampToTuning();
                return MakeResult();
            }
            finally
            {
                insideTick = false;
            }
        }

        PlayerTickResult MakeResult()
        {
            return new PlayerTickResult
            {
                Velocity = lastVelocity,
                FacingYaw = facingYaw,
                Events = new EventList<PlayerEvent>(events)
            };
        }

        void ReadInput(in PlayerInputFrame input, float dt)
        {
            moveStick = input.Move;
            if (moveStick.LengthSquared() > 1f) moveStick = Vector2.Normalize(moveStick);

            bool lightPressed = Pressed(input.Light, ref lightHeld);
            bool heavyPressed = Pressed(input.Heavy, ref heavyHeld);
            bool jumpPressed = Pressed(input.Jump, ref jumpHeld);
            bool skillPressed = Pressed(input.Skill, ref skillHeld);
            bool healPressed = Pressed(input.Heal, ref healHeld);
            if (Pressed(input.Guard, ref guardHeld)) guardPressPending = true;

            bool dodgePressed = input.Dodge.Pressed || (input.Dodge.Held && !dodgeButton.IsHeld);
            bool dodgeTap = dodgeButton.Update(dodgePressed, input.Dodge.Released, input.Dodge.Held, dt,
                tuning.DodgeTrigger, tuning.TapHoldThreshold);

            if (state == PlayerState.Dead) return;
            // The buffer keeps only the latest press. Pushed in this order so that if two buttons go down in
            // the same frame, the defensive one wins.
            if (healPressed) buffer.Push(PlayerCommand.Heal, clock);
            if (skillPressed) buffer.Push(PlayerCommand.Skill, clock);
            if (heavyPressed) buffer.Push(PlayerCommand.Heavy, clock);
            if (lightPressed) buffer.Push(PlayerCommand.Light, clock);
            if (jumpPressed) buffer.Push(PlayerCommand.Jump, clock);
            if (dodgeTap) buffer.Push(PlayerCommand.Dodge, clock);
        }

        // A press is the Pressed flag, or "held now but not last frame" (covers a skipped frame).
        static bool Pressed(ButtonState button, ref bool wasHeld)
        {
            bool pressed = button.Pressed || (button.Held && !wasHeld);
            wasHeld = button.Held;
            return pressed;
        }

        void Emit(PlayerEvent evt)
        {
            if (insideTick) events.Add(evt);
            else pendingEvents.Add(evt);
        }

        void Emit(PlayerEventType type)
        {
            Emit(new PlayerEvent { Type = type });
        }

        void EmitMoveEvent(PlayerEventType type)
        {
            Emit(new PlayerEvent
            {
                Type = type, Move = currentMove, AttackKind = attackKind, AttackId = currentAttackId,
                ChargeTier = chargeTier, IsCounter = isCounter
            });
        }

        void UpdateTimers(float dt)
        {
            float regen = tuning.StaminaRegen * (state == PlayerState.Guarding ? tuning.GuardRegenMultiplier : 1f);
            stamina.Tick(dt, MaxStamina, regen);
            poise.Tick(dt, tuning.MaxPoise, tuning.PoiseRegenDelay, tuning.PoiseRegenRate);
            UpdateDeflectWindow();
        }

        // Live tuning safety: if someone drags a maximum below the current value mid-fight, follow it.
        void ClampToTuning()
        {
            if (state != PlayerState.Dead && health > MaxHealth) health = MaxHealth;
            if (stamina.Current > MaxStamina) stamina.Set(stamina.Current, MaxStamina);
            if (healCharges > MaxHealCharges) healCharges = MaxHealCharges;
        }

        // ---------------------------------------------------------------- feedback from the hit system

        // Our attack connected. Only clean hits build Momentum, once per attack (a wide kick hitting two
        // enemies still counts once). Use the AttackId from the event for projectiles that land later.
        public void OnAttackLanded(in HitResult result, int attackId)
        {
            if (result.Outcome != HitOutcome.Hit || attackId == 0) return;
            for (int i = 0; i < recentAttacks.Length; i++)
            {
                if (recentAttacks[i].AttackId != attackId) continue;
                if (recentAttacks[i].Rewarded) return;
                recentAttacks[i].Rewarded = true;
                momentum.Gain(recentAttacks[i].MomentumGain, MomentumRules);
                return;
            }
        }

        // Same, for the attack started most recently.
        public void OnAttackLanded(in HitResult result)
        {
            OnAttackLanded(result, lastAttackId);
        }

        // An enemy deflected our attack: we stagger.
        public void OnParried()
        {
            if (state == PlayerState.Dead) return;
            Emit(PlayerEventType.Parried);
            Stagger(tuning.ParriedStaggerDuration);
        }

        void RememberAttack(int attackId, float momentumGain)
        {
            recentAttacks[recentAttackCursor] = new AttackRecord { AttackId = attackId, MomentumGain = momentumGain };
            recentAttackCursor = (recentAttackCursor + 1) % recentAttacks.Length;
            lastAttackId = attackId;
        }

        // ---------------------------------------------------------------- damage out

        // The DamageInfo for one of our attacks: the move's numbers x Momentum multiplier x counter bonus x
        // fa jin/charge multipliers. The hit system fills Direction and Point per target.
        public DamageInfo BuildDamage(MoveData move, int attackId, ChargeTier tier = ChargeTier.None, bool counter = false)
        {
            var info = new DamageInfo();
            if (move == null) return info;
            float damageScale = momentum.DamageMultiplier(MomentumRules);
            float poiseScale = 1f;
            float hitstop = move.Hitstop;
            if (tier == ChargeTier.FaJin)
            {
                damageScale *= Charge.FaJinDamageMultiplier;
                poiseScale *= Charge.FaJinPoiseMultiplier;
                hitstop += Charge.FaJinHitstopBonus;
            }
            else if (tier == ChargeTier.Charged)
            {
                damageScale *= Charge.ChargedDamageMultiplier;
            }
            if (counter) damageScale *= Dodge.CounterDamageMultiplier;

            info.Damage = move.Damage * damageScale;
            info.PoiseDamage = move.PoiseDamage * poiseScale;
            info.GuardStaminaDamage = move.GuardStaminaDamage;
            info.Knockback = move.Knockback;
            info.Hitstop = hitstop;
            info.Kind = move.Kind;
            info.SourceTeam = Team.Player;
            info.SourceId = OwnerId;
            info.AttackId = attackId;
            info.Parryable = move.Parryable;
            info.Unblockable = move.Unblockable;
            return info;
        }

        public DamageInfo BuildDamage(in PlayerEvent evt)
        {
            return BuildDamage(evt.Move, evt.AttackId, evt.ChargeTier, evt.IsCounter);
        }

        // For the per-frame melee query while IsAttackActive (or a plunge landing this frame).
        public DamageInfo BuildCurrentDamage()
        {
            return IsInMove ? BuildDamage(currentMove, currentAttackId, chargeTier, isCounter) : new DamageInfo();
        }

        // Where the current strike starts, for a player standing at 'feet' (follow the lunge each frame).
        public Vector3 GetStrikeOrigin(Vector3 feet)
        {
            MoveData move = currentMove;
            if (move == null) return feet;
            return feet + new Vector3(0f, move.OriginHeight, 0f) + Directions.FromYaw(facingYaw) * move.OriginForward;
        }

        // ---------------------------------------------------------------- presets, respawn

        // Swaps presets live (F5/F6). Any action in progress ends cleanly (active windows are closed with
        // events), health and stamina keep their share of the maximum, the input buffer is cleared.
        public void ApplyTuning(PlayerTuning newTuning, ElementMoveSet newMoveSet)
        {
            float healthShare = health / MaxHealth;
            float staminaShare = MaxStamina > 0f ? stamina.Current / MaxStamina : 1f;
            if (state != PlayerState.Dead && state != PlayerState.Staggered)
            {
                ExitAction(true);
                EnterFreeState();
            }
            tuning = newTuning ?? PlayerTuning.CreateFluid();
            moveSet = newMoveSet ?? ElementMoveSet.CreateFireFluid();
            if (state != PlayerState.Dead) health = Math.Max(1f, healthShare * MaxHealth);
            stamina.Set(staminaShare * MaxStamina, MaxStamina);
            poise.Reset(tuning.MaxPoise);
            healCharges = Math.Min(healCharges, MaxHealCharges);
            buffer.Clear();
        }

        public void Respawn()
        {
            Respawn(facingYaw);
        }

        // Full reset (death screen, R key). The Unity side teleports the body to the spawn point.
        public void Respawn(float newFacingYaw)
        {
            if (state != PlayerState.Dead) ExitAction(true);
            ResetState(newFacingYaw);
            Emit(PlayerEventType.Respawned);
        }

        void ResetState(float newFacingYaw)
        {
            action.Stop();
            state = PlayerState.Locomotion;
            health = MaxHealth;
            stamina.Reset(MaxStamina);
            poise.Reset(tuning.MaxPoise);
            momentum.Reset();
            healCharges = MaxHealCharges;
            buffer.Clear();
            dodgeButton.Reset();
            guardPressPending = false;
            currentMove = null;
            attackKind = PlayerAttackKind.None;
            currentAttackId = 0;
            chainIndex = -1;
            chainGraceUntil = double.NegativeInfinity;
            activeOpen = false;
            isCounter = false;
            releasingCharge = false;
            chargeTier = ChargeTier.None;
            chargeTime = 0f;
            counterWindowUntil = double.NegativeInfinity;
            ResetDefense();
            ResetLocomotion(newFacingYaw);
        }

        // ---------------------------------------------------------------- debug

        AttackPhase ComputePhase(out float phaseTime)
        {
            phaseTime = 0f;
            float t = action.Time;
            switch (state)
            {
                case PlayerState.Attacking:
                    if (currentMove == null) return AttackPhase.None;
                    if (t < currentMove.Startup)
                    {
                        phaseTime = t;
                        return AttackPhase.Startup;
                    }
                    if (t < currentMove.ActiveEnd)
                    {
                        phaseTime = t - currentMove.Startup;
                        return AttackPhase.Active;
                    }
                    phaseTime = t - currentMove.ActiveEnd;
                    return AttackPhase.Recovery;
                case PlayerState.Charging:
                    phaseTime = t;
                    return AttackPhase.Startup;
                case PlayerState.Plunging:
                    if (!plungeFalling)
                    {
                        phaseTime = t;
                        return AttackPhase.Startup;
                    }
                    if (!plungeLanded)
                    {
                        phaseTime = t - Plunge.HangTime;
                        return AttackPhase.Active;
                    }
                    phaseTime = t - landingTime;
                    return AttackPhase.Recovery;
                default:
                    return AttackPhase.None;
            }
        }

        // One line for the F3 panel and harness logs (allocates; call it only when you show it).
        public string DebugString
        {
            get
            {
                AttackPhase phase = ComputePhase(out float phaseTime);
                string move = CurrentMove != null ? " " + CurrentMove.DisplayName : "";
                string phaseText = phase != AttackPhase.None
                    ? string.Format(CultureInfo.InvariantCulture, " {0} {1:0.00}s", phase, phaseTime) : "";
                string bufferText = buffer.HasCommand ? buffer.Command + (buffer.Locked ? "(queued)" : "") : "-";
                return string.Format(CultureInfo.InvariantCulture,
                    "{0}{1}{2} | HP {3:0}/{4:0} ST {5:0}/{6:0} PO {7:0} MO {8:0} x{9:0.00} | heal {10} | buf {11} | inv {12} | charge {13:0.00} | ctr {14:0.00}",
                    state, move, phaseText, health, MaxHealth, stamina.Current, MaxStamina, poise.Current, momentum.Current,
                    MomentumMultiplier, healCharges, bufferText, IsInvulnerable ? "YES" : "no", ChargeTime, CounterWindowRemaining);
            }
        }
    }
}
