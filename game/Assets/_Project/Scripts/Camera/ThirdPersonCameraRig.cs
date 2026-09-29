using System.Collections.Generic;
using UnityEngine;
using VaatusRevenge.Core;

namespace VaatusRevenge
{
    // The third-person camera: looks over the player's shoulder (like Marvel's Spider-Man 2), frames the lock-on
    // target, widens the view in fights, slides in front of walls, and plays screen shake and the sprint FOV
    // boost. Put it on the GameObject that has the scene's Camera (it adds one if missing). Don't add a
    // Cinemachine Brain to that camera: it would fight this rig.
    //
    // It runs in LateUpdate, after the player and enemies have moved in Update, so it always frames where
    // everyone is this frame (moving the camera before the player moves is the classic cause of jitter).
    // The rules live in OrbitCameraModel (pure C#); this class feeds it input, lock-on, nearby foes and wall
    // probes and copies the result onto the Camera.
    //
    // Setup from code (e.g. an editor scene builder): AddComponent, then Configure(tuningAsset, playerRoot).
    [DefaultExecutionOrder(100)]
    [DisallowMultipleComponent]
    [RequireComponent(typeof(Camera))]
    public class ThirdPersonCameraRig : MonoBehaviour
    {
        public static ThirdPersonCameraRig Instance { get; private set; }

        // Metres the collision probes stop short of a wall (a tiny safety gap, not a tuning number).
        const float ProbeSkin = 0.02f;

        [Tooltip("Camera and lock-on numbers (Create > Vaatu's Revenge > Tuning > Camera). Empty = built-in defaults.")]
        [SerializeField] private CameraTuningAsset tuningAsset;
        [Tooltip("What the camera follows: the player's root object, whose position is at its feet.")]
        [SerializeField] private Transform followTarget;
        [Tooltip("With no follow target set, follow the registered player (the Combatant on Team Player).")]
        [SerializeField] private bool followPlayerIfUnset = true;

        readonly CameraTuning defaultTuning = new CameraTuning();
        Camera cachedCamera;
        OrbitCameraModel model;
        bool snapPending = true;
        float fovBoostTarget;
        float fovBoost;
        float fovBoostVelocity;
        Transform followCombatantOf;
        Combatant followCombatant;
        bool warnedNoTuning;
        bool warnedNoCamera;
        bool warnedNoTarget;

        // Camera yaw in degrees (clockwise from +Z). The player moves relative to this.
        public float Yaw => Model.Yaw;
        // Camera pitch in degrees, positive = looking down (as rendered, including the rise over the head near walls).
        public float Pitch => Model.ViewPitch;
        public Camera Camera => ResolveCamera();
        public Transform FollowTarget => followTarget;
        // The numbers in use right now (the asset's, or the defaults when none is assigned).
        public CameraTuning Tuning => ActiveTuning;
        // What the player should pass to SetFovBoost while sprinting.
        public float SprintFovBoost => ActiveTuning.SprintFovBoost;
        // Flat forward direction of the camera, handy for camera-relative movement.
        public Vector3 PlanarForward => Directions.FromYaw(Model.Yaw).ToUnity();
        // +1 = looking over the right shoulder, -1 = the left, 0 = centred (e.g. for a HUD hint).
        public int ShoulderSide => Model.ShoulderSide;

        public CameraTuningAsset TuningAsset
        {
            get { return tuningAsset; }
            set
            {
                tuningAsset = value;
                warnedNoTuning = false;
            }
        }

        public bool FollowPlayerIfUnset
        {
            get { return followPlayerIfUnset; }
            set { followPlayerIfUnset = value; }
        }

        // One-call setup that works at edit time (Awake/OnEnable haven't run) as well as in play mode.
        // Places the camera behind the follow target straight away, so a saved scene already looks right.
        public void Configure(CameraTuningAsset tuning, Transform follow)
        {
            TuningAsset = tuning;
            SetFollowTarget(follow);
        }

        // Starts following a new target and cuts the camera behind it. Null stops following (the camera then
        // holds still, or picks up the registered player if FollowPlayerIfUnset is on).
        public void SetFollowTarget(Transform target)
        {
            followTarget = target;
            warnedNoTarget = false;
            if (target != null) SnapBehindTarget();
            else snapPending = true;
        }

