using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using UnityEngine;
using VaatusRevenge.Core;

namespace VaatusRevenge
{
    // The player in the scene: the bridge between the combat rules (PlayerCombatModel, plain C#) and
    // everything you see and feel. Once per frame (Update, execution order 0: after input and lock-on have
    // run, before enemies and projectiles) it:
    //   1. tells the model about the world: where the camera looks, the ground, lock-on and soft-lock targets
    //   2. ticks the model with this frame's buttons and stick
    //   3. moves the CharacterController exactly once and turns the body to the model's facing
    //   4. turns the model's events into hit checks, projectiles (in their element), hitstop and slow motion
    //   5. keeps an attack's hitbox checking every frame while it's active
    //   6. hands element effects, flashes, sounds, screen shake and rumble to PlayerFeedback, and tells the body's martial-arts
    //      animator what the player is doing (PlayerAnimationFeed -> BodyAnimatorDriver)
    // The rules and every gameplay number live in the model and the tuning assets; this class only wires.
    //
    // Setup: PlayerController.Spawn builds a complete player (editor code can call it too), or add this to a
    // fighter that has a CharacterController, a Combatant, a built HumanoidBody and a BodyAnimatorDriver, then
    // call Configure.
    [DefaultExecutionOrder(0)]
    [DisallowMultipleComponent]
    [SelectionBase]
    [RequireComponent(typeof(CharacterController), typeof(Combatant), typeof(HumanoidBody))]
    public class PlayerController : MonoBehaviour, IDamageReceiver
    {
        // GameplayEvent's signature: every combat-model event, by reference (no copy, no garbage).
        public delegate void PlayerEventHandler(in PlayerEvent e);

        public static PlayerController Instance { get; private set; }

        [Tooltip("Player numbers (health, stamina, movement, input feel) plus the feel settings (shake, rumble, flashes). "
                 + "Empty = built-in Fluid defaults.")]
        [SerializeField] private PlayerTuningAsset tuningAsset;
        [Tooltip("The four elements' move sets and which are learned (RB + a face button switches). When set, it is used "
                 + "instead of the single move set below.")]
        [SerializeField] private ElementLoadoutAsset loadoutAsset;
        [Tooltip("One element's moves, used when there is no loadout: light chain, heavy, sprint and jump attacks, skill, "
                 + "dodge, guard. Empty = built-in Fire (Fluid) defaults.")]
        [SerializeField] private MoveSetAsset moveSetAsset;

        CharacterController body;
        Combatant combatant;
        HumanoidBody rig;
        BodyAnimatorDriver animatorDriver;
        readonly PlayerAnimationFeed animationFeed = new PlayerAnimationFeed();   // combat state -> body animation
        PlayerCombatModel model;
        PlayerFeedback feel;         // element effects, flashes, sounds, shake and rumble (the Unity-side "feel")
        readonly List<HitReport> hits = new List<HitReport>(8);

        // Only used when an asset is missing, so the player still works (with a warning).
        PlayerTuning fallbackTuning;
        ElementMoveSet fallbackMoveSet;
        PlayerFeedbackSettings fallbackFeedback;
        // The loadout handed to the model. Built from the assets and rebuilt only when what they hold changes, so the
        // model keeps one stable object (a new one would end the combo every frame).
        ElementLoadout cachedLoadout;
        // Set by ApplyTuning(PlayerTuning, ElementLoadout): plain data that wins over the assets until Configure is called.
        PlayerTuning runtimeTuning;
        ElementLoadout runtimeLoadout;
        bool warnedNoTuning;
        bool warnedNoMoveSet;

        bool diedRaised;
        int openedAttackId;          // the attack whose hitbox opened (and was already checked) this frame
        float fallbackCameraYaw;     // what "forward" on the stick means when there's no camera rig
        Combatant lockTarget;
        Combatant softTarget;
        Combatant zipTarget;
        string elementMessage = "";
        float elementMessageRemaining;
        StringBuilder debugBuilder;
        string debugText;
        int debugTextFrame = -1;

        static string[] stateNames;

        // Raised once each time the player dies, from this component's Update.
        public event Action Died;

        // Every event the combat model raised this frame, in order, after this component's own handling (hit checks,
        // projectiles, effects). For the tutorial and anything else that follows the fight. Valid during the call only.
        public event PlayerEventHandler GameplayEvent;

        // ---------------------------------------------------------------- building the player

        // Builds a complete player: CharacterController (pivot at the feet), Combatant on Team Player with a
        // chest-height aim point, grey-box body on the Player layer, and this component configured with the
        // given assets. Works in edit mode (the sandbox builder saves the result into the scene) and at runtime.
        public static PlayerController Spawn(Transform parent, Vector3 position, float yaw, PlayerTuningAsset tuning, MoveSetAsset moveSet)
        {
            PlayerController player = SpawnBody(parent, position, yaw, tuning);
            player.Configure(tuning, moveSet);
            player.gameObject.SetActive(true);
            return player;
        }

        // The same with all four elements.
        public static PlayerController Spawn(Transform parent, Vector3 position, float yaw, PlayerTuningAsset tuning, ElementLoadoutAsset loadout)
        {
            PlayerController player = SpawnBody(parent, position, yaw, tuning);
            player.Configure(tuning, loadout);
            player.gameObject.SetActive(true);
            return player;
        }

