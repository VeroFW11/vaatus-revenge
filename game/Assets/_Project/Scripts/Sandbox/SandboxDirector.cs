using System.Collections.Generic;
using System.Globalization;
using UnityEngine;
using UnityEngine.InputSystem;

namespace VaatusRevenge
{
    // Runs the combat sandbox around the fight itself: the sandbox keys, the death loop and the short on-screen
    // messages ("toasts") that CombatHud draws.
    //   F2       debug slow motion, for studying moves
    //   F4       respawn the player at the spawn point (while dead: skip the wait)
    //   F5 / F6  switch the player to the Fluid / Punishing preset, live, to compare the two feels (with loadouts
    //            assigned, the whole loadout swaps: all four elements' moves)
    //   T        reset every enemy and sparring dummy (projectiles in flight are cleared too)
    //   Esc      pause / resume (PlayerInputReader also releases the mouse on Esc)
    // F1 (controls) and F3 (debug panel) belong to CombatHud; View, F7 and F8 (the tutorial) to TutorialDirector. None
    // of these keys is bound to a gameplay action.
    //
    // The tutorial borrows the arena through this director: it hides the enemies (SetEnemiesActive), forces the Fluid
    // preset and locks F5 / F6 (PresetsLocked), and moves the respawn point (SpawnOverride) while it runs.
    //
    // The death loop is souls-like: when the player dies, wait a moment, then respawn them and reset every
    // enemy, so each attempt starts from the same situation. That repetition is what makes a hard fight feel
    // fair: you learn the fight, instead of facing a new random mess each time.
    //
    // Timers run on real (unscaled) time, so hitstop and slow motion don't stretch them, and they stop while
    // paused. Setup from code (the sandbox builder): AddComponent, Configure(...), then set Player and call
    // RegisterEnemy / RegisterDummy for every fighter it should reset.
    [DefaultExecutionOrder(-90)] // after PlayerInputReader (-100); before lock-on (-50) and the player (0), so a
                                 // pause, respawn or preset swap applies to everyone in the same frame
    [DisallowMultipleComponent]
    public class SandboxDirector : MonoBehaviour
    {
        // The last part of a toast's life fades it out instead of letting it pop off the screen.
        const float ToastFadeTime = 0.35f;

        public static SandboxDirector Instance { get; private set; }

        [Header("Presets (F5 / F6)")]
        [SerializeField] private PlayerTuningAsset fluidTuning;
        [SerializeField] private MoveSetAsset fluidMoves;
        [SerializeField] private PlayerTuningAsset punishingTuning;
        [SerializeField] private MoveSetAsset punishingMoves;
        [Tooltip("The four elements for each preset. When set, F5 / F6 swap these whole loadouts instead of the single move sets.")]
        [SerializeField] private ElementLoadoutAsset fluidLoadout;
        [SerializeField] private ElementLoadoutAsset punishingLoadout;

        [Header("Fighters")]
        [Tooltip("Where the player respawns: its position is the feet, its forward the facing. Empty = where the player started.")]
        [SerializeField] private Transform playerSpawn;
        [Tooltip("The player. Empty = PlayerController.Instance.")]
        [SerializeField] private PlayerController player;
        [Tooltip("Enemies to reset. Every active enemy (EnemyController.All) is reset too, so this list only has to "
                 + "cover the ones the builder placed.")]
        [SerializeField] private List<EnemyController> enemies = new List<EnemyController>();
        [Tooltip("Sparring dummies to reset. Every active dummy (TrainingDummy.All) is reset too.")]
        [SerializeField] private List<TrainingDummy> dummies = new List<TrainingDummy>();

        [Header("Death and respawn")]
        [Tooltip("Real seconds between the player dying and respawning at the spawn point.")]
        [SerializeField] private float respawnDelay = 3f;
        [Tooltip("Souls-like: dying also resets every enemy and dummy, so each attempt starts fresh.")]
        [SerializeField] private bool resetEnemiesOnDeath = true;
        [Tooltip("When every enemy is dead, bring them all back after a short pause, so the sandbox never runs empty.")]
        [SerializeField] private bool autoResetEnemiesWhenAllDead = true;
        [Tooltip("Real seconds between the last enemy dying and everyone coming back.")]
        [SerializeField] private float allDeadResetDelay = 4f;

        [Header("Messages")]
        [Tooltip("How long a message such as \"Preset: Punishing\" stays on screen, in real seconds.")]
        [SerializeField] private float toastDuration = 1.8f;