        // Cuts (no smoothing) to behind the follow target at the default pitch: use after a respawn or teleport.
        public void SnapBehindTarget()
        {
            Transform target = ResolveFollowTarget();
            if (target == null)
            {
                snapPending = true;
                return;
            }
            SnapModelBehind(target);

            Camera cam = ResolveCamera();
            if (cam == null) return;
            CameraTuning tuning = ActiveTuning;
            bool playing = Application.isPlaying;
            // In play mode, respect walls straight away so a respawn next to one doesn't show a frame from
            // inside it. At edit time physics isn't reliable yet; the first played frame takes care of it.
            if (playing) ApplyCollision(tuning, 0f);
            ApplyToCamera(cam, tuning, 0f, playing);
        }

        // Smoothly swings the camera behind the follow target (Elden Ring's "lock-on with nothing to lock").
        // Moving the camera yourself cancels it.
        public void RecenterBehindTarget()
        {
            Transform target = ResolveFollowTarget();
            if (target != null) Model.BeginRecenter(target.eulerAngles.y);
        }

        // Moves the camera smoothly to the other shoulder straight away. Holding L3 / V does the same through
        // the input reader, after CameraTuning.ShoulderSwapHoldTime.
        public void SwapShoulder()
        {
            Model.SwapShoulder();
        }

        // Extra field of view in degrees on top of the base FOV, eased in and out (e.g. +5 while sprinting to
        // sell speed). Pass 0 to return to normal.
        public void SetFovBoost(float degrees)
        {
            fovBoostTarget = float.IsNaN(degrees) || float.IsInfinity(degrees) ? 0f : degrees;
        }

        void OnEnable()
        {
            if (Instance != null && Instance != this)
            {
                Debug.LogWarning("ThirdPersonCameraRig: more than one camera rig is active. Only the first one ('"
                                 + Instance.name + "') is ThirdPersonCameraRig.Instance and plays screen shake.", this);
                return;
            }
            Instance = this;
            snapPending = true;
        }

        void OnDisable()
        {
            if (Instance == this) Instance = null;
        }

        void Start()
        {
            // Frame the player before anyone's first Update, so the first frame of movement uses the right yaw.
            if (ResolveFollowTarget() != null) SnapBehindTarget();
        }

        void LateUpdate()
        {
            Camera cam = ResolveCamera();
            if (cam == null) return;
            Transform target = ResolveFollowTarget();
            if (target == null)
            {
                if (!warnedNoTarget)
                {
                    warnedNoTarget = true;
                    Debug.LogWarning("ThirdPersonCameraRig has nothing to follow yet. Call Configure/SetFollowTarget with the "
                                     + "player's root, or give the player a Combatant on Team Player. The camera holds still until then.", this);
                }
                return;
            }

            CameraTuning tuning = ActiveTuning;
            OrbitCameraModel orbit = Model;
            orbit.Tuning = tuning;
            if (snapPending) SnapModelBehind(target);

            // Real time for anything the player controls or feels (look, lock-on swing, shoulder, wall easing,
            // FOV), so the camera stays responsive while hitstop or slow motion changes Time.timeScale.
            float realDt = Time.unscaledDeltaTime;

            PlayerInputReader reader = PlayerInputReader.Instance;
            PlayerInputFrame frame = reader != null ? reader.Frame : default(PlayerInputFrame);
            OrbitCameraInput input = new OrbitCameraInput
            {
                Look = frame.Look,
                LookIsMouse = frame.LookIsMouse,
                // Held, not pressed: the camera only swaps once the button has been held a moment (CameraTuning).
                SwapShoulderHeld = frame.SwapShoulder.Held,
            };
            LockOnController lockOn = LockOnController.Instance;
            Combatant lockTarget = lockOn != null ? lockOn.Target : null;
            if (lockTarget != null)
            {
                input.HasLockTarget = true;
                input.LockTargetPoint = lockTarget.AimPoint.position.ToNumerics();
            }
            // Combat pull-back only needs the nearest foe; skip the search when it's switched off.
            if (tuning.CombatPullback > 0f || tuning.CombatPullbackHeight > 0f)
            {
                float foeDistance;
                input.HasFoe = FindNearestFoe(target, out foeDistance);
                input.NearestFoeDistance = foeDistance;
            }

            // Game time for following the player: see OrbitCameraModel.UpdatePivot for why.
            orbit.UpdatePivot(target.position.ToNumerics(), Time.deltaTime);
            orbit.UpdateOrientation(input, realDt);
            ApplyCollision(tuning, realDt);
            ApplyToCamera(cam, tuning, realDt, true);
        }