        // Everything but the move assets; returned switched off (the caller configures it, then switches it on).
        static PlayerController SpawnBody(Transform parent, Vector3 position, float yaw, PlayerTuningAsset tuning)
        {
            PlayerBodySettings settings = tuning != null && tuning.Body != null ? tuning.Body : new PlayerBodySettings();
            float radius = Mathf.Max(0.01f, settings.Radius);
            float height = Mathf.Max(radius * 2f, settings.Height);

            var go = new GameObject("Player");
            // Built switched off so no component wakes up (Awake / OnEnable) half-configured; switched on at the end.
            go.SetActive(false);
            Transform root = go.transform;
            root.SetParent(parent, false);
            root.SetPositionAndRotation(position, Quaternion.Euler(0f, yaw, 0f));

            CharacterController controller = go.AddComponent<CharacterController>();
            controller.height = height;
            controller.radius = radius;
            controller.center = new Vector3(0f, height * 0.5f, 0f); // pivot at the feet
            controller.stepOffset = Mathf.Clamp(settings.StepOffset, 0f, height);
            controller.slopeLimit = settings.SlopeLimit;
            // Unity's default skips moves shorter than a millimetre, which would drop the tiny steps taken during
            // hitstop and make the ground flicker out from under the player.
            controller.minMoveDistance = 0f;

            Transform aim = new GameObject("AimPoint").transform;
            aim.SetParent(root, false);
            aim.localPosition = new Vector3(0f, settings.AimPointHeight, 0f);

            Combatant fighter = go.AddComponent<Combatant>();
            fighter.Configure(VaatusRevenge.Core.Team.Player, aim, radius, height, settings.DisplayName);

            // The jointed martial-artist body (Fire Nation red, gold sash and wraps), animated procedurally.
            HumanoidBody humanoid = go.AddComponent<HumanoidBody>();
            go.AddComponent<BodyAnimatorDriver>();
            BodyLook look = BodyLook.Player();
            look.Cloth = settings.BodyColor;
            if (!string.IsNullOrEmpty(settings.DisplayName)) look.Name = settings.DisplayName;
            humanoid.Build(look);
            Layers.SetRecursively(go, Layers.Player); // after Build, so every body part is on the layer too

            return go.AddComponent<PlayerController>();
        }

        // Edit-time wiring: only stores the assets (safe before Awake). At runtime an existing model picks the
        // new assets up on its next frame; use ApplyTuning to swap presets straight away.
        public void Configure(PlayerTuningAsset tuning, MoveSetAsset moveSet)
        {
            tuningAsset = tuning;
            moveSetAsset = moveSet;
            loadoutAsset = null;
            ClearRuntimeTuning();
        }

        // The same with all four elements (the loadout wins over a single move set).
        public void Configure(PlayerTuningAsset tuning, ElementLoadoutAsset loadout)
        {
            tuningAsset = tuning;
            loadoutAsset = loadout;
            ClearRuntimeTuning();
        }

        void ClearRuntimeTuning()
        {
            runtimeTuning = null;
            runtimeLoadout = null;
            cachedLoadout = null;
            warnedNoTuning = false;
            warnedNoMoveSet = false;
        }

        // Live preset swap (F5 / F6). The model ends any move in progress cleanly and keeps health and stamina
        // as the same share of their maximums. Re-applying the preset already in use changes nothing (values
        // edited in the Inspector apply live anyway), so it doesn't cut the current move short.
        public void ApplyTuning(PlayerTuningAsset tuning, MoveSetAsset moveSet)
        {
            Configure(tuning, moveSet);
            if (model != null) SyncTuning();
        }

        public void ApplyTuning(PlayerTuningAsset tuning, ElementLoadoutAsset loadout)
        {
            Configure(tuning, loadout);
            if (model != null) SyncTuning();
        }

        // Plain data instead of assets (e.g. the tutorial forcing the Fluid rules): wins until Configure is called again.
        // Null keeps what the assets give for that part.
        public void ApplyTuning(PlayerTuning tuning, ElementLoadout loadout)
        {
            runtimeTuning = tuning;
            runtimeLoadout = loadout;
            if (model != null) SyncTuning();
        }

        // Back to full health at a spawn point (death screen, F4).
        public void Respawn(Vector3 position, float yaw)
        {
            CacheComponents();
            // An enabled CharacterController keeps its own idea of where it is and would undo a plain transform move.
            bool controllerOn = body != null && body.enabled;
            if (controllerOn) body.enabled = false;
            transform.SetPositionAndRotation(position, Quaternion.Euler(0f, yaw, 0f));
            if (controllerOn) body.enabled = true;
            fallbackCameraYaw = yaw;

            if (!EnsureModel()) return; // edit time: the teleport is all there is to do
            model.Respawn(yaw);
            diedRaised = false;
            openedAttackId = 0;
            ClearElementMessage();
            feel.ResetAll();
            animationFeed.Reset();
            if (animatorDriver != null) animatorDriver.ResetPose();

            LockOnController lockOn = LockOnController.Instance;
            if (lockOn != null) lockOn.ClearLock();
            ThirdPersonCameraRig cam = ThirdPersonCameraRig.Instance;
            if (cam != null) cam.SnapBehindTarget();
            TimeScaleController.ClearEffects(); // don't come back in slow motion or a freeze-frame
        }

        // ---------------------------------------------------------------- read-only state (HUD, director)

        // Null until the model exists (it's created when play starts).
        public PlayerCombatModel Model => model;
        public PlayerTuningAsset TuningAsset => tuningAsset;
        public MoveSetAsset MoveSetAsset => moveSetAsset;
        public ElementLoadoutAsset LoadoutAsset => loadoutAsset;