        // Reused every frame, so gathering fighters never allocates.
        readonly List<EnemyController> enemyBuffer = new List<EnemyController>(16);
        readonly List<TrainingDummy> dummyBuffer = new List<TrainingDummy>(16);

        PlayerController hookedPlayer;
        bool playerWasDead;
        bool hasStartPoint;
        Vector3 startPosition;
        float startYaw;
        bool paused;
        bool respawnPending;
        float respawnRemaining;
        float allDeadRemaining = -1f; // < 0 = no reset counting down
        string toastText = "";
        float toastRemaining;
        bool warnedNoPlayer;
        bool punishingActive;          // the preset last applied (the player is built with Fluid)
        readonly List<EnemyController> hiddenEnemies = new List<EnemyController>(8);

        public bool IsPaused => paused;
        // True between the player dying and the automatic respawn.
        public bool RespawnPending => respawnPending;
        public float RespawnRemaining => respawnPending ? Mathf.Max(0f, respawnRemaining) : 0f;
        // The current message, or "" when there is none. ToastAlpha fades it out at the end.
        public string ToastText => toastRemaining > 0f ? toastText : "";
        public float ToastAlpha => toastRemaining <= 0f ? 0f : Mathf.Clamp01(toastRemaining / ToastFadeTime);

        // The player this director respawns: the one set here (the builder sets it), else PlayerController.Instance.
        public PlayerController Player
        {
            get { return ResolvePlayer(); }
            set { player = value; }
        }

        public Transform PlayerSpawn
        {
            get { return playerSpawn; }
            set { playerSpawn = value; }
        }

        public float RespawnDelay
        {
            get { return respawnDelay; }
            set { respawnDelay = Mathf.Max(0f, value); }
        }

        public bool AutoResetEnemiesWhenAllDead
        {
            get { return autoResetEnemiesWhenAllDead; }
            set { autoResetEnemiesWhenAllDead = value; }
        }

        // True after F6 (or ApplyPunishingPreset) until F5.
        public bool IsPunishing => punishingActive;

        // While true, F5 / F6 only say why they can't be used (the tutorial runs on Fluid).
        public bool PresetsLocked { get; set; }

        // When set, deaths and respawns put the player here instead of at PlayerSpawn (the tutorial's start).
        public Transform SpawnOverride { get; set; }

        // An enemy SetEnemiesActive leaves alone (the tutorial's sparring partner).
        public EnemyController TutorialPartner { get; set; }

        // False while SetEnemiesActive(false) has them hidden.
        public bool EnemiesActive => hiddenEnemies.Count == 0;

        // One-call setup that works at edit time (the builder calls it before saving the scene) and in play mode.
        public void Configure(PlayerTuningAsset fluidTuning, MoveSetAsset fluidMoves,
                              PlayerTuningAsset punishingTuning, MoveSetAsset punishingMoves, Transform playerSpawn)
        {
            this.fluidTuning = fluidTuning;
            this.fluidMoves = fluidMoves;
            this.punishingTuning = punishingTuning;
            this.punishingMoves = punishingMoves;
            this.playerSpawn = playerSpawn;
        }

        // The same with all four elements per preset.
        public void Configure(PlayerTuningAsset fluidTuning, ElementLoadoutAsset fluidLoadout,
                              PlayerTuningAsset punishingTuning, ElementLoadoutAsset punishingLoadout, Transform playerSpawn)
        {
            this.fluidTuning = fluidTuning;
            this.fluidLoadout = fluidLoadout;
            this.punishingTuning = punishingTuning;
            this.punishingLoadout = punishingLoadout;
            this.playerSpawn = playerSpawn;
        }

        // Shows or hides every enemy the director resets (the tutorial clears the arena for the sparring partner).
        // Sparring dummies stay. Hiding clears projectiles in flight; showing brings the hidden ones back fresh at their
        // spawn points, so nobody resumes a swing from before.
        public void SetEnemiesActive(bool active)
        {
            if (!active)
            {
                if (hiddenEnemies.Count > 0) return;
                FireProjectile.ClearAll();
                GatherEnemies();
                for (int i = 0; i < enemyBuffer.Count; i++)
                {
                    EnemyController enemy = enemyBuffer[i];
                    if (ReferenceEquals(enemy, TutorialPartner)) continue;
                    hiddenEnemies.Add(enemy);
                    enemy.gameObject.SetActive(false);
                }
                allDeadRemaining = -1f;
                return;
            }
            for (int i = 0; i < hiddenEnemies.Count; i++)
            {
                EnemyController enemy = hiddenEnemies[i];
                if (enemy == null) continue;
                enemy.gameObject.SetActive(true);
                enemy.ResetEnemy();
            }
            hiddenEnemies.Clear();
        }

