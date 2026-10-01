using UnityEngine;
using VaatusRevenge.Core;

namespace VaatusRevenge
{
    // What EnemyController (soldiers, crossbowmen) and TrainingDummy have in common: the Unity side of an enemy.
    // The decisions (when to attack, where to move, health, poise, stagger) live in a pure C# EnemyBrain; this
    // component is its body. Each frame it:
    //   1. tells the brain about the world (where the player is, whether a wall hides them),
    //   2. ticks the brain,
    //   3. moves the CharacterController once and turns to the brain's facing,
    //   4. turns the brain's events into attacks (EnemyStrikes) and glows and effects (EnemyRigPresenter),
    //   5. tells the body's animator what the enemy is doing (EnemyAnimationFeed -> BodyAnimatorDriver).
    // Incoming hits go through ReceiveHit (IDamageReceiver) straight to the brain; their effects arrive as events
    // on the next tick, which for melee hits is later in the same frame (enemies update after the player).
    //
    // The brain is created at runtime only (Start, or the first hit if that comes sooner), after every object's
    // OnEnable, so the scene's EnemyEncounter is found whatever order things load in. Configure only stores
    // references, so the sandbox builder can call it at edit time.
    [DisallowMultipleComponent]
    [RequireComponent(typeof(CharacterController), typeof(Combatant), typeof(HumanoidBody))]
    public abstract class EnemyFighter : MonoBehaviour, IDamageReceiver
    {
        // A corpse stops blocking once it lands; this is the longest it may keep falling (e.g. off the world).
        const float MaxCorpseFallSeconds = 3f;
        const float SeedPrecision = 100f; // spawn position in centimetres for the brain's random seed
        const float FallbackEyeHeightShare = 0.72f; // about chest height, where Spawn puts the aim point
        // Ledge check: the ground is probed this far beyond the end of each step (so the body never hangs over
        // an edge), with a tiny sphere to tell "inside a wall" from "over a drop".
        const float LedgeProbeAhead = 0.2f;
        const float LedgeProbeRadius = 0.05f;
        const float MinStepLength = 1e-5f;
        const float LaunchPillarHeight = 3.2f;       // the column under a launched enemy (visual only)
        const float KnockdownRingRadius = 1.6f;      // the ring where a launched or slammed enemy lands (visual only)

        static readonly string[] StateNames = System.Enum.GetNames(typeof(EnemyState));
        static AttackTokenPool fallbackTokens;
        static bool warnedNoEncounter;

        [Tooltip("This enemy type's rules and feel. Empty = built-in defaults, with a warning.")]
        [SerializeField] private EnemyTuningAsset tuningAsset;

        readonly EnemyRigPresenter presenter = new EnemyRigPresenter();
        readonly EnemyStrikes strikes = new EnemyStrikes();
        readonly EnemyFeedbackSettings fallbackFeedback = new EnemyFeedbackSettings();
        readonly EnemyAnimationFeed animationFeed = new EnemyAnimationFeed();

        CharacterController controller;
        Combatant combatant;
        HumanoidBody body;
        BodyAnimatorDriver animatorDriver;
        EnemyHealthBar healthBar;
        EnemyBrain brain;
        AttackTokenPool brainTokens;
        bool bound;
        bool hasSpawnPoint;
        Vector3 spawnPosition;
        float spawnYaw;
        float deadTime;
        bool warnedNoTuning;
        ElementId lastHitElement = ElementId.None;   // the element of the last attack that reached us (launch and landing effects)
        string debugText;
        int debugFrame = -1;

        // ---------------------------------------------------------------- read-only state

        public EnemyTuningAsset TuningAsset => tuningAsset;
        // Null until the brain exists (play mode only).
        public EnemyBrain Brain => brain;
        public Combatant Combatant
        {
            get
            {
                if (combatant == null) combatant = GetComponent<Combatant>();
                return combatant;
            }
        }
        public bool IsDead => brain != null && !brain.IsAlive;
        public float Health01 => brain != null ? Mathf.Clamp01(brain.Health / brain.MaxHealth) : 1f;
        public string StateName
        {
            get
            {
                int index = brain != null ? (int)brain.State : 0;
                return index >= 0 && index < StateNames.Length ? StateNames[index] : StateNames[0];
            }
        }
        public string DisplayName
        {
            get
            {
                Combatant body = Combatant;
                return body != null ? body.DisplayName : name;
            }
        }
        // Never null: the asset's feel settings, or built-in defaults.
        public EnemyFeedbackSettings Feedback =>
            tuningAsset != null && tuningAsset.Feedback != null ? tuningAsset.Feedback : fallbackFeedback;
        // Where ResetEnemy / ResetDummy put it back: where it stood when play started, unless SetSpawnPoint moved it.
        public Vector3 SpawnPosition => hasSpawnPoint ? spawnPosition : transform.position;
        public float SpawnYaw => hasSpawnPoint ? spawnYaw : transform.eulerAngles.y;

