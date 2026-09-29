using System.Collections.Generic;
using UnityEngine;
using VaatusRevenge.Core;

namespace VaatusRevenge
{
    // Souls-style lock-on. Press lock-on to lock the enemy nearest the centre of the screen (or, with nothing
    // in range, to swing the camera behind the player). While locked: flick the right stick, roll the mouse
    // wheel or press Z / C to switch left/right. The lock breaks when the target gets too far away, stays
    // hidden behind walls for a moment, or dies (then it moves on to the next enemy, if that's switched on).
    //
    // Runs before the player (execution order -50), so PlayerController sees this frame's Target. The camera
    // rig and the player just read Target; this class owns every rule about choosing it (see LockOnSelector).
    //
    // Setup from code (e.g. an editor scene builder): AddComponent, then Configure(tuningAsset, player).
    [DefaultExecutionOrder(-50)]
    [DisallowMultipleComponent]
    public class LockOnController : MonoBehaviour
    {
        public static LockOnController Instance { get; private set; }

        [Tooltip("Lock-on numbers. Empty = use the camera rig's tuning asset, or the built-in defaults.")]
        [SerializeField] private CameraTuningAsset tuningAsset;
        [Tooltip("The player's fighter. Empty = the registered player (the Combatant on Team Player).")]
        [SerializeField] private Combatant player;
        [Tooltip("Draw a marker on the locked target.")]
        [SerializeField] private bool showReticle = true;
        [Tooltip("Marker size in pixels.")]
        [SerializeField] private float reticleSize = 12f;
        [SerializeField] private Color reticleColor = new Color(1f, 0.85f, 0.35f, 1f);

        readonly LockOnTuning defaultTuning = new LockOnTuning();
        readonly CameraTuning defaultCameraTuning = new CameraTuning();
        readonly List<LockOnCandidate> candidates = new List<LockOnCandidate>(16);
        readonly List<Combatant> candidateFighters = new List<Combatant>(16);
        LockOnSelector selector;
        StickFlickDetector flick;
        Combatant target;
        float switchCooldown;
        bool warnedNoTuning;
        bool warnedNoPlayer;

        // The locked fighter, or null. Destroyed fighters read as null.
        public Combatant Target => target != null ? target : null;
        public bool IsLocked => Target != null;

        // Raised whenever the target changes: the new target, or null when the lock ends.
        public event System.Action<Combatant> TargetChanged;

        public CameraTuningAsset TuningAsset
        {
            get { return tuningAsset; }
            set
            {
                tuningAsset = value;
                warnedNoTuning = false;
            }
        }

        public Combatant Player
        {
            get { return player; }
            set
            {
                player = value;
                warnedNoPlayer = false;
            }
        }

        public bool ShowReticle
        {
            get { return showReticle; }
            set { showReticle = value; }
        }

        // One-call setup that works at edit time as well as in play mode. playerFighter may be null: the
        // registered player (Combatant.Player) is then used.
        public void Configure(CameraTuningAsset tuning, Combatant playerFighter = null)
        {
            TuningAsset = tuning;
            Player = playerFighter;
        }

        public void ClearLock()
        {
            SetTarget(null);
        }

        void OnEnable()
        {
            if (Instance != null && Instance != this)
            {
                Debug.LogWarning("LockOnController: more than one is active. Only the first one ('" + Instance.name
                                 + "') is LockOnController.Instance, which the camera and player read.", this);
                return;
            }
            Instance = this;
        }

        void OnDisable()
        {
            ClearLock();
            if (Instance == this) Instance = null;
        }

        void Update()
        {
            LockOnTuning tuning = ActiveTuning;
            Selector.Tuning = tuning;
            Flick.Tuning = tuning;
            // Switching is input handling, so its cooldown runs on real time (unaffected by hitstop/slow-mo).
            float realDt = Time.unscaledDeltaTime;
            if (switchCooldown > 0f) switchCooldown = Mathf.Max(0f, switchCooldown - realDt);

            Combatant me = ResolvePlayer();
            // A player without a damage receiver yet (not fully wired up) counts as alive, so the camera and
            // lock-on can be tried out before the combat side exists.
            if (me == null || (me.Receiver != null && !me.IsAlive))
            {
                if (!ReferenceEquals(target, null)) ClearLock();
                Flick.Reset();
                return;
            }

            PlayerInputReader reader = PlayerInputReader.Instance;
            PlayerInputFrame input = reader != null ? reader.Frame : default(PlayerInputFrame);
            Vector3 eye = EyePoint(me);

            // 1. Keep, retarget or drop the current lock.
            if (!ReferenceEquals(target, null)) CheckLock(me, eye, tuning);

            // 2. Lock-on button toggles.
            if (input.LockOn.Pressed)
            {
                if (IsLocked)
                {
                    ClearLock();
                }
                else if (!TryAcquire(me, eye))
                {
                    ThirdPersonCameraRig rig = ThirdPersonCameraRig.Instance;
                    if (rig != null) rig.RecenterBehindTarget();
                }
            }

            // 3. Switching left/right.
            if (IsLocked)
            {
                System.Numerics.Vector2 stick = input.LookIsMouse ? System.Numerics.Vector2.Zero : input.Look;
                int direction = Flick.Update(stick, realDt);
                if (direction == 0) direction = input.SwitchTargetDelta;
                if (direction != 0 && switchCooldown <= 0f) TrySwitch(me, eye, direction, tuning);
            }
            else
            {
                // Not locked: forget any stick push, so locking on while the stick is tilted doesn't switch at once.
                Flick.Reset();
            }
        }

