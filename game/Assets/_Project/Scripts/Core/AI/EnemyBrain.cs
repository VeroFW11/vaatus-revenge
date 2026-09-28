using System;
using System.Collections.Generic;
using System.Globalization;
using System.Numerics;

namespace VaatusRevenge.Core
{
    // Shared rules for every enemy: health, poise, stagger, death, the attack timeline and movement. The
    // subclasses (MeleeEnemyBrain, RangedEnemyBrain, SparringDummyBrain) only decide what to do while they're
    // free: where to move and when to attack. Create one with EnemyBrain.Create(tuning, tokens, id, seed).
    //
    // ATTACKS: an attack is a telegraph (Move.Startup, the wind-up players learn to read), then HitCount
    // strikes or bolts HitInterval apart, then Recovery. The enemy turns toward the player during the
    // telegraph and STOPS TRACKING when the first strike becomes active (or the first bolt flies): that's the
    // moment a dodge sidesteps it. Every strike gets its own AttackId.
    // TOKENS: an enemy holds an attack token from the moment it commits to an attack until the attack ends or
    // is interrupted (stagger, death, reset, losing interest, giving up the approach). As a safety net, a
    // token is also dropped at the end of any frame where the brain isn't attacking or about to.
    //
    // Tuning is read live and never written (one EnemyTuning may be shared by several enemies).
    public abstract class EnemyBrain
    {
        const float Epsilon = 1e-4f;
        static readonly EnemyAttackData[] NoAttacks = new EnemyAttackData[0];

        readonly List<EnemyEvent> events = new List<EnemyEvent>(16);
        readonly List<EnemyEvent> pendingEvents = new List<EnemyEvent>(8);
        readonly PoiseMeter poise = new PoiseMeter();
        readonly ActionTimeline action = new ActionTimeline();
        readonly int[] recentHitsTaken = new int[8];
        readonly AttackTokenPool tokens;
        readonly int seed;
        int recentHitCursor;
        float[] cooldowns = new float[0];
        bool insideTick;

        EnemyTuning tuning;
        double clock;
        EnemyState state;
        float stateTime;
        bool aggro;
        float health;
        float facingYaw;
        float spawnYaw;
        Vector3 moveVelocity;
        float verticalVelocity;
        Vector3 lastVelocity;
        Vector3 desiredVelocity;
        float sinceHit;
        float staggerDuration;
        PushMotion knockback;

        EnemyAttackData currentAttack;
        int currentAttackId;
        int openHitIndex = -1;
        bool activeOpen;
        bool committed;               // tracking has stopped for this attack
        float committedPitch;

        protected EnemyBrain(EnemyTuning tuning, AttackTokenPool tokens, int ownerId, int seed, float facingYaw)
        {
            this.tuning = tuning ?? EnemyTuning.CreateDaoSoldier();
            this.tokens = tokens;
            this.seed = seed;
            OwnerId = ownerId;
            Random = new DeterministicRandom(seed);
            spawnYaw = Angles.Wrap180(facingYaw);
            PendingAttackIndex = -1;
            ResetState();
        }

        // Picks the brain for the tuning's archetype. seed makes its choices repeatable; tokens may be null
        // (no limit on simultaneous attackers).
        public static EnemyBrain Create(EnemyTuning tuning, AttackTokenPool tokens, int ownerId, int seed, float facingYaw = 0f)
        {
            EnemyTuning t = tuning ?? EnemyTuning.CreateDaoSoldier();
            switch (t.Archetype)
            {
                case EnemyArchetype.Ranged: return new RangedEnemyBrain(t, tokens, ownerId, seed, facingYaw);
                case EnemyArchetype.Dummy: return new SparringDummyBrain(t, tokens, ownerId, seed, facingYaw);
                default: return new MeleeEnemyBrain(t, tokens, ownerId, seed, facingYaw);
            }
        }

        // ---------------------------------------------------------------- read-only state

