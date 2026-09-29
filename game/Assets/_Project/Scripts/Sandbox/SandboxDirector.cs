using System.Collections.Generic;
using System.Globalization;
using UnityEngine;
using UnityEngine.InputSystem;

namespace VaatusRevenge
{
    // Runs the Fire combat sandbox around the fight itself: the sandbox keys, the death loop and the short
    // on-screen messages ("toasts") that CombatHud draws.
    //   F2       debug slow motion, for studying moves
    //   F4       respawn the player at the spawn point (while dead: skip the wait)
    //   F5 / F6  switch the player to the Fluid / Punishing tuning preset, live, to compare the two feels
    //   T        reset every enemy and sparring dummy (projectiles in flight are cleared too)
    //   Esc      pause / resume (PlayerInputReader also releases the mouse on Esc)
    // F1 (controls) and F3 (debug panel) belong to CombatHud. None of these keys is bound to a gameplay action.
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
            toastText = message ?? "";
            toastRemaining = string.IsNullOrEmpty(toastText) ? 0f : Mathf.Max(ToastFadeTime, toastDuration);
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
            if (keyboard.f5Key.wasPressedThisFrame) ApplyPreset(fluidTuning, fluidMoves, "F5");
            if (keyboard.f6Key.wasPressedThisFrame) ApplyPreset(punishingTuning, punishingMoves, "F6");
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

        void ApplyPreset(PlayerTuningAsset tuning, MoveSetAsset moves, string key)
        {
            PlayerController current = ResolvePlayer();
            if (current == null)
            {
                ShowToast("No player to apply the preset to");
                return;
            }
            if (tuning == null || moves == null)
            {
                Debug.LogWarning("SandboxDirector: the " + key + " preset has no tuning or move set asset assigned. Select 'Systems' "
                                 + "and fill in the Presets fields, or rebuild with Vaatu's Revenge > Build Fire Combat Sandbox.", this);
                ShowToast(key + " preset is missing (see Console)");
                return;
            }
            current.ApplyTuning(tuning, moves);
            ShowToast("Preset: " + (string.IsNullOrEmpty(tuning.PresetName) ? tuning.name : tuning.PresetName));
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
            if (playerSpawn != null)
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
            if (enemy == null || (!enemy.isActiveAndEnabled && !enemy.IsDead)) return;
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
                                 + "Vaatu's Revenge > Build Fire Combat Sandbox, or assign the Player field on 'Systems'.", this);
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