        // The feel settings in use (the tuning asset's, or built-in defaults). Never null.
        public PlayerFeedbackSettings Feedback
        {
            get
            {
                if (tuningAsset != null && tuningAsset.Feedback != null) return tuningAsset.Feedback;
                if (fallbackFeedback == null) fallbackFeedback = new PlayerFeedbackSettings();
                return fallbackFeedback;
            }
        }

        public string PresetName
        {
            get
            {
                if (tuningAsset != null && !string.IsNullOrEmpty(tuningAsset.PresetName)) return tuningAsset.PresetName;
                PlayerTuning tuning = ActiveTuning;
                return tuning != null && tuning.PresetName != null ? tuning.PresetName : "";
            }
        }

        public string MoveSetPresetName
        {
            get
            {
                if (loadoutAsset != null) return loadoutAsset.PresetName ?? "";
                return moveSetAsset != null && moveSetAsset.PresetName != null ? moveSetAsset.PresetName : "";
            }
        }

        public bool IsDead => model != null && model.State == PlayerState.Dead;
        public float Health01 => model != null ? Mathf.Clamp01(model.Health / model.MaxHealth) : 1f;

        public float Stamina01
        {
            get
            {
                if (model == null) return 1f;
                return model.MaxStamina > 0f ? Mathf.Clamp01(model.Stamina / model.MaxStamina) : 0f;
            }
        }

        public float Momentum01 => model != null && model.MaxMomentum > 0f ? Mathf.Clamp01(model.Momentum / model.MaxMomentum) : 0f;
        public float MomentumMultiplier => model != null ? model.MomentumMultiplier : 1f;
        public int HealCharges => model != null ? model.HealCharges : 0;
        public int MaxHealCharges => model != null ? model.MaxHealCharges : 0;
        public bool IsCharging => model != null && model.State == PlayerState.Charging;
        public float ChargeLevel01 => model != null ? model.ChargeLevel : 0f;
        public bool InSweetSpot => model != null && model.InSweetSpot;
        // Where the fa jin window sits on a 0..1 charge meter.
        public float SweetSpotStart01 => ChargeShare(true);
        public float SweetSpotEnd01 => ChargeShare(false);
        public bool IsInvulnerable => model != null && model.IsInvulnerable;

        public ElementId CurrentElement
        {
            get
            {
                ElementMoveSet moveSet = ActiveMoveSet;
                return moveSet != null ? moveSet.Element : ElementId.None;
            }
        }

        // The element's display name from the move set (data, not code).
        public string ElementName
        {
            get
            {
                ElementMoveSet moveSet = ActiveMoveSet;
                return moveSet != null && moveSet.DisplayName != null ? moveSet.DisplayName : "";
            }
        }

        // "" unless an element not learned yet was just picked (ElementSwitchDenied); shown for
        // Feedback.ElementMessageDuration real seconds.
        public string ElementMessage => elementMessage;
        public float ElementMessageRemaining => elementMessageRemaining;

        // The rhythm's sounds (chime, tick, finisher, switch whoosh), for anything that wants to sound like the fight, such
        // as the tutorial's "step passed" chime. Null until play starts.
        public RhythmAudio RhythmAudio => feel != null ? feel.Audio : null;

        public string StateName => model != null ? NameOf(model.State) : "";

        // The move running now (attack, charge, plunge, or the dodge's name), "" when free.
        public string MoveName
        {
            get
            {
                if (model == null) return "";
                MoveData move = model.CurrentMove;
                if (move != null) return move.DisplayName ?? "";
                if (model.State != PlayerState.Dodging || model.MoveSet == null || model.MoveSet.Dodge == null) return "";
                return model.MoveSet.Dodge.DisplayName ?? "";
            }
        }

        // Several lines for the F3 panel. Built at most once per frame, and only when something asks for it.
        public string DebugText
        {
            get
            {
                if (model == null) return "Player: the combat model is created when play starts.";
                int frame = Time.frameCount;
                if (debugText == null || debugTextFrame != frame)
                {
                    debugText = BuildDebugText();
                    debugTextFrame = frame;
                }
                return debugText;
            }
        }

        // ---------------------------------------------------------------- IDamageReceiver

        public Team Team => Fighter != null ? Fighter.Team : VaatusRevenge.Core.Team.Player;
        public bool IsAlive => model == null || model.IsAlive;

        // Every hit on the player comes through here. The model decides what happens (dodged, blocked,
        // deflected, hurt...); its events for it arrive at the start of the next frame's Tick.
        public HitResult ReceiveHit(in DamageInfo hit)
        {
            if (!isActiveAndEnabled || !EnsureModel()) return HitResult.Ignored;
            HitResult result = model.ReceiveHit(in hit, transform.forward.ToNumerics());
            // Topple straight away; Died itself is raised from Update so listeners run at a predictable moment.
            if (result.Killed) feel.OnKilled();
            return result;
        }

        // ---------------------------------------------------------------- Unity messages

        // Domain reload is off in this project (fast Play), so statics survive from one Play session to the next.
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics()
        {
            Instance = null;
            stateNames = null;
        }

        void Awake()
        {
            CacheComponents();
            fallbackCameraYaw = transform.eulerAngles.y;
            EnsureModel();
        }

        void OnEnable()
        {
            if (Instance != null && Instance != this)
            {
                Debug.LogWarning("PlayerController: another player ('" + Instance.name + "') is already active. Only the first one is "
                                 + "PlayerController.Instance, which the HUD and the sandbox director read.", this);
                return;
            }
            Instance = this;
        }