        public EnemyTuning Tuning => tuning;
        public int OwnerId { get; }
        public EnemyState State => state;
        public bool IsAlive => state != EnemyState.Dead;
        public bool IsAggro => aggro;
        public float Health => health;
        public float MaxHealth => Math.Max(1f, tuning.MaxHealth);
        public float Poise => poise.Current;
        public float FacingYaw => facingYaw;
        public Vector3 Forward => Directions.FromYaw(facingYaw);
        public Vector3 Velocity => lastVelocity;
        public EnemyAttackData CurrentAttack => state == EnemyState.Attacking ? currentAttack : null;
        public MoveData CurrentMove => CurrentAttack != null ? currentAttack.Move : null;
        public bool IsTelegraphing => state == EnemyState.Attacking && !committed;
        public bool IsAttackActive => state == EnemyState.Attacking && activeOpen;
        public int ActiveAttackId => IsAttackActive ? currentAttackId : 0;
        public bool HoldsToken => tokens != null && tokens.IsHolding(OwnerId);
        public float AttackCooldownRemaining => Math.Max(0f, AttackTimer);
        public float StaggerRemaining => state == EnemyState.Staggered ? Math.Max(0f, staggerDuration - action.Time) : 0f;

        public AttackPhase Phase
        {
            get
            {
                if (state != EnemyState.Attacking || currentAttack == null) return AttackPhase.None;
                if (!committed) return AttackPhase.Startup;
                return action.Time < LastActiveEnd(currentAttack) ? AttackPhase.Active : AttackPhase.Recovery;
            }
        }

        // For subclasses.
        protected DeterministicRandom Random { get; }
        protected float StateTime => stateTime;
        protected double Clock => clock;
        protected float AttackTimer { get; set; }      // next attack allowed when this reaches 0
        protected int PendingAttackIndex { get; private set; }
        protected EnemyAttackData PendingAttack => AttackAt(PendingAttackIndex);
        protected EnemyAttackData[] Attacks => tuning.Attacks ?? NoAttacks;

        // ---------------------------------------------------------------- one frame

        public EnemyTickResult Tick(float dt, in EnemyWorldState world)
        {
            events.Clear();
            insideTick = true;
            try
            {
                events.AddRange(pendingEvents);
                pendingEvents.Clear();
                if (!(dt > 0f) || float.IsInfinity(dt)) return MakeResult();   // paused / frozen: nothing advances

                clock += dt;
                stateTime += dt;
                sinceHit += dt;
                UpdateTimers(dt);
                desiredVelocity = Vector3.Zero;
                if (state != EnemyState.Dead)
                {
                    UpdateAwareness(world);
                    AdvanceAction(dt, world);
                    if (IsFree) Think(dt, world);
                }
                ComputeMotion(dt, world);
                if (tokens != null && state != EnemyState.Attacking && PendingAttackIndex < 0) tokens.Release(OwnerId);
                return MakeResult();
            }
            finally
            {
                insideTick = false;
            }
        }

        // Decide what to do while free (Idle, Approach, Circle, Retreat): call Desire(...) to move, and
        // ReserveAttack / StartAttack to attack.
        protected abstract void Think(float dt, in EnemyWorldState world);

        protected virtual void OnAttackStarted() { }

        // Default: circle and wait for the next attack. Called after the recovery ends.
        protected virtual void OnAttackFinished()
        {
            SetState(EnemyState.Circle);
        }

        protected virtual float NextAttackDelay()
        {
            return Random.Range(tuning.AttackIntervalMin, tuning.AttackIntervalMax);
        }

        protected virtual void OnDamaged(float damage) { }
        protected virtual void OnHealthRefilled() { }
        protected virtual void OnReset() { }

        bool IsFree => state == EnemyState.Idle || state == EnemyState.Approach || state == EnemyState.Circle || state == EnemyState.Retreat;

        EnemyTickResult MakeResult()
        {
            return new EnemyTickResult { Velocity = lastVelocity, FacingYaw = facingYaw, Events = new EventList<EnemyEvent>(events) };
        }