        void CheckLock(Combatant me, Vector3 eye, LockOnTuning tuning)
        {
            Combatant current = target;
            LockOnBreakReason reason;
            if (current == null || !current.isActiveAndEnabled || !current.IsAlive)
            {
                // Dead, destroyed or switched off all count as gone.
                reason = LockOnBreakReason.TargetDead;
            }
            else
            {
                Vector3 aim = current.AimPoint.position;
                bool visible = HasLineOfSight(eye, aim, current, me);
                // Game time for the grace period: it's a game rule, so it pauses during hitstop like everything else.
                reason = Selector.UpdateBreak(true, visible, Vector3.Distance(eye, aim), Time.deltaTime);
            }

            if (reason == LockOnBreakReason.None) return;
            if (reason == LockOnBreakReason.TargetDead && tuning.AutoRetargetOnKill && TryAcquire(me, eye)) return;
            ClearLock();
        }

        bool TryAcquire(Combatant me, Vector3 eye)
        {
            GatherCandidates(me, eye);
            Camera cam = RigCamera();
            Vector3 cameraPosition = cam != null ? cam.transform.position : eye;
            Vector3 cameraForward = cam != null ? cam.transform.forward : me.transform.forward;
            int index = Selector.PickInitial(candidates, eye.ToNumerics(), cameraPosition.ToNumerics(), cameraForward.ToNumerics());
            if (index < 0) return false;
            SetTarget(candidateFighters[index]);
            return true;
        }

        void TrySwitch(Combatant me, Vector3 eye, int direction, LockOnTuning tuning)
        {
            Combatant current = Target;
            if (current == null) return;
            GatherCandidates(me, eye);

            ThirdPersonCameraRig rig = ThirdPersonCameraRig.Instance;
            Camera cam = RigCamera();
            Vector3 cameraPosition = cam != null ? cam.transform.position : eye;
            float cameraYaw = rig != null ? rig.Yaw : me.transform.eulerAngles.y;
            int index = Selector.PickNext(candidates, current.Id, current.AimPoint.position.ToNumerics(), direction,
                eye.ToNumerics(), cameraPosition.ToNumerics(), cameraYaw);
            if (index < 0) return; // nothing on that side: keep the current target

            SetTarget(candidateFighters[index]);
            switchCooldown = Mathf.Max(0f, tuning.SwitchCooldown);
        }

        // Every living fighter that isn't on the player's team (enemies, and neutral targets such as the
        // sparring dummy) within acquire range, with a line-of-sight test.
        void GatherCandidates(Combatant me, Vector3 eye)
        {
            candidates.Clear();
            candidateFighters.Clear();
            float range = Mathf.Max(0f, ActiveTuning.AcquireRange);
            float rangeSquared = range * range;
            List<Combatant> all = Combatant.All;
            for (int i = 0; i < all.Count; i++)
            {
                Combatant fighter = all[i];
                if (fighter == null || fighter == me || fighter.Team == me.Team || !fighter.IsAlive) continue;
                Vector3 aim = fighter.AimPoint.position;
                if ((aim - eye).sqrMagnitude > rangeSquared) continue; // out of range anyway: skip the physics test

                candidates.Add(new LockOnCandidate
                {
                    Id = fighter.Id,
                    Point = aim.ToNumerics(),
                    IsAlive = true,
                    HasLineOfSight = HasLineOfSight(eye, aim, fighter, me),
                });
                candidateFighters.Add(fighter);
            }
        }

        void SetTarget(Combatant next)
        {
            if (ReferenceEquals(target, next)) return;
            target = next;
            Selector.ResetBreakTimer();
            // After any change the stick must come back to the centre before the next flick counts.
            Flick.Reset();
            System.Action<Combatant> handler = TargetChanged;
            if (handler != null) handler(Target);
        }