        // One line for the F3 debug panel (rebuilt at most once a frame).
        public string DebugText
        {
            get
            {
                if (brain == null) return DisplayName + ": waiting for play mode";
                if (debugText == null || debugFrame != Time.frameCount)
                {
                    debugText = DisplayName + ": " + brain.DebugString;
                    debugFrame = Time.frameCount;
                }
                return debugText;
            }
        }

        // Extra line under the health bar (the dummy's combo stats); null = none. Must not allocate per call.
        public virtual string StatusText => null;

        // ---------------------------------------------------------------- IDamageReceiver

        public Team Team => Team.Enemy;
        public bool IsAlive => isActiveAndEnabled && (brain == null || brain.IsAlive);

        public HitResult ReceiveHit(in DamageInfo hit)
        {
            if (!Application.isPlaying || !isActiveAndEnabled) return HitResult.Ignored;
            EnemyBrain b = EnsureBrain();
            if (b == null) return HitResult.Ignored;
            // The brain ignores friendly fire and repeats of the same AttackId, and decides stagger and death. The element is
            // remembered so a launch or a landing next tick is drawn in the element that caused it.
            HitResult result = b.ReceiveHit(hit, b.Forward);
            if (result.Outcome == HitOutcome.Hit && hit.Element != ElementId.None) lastHitElement = hit.Element;
            return result;
        }

        // The element a launch or a landing is drawn in: the last one that hit us, else the player's element in hand.
        ElementId EffectElement
        {
            get
            {
                if (lastHitElement != ElementId.None) return lastHitElement;
                PlayerController player = PlayerController.Instance;
                return player != null && player.Model != null ? player.Model.ActiveElement : ElementId.Fire;
            }
        }

        // ---------------------------------------------------------------- setup

        // At runtime: where the next reset puts it. In edit mode an enemy's placement IS its spawn point, so
        // this simply moves it there.
        public void SetSpawnPoint(Vector3 position, float yaw)
        {
            if (!Application.isPlaying)
            {
                transform.SetPositionAndRotation(position, Quaternion.Euler(0f, yaw, 0f));
                return;
            }
            hasSpawnPoint = true;
            spawnPosition = position;
            spawnYaw = yaw;
        }

        // Stores the tuning (edit-time safe). If the enemy is already running, its brain is rebuilt with the new
        // numbers on the next frame.
        protected void SetTuningAsset(EnemyTuningAsset newTuning)
        {
            bool changed = tuningAsset != newTuning;
            tuningAsset = newTuning;
            warnedNoTuning = false;
            EnemyTuning data = newTuning != null ? newTuning.Tuning : null;
            Combatant body = Combatant;
            if (body != null && data != null && !string.IsNullOrEmpty(data.DisplayName) && body.DisplayName != data.DisplayName)
                body.Configure(body.Team, body.AimPoint, body.Radius, body.Height, data.DisplayName);
            if (changed && brain != null) DropBrain();
        }

        // Builds the brain for this kind of fighter. tokens may be null (no limit on simultaneous attackers).
        protected abstract EnemyBrain CreateBrain(EnemyTuning tuning, AttackTokenPool tokens, int ownerId, int seed, float yaw);

        // Default: the encounter's shared pool, or a fallback pool (with a one-time warning) so the "only a few
        // attack at once" rule still holds in a scene without an EnemyEncounter.
        protected virtual AttackTokenPool ResolveTokens(EnemyTuning tuning)
        {
            EnemyEncounter encounter = EnemyEncounter.Instance;
            if (encounter != null) return encounter.Tokens;
            if (fallbackTokens == null) fallbackTokens = new AttackTokenPool(EncounterTuningAsset.DefaultMaxSimultaneousAttackers);
            if (!warnedNoEncounter && (tuning == null || tuning.UsesAttackToken))
            {
                warnedNoEncounter = true;
                Debug.LogWarning("Enemies: there's no EnemyEncounter in the scene, so they share a fallback pool of "
                                 + EncounterTuningAsset.DefaultMaxSimultaneousAttackers + " attack tokens. Add an EnemyEncounter "
                                 + "(with an EncounterTuningAsset) to set how many may attack at once.", this);
            }
            return fallbackTokens;
        }