        void Emit(EnemyEvent evt)
        {
            if (insideTick) events.Add(evt);
            else pendingEvents.Add(evt);
        }

        void Emit(EnemyEventType type)
        {
            Emit(new EnemyEvent { Type = type });
        }

        void UpdateTimers(float dt)
        {
            poise.Tick(dt, tuning.MaxPoise, tuning.PoiseRegenDelay, tuning.PoiseRegenRate);
            EnsureCooldowns();
            for (int i = 0; i < cooldowns.Length; i++) cooldowns[i] = Math.Max(0f, cooldowns[i] - dt);
            if (state != EnemyState.Attacking) AttackTimer = Math.Max(0f, AttackTimer - dt);
            if (state != EnemyState.Dead && tuning.HealthRefillDelay > 0f && sinceHit >= tuning.HealthRefillDelay && health < MaxHealth)
            {
                health = MaxHealth;
                Emit(EnemyEventType.HealthRefilled);
                OnHealthRefilled();
            }
            if (health > MaxHealth) health = MaxHealth;
        }

        void EnsureCooldowns()
        {
            if (cooldowns.Length != Attacks.Length) cooldowns = new float[Attacks.Length];   // only when the list is edited
        }

        void UpdateAwareness(in EnemyWorldState world)
        {
            if (!world.HasTarget)
            {
                if (aggro) LoseAggro();
                return;
            }
            float range = RangeTo(world);
            if (!aggro && range <= tuning.AggroRange && !world.TargetHidden) BecomeAggro();
            else if (aggro && range > tuning.LoseAggroRange) LoseAggro();
        }

        void BecomeAggro()
        {
            if (aggro) return;
            aggro = true;
            AttackTimer = NextAttackDelay();   // so a group doesn't all swing on the same frame
            Emit(EnemyEventType.Aggroed);
        }

        void LoseAggro()
        {
            aggro = false;
            if (state == EnemyState.Attacking) ExitAttack();
            CancelPendingAttack();
            if (state != EnemyState.Staggered) SetState(EnemyState.Idle);
        }

        // ---------------------------------------------------------------- helpers for subclasses

        protected void SetState(EnemyState newState)
        {
            if (state == newState) return;
            state = newState;
            stateTime = 0f;
        }

        protected void Desire(Vector3 velocity)
        {
            desiredVelocity = Directions.Flatten(velocity);
        }

        // Distance from this enemy's centre to the target's body (what attack ranges are measured in).
        protected static float RangeTo(in EnemyWorldState world)
        {
            float centre = Directions.Flatten(world.TargetPosition - world.Position).Length();
            return Math.Max(0f, centre - Math.Max(0f, world.TargetRadius));
        }

        // Horizontal direction to the target; the current forward if standing on it (never NaN).
        protected Vector3 DirectionTo(in EnemyWorldState world)
        {
            return Directions.SafeNormalize(Directions.Flatten(world.TargetPosition - world.Position), Forward);
        }

        protected EnemyAttackData AttackAt(int index)
        {
            EnemyAttackData[] attacks = Attacks;
            if (index < 0 || index >= attacks.Length) return null;
            EnemyAttackData attack = attacks[index];
            return attack != null && attack.Move != null ? attack : null;
        }

        protected bool CanUseTokenNow => tokens == null || !tuning.UsesAttackToken || tokens.CanAcquire(OwnerId);

        // Weighted random pick among attacks that are off cooldown. Prefers attacks usable at 'range'; with
        // mustBeInRange false it falls back to any ready attack (the brain then walks into its range).
        // Returns -1 when nothing fits.
        protected int PickAttack(float range, bool mustBeInRange)
        {
            EnsureCooldowns();
            int index = PickWeighted(range, true);
            if (index < 0 && !mustBeInRange) index = PickWeighted(range, false);
            return index;
        }