        // The two presets by call (the tutorial forces Fluid, then puts back what was there). False when the preset
        // can't be applied (no player or no assets: the reason is shown).
        public bool ApplyFluidPreset(bool announce)
        {
            return ApplyPreset(fluidTuning, fluidLoadout, fluidMoves, "F5", false, announce);
        }

        public bool ApplyPunishingPreset(bool announce)
        {
            return ApplyPreset(punishingTuning, punishingLoadout, punishingMoves, "F6", true, announce);
        }

        // Puts the player at the spawn point (SpawnOverride while it's set) without a message: a fresh start there.
        public bool PlacePlayerAtSpawn()
        {
            respawnPending = false;
            return RespawnPlayerAtSpawn();
        }

        public void RegisterEnemy(EnemyController enemy)
        {
            if (enemy != null && !enemies.Contains(enemy)) enemies.Add(enemy);
        }

        public void RegisterDummy(TrainingDummy dummy)
        {
            if (dummy != null && !dummies.Contains(dummy)) dummies.Add(dummy);
        }

        // Shows a short message in the middle of the screen (drawn by CombatHud).
        public void ShowToast(string message)
        {
            ShowToast(message, toastDuration);
        }

        // The same, for this many real seconds (longer for something the player should read, like the tutorial hint).
        public void ShowToast(string message, float seconds)
        {
            toastText = message ?? "";
            toastRemaining = string.IsNullOrEmpty(toastText) ? 0f : Mathf.Max(ToastFadeTime, seconds);
        }

        // Pause stops time (TimeScaleController) and gameplay input (PlayerInputReader); resuming restores both.
        public void SetPaused(bool pause)
        {
            paused = pause;
            TimeScaleController.SetPaused(pause);
            PlayerInputReader reader = PlayerInputReader.Instance;
            if (reader != null) reader.SetGameplayEnabled(!pause);
        }

        // F4: respawn the player at the spawn point. While a death respawn is counting down, do it now instead.
        public void RespawnPlayer()
        {
            if (respawnPending)
            {
                FinishDeathRespawn();
                return;
            }
            if (RespawnPlayerAtSpawn()) ShowToast("Respawned");
        }

        // T: every enemy and sparring dummy back to its spawn point, full health, alive.
        public void ResetEnemies()
        {
            ResetAllEnemies(true);
        }

        // With domain reload off (this project's Enter Play Mode settings), statics survive between Play
        // sessions, so they are cleared by hand when play starts.
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics()
        {
            Instance = null;
        }

        void OnEnable()
        {
            if (Instance != null && Instance != this)
            {
                // Two directors would both toggle pause on Esc (cancelling each other out), so only one may run.
                Debug.LogWarning("SandboxDirector: another one ('" + Instance.name + "') is already active, so this one on '"
                                 + name + "' switches itself off. Keep a single director in the scene.", this);
                enabled = false;
                return;
            }
            Instance = this;
        }

        void OnDisable()
        {
            UnhookPlayer();
            if (Instance != this) return;
            // Never leave the game frozen or deaf to input because the director went away (e.g. Play stopped).
            if (paused) SetPaused(false);
            respawnPending = false;
            Instance = null;
        }

        void Update()
        {
            if (Instance != this) return;
            float realDt = Time.unscaledDeltaTime;
            PlayerController current = ResolvePlayer();
            HookPlayer(current);
            RememberStartPoint(current);
            WatchForDeath(current);
            ReadKeys();

            if (!paused)
            {
                TickRespawn(realDt);
                TickAllDeadReset(realDt);
            }
            if (toastRemaining > 0f) toastRemaining = Mathf.Max(0f, toastRemaining - realDt);
        }

        void ReadKeys()
        {
            // Keyboard.current is null when no keyboard is connected (e.g. gamepad-only). The Input System only
            // passes keys to the game while the Game view has focus, so typing in the Inspector never triggers these.
            Keyboard keyboard = Keyboard.current;
            if (keyboard == null) return;
            if (keyboard.escapeKey.wasPressedThisFrame) SetPaused(!paused);
            if (keyboard.f2Key.wasPressedThisFrame) ToggleDebugSlowMotion();
            if (keyboard.f4Key.wasPressedThisFrame) RespawnPlayer();
            if (keyboard.f5Key.wasPressedThisFrame) PresetKey(false);
            if (keyboard.f6Key.wasPressedThisFrame) PresetKey(true);
            if (keyboard.tKey.wasPressedThisFrame) ResetEnemies();
        }