        // Keeps the camera out of level geometry, in the order the camera is built: rise from the pivot (combat
        // lift), step out to the shoulder, then back away from the shoulder point. Each probe starts where the
        // previous one safely ended, so a wall on the shoulder side can never end up between the pivot and the
        // camera. Only the environment layer counts, so fighters walking past never shove the camera.
        // Extra probes keep the view steady (report 02, NEW-03): sideways at the camera's end too, so the shoulder
        // offset yields to a wall the camera is still passing; from the camera toward the player, which tells the
        // model whether a wall is touching the camera itself (move in now) or only blocking the view (wait a moment,
        // then glide); and a fatter copy of the distance probe that spots a pillar before it touches the camera.
        void ApplyCollision(CameraTuning tuning, float realDt)
        {
            OrbitCameraModel orbit = Model;
            float radius = Mathf.Max(0f, tuning.CollisionRadius);

            orbit.UpdateLift(Probe(orbit.Pivot.ToUnity(), Vector3.up, orbit.DesiredLift, radius), realDt);

            Vector3 lifted = orbit.LiftedPivot.ToUnity();
            Vector3 right = orbit.Right.ToUnity();
            float reach = orbit.ShoulderReach;
            float freeRight = Probe(lifted, right, reach, radius);
            float freeLeft = Probe(lifted, -right, reach, radius);
            Vector3 cameraEnd = orbit.CameraEndCentre.ToUnity();
            float freeRightAtCamera = Probe(cameraEnd, right, reach, radius);
            float freeLeftAtCamera = Probe(cameraEnd, -right, reach, radius);
            orbit.UpdateShoulder(freeRight, freeLeft, freeRightAtCamera, freeLeftAtCamera, realDt);

            Vector3 shoulder = orbit.ShoulderPoint.ToUnity();
            Vector3 back = -orbit.Forward.ToUnity();
            float maxDistance = Probe(shoulder, back, orbit.DesiredDistance, radius);
            float cameraFree = CameraFree(shoulder, back, orbit.Distance, radius);
            float lookAhead = tuning.CollisionLookAhead > 0f
                ? LookAhead(shoulder, back, orbit.DesiredDistance, radius + tuning.CollisionLookAhead)
                : float.PositiveInfinity;
            orbit.UpdateDistance(maxDistance, cameraFree, lookAhead, realDt);
        }

        // The fatter distance probe. It only warns about something ahead: if the fat sphere already overlaps a wall where
        // it starts (you're standing next to one), it has nothing useful to say, so it reports open space.
        static float LookAhead(Vector3 shoulder, Vector3 back, float length, float fatRadius)
        {
            if (!(length > 0f) || !(fatRadius > 0f)) return float.PositiveInfinity;
            if (Physics.CheckSphere(shoulder, fatRadius, Layers.EnvironmentMask, QueryTriggerInteraction.Ignore)) return float.PositiveInfinity;
            RaycastHit hit;
            return Physics.SphereCast(shoulder, fatRadius, back, out hit, length, Layers.EnvironmentMask, QueryTriggerInteraction.Ignore)
                ? Mathf.Max(0f, hit.distance - ProbeSkin)
                : float.PositiveInfinity;
        }

        // How far the camera's sphere, left at 'distance' behind the shoulder point, could slide toward it before
        // touching level geometry. 0 when it already touches something where it is.
        static float CameraFree(Vector3 shoulder, Vector3 back, float distance, float radius)
        {
            if (!(distance > 0f)) return 0f;
            Vector3 position = shoulder + back * distance;
            if (Physics.CheckSphere(position, Mathf.Max(radius, 0.001f), Layers.EnvironmentMask, QueryTriggerInteraction.Ignore)) return 0f;
            return Probe(position, -back, distance, radius);
        }

        // How far the camera's collision sphere can travel from origin along direction before touching level
        // geometry. Returns length when nothing is in the way. Stops a hair (ProbeSkin) short of what it hits, so
        // the next probe in the chain starts in free space instead of already touching the wall.
        static float Probe(Vector3 origin, Vector3 direction, float length, float radius)
        {
            if (!(length > 0f)) return 0f;
            RaycastHit hit;
            if (radius <= 0.001f)
            {
                return Physics.Raycast(origin, direction, out hit, length, Layers.EnvironmentMask, QueryTriggerInteraction.Ignore)
                    ? Mathf.Max(0f, hit.distance - ProbeSkin)
                    : length;
            }
            if (Physics.CheckSphere(origin, radius, Layers.EnvironmentMask, QueryTriggerInteraction.Ignore))
            {
                // The sphere already touches something where it starts (e.g. the pivot trailed round a pillar
                // corner). SphereCast can't see colliders it starts inside, so use a thin ray and stay one radius
                // clear of the hit.
                return Physics.Raycast(origin, direction, out hit, length, Layers.EnvironmentMask, QueryTriggerInteraction.Ignore)
                    ? Mathf.Max(0f, hit.distance - radius - ProbeSkin)
                    : length;
            }
            return Physics.SphereCast(origin, radius, direction, out hit, length, Layers.EnvironmentMask, QueryTriggerInteraction.Ignore)
                ? Mathf.Max(0f, hit.distance - ProbeSkin)
                : length;
        }