        int PickWeighted(float range, bool inRangeOnly)
        {
            float total = 0f;
            for (int i = 0; i < Attacks.Length; i++) total += PickWeight(i, range, inRangeOnly);
            if (!(total > 0f)) return -1;
            float roll = Random.NextFloat() * total;
            int last = -1;
            for (int i = 0; i < Attacks.Length; i++)
            {
                float w = PickWeight(i, range, inRangeOnly);
                if (!(w > 0f)) continue;
                last = i;
                if (roll < w) return i;
                roll -= w;
            }
            return last;
        }

        float PickWeight(int index, float range, bool inRangeOnly)
        {
            EnemyAttackData attack = AttackAt(index);
            if (attack == null || !(attack.Weight > 0f) || cooldowns[index] > 0f) return 0f;
            if (inRangeOnly && (range < attack.MinRange || range > attack.MaxRange)) return 0f;
            return attack.Weight;
        }

        // Commits to an attack: takes a token if this enemy uses them. False = no token free (keep circling).
        protected bool ReserveAttack(int index)
        {
            if (AttackAt(index) == null) return false;
            if (tokens != null && tuning.UsesAttackToken && !tokens.TryAcquire(OwnerId)) return false;
            PendingAttackIndex = index;
            return true;
        }

        protected void CancelPendingAttack()
        {
            PendingAttackIndex = -1;
            if (tokens != null) tokens.Release(OwnerId);
        }

        // ---------------------------------------------------------------- attacks

        protected void StartAttack(int index, in EnemyWorldState world)
        {
            EnemyAttackData attack = AttackAt(index);
            if (attack == null)
            {
                CancelPendingAttack();
                return;
            }
            if (PendingAttackIndex != index && !ReserveAttack(index)) return;
            PendingAttackIndex = -1;          // the token now belongs to the running attack
            EnsureCooldowns();
            cooldowns[index] = Math.Max(0f, attack.Cooldown);
            SetState(EnemyState.Attacking);
            currentAttack = attack;
            currentAttackId = CombatIds.Next();
            openHitIndex = -1;
            activeOpen = false;
            committed = false;
            committedPitch = 0f;
            moveVelocity = Vector3.Zero;
            action.Begin();
            Emit(new EnemyEvent
            {
                Type = EnemyEventType.TelegraphStarted, Attack = attack, Move = attack.Move, Telegraph = attack.Telegraph,
                AttackId = currentAttackId, Duration = attack.Move.Startup
            });
            OnAttackStarted();
            UpdateAttack(world);
        }

        static int HitCountOf(EnemyAttackData attack)
        {
            return Math.Max(1, attack.HitCount);
        }

        static float HitStart(EnemyAttackData attack, int hit)
        {
            return attack.Move.Startup + hit * Math.Max(0f, attack.HitInterval);
        }

        static float LastActiveEnd(EnemyAttackData attack)
        {
            return HitStart(attack, HitCountOf(attack) - 1) + attack.Move.Active;
        }

        static float TotalDuration(EnemyAttackData attack)
        {
            return LastActiveEnd(attack) + attack.Move.Recovery;
        }

        void UpdateAttack(in EnemyWorldState world)
        {
            EnemyAttackData attack = currentAttack;
            MoveData move = attack.Move;
            int hits = HitCountOf(attack);
            for (int hit = 0; hit < hits; hit++)
            {
                float start = HitStart(attack, hit);
                if (action.Crossed(start))
                {
                    if (!committed)
                    {
                        committed = true;             // tracking stops here: this is what makes dodging possible
                        committedPitch = PitchToTarget(world, move);
                    }
                    if (activeOpen) CloseActive();    // windows can't overlap: close the previous strike first
                    if (hit > 0) currentAttackId = CombatIds.Next();
                    if (move.LaunchesProjectile) Launch(hit, world);
                    else OpenActive(hit, world);
                }
                if (activeOpen && openHitIndex == hit && action.Crossed(start + move.Active)) CloseActive();
            }
            action.MarkChecked();
            if (action.Time >= TotalDuration(attack)) EndAttack();
        }