        void ToggleDebugSlowMotion()
        {
            bool on = !TimeScaleController.IsDebugSlowMotion;
            TimeScaleController.SetDebugSlowMotion(on);
            ShowToast(on
                ? "Slow motion " + TimeScaleController.DebugSlowMotionScale.ToString("0.##", CultureInfo.InvariantCulture) + "x (F2 to stop)"
                : "Slow motion off");
        }

        void PresetKey(bool punishing)
        {
            if (PresetsLocked)
            {
                ShowToast("The tutorial runs on Fluid (hold View or press F7 to quit it)");
                return;
            }
            if (punishing) ApplyPunishingPreset(true);
            else ApplyFluidPreset(true);
        }

        bool ApplyPreset(PlayerTuningAsset tuning, ElementLoadoutAsset loadout, MoveSetAsset moves, string key, bool punishing,
                         bool announce)
        {
            PlayerController current = ResolvePlayer();
            if (current == null)
            {
                ShowToast("No player to apply the preset to");
                return false;
            }
            if (tuning == null || (loadout == null && moves == null))
            {
                Debug.LogWarning("SandboxDirector: the " + key + " preset has no tuning or move set asset assigned. Select 'Systems' "
                                 + "and fill in the Presets fields, or rebuild with Vaatu's Revenge > Build Combat Sandbox.", this);
                ShowToast(key + " preset is missing (see Console)");
                return false;
            }
            if (loadout != null) current.ApplyTuning(tuning, loadout);
            else current.ApplyTuning(tuning, moves);
            punishingActive = punishing;
            if (announce) ShowToast("Preset: " + (string.IsNullOrEmpty(tuning.PresetName) ? tuning.name : tuning.PresetName));
            return true;
        }

        // The Died event is the main trigger; watching IsDead as well catches a death that happened before
        // this director had hooked the event (e.g. the player spawned dead or died on its first frame).
        void WatchForDeath(PlayerController current)
        {
            bool dead = current != null && current.IsDead;
            if (dead && !playerWasDead) BeginDeathRespawn();
            playerWasDead = dead;
        }

        void OnPlayerDied()
        {
            playerWasDead = true;
            BeginDeathRespawn();
        }

        void BeginDeathRespawn()
        {
            if (respawnPending) return;
            respawnPending = true;
            respawnRemaining = Mathf.Max(0f, respawnDelay);
        }

        void TickRespawn(float realDt)
        {
            if (!respawnPending) return;
            respawnRemaining -= realDt;
            if (respawnRemaining <= 0f) FinishDeathRespawn();
        }

        void FinishDeathRespawn()
        {
            respawnPending = false;
            if (resetEnemiesOnDeath) ResetAllEnemies(false); // also clears projectiles in flight
            else FireProjectile.ClearAll();                   // a leftover bolt mustn't hit the fresh player at the spawn
            RespawnPlayerAtSpawn();
        }

        bool RespawnPlayerAtSpawn()
        {
            // A respawn is a fresh start: never leave the game paused or stuck in hitstop / slow motion. The F2
            // debug slow motion stays as you set it (ClearEffects keeps it), since it's a study tool you toggled.
            SetPaused(false);
            TimeScaleController.ClearEffects();

            PlayerController current = ResolvePlayer();
            if (current == null)
            {
                ShowToast("No player to respawn");
                return false;
            }
            Vector3 position;
            float yaw;
            GetSpawn(current, out position, out yaw);
            current.Respawn(position, yaw);
            playerWasDead = current.IsDead;

            // A lock held from before the death would swing the camera round the arena: start unlocked, camera behind.
            LockOnController lockOn = LockOnController.Instance;
            if (lockOn != null) lockOn.ClearLock();
            ThirdPersonCameraRig rig = ThirdPersonCameraRig.Instance;
            if (rig != null) rig.SnapBehindTarget();
            return true;
        }

        void GetSpawn(PlayerController current, out Vector3 position, out float yaw)
        {
            if (SpawnOverride != null)
            {
                position = SpawnOverride.position;
                yaw = SpawnOverride.eulerAngles.y;
            }
            else if (playerSpawn != null)
            {
                position = playerSpawn.position;
                yaw = playerSpawn.eulerAngles.y;
            }
            else if (hasStartPoint)
            {
                position = startPosition;
                yaw = startYaw;
            }
            else
            {
                position = current.transform.position;
                yaw = current.transform.eulerAngles.y;
            }
        }