        // Distance from the player's feet to the nearest living fighter who isn't on their side (enemies and
        // neutral targets such as the sparring dummy). False when there's none.
        bool FindNearestFoe(Transform target, out float distance)
        {
            distance = float.PositiveInfinity;
            Combatant self = FollowCombatant(target);
            Team team = self != null ? self.Team : Team.Player;
            Vector3 feet = target.position;
            float nearestSquared = float.PositiveInfinity;
            List<Combatant> all = Combatant.All;
            for (int i = 0; i < all.Count; i++)
            {
                Combatant fighter = all[i];
                if (fighter == null || fighter == self || fighter.Team == team || !fighter.IsAlive) continue;
                float squared = (fighter.Feet - feet).sqrMagnitude;
                if (squared < nearestSquared) nearestSquared = squared;
            }
            if (float.IsPositiveInfinity(nearestSquared)) return false;
            distance = Mathf.Sqrt(nearestSquared);
            return true;
        }

        Combatant FollowCombatant(Transform target)
        {
            if (followCombatantOf != target)
            {
                followCombatantOf = target;
                followCombatant = target != null ? target.GetComponent<Combatant>() : null;
            }
            return followCombatant;
        }

        void ApplyToCamera(Camera cam, CameraTuning tuning, float realDt, bool withShake)
        {
            OrbitCameraModel orbit = Model;
            // Shake only rotates the view (a moved camera could end up inside a wall) and doesn't touch Yaw/Pitch,
            // so movement directions never wobble with it.
            Vector3 shake = withShake && Instance == this
                ? CameraShake.Advance(realDt, tuning.ShakeFrequency, tuning.ShakeMaxAngle)
                : Vector3.zero;
            // ViewPitch, not Pitch: it includes the rise over the head when a wall is close behind.
            Quaternion rotation = Quaternion.Euler(orbit.ViewPitch + shake.x, orbit.Yaw + shake.y, shake.z);
            cam.transform.SetPositionAndRotation(orbit.CameraPosition.ToUnity(), rotation);

            // Smooth.Damp rather than Mathf.SmoothDamp: Unity's version produces NaN when deltaTime is 0 (paused).
            fovBoost = Smooth.Damp(fovBoost, fovBoostTarget, ref fovBoostVelocity, tuning.FovSmoothTime, realDt);
            float fov = Mathf.Clamp(tuning.BaseFov + fovBoost, 1f, 179f);
            if (!Mathf.Approximately(cam.fieldOfView, fov)) cam.fieldOfView = fov;
            float near = Mathf.Max(0.01f, tuning.NearClipPlane);
            if (!Mathf.Approximately(cam.nearClipPlane, near)) cam.nearClipPlane = near;
        }

        void SnapModelBehind(Transform target)
        {
            snapPending = false;
            OrbitCameraModel orbit = Model;
            orbit.Tuning = ActiveTuning;
            orbit.Snap(target.position.ToNumerics(), target.eulerAngles.y, orbit.Tuning.DefaultPitch);
        }

        Transform ResolveFollowTarget()
        {
            if (followTarget != null) return followTarget;
            if (!followPlayerIfUnset) return null;
            Combatant player = Combatant.Player;
            if (player == null) return null;
            followTarget = player.transform;
            snapPending = true;
            return followTarget;
        }

        Camera ResolveCamera()
        {
            if (cachedCamera == null) cachedCamera = GetComponent<Camera>();
            if (cachedCamera == null && !warnedNoCamera)
            {
                warnedNoCamera = true;
                Debug.LogWarning("ThirdPersonCameraRig needs a Camera on the same GameObject; the camera won't move until one is added.", this);
            }
            return cachedCamera;
        }

        CameraTuning ActiveTuning
        {
            get
            {
                if (tuningAsset != null && tuningAsset.Camera != null) return tuningAsset.Camera;
                if (!warnedNoTuning)
                {
                    warnedNoTuning = true;
                    Debug.LogWarning("ThirdPersonCameraRig has no CameraTuningAsset assigned, so it's using the built-in "
                                     + "default numbers. Assign one (or call Configure) to tune the camera in the Inspector.", this);
                }
                return defaultTuning;
            }
        }

        OrbitCameraModel Model
        {
            get
            {
                if (model == null) model = new OrbitCameraModel(ActiveTuning);
                return model;
            }
        }
    }
}