        void OpenActive(int hit, in EnemyWorldState world)
        {
            activeOpen = true;
            openHitIndex = hit;
            Emit(new EnemyEvent
            {
                Type = EnemyEventType.AttackActiveStart, Attack = currentAttack, Move = currentAttack.Move, Telegraph = currentAttack.Telegraph,
                AttackId = currentAttackId, HitIndex = hit, Origin = GetStrikeOrigin(world.Position), Direction = Forward
            });
        }

        void CloseActive()
        {
            activeOpen = false;
            Emit(new EnemyEvent
            {
                Type = EnemyEventType.AttackActiveEnd, Attack = currentAttack, Move = currentAttack.Move, Telegraph = currentAttack.Telegraph,
                AttackId = currentAttackId, HitIndex = openHitIndex
            });
        }

        void Launch(int hit, in EnemyWorldState world)
        {
            Emit(new EnemyEvent
            {
                Type = EnemyEventType.ProjectileLaunched, Attack = currentAttack, Move = currentAttack.Move, Telegraph = currentAttack.Telegraph,
                AttackId = currentAttackId, HitIndex = hit, Origin = GetStrikeOrigin(world.Position),
                Direction = Directions.FromYawPitch(facingYaw, committedPitch)
            });
        }

        float PitchToTarget(in EnemyWorldState world, MoveData move)
        {
            if (!world.HasTarget) return 0f;
            Vector3 aim = world.TargetAimPoint != Vector3.Zero ? world.TargetAimPoint
                : world.TargetPosition + new Vector3(0f, move.OriginHeight, 0f);
            return Directions.PitchOf(aim - GetStrikeOrigin(world.Position));
        }

        void EndAttack()
        {
            ExitAttack();
            AttackTimer = NextAttackDelay();
            OnAttackFinished();
            if (state == EnemyState.Attacking) SetState(EnemyState.Circle);
        }

        // Leaves an attack tidily: an open active window always gets its AttackActiveEnd, and the token goes back.
        void ExitAttack()
        {
            if (currentAttack != null)
            {
                if (activeOpen) CloseActive();
                Emit(new EnemyEvent { Type = EnemyEventType.AttackEnded, Attack = currentAttack, Move = currentAttack.Move, Telegraph = currentAttack.Telegraph });
            }
            activeOpen = false;
            currentAttack = null;
            action.Stop();
            if (tokens != null) tokens.Release(OwnerId);
        }

        void AdvanceAction(float dt, in EnemyWorldState world)
        {
            if (state == EnemyState.Attacking && currentAttack != null)
            {
                action.Advance(dt);
                UpdateAttack(world);
            }
            else if (state == EnemyState.Staggered)
            {
                action.Advance(dt);
                action.MarkChecked();
                if (action.Time < staggerDuration) return;
                action.Stop();
                Emit(EnemyEventType.StaggerEnded);
                SetState(aggro ? EnemyState.Circle : EnemyState.Idle);
            }
        }

        // ---------------------------------------------------------------- movement

        void ComputeMotion(float dt, in EnemyWorldState world)
        {
            Vector3 displacement = Vector3.Zero;
            float yawToTarget = world.HasTarget
                ? Directions.YawOf(Directions.Flatten(world.TargetPosition - world.Position), facingYaw) : facingYaw;
            switch (state)
            {
                case EnemyState.Attacking:
                    moveVelocity = Vector3.Zero;
                    if (currentAttack == null) break;
                    // Tracking during the telegraph only.
                    if (!committed && world.HasTarget)
                        facingYaw = LocomotionRules.Turn(facingYaw, yawToTarget, currentAttack.Move.TrackingTurnRate, dt);
                    displacement = LungeStep(world);
                    break;
                case EnemyState.Staggered:
                case EnemyState.Dead:
                    moveVelocity = Vector3.Zero;
                    break;
                default:
                    moveVelocity = LocomotionRules.Accelerate(moveVelocity, desiredVelocity, tuning.Acceleration, tuning.Acceleration, dt);
                    if (aggro && world.HasTarget) facingYaw = LocomotionRules.Turn(facingYaw, yawToTarget, tuning.TurnRate, dt);
                    else if (desiredVelocity.LengthSquared() > Epsilon)
                        facingYaw = LocomotionRules.Turn(facingYaw, Directions.YawOf(desiredVelocity, facingYaw), tuning.TurnRate, dt);
                    break;
            }
            displacement += knockback.Step(dt);
            if (world.Grounded) verticalVelocity = -tuning.GroundStickSpeed;
            else verticalVelocity = LocomotionRules.ApplyGravity(verticalVelocity, tuning.Gravity, 0f, dt);
            lastVelocity = moveVelocity + displacement / dt + new Vector3(0f, verticalVelocity, 0f);
        }