        void OnDisable()
        {
            if (Instance == this) Instance = null;
            if (feel != null) feel.Shutdown();
        }

        void OnDestroy()
        {
            if (feel != null) feel.Dispose();
        }

        void OnApplicationFocus(bool hasFocus)
        {
            // Alt-tabbing away mid-buzz must not leave the controller rumbling on the desk.
            if (!hasFocus && feel != null) feel.StopRumble();
        }

        void OnApplicationPause(bool paused)
        {
            if (paused && feel != null) feel.StopRumble();
        }

        void OnApplicationQuit()
        {
            if (feel != null) feel.Shutdown();
        }

        void Update()
        {
            if (!EnsureModel()) return;
            SyncTuning();

            float dt = Time.deltaTime; // 0 while paused: the model then holds still (and keeps button presses)
            PlayerInputReader reader = PlayerInputReader.Instance;
            PlayerInputFrame input = reader != null ? reader.Frame : default(PlayerInputFrame);
            float cameraYaw = CameraYaw();

            // 1-2. Tell the model what's around it and let it decide what happens this frame.
            PlayerWorldState world = BuildWorldState(in input, cameraYaw);
            PlayerTickResult result = model.Tick(dt, in input, in world);

            // 3. Move once, then face where the model says. Nothing else turns the body, so the two never fight.
            if (dt > 0f) MoveBody(result.Velocity, dt);
            transform.rotation = Quaternion.Euler(0f, result.FacingYaw, 0f);

            // 4. Events, in order: gameplay side first (hit checks, projectiles, time effects), then the visuals.
            PlayerFeedbackSettings settings = Feedback;
            feel.RumbleAllowed = model.IsAlive && !TimeScaleController.IsPaused
                                     && (reader == null || (reader.UsingGamepad && reader.GameplayEnabled));
            feel.BeginFrame();
            openedAttackId = 0;
            EventList<PlayerEvent> events = result.Events;
            for (int i = 0; i < events.Count; i++)
            {
                PlayerEvent e = events[i];
                HandleGameplayEvent(in e, settings);
                feel.OnEvent(in e, model, settings);
                animationFeed.OnEvent(in e);
                RaiseGameplayEvent(in e);
            }

            // 5. A hitbox keeps checking while it's open: an enemy can step into a swing after it started.
            if (dt > 0f) QueryActiveAttack(settings);

            UpdateDeath();
            UpdateElementMessage();
            feel.Tick(model, settings, dt, Time.unscaledDeltaTime);
            if (animatorDriver != null)
            {
                // The body aims its strikes at whoever the combat rules are tracking (locked, else soft target).
                Combatant aimAt = lockTarget != null ? lockTarget : softTarget;
                animatorDriver.SetInput(aimAt != null
                    ? animationFeed.Build(model, dt, true, transform.position.ToNumerics(), aimAt.AimPoint.position.ToNumerics())
                    : animationFeed.Build(model, dt));
            }
        }

        // ---------------------------------------------------------------- one frame, step by step

        PlayerWorldState BuildWorldState(in PlayerInputFrame input, float cameraYaw)
        {
            Vector3 position = transform.position;
            var world = new PlayerWorldState
            {
                Position = position.ToNumerics(),
                Grounded = body != null && body.enabled && body.isGrounded, // from last frame's Move
                CameraYaw = cameraYaw,
                SelfRadius = combatant != null ? combatant.Radius : 0f,
                SelfHeight = combatant != null ? combatant.Height : 0f,
                RealDeltaTime = Time.unscaledDeltaTime, // the heavy's charge clock runs on real time (slow-mo doesn't shift it)
            };
            Combatant nearest = NearestEnemy(position);  // backing away from it drains Momentum, even without lock-on
            if (nearest != null)
            {
                world.HasNearestEnemy = true;
                world.NearestEnemyPosition = nearest.Feet.ToNumerics();
                world.NearestEnemyRadius = nearest.Radius;
            }

            LockOnController lockOn = LockOnController.Instance;
            lockTarget = UsableTarget(lockOn != null ? lockOn.Target : null);
            softTarget = null;
            float aimYaw = model.GetAimYaw(input.Move, cameraYaw);

            // The zip strike reaches much further than the soft lock. Locked on, it goes for the lock target if that's
            // in reach; otherwise the best candidate where you aim.
            zipTarget = FindZipTarget(position, aimYaw, lockTarget);
            if (zipTarget != null)
            {
                world.HasZipTarget = true;
                world.ZipTargetPosition = zipTarget.Feet.ToNumerics();
                world.ZipTargetAimPoint = zipTarget.AimPoint.position.ToNumerics();
                world.ZipTargetRadius = zipTarget.Radius;
            }

            if (lockTarget != null)
            {
                world.HasLockTarget = true;
                world.LockTargetPosition = lockTarget.Feet.ToNumerics();
                world.LockTargetAimPoint = lockTarget.AimPoint.position.ToNumerics();
                world.LockTargetRadius = lockTarget.Radius;
                return world;
            }

            softTarget = FindSoftTarget(position, aimYaw, model.IsStickNeutral(input.Move));
            if (softTarget != null)
            {
                world.HasSoftTarget = true;
                world.SoftTargetPosition = softTarget.Feet.ToNumerics();
                world.SoftTargetAimPoint = softTarget.AimPoint.position.ToNumerics();
                world.SoftTargetRadius = softTarget.Radius;
            }
            return world;
        }