        // Dummies are training posts: they turn to face you but are never moved (or knocked back).
        protected virtual bool Planted => false;

        // For subclasses that keep extra state from the brain's events (e.g. the dummy's stats text).
        protected virtual void OnBrainEvent(in EnemyEvent e) { }

        // Back to the spawn point, full health, alive, calm. Play mode only.
        protected void ResetFighter()
        {
            if (!Application.isPlaying) return;
            CacheComponents();
            EnsureSpawnPoint();
            strikes.EndAll();
            lastHitElement = ElementId.None;     // a fresh fight: the last life's element doesn't colour this one's launch
            Teleport(spawnPosition, spawnYaw);
            EnemyBrain b = EnsureBrain();
            if (b != null) b.Reset(spawnYaw);   // hands back its attack token and closes any open strike
            ReviveVisuals();                     // straight away, not a frame later when the brain's Reset event arrives
        }

        protected EnemyBrain EnsureBrain()
        {
            if (brain != null) return brain;
            if (!Application.isPlaying) return null;
            CacheComponents();
            EnsureSpawnPoint();
            EnemyTuning tuning = tuningAsset != null ? tuningAsset.Tuning : null;
            if (tuning == null && !warnedNoTuning)
            {
                warnedNoTuning = true;
                Debug.LogWarning(name + " has no EnemyTuningAsset (or it's empty), so it uses the built-in default numbers.", this);
            }
            int ownerId = combatant != null ? combatant.Id : CombatIds.Next(); // MeleeHitQuery skips the attacker by this id
            brainTokens = ResolveTokens(tuning);
            brain = CreateBrain(tuning, brainTokens, ownerId, SeedFor(spawnPosition), transform.eulerAngles.y);
            return brain;
        }

        // ---------------------------------------------------------------- Unity messages

        protected virtual void Awake()
        {
            CacheComponents();
        }

        protected virtual void OnEnable()
        {
        }

        protected virtual void Start()
        {
            EnsureSpawnPoint();
            EnsureBrain();
            BindPresenter();
        }

        protected virtual void Update()
        {
            EnemyBrain b = EnsureBrain();
            if (b == null) return;
            if (!bound) BindPresenter();
            float dt = Time.deltaTime;
            EnemyWorldState world = BuildWorldState(b);
            EnemyTickResult result = b.Tick(dt, in world);
            if (dt > 0f) ApplyMotion(b, in result, dt);
            bool strikeOpened = HandleEvents(result.Events, b);
            // Keep sweeping an open swing each frame (not on the frame it opened: that query already ran).
            if (dt > 0f && !strikeOpened && b.IsAttackActive) strikes.ContinueMelee(b, transform.position, Feedback);
            presenter.Tick(dt, b, Feedback);
            UpdateAnimation(b, in world, dt);
            if (!b.IsAlive) SettleCorpse(dt);
        }

        protected virtual void OnDisable()
        {
            // Nothing may outlive a switched-off enemy: no open hit record, no glow, and above all no attack
            // token (it would starve the others). If it's switched back on mid-attack, that attack just finishes.
            strikes.EndAll();
            presenter.OnDisabled();
            if (brain != null && brainTokens != null) brainTokens.Release(brain.OwnerId);
        }

        protected virtual void OnDestroy()
        {
            presenter.Dispose();
        }

        // Domain reload is off in this project, so statics survive between play sessions: start each one clean.
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics()
        {
            fallbackTokens = null;
            warnedNoEncounter = false;
        }

        // ---------------------------------------------------------------- per frame

        EnemyWorldState BuildWorldState(EnemyBrain b)
        {
            bool controllerActive = controller != null && controller.enabled;
            var world = new EnemyWorldState
            {
                Position = transform.position.ToNumerics(),
                // A planted dummy or a settled corpse doesn't fall; everyone else asks the controller.
                Grounded = Planted || !controllerActive || controller.isGrounded,
                SelfRadius = combatant != null ? combatant.Radius : 0f
            };
            if (!b.IsAlive) return world;

            Combatant target = Combatant.Player;
            if (!IsValidTarget(target)) return world;
            Vector3 targetAim = target.AimPoint.position;
            world.HasTarget = true;
            world.TargetPosition = target.Feet.ToNumerics();
            world.TargetAimPoint = targetAim.ToNumerics();
            world.TargetRadius = target.Radius;
            // Chest to chest against walls only: a hidden player isn't noticed, and the crossbowman won't shoot.
            world.TargetHidden = CombatPhysics.IsBlocked(EyePosition(), targetAim);
            return world;
        }