        // Forward step during the first strike (over its last LungeTime seconds), stopping short of the target.
        Vector3 LungeStep(in EnemyWorldState world)
        {
            MoveData move = currentAttack.Move;
            if (!(move.LungeDistance > 0f)) return Vector3.Zero;
            float end = move.ActiveEnd;
            float start = move.LungeTime > 0f ? Math.Max(0f, end - move.LungeTime) : 0f;
            float before = MotionCurves.WindowProgress(action.PreviousTime, start, end, 0f);
            float after = MotionCurves.WindowProgress(action.Time, start, end, 0f);
            Vector3 step = Forward * (move.LungeDistance * (after - before));
            if (!world.HasTarget) return step;

            Vector3 toTarget = Directions.Flatten(world.TargetPosition - world.Position);
            float distance = toTarget.Length();
            if (distance < 1e-4f) return Vector3.Zero;
            Vector3 dir = toTarget / distance;
            float along = Vector3.Dot(step, dir);
            if (along <= 0f) return step;
            float allowed = Math.Max(0f, distance - Math.Max(0f, world.SelfRadius) - Math.Max(0f, world.TargetRadius));
            return along <= allowed ? step : step - dir * (along - allowed);
        }

        public Vector3 GetStrikeOrigin(Vector3 feet)
        {
            MoveData move = currentAttack != null ? currentAttack.Move : null;
            if (move == null) return feet;
            return feet + new Vector3(0f, move.OriginHeight, 0f) + Forward * move.OriginForward;
        }

        // ---------------------------------------------------------------- incoming hits

        // Enemies don't dodge or block: hits cost health and poise; broken poise = stagger (and knockback).
        // Hyper-armoured attacks can't be staggered during their telegraph and active frames.
        public HitResult ReceiveHit(in DamageInfo hit, Vector3 facing)
        {
            if (state == EnemyState.Dead || hit.SourceTeam == Team.Enemy) return HitResult.Ignored;
            if (hit.AttackId != 0)
            {
                if (Array.IndexOf(recentHitsTaken, hit.AttackId) >= 0) return HitResult.Ignored;
                recentHitsTaken[recentHitCursor] = hit.AttackId;
                recentHitCursor = (recentHitCursor + 1) % recentHitsTaken.Length;
            }

            BecomeAggro();
            sinceHit = 0f;
            float damage = Math.Max(0f, hit.Damage);
            health -= damage;
            if (tuning.Unkillable) health = Math.Max(health, Math.Min(1f, MaxHealth));
            var result = new HitResult { Outcome = HitOutcome.Hit, DamageDealt = damage };
            Emit(new EnemyEvent { Type = EnemyEventType.Damaged, Amount = damage, Direction = hit.Direction });
            OnDamaged(damage);
            if (health <= 0f)
            {
                result.Killed = true;
                Die();
                return result;
            }
            if (!HasHyperArmor && poise.Damage(hit.PoiseDamage, tuning.MaxPoise))
            {
                result.PoiseBroken = true;
                Stagger(tuning.StaggerDuration);
                knockback.Start(hit.Direction, hit.Knockback, tuning.KnockbackTime);
            }
            return result;
        }