        // The stick is relative to the camera: pushing up walks away from it, whichever way the body faces.
        float CameraYaw()
        {
            ThirdPersonCameraRig cam = ThirdPersonCameraRig.Instance;
            // With no camera rig, a fixed reference (the spawn facing) keeps the stick steady. The body's own facing
            // would turn with every step, so holding one direction would run in circles.
            return cam != null ? cam.Yaw : fallbackCameraYaw;
        }

        // Not locked on, attacks still turn toward the enemy you're obviously going for: the nearest one within
        // range and angle of where you aim (SoftLockSelector), as long as no wall is in the way.
        // With the stick neutral it looks all round (a counter straight out of a dodge still finds the attacker).
        Combatant FindSoftTarget(Vector3 position, float aimYaw, bool stickNeutral)
        {
            float angle = stickNeutral ? 180f : model.Tuning.SoftLockAngle;
            PlayerTuning tuning = model.Tuning;
            System.Numerics.Vector3 self = position.ToNumerics();
            Vector3 eye = combatant != null ? combatant.AimPoint.position : position;
            Combatant best = null;
            float bestScore = float.MaxValue;
            List<Combatant> all = Combatant.All;
            for (int i = 0; i < all.Count; i++)
            {
                Combatant candidate = UsableTarget(all[i]);
                if (candidate == null) continue;
                if (!SoftLockSelector.TryScore(self, aimYaw, candidate.Feet.ToNumerics(), tuning.SoftLockRange, angle,
                        tuning.SoftLockMaxHeightDifference, out float score) || score >= bestScore) continue;
                if (CombatPhysics.IsBlocked(eye, candidate.AimPoint.position)) continue;
                best = candidate;
                bestScore = score;
            }
            return best;
        }

        // The zip strike's target (see ZipStrikeSettings): like the soft lock, but much further and a little narrower.
        Combatant FindZipTarget(Vector3 position, float aimYaw, Combatant locked)
        {
            ElementMoveSet moves = model.MoveSet;
            ZipStrikeSettings zip = moves != null ? moves.Zip : null;
            if (zip == null || moves.ZipStrike == null) return null;
            System.Numerics.Vector3 self = position.ToNumerics();
            Vector3 eye = combatant != null ? combatant.AimPoint.position : position;
            if (locked != null)
            {
                bool inReach = SoftLockSelector.TryScore(self, aimYaw, locked.Feet.ToNumerics(), zip.Range, 360f,
                    zip.MaxHeightDifference, out _);
                return inReach && !CombatPhysics.IsBlocked(eye, locked.AimPoint.position) ? locked : null;
            }
            Combatant best = null;
            float bestScore = float.MaxValue;
            List<Combatant> all = Combatant.All;
            for (int i = 0; i < all.Count; i++)
            {
                Combatant candidate = UsableTarget(all[i]);
                if (candidate == null) continue;
                if (!SoftLockSelector.TryScore(self, aimYaw, candidate.Feet.ToNumerics(), zip.Range, zip.AngleDegrees,
                        zip.MaxHeightDifference, out float score) || score >= bestScore) continue;
                if (CombatPhysics.IsBlocked(eye, candidate.AimPoint.position)) continue;
                best = candidate;
                bestScore = score;
            }
            return best;
        }

        // The closest living enemy, any distance (the model decides what's near enough to count).
        Combatant NearestEnemy(Vector3 position)
        {
            Combatant best = null;
            float bestDistanceSq = float.MaxValue;
            List<Combatant> all = Combatant.All;
            for (int i = 0; i < all.Count; i++)
            {
                Combatant candidate = UsableTarget(all[i]);
                if (candidate == null) continue;
                float distanceSq = (candidate.Feet - position).sqrMagnitude;
                if (distanceSq >= bestDistanceSq) continue;
                best = candidate;
                bestDistanceSq = distanceSq;
            }
            return best;
        }

        // A fighter we can aim at: still there, alive, not us, not on our team. Destroyed fighters read as null.
        Combatant UsableTarget(Combatant candidate)
        {
            if (candidate == null || candidate == combatant || !candidate.isActiveAndEnabled || !candidate.IsAlive) return null;
            if (combatant != null && candidate.Team == combatant.Team) return null;
            return candidate;
        }

        void MoveBody(System.Numerics.Vector3 velocity, float dt)
        {
            if (body == null || !body.enabled) return;
            Vector3 step = velocity.ToUnity() * dt;
            // Safety net: a broken tuning value must never hand the physics engine a NaN.
            if (!IsFinite(step)) return;
            body.Move(step);
        }