        // The player, if it's there to fight. A player without a damage receiver yet (still being wired up)
        // counts as a target, like lock-on does, so enemies can be tried out on their own.
        static bool IsValidTarget(Combatant target)
        {
            if (target == null || !target.isActiveAndEnabled || target.Team == Team.Enemy) return false;
            return target.Receiver == null || target.IsAlive;
        }

        Vector3 EyePosition()
        {
            if (combatant != null && combatant.AimPoint != transform) return combatant.AimPoint.position;
            float height = combatant != null ? combatant.Height : 0f;
            return transform.position + Vector3.up * (height * FallbackEyeHeightShare);
        }

        void ApplyMotion(EnemyBrain b, in EnemyTickResult result, float dt)
        {
            if (!float.IsNaN(result.FacingYaw) && !float.IsInfinity(result.FacingYaw))
                transform.rotation = Quaternion.Euler(0f, result.FacingYaw, 0f);
            if (Planted || controller == null || !controller.enabled) return;
            Vector3 velocity = result.Velocity.ToUnity();
            if (float.IsNaN(velocity.x) || float.IsNaN(velocity.y) || float.IsNaN(velocity.z)) return;
            // Brains don't know where the edges are, so their own steps (walking, strafing, lunging) stop at a
            // ledge. Knockback only happens while staggered, and being knocked off a ledge is fine.
            if (b.IsAlive && b.State != EnemyState.Staggered && b.State != EnemyState.Launched
                && WouldStepOffLedge(new Vector3(velocity.x * dt, 0f, velocity.z * dt)))
            {
                velocity.x = 0f;
                velocity.z = 0f;
            }
            controller.Move(velocity * dt); // the one move this frame
        }

        // True when this horizontal step would take the enemy over a drop bigger than the controller can step
        // down (e.g. off the raised platform). It looks at the ground a little beyond the end of the step: ground
        // within a step up or down is fine (floors, ramps, stairs). No ground there is a drop, unless the probe
        // started inside something solid (a wall, a tall step): the controller simply bumps into that, and
        // refusing the step would stop the enemy sliding along walls.
        bool WouldStepOffLedge(Vector3 step)
        {
            float length = step.magnitude;
            if (length < MinStepLength) return false;
            Vector3 ahead = transform.position + step * ((length + LedgeProbeAhead) / length);
            float stepHeight = controller.stepOffset + controller.skinWidth;
            Vector3 probeTop = ahead + Vector3.up * stepHeight;
            if (CombatPhysics.IsBlocked(probeTop, ahead - Vector3.up * stepHeight)) return false;
            return !CombatPhysics.IsOverlapping(probeTop, LedgeProbeRadius);
        }

        // Hands the body's animator this frame's picture of the enemy (state, attack timing, hits, movement).
        void UpdateAnimation(EnemyBrain b, in EnemyWorldState world, float dt)
        {
            if (animatorDriver == null) return;
            bool grounded = Planted || controller == null || !controller.enabled || controller.isGrounded;
            float aim = world.HasTarget ? presenter.AimPitch(EyePosition(), world.TargetAimPoint.ToUnity(), Feedback) : 0f;
            animatorDriver.SetInput(animationFeed.Build(b, dt, grounded, aim, Planted));
        }