        bool HasHyperArmor => state == EnemyState.Attacking && currentAttack != null && currentAttack.Move.HyperArmor
            && action.Time < LastActiveEnd(currentAttack);

        // The player deflected our attack: stagger. (For a deflected bolt the Unity side may skip this.)
        public void OnParried()
        {
            if (state == EnemyState.Dead) return;
            Stagger(tuning.ParriedStaggerDuration);
        }

        void Stagger(float duration)
        {
            if (state == EnemyState.Dead) return;
            if (state == EnemyState.Attacking) ExitAttack();
            CancelPendingAttack();
            SetState(EnemyState.Staggered);
            staggerDuration = Math.Max(0f, duration);
            moveVelocity = Vector3.Zero;
            action.Begin();
            Emit(new EnemyEvent { Type = EnemyEventType.Staggered, Duration = staggerDuration });
        }

        void Die()
        {
            if (state == EnemyState.Attacking) ExitAttack();
            CancelPendingAttack();
            action.Stop();
            SetState(EnemyState.Dead);
            health = 0f;
            moveVelocity = Vector3.Zero;
            knockback.Stop();
            Emit(EnemyEventType.Died);
        }

        // ---------------------------------------------------------------- damage out, reset

        public DamageInfo BuildDamage(EnemyAttackData attack, int attackId)
        {
            var info = new DamageInfo();
            if (attack == null || attack.Move == null) return info;
            MoveData move = attack.Move;
            info.Damage = move.Damage;
            info.PoiseDamage = move.PoiseDamage;
            info.GuardStaminaDamage = move.GuardStaminaDamage;
            info.Knockback = move.Knockback;
            info.Hitstop = move.Hitstop;
            info.Kind = move.Kind;
            info.SourceTeam = Team.Enemy;
            info.SourceId = OwnerId;
            info.AttackId = attackId;
            info.Parryable = move.Parryable;
            info.Unblockable = move.Unblockable;
            return info;
        }

        public DamageInfo BuildDamage(in EnemyEvent evt)
        {
            return BuildDamage(evt.Attack, evt.AttackId);
        }

        // For the per-frame melee query while IsAttackActive.
        public DamageInfo BuildCurrentDamage()
        {
            return BuildDamage(CurrentAttack, currentAttackId);
        }

        public void Reset()
        {
            Reset(spawnYaw);
        }

        // Back to full health, unaware, with the same random sequence as a fresh brain (repeatable tests).
        public void Reset(float newFacingYaw)
        {
            if (state == EnemyState.Attacking) ExitAttack();
            spawnYaw = Angles.Wrap180(newFacingYaw);
            ResetState();
            Emit(EnemyEventType.Reset);
        }

        void ResetState()
        {
            CancelPendingAttack();
            action.Stop();
            currentAttack = null;
            activeOpen = false;
            committed = false;
            state = EnemyState.Idle;
            stateTime = 0f;
            aggro = false;
            health = MaxHealth;
            poise.Reset(tuning.MaxPoise);
            facingYaw = spawnYaw;
            moveVelocity = Vector3.Zero;
            desiredVelocity = Vector3.Zero;
            verticalVelocity = 0f;
            lastVelocity = Vector3.Zero;
            knockback.Stop();
            sinceHit = 0f;
            AttackTimer = 0f;
            Array.Clear(cooldowns, 0, cooldowns.Length);
            Array.Clear(recentHitsTaken, 0, recentHitsTaken.Length);
            Random.Reseed(seed);
            OnReset();
        }

        public string DebugString
        {
            get
            {
                string attack = CurrentAttack != null ? " " + CurrentMove.DisplayName + " " + Phase : "";
                return string.Format(CultureInfo.InvariantCulture, "{0}{1} | HP {2:0}/{3:0} PO {4:0} | token {5} | next attack {6:0.00}s",
                    state, attack, health, MaxHealth, poise.Current, HoldsToken ? "yes" : "no", AttackCooldownRemaining);
            }
        }
    }
}