        void HandleGameplayEvent(in PlayerEvent e, PlayerFeedbackSettings settings)
        {
            switch (e.Type)
            {
                case PlayerEventType.AttackActiveStart:
                {
                    openedAttackId = e.AttackId;
                    MoveData move = e.Move;
                    if (move == null) break;
                    DamageInfo damage = model.BuildDamage(in e);
                    MeleeHitQuery.Arc(e.Origin.ToUnity(), e.Direction.ToUnity(), move.Range, move.ArcDegrees, move.VerticalReach, damage, hits);
                    ResolveHits(move, e.AttackId, damage.Hitstop, damage.Element, e.ChargeTier == ChargeTier.FaJin, settings);
                    break;
                }
                case PlayerEventType.AttackActiveEnd:
                    MeleeHitQuery.EndAttack(e.AttackId); // this swing may hit the same enemy again next time
                    break;
                case PlayerEventType.ProjectileLaunched:
                    LaunchProjectile(in e);
                    break;
                case PlayerEventType.PlungeImpact:
                {
                    MoveData move = e.Move;
                    if (move != null)
                    {
                        // The landing ring is a one-off sphere check from waist height (a centre on the floor would
                        // have its line of sight blocked by the floor itself).
                        DamageInfo damage = model.BuildDamage(in e);
                        Vector3 centre = e.Origin.ToUnity() + Vector3.up * BodyCentreHeight;
                        MeleeHitQuery.Sphere(centre, e.Radius, damage, hits);
                        ResolveHits(move, e.AttackId, damage.Hitstop, damage.Element, e.ChargeTier == ChargeTier.FaJin, settings);
                    }
                    MeleeHitQuery.EndAttack(e.AttackId);
                    break;
                }
                case PlayerEventType.PerfectDodge:
                    // The reward for dodging at the last instant: the world slows for a moment (real-time seconds).
                    if (model.IsAlive) TimeScaleController.SlowMotion(e.Duration, e.TimeScale);
                    break;
                case PlayerEventType.Deflected:
                    if (model.IsAlive && settings.DeflectHitstop > 0f) TimeScaleController.Hitstop(settings.DeflectHitstop);
                    break;
                case PlayerEventType.ElementSwitchDenied:
                    // Only an element you haven't learned gets a message; a cooldown or same-element pick shakes the HUD's
                    // element wheel instead (HudElementWheel).
                    if (e.DenyReason == SwitchDeniedReason.NotLearned) ShowElementMessage(e.Element, settings);
                    break;
            }
        }

        void RaiseGameplayEvent(in PlayerEvent e)
        {
            PlayerEventHandler handler = GameplayEvent;
            if (handler == null) return;
            try
            {
                handler(in e);
            }
            catch (Exception exception)
            {
                Debug.LogException(exception, this); // a broken listener must not break the player's frame
            }
        }

        // What our swing touched. Each target already decided the outcome; we react: Momentum for clean hits,
        // hitstop and shake once per swing, a stagger for us if an enemy deflected it.
        void ResolveHits(MoveData move, int attackId, float hitstop, ElementId element, bool faJin, PlayerFeedbackSettings settings)
        {
            if (hits.Count == 0) return;
            bool clean = false;
            bool parried = false;
            for (int i = 0; i < hits.Count; i++)
            {
                HitReport report = hits[i];
                model.OnAttackLanded(in report.Result, attackId, IsAirborne(report.Target)); // counter, Momentum: once per attack
                feel.OnHitReport(in report, element, settings);
                if (report.Result.Outcome == HitOutcome.Hit) clean = true;
                else if (report.Result.Outcome == HitOutcome.Parried) parried = true;
            }
            hits.Clear();
            if (clean)
            {
                // Hitstop: the attacker freezes the frame on a clean hit (the hit system never does it). The freeze
                // is what makes a blow feel like it connected instead of passing through.
                TimeScaleController.Hitstop(hitstop);
                feel.OnCleanHit(IsHeavyFeel(move), faJin, settings);
            }
            if (parried) model.OnParried(); // once, however many enemies deflected the same swing
        }

        // A separate method so the little closure below is only created when a projectile really is launched
        // (once per cast, never per frame). It remembers the damage and AttackId, because a projectile can land
        // after the next attack has already started.
        void LaunchProjectile(in PlayerEvent e)
        {
            MoveData move = e.Move;
            if (move == null) return;
            DamageInfo damage = model.BuildDamage(in e);
            FireProjectile.Launch(e.Origin.ToUnity(), e.Direction.ToUnity(), move.Projectile, damage, ProjectileVisuals.ForElement(e.Element),
                report => OnProjectileHit(report, damage));
        }

        void OnProjectileHit(HitReport report, DamageInfo damage)
        {
            // A projectile can land after the player died, respawned or was removed from the scene.
            if (this == null || !isActiveAndEnabled || model == null || !model.IsAlive) return;
            model.OnAttackLanded(in report.Result, damage.AttackId, IsAirborne(report.Target));
            PlayerFeedbackSettings settings = Feedback;
            feel.OnHitReport(in report, damage.Element, settings);
            // A deflected projectile just fizzles: the caster isn't staggered from across the arena.
            if (report.Result.Outcome != HitOutcome.Hit) return;
            TimeScaleController.Hitstop(damage.Hitstop);
            feel.OnCleanHit(false, false, settings);
        }

        // Every frame after the first one of an active window (the event already checked that one). MeleeHitQuery
        // remembers who this AttackId hit, so a target inside the swing for several frames is hit once.
        void QueryActiveAttack(PlayerFeedbackSettings settings)
        {
            if (!model.IsAttackActive) return;
            int attackId = model.ActiveAttackId;
            MoveData move = model.CurrentMove;
            if (move == null || attackId == 0 || attackId == openedAttackId) return;
            Vector3 origin = model.GetStrikeOrigin(transform.position.ToNumerics()).ToUnity(); // follows the lunge
            DamageInfo damage = model.BuildCurrentDamage();
            MeleeHitQuery.Arc(origin, model.Forward.ToUnity(), move.Range, move.ArcDegrees, move.VerticalReach, damage, hits);
            ResolveHits(move, attackId, damage.Hitstop, damage.Element, model.CurrentChargeTier == ChargeTier.FaJin, settings);
        }

        void UpdateDeath()
        {
            if (model.State != PlayerState.Dead)
            {
                diedRaised = false; // alive (again)
                return;
            }
            if (diedRaised) return;
            diedRaised = true;
            feel.OnKilled();
            Action handler = Died;
            if (handler == null) return;
            try
            {
                handler();
            }
            catch (Exception exception)
            {
                Debug.LogException(exception, this); // a broken listener must not break the player's frame
            }
        }