        // A target counts as visible if either the player's eyes or the camera can see its aim point. Being
        // forgiving here stops the lock flickering when only a pillar edge is in the way of one of them.
        bool HasLineOfSight(Vector3 eye, Vector3 aim, Combatant fighter, Combatant me)
        {
            if (IsClear(eye, aim, fighter, me)) return true;
            Camera cam = RigCamera();
            return cam != null && IsClear(cam.transform.position, aim, fighter, me);
        }

        static bool IsClear(Vector3 from, Vector3 to, Combatant fighter, Combatant me)
        {
            RaycastHit hit;
            if (!Physics.Linecast(from, to, out hit, Layers.EnvironmentMask, QueryTriggerInteraction.Ignore)) return true;
            // Fighters belong on the Player/Enemy layers; if one was left on Default, its own body mustn't hide it.
            Transform blocker = hit.collider.transform;
            return blocker.IsChildOf(fighter.transform) || blocker.IsChildOf(me.transform);
        }

        Vector3 EyePoint(Combatant me)
        {
            return me.Feet + Vector3.up * ActiveCameraTuning.PivotHeight;
        }

        Combatant ResolvePlayer()
        {
            if (player != null) return player;
            Combatant registered = Combatant.Player;
            if (registered != null) return registered;
            if (!warnedNoPlayer)
            {
                warnedNoPlayer = true;
                Debug.LogWarning("LockOnController can't find the player. Call Configure(tuning, player) or give the player "
                                 + "a Combatant on Team Player. Lock-on stays off until then.", this);
            }
            return null;
        }

        static Camera RigCamera()
        {
            ThirdPersonCameraRig rig = ThirdPersonCameraRig.Instance;
            return rig != null ? rig.Camera : null;
        }

        // Our own asset first, then the camera rig's, so one asset set up on the rig is enough.
        CameraTuningAsset ResolveAsset()
        {
            if (tuningAsset != null) return tuningAsset;
            ThirdPersonCameraRig rig = ThirdPersonCameraRig.Instance;
            return rig != null ? rig.TuningAsset : null;
        }

        LockOnTuning ActiveTuning
        {
            get
            {
                CameraTuningAsset asset = ResolveAsset();
                if (asset != null && asset.LockOn != null) return asset.LockOn;
                if (!warnedNoTuning)
                {
                    warnedNoTuning = true;
                    Debug.LogWarning("LockOnController has no CameraTuningAsset (here or on the camera rig), so it's using the "
                                     + "built-in default lock-on numbers.", this);
                }
                return defaultTuning;
            }
        }

        CameraTuning ActiveCameraTuning
        {
            get
            {
                CameraTuningAsset asset = ResolveAsset();
                return asset != null && asset.Camera != null ? asset.Camera : defaultCameraTuning;
            }
        }

        LockOnSelector Selector
        {
            get
            {
                if (selector == null) selector = new LockOnSelector(ActiveTuning);
                return selector;
            }
        }

        StickFlickDetector Flick
        {
            get
            {
                if (flick == null) flick = new StickFlickDetector(ActiveTuning);
                return flick;
            }
        }

        void OnGUI()
        {
            if (!showReticle || Event.current.type != EventType.Repaint) return;
            Combatant current = Target;
            if (current == null) return;
            Camera cam = RigCamera();
            if (cam == null) return;

            Vector3 screen = cam.WorldToScreenPoint(current.AimPoint.position);
            // Behind the camera, WorldToScreenPoint mirrors the point onto the screen: don't draw a ghost marker.
            if (screen.z <= 0f) return;
            // The GUI's y axis points down; the screen's points up.
            DrawReticle(new Vector2(screen.x, Screen.height - screen.y));
        }

        void DrawReticle(Vector2 centre)
        {
            float size = Mathf.Max(2f, reticleSize);
            Matrix4x4 oldMatrix = GUI.matrix;
            Color oldColor = GUI.color;
            GUIUtility.RotateAroundPivot(45f, centre); // a square turned 45 degrees reads as a diamond marker
            GUI.color = new Color(0f, 0f, 0f, 0.6f * reticleColor.a);
            GUI.DrawTexture(new Rect(centre.x - size * 0.5f - 2f, centre.y - size * 0.5f - 2f, size + 4f, size + 4f), Texture2D.whiteTexture);
            GUI.color = reticleColor;
            GUI.DrawTexture(new Rect(centre.x - size * 0.5f, centre.y - size * 0.5f, size, size), Texture2D.whiteTexture);
            GUI.matrix = oldMatrix;
            GUI.color = oldColor;
        }
    }
}