        void ResetAllEnemies(bool announce)
        {
            allDeadRemaining = -1f;
            // Projectiles in flight (bolts and fire blasts) belong to the fight being reset, so they go too.
            FireProjectile.ClearAll();
            GatherEnemies();
            for (int i = 0; i < enemyBuffer.Count; i++) enemyBuffer[i].ResetEnemy();
            GatherDummies();
            for (int i = 0; i < dummyBuffer.Count; i++) dummyBuffer[i].ResetDummy();
            if (announce) ShowToast("Enemies reset");
        }

        void TickAllDeadReset(float realDt)
        {
            if (!autoResetEnemiesWhenAllDead)
            {
                allDeadRemaining = -1f;
                return;
            }
            GatherEnemies();
            int alive = 0;
            for (int i = 0; i < enemyBuffer.Count; i++)
            {
                if (!enemyBuffer[i].IsDead) alive++;
            }
            if (enemyBuffer.Count == 0 || alive > 0)
            {
                allDeadRemaining = -1f;
                return;
            }
            if (allDeadRemaining < 0f)
            {
                allDeadRemaining = Mathf.Max(0f, allDeadResetDelay);
                return;
            }
            allDeadRemaining -= realDt;
            if (allDeadRemaining > 0f) return;

            allDeadRemaining = -1f;
            FireProjectile.ClearAll();
            for (int i = 0; i < enemyBuffer.Count; i++) enemyBuffer[i].ResetEnemy();
            ShowToast("All enemies down: here they come again");
        }

        // Everyone this director resets: the registered fighters plus any in the live registries, without
        // duplicates. A fighter someone switched off by hand (inactive and not dead) is left alone, but a dead
        // one is included even if it hid itself. The list is copied first, because resetting a fighter can
        // change EnemyController.All while we loop over it.
        void GatherEnemies()
        {
            enemyBuffer.Clear();
            for (int i = 0; i < enemies.Count; i++) AddEnemy(enemies[i]);
            IReadOnlyList<EnemyController> live = EnemyController.All;
            if (live == null) return;
            for (int i = 0; i < live.Count; i++) AddEnemy(live[i]);
        }

        void AddEnemy(EnemyController enemy)
        {
            if (enemy == null || (!enemy.isActiveAndEnabled && !enemy.IsDead) || hiddenEnemies.Contains(enemy)) return;
            if (!enemyBuffer.Contains(enemy)) enemyBuffer.Add(enemy);
        }

        void GatherDummies()
        {
            dummyBuffer.Clear();
            for (int i = 0; i < dummies.Count; i++) AddDummy(dummies[i]);
            IReadOnlyList<TrainingDummy> live = TrainingDummy.All;
            if (live == null) return;
            for (int i = 0; i < live.Count; i++) AddDummy(live[i]);
        }

        void AddDummy(TrainingDummy dummy)
        {
            if (dummy == null || !dummy.isActiveAndEnabled) return;
            if (!dummyBuffer.Contains(dummy)) dummyBuffer.Add(dummy);
        }

        PlayerController ResolvePlayer()
        {
            // The explicit checks turn a destroyed player (which Unity reports as == null) into a real null.
            if (player != null) return player;
            PlayerController registered = PlayerController.Instance;
            if (registered != null) return registered;
            if (!warnedNoPlayer)
            {
                warnedNoPlayer = true;
                Debug.LogWarning("SandboxDirector can't find the player, so respawn and presets do nothing. Rebuild the scene with "
                                 + "Vaatu's Revenge > Build Combat Sandbox, or assign the Player field on 'Systems'.", this);
            }
            return null;
        }

        void HookPlayer(PlayerController current)
        {
            if (ReferenceEquals(hookedPlayer, current)) return;
            UnhookPlayer();
            hookedPlayer = current;
            if (current != null) current.Died += OnPlayerDied;
        }

        void UnhookPlayer()
        {
            // Died is a plain C# event, so unsubscribing is safe even if the player's GameObject was destroyed.
            if (!ReferenceEquals(hookedPlayer, null)) hookedPlayer.Died -= OnPlayerDied;
            hookedPlayer = null;
        }

        void RememberStartPoint(PlayerController current)
        {
            if (hasStartPoint || current == null) return;
            hasStartPoint = true;
            startPosition = current.transform.position;
            startYaw = current.transform.eulerAngles.y;
        }
    }
}