        // The model switches elements itself (RB + face button / 1-4, from PlayerInputFrame.ElementSelect); picking one
        // not learned yet raises ElementSwitchDenied, which shows a message for a moment.
        void ShowElementMessage(ElementId picked, PlayerFeedbackSettings settings)
        {
            elementMessageRemaining = Mathf.Max(0f, settings.ElementMessageDuration);
            elementMessage = elementMessageRemaining > 0f ? LockedElementMessage(settings.ElementLockedMessage, picked) : "";
        }

        void UpdateElementMessage()
        {
            if (elementMessageRemaining <= 0f) return;
            // Real time: it's UI, so it shouldn't hang around longer during hitstop or slow motion.
            elementMessageRemaining -= Time.unscaledDeltaTime;
            if (elementMessageRemaining <= 0f) ClearElementMessage();
        }

        // A target in the air (juggled): its CharacterController isn't touching the ground.
        static bool IsAirborne(Combatant target)
        {
            return target != null && target.TryGetComponent(out CharacterController controller) && controller.enabled && !controller.isGrounded;
        }

        void ClearElementMessage()
        {
            elementMessage = "";
            elementMessageRemaining = 0f;
        }

        // ---------------------------------------------------------------- model and tuning

        bool EnsureModel()
        {
            if (model != null) return true;
            if (!Application.isPlaying) return false; // edit time: nothing to simulate
            CacheComponents();
            // OwnerId must be our Combatant's id: the hit system uses it to stop our own swings hitting us.
            model = new PlayerCombatModel(ResolveTuning(), ResolveLoadout(), combatant != null ? combatant.Id : 0, transform.eulerAngles.y);
            return true;
        }

        // The model reads the tuning objects every frame, which is what makes Inspector edits apply live. If the
        // asset now holds different objects (another asset was assigned, or Unity rebuilt its data after an edit),
        // point the model at them. Runs at the start of every Update, so Configure at runtime takes effect there.
        void SyncTuning()
        {
            PlayerTuning tuning = ResolveTuning();
            ElementLoadout loadout = ResolveLoadout();
            if (!ReferenceEquals(model.Tuning, tuning) || !ReferenceEquals(model.Loadout, loadout)) model.ApplyTuning(tuning, loadout);
        }

        PlayerTuning ResolveTuning()
        {
            if (runtimeTuning != null) return runtimeTuning;
            if (tuningAsset != null && tuningAsset.Tuning != null) return tuningAsset.Tuning;
            if (!warnedNoTuning)
            {
                warnedNoTuning = true;
                Debug.LogWarning("PlayerController on '" + name + "' has no PlayerTuningAsset, so it's using the built-in Fluid numbers. "
                                 + "Assign one (or call Configure) to tune the player in the Inspector.", this);
            }
            if (fallbackTuning == null) fallbackTuning = PlayerTuning.CreateFluid();
            return fallbackTuning;
        }

        // The runtime loadout if one was applied, else the loadout asset's, else the single move set's. The cached object is
        // kept for as long as it still holds what the assets hold.
        ElementLoadout ResolveLoadout()
        {
            if (runtimeLoadout != null) return runtimeLoadout;
            if (loadoutAsset != null)
            {
                if (!loadoutAsset.Matches(cachedLoadout)) cachedLoadout = loadoutAsset.ToLoadout();
                return cachedLoadout;
            }
            ElementMoveSet single = ResolveMoveSet();
            if (cachedLoadout == null || !ReferenceEquals(cachedLoadout.Get(cachedLoadout.Starting), single)) cachedLoadout = ElementLoadout.FromSingle(single);
            return cachedLoadout;
        }

        ElementMoveSet ResolveMoveSet()
        {
            if (moveSetAsset != null && moveSetAsset.MoveSet != null) return moveSetAsset.MoveSet;
            if (!warnedNoMoveSet)
            {
                warnedNoMoveSet = true;
                Debug.LogWarning("PlayerController on '" + name + "' has no MoveSetAsset, so it's using the built-in Fire (Fluid) moves. "
                                 + "Assign one (or call Configure) to tune the moves in the Inspector.", this);
            }
            if (fallbackMoveSet == null) fallbackMoveSet = ElementMoveSet.CreateFireFluid();
            return fallbackMoveSet;
        }

        // For read-only getters: never warns or creates anything.
        PlayerTuning ActiveTuning
        {
            get
            {
                if (model != null) return model.Tuning;
                if (runtimeTuning != null) return runtimeTuning;
                return tuningAsset != null ? tuningAsset.Tuning : null;
            }
        }

        ElementMoveSet ActiveMoveSet
        {
            get
            {
                if (model != null) return model.MoveSet;
                if (runtimeLoadout != null) return runtimeLoadout.Get(runtimeLoadout.FirstUsable());
                if (loadoutAsset != null && loadoutAsset.Fire != null) return loadoutAsset.Fire.MoveSet;
                return moveSetAsset != null ? moveSetAsset.MoveSet : null;
            }
        }

        void CacheComponents()
        {
            if (body == null) body = GetComponent<CharacterController>();
            if (combatant == null) combatant = GetComponent<Combatant>();
            if (rig == null) rig = GetComponent<HumanoidBody>();
            if (animatorDriver == null) animatorDriver = GetComponent<BodyAnimatorDriver>();
            if (feel == null) feel = new PlayerFeedback(transform, rig, combatant);
        }

        Combatant Fighter
        {
            get
            {
                if (combatant == null) combatant = GetComponent<Combatant>();
                return combatant;
            }
        }