        bool HandleEvents(in EventList<EnemyEvent> events, EnemyBrain b)
        {
            bool strikeOpened = false;
            EnemyFeedbackSettings feedback = Feedback;
            for (int i = 0; i < events.Count; i++)
            {
                EnemyEvent e = events[i];
                animationFeed.OnEvent(in e);
                strikes.RelayDanger(in e, b, transform.position);   // danger sense: the player hears about every wind-up
                switch (e.Type)
                {
                    case EnemyEventType.Aggroed:
                        if (!Planted) presenter.OnAggroed(feedback);
                        break;
                    case EnemyEventType.TelegraphStarted:
                        presenter.OnTelegraphStarted(in e, feedback);
                        break;
                    case EnemyEventType.AttackActiveStart:
                        presenter.OnStrike(in e, feedback);
                        strikes.OpenMelee(in e, b, transform.position, feedback);
                        strikeOpened = true;
                        break;
                    case EnemyEventType.AttackActiveEnd:
                        strikes.CloseMelee(e.AttackId);
                        presenter.OnStrikeEnd();
                        break;
                    case EnemyEventType.ProjectileLaunched:
                        presenter.OnBoltLaunched(in e, feedback);
                        strikes.LaunchBolt(in e, b, this);
                        break;
                    case EnemyEventType.AttackEnded:
                        presenter.OnAttackEnded();
                        break;
                    case EnemyEventType.Launched:
                        presenter.OnLaunched(transform.position, LaunchPillarHeight, EffectElement);
                        break;
                    case EnemyEventType.KnockedDown:
                        presenter.OnKnockedDown(transform.position, KnockdownRingRadius, EffectElement);
                        break;
                    case EnemyEventType.Damaged:
                        presenter.OnDamaged(feedback);
                        if (healthBar != null) healthBar.Show(feedback.HealthBarShowTime);
                        break;
                    case EnemyEventType.Staggered:
                        // Events queued between ticks can be stale (e.g. staggered, then killed by the next hit).
                        if (b.State == EnemyState.Staggered) presenter.OnStaggered(feedback);
                        break;
                    case EnemyEventType.StaggerEnded:
                        presenter.OnStaggerEnded();
                        break;
                    case EnemyEventType.Died:
                        if (!b.IsAlive) Die(feedback);
                        break;
                    case EnemyEventType.HealthRefilled:
                        presenter.OnHealthRefilled(feedback);
                        break;
                    case EnemyEventType.Reset:
                        ReviveVisuals();
                        break;
                }
                OnBrainEvent(in e);
            }
            return strikeOpened;
        }

        void Die(EnemyFeedbackSettings feedback)
        {
            strikes.EndAll();
            presenter.OnDied(feedback);
            deadTime = 0f;
            if (healthBar != null) healthBar.Hide();
            // A planted body stops blocking at once; anyone else lands first (SettleCorpse), then stops blocking.
            if (Planted && controller != null) controller.enabled = false;
        }

        // Corpses shouldn't block the player: once the body is on the ground its controller is switched off.
        void SettleCorpse(float dt)
        {
            if (controller == null || !controller.enabled) return;
            deadTime += dt;
            if (Planted || controller.isGrounded || deadTime >= MaxCorpseFallSeconds) controller.enabled = false;
        }

        // Alive again: collision back on, guard stance, no effects, health bar hidden.
        void ReviveVisuals()
        {
            deadTime = 0f;
            if (controller != null) controller.enabled = true;
            presenter.OnReset();
            animationFeed.Reset();
            if (animatorDriver != null) animatorDriver.ResetPose();
            if (healthBar != null) healthBar.Hide();
        }

        // A CharacterController overrides transform changes while it's enabled, so switch it off to teleport.
        void Teleport(Vector3 position, float yaw)
        {
            if (controller != null) controller.enabled = false;
            transform.SetPositionAndRotation(position, Quaternion.Euler(0f, yaw, 0f));
            if (controller != null) controller.enabled = true;
        }

        // The tuning asset changed while running: retire the old brain cleanly; a new one starts next frame.
        void DropBrain()
        {
            brain.Reset(); // hands back any attack token
            strikes.EndAll();
            brain = null;
            brainTokens = null;
            ReviveVisuals();
        }

        void BindPresenter()
        {
            CacheComponents();
            presenter.Bind(body, Feedback);
            bound = true;
        }

        void CacheComponents()
        {
            if (controller == null) controller = GetComponent<CharacterController>();
            if (combatant == null) combatant = GetComponent<Combatant>();
            if (body == null) body = GetComponent<HumanoidBody>();
            if (animatorDriver == null) animatorDriver = GetComponent<BodyAnimatorDriver>();
            if (healthBar == null) healthBar = GetComponent<EnemyHealthBar>();
        }

        void EnsureSpawnPoint()
        {
            if (hasSpawnPoint) return;
            hasSpawnPoint = true;
            spawnPosition = transform.position;
            spawnYaw = transform.eulerAngles.y;
        }

        // Same spawn point = same seed = the same choices every time you press Play (and after each reset).
        static int SeedFor(Vector3 position)
        {
            unchecked
            {
                int x = Mathf.RoundToInt(position.x * SeedPrecision);
                int y = Mathf.RoundToInt(position.y * SeedPrecision);
                int z = Mathf.RoundToInt(position.z * SeedPrecision);
                return (x * 73856093) ^ (y * 19349663) ^ (z * 83492791);
            }
        }
    }
}