        // The middle of the body (the capsule's centre, at the waist).
        float BodyCentreHeight
        {
            get
            {
                if (body != null) return body.center.y;
                return combatant != null ? combatant.Height * 0.5f : 0f;
            }
        }

        // The light chain's last move is its finisher: it hits harder, so it gets the heavy shake and rumble.
        bool IsHeavyFeel(MoveData move)
        {
            if (move == null) return false;
            if (move.Kind == HitKind.Heavy || move.Kind == HitKind.Sprint || move.Kind == HitKind.Plunge) return true;
            MoveData[] chain = model.MoveSet != null ? model.MoveSet.LightChain : null;
            return chain != null && chain.Length > 1 && move == chain[chain.Length - 1];
        }

        float ChargeShare(bool start)
        {
            ElementMoveSet moveSet = ActiveMoveSet;
            ChargeSettings charge = moveSet != null ? moveSet.Charge : null;
            if (charge == null || !(charge.MaxChargeTime > 0f)) return 0f;
            return Mathf.Clamp01((start ? charge.SweetSpotStart : charge.SweetSpotEnd) / charge.MaxChargeTime);
        }

        // ---------------------------------------------------------------- text

        // The element's display name comes from its move set (data); the enum name is only a fallback for an empty slot.
        string LockedElementMessage(string template, ElementId element)
        {
            ElementMoveSet set = model != null && model.Loadout != null ? model.Loadout.Get(element) : null;
            string elementName = set != null && !string.IsNullOrEmpty(set.DisplayName) ? set.DisplayName : element.ToString();
            return string.IsNullOrEmpty(template) ? elementName : template.Replace("{0}", elementName);
        }

        // Enum names cached once: ToString() on an enum makes a new string every call, and the HUD asks every frame.
        static string NameOf(PlayerState state)
        {
            if (stateNames == null)
            {
                int max = 0;
                foreach (PlayerState value in Enum.GetValues(typeof(PlayerState))) max = Math.Max(max, (int)value);
                stateNames = new string[max + 1];
                for (int i = 0; i < stateNames.Length; i++) stateNames[i] = ((PlayerState)i).ToString();
            }
            int index = (int)state;
            return index >= 0 && index < stateNames.Length ? stateNames[index] : state.ToString();
        }

        string BuildDebugText()
        {
            if (debugBuilder == null) debugBuilder = new StringBuilder(640);
            StringBuilder sb = debugBuilder;
            sb.Length = 0;
            CultureInfo c = CultureInfo.InvariantCulture;
            MoveData move = model.CurrentMove;

            sb.AppendFormat(c, "Preset {0} (moves {1}) | element {2}\n", PresetName, MoveSetPresetName, ElementName);
            sb.AppendFormat(c, "State {0} {1} | phase {2} {3:0.00}s | action {4:0.00}s\n",
                StateName, MoveName, model.Phase, model.PhaseTime, model.ActionTime);
            if (move != null)
            {
                sb.AppendFormat(c, "Frame data: startup {0:0.00} active {1:0.00} recovery {2:0.00} | chain {3} | counter {4} | charge tier {5}\n",
                    move.Startup, move.Active, move.Recovery, model.ChainIndex, YesNo(model.IsCounterAttack), model.CurrentChargeTier);
            }
            sb.AppendFormat(c, "HP {0:0}/{1:0} | stamina {2:0}/{3:0} | poise {4:0} | momentum {5:0} (x{6:0.00}) | heal {7}/{8}\n",
                model.Health, model.MaxHealth, model.Stamina, model.MaxStamina, model.Poise, model.Momentum,
                model.MomentumMultiplier, model.HealCharges, model.MaxHealCharges);
            if (model.BufferedCommand != PlayerCommand.None)
            {
                sb.AppendFormat(c, "Buffered {0}{1} ({2:0.00}s old)", model.BufferedCommand,
                    model.BufferedCommandQueued ? " queued" : "", model.BufferedCommandAge);
            }
            else
            {
                sb.Append("Buffered -");
            }
            sb.AppendFormat(c, " | i-frames {0} | guard {1} | deflect window {2} (lockout {3:0.00}s)\n",
                YesNo(model.IsInvulnerable), YesNo(model.IsGuarding), YesNo(model.IsDeflectWindowOpen), model.DeflectLockoutRemaining);
            sb.AppendFormat(c, "Charge {0:0.00}s sweet spot {1} | counter window {2:0.00}s | stagger {3:0.00}s | sprint {4:0.00}s\n",
                model.ChargeTime, YesNo(model.InSweetSpot), model.CounterWindowRemaining, model.StaggerRemaining, model.SprintTime);
            Vector3 velocity = model.Velocity.ToUnity();
            sb.AppendFormat(c, "Velocity ({0:0.0}, {1:0.0}, {2:0.0}) | grounded {3} | lock {4} | soft lock {5}",
                velocity.x, velocity.y, velocity.z, YesNo(model.IsGrounded), TargetName(lockTarget), TargetName(softTarget));
            return sb.ToString();
        }

        static string YesNo(bool value)
        {
            return value ? "yes" : "no";
        }

        static string TargetName(Combatant target)
        {
            return target != null ? target.DisplayName : "-";
        }

        static bool IsFinite(Vector3 v)
        {
            return !(float.IsNaN(v.x) || float.IsNaN(v.y) || float.IsNaN(v.z)
                     || float.IsInfinity(v.x) || float.IsInfinity(v.y) || float.IsInfinity(v.z));
        }
    }
}
