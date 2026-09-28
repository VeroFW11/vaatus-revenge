using UnityEngine;
using VaatusRevenge.Core;

namespace VaatusRevenge
{
    // The third-person camera: orbits the player, frames the lock-on target, slides in front of walls, and
    // plays screen shake and the sprint FOV boost. Put it on the GameObject that has the scene's Camera
    // (it adds one if missing). Don't add a Cinemachine Brain to that camera: it would fight this rig.
    //
    // It runs in LateUpdate, after the player and enemies have moved in Update, so it always frames where
    // everyone is this frame (moving the camera before the player moves is the classic cause of jitter).
    // The rules live in OrbitCameraModel (pure C#); this class feeds it input, lock-on and wall probes and
    // copies the result onto the Camera.
    //
    // Setup from code (e.g. an editor scene builder): AddComponent, then Configure(tuningAsset, playerRoot).
    [DefaultExecutionOrder(100)]
    [DisallowMultipleComponent]
    [RequireComponent(typeof(Camera))]
    public class ThirdPersonCameraRig : MonoBehaviour
    {
        public static ThirdPersonCameraRig Instance { get; private set; }

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
        bool warnedNoTuning;
        bool warnedNoCamera;
        bool warnedNoTarget;

        // Camera yaw in degrees (clockwise from +Z). The player moves relative to this.
        public float Yaw => Model.Yaw;
        // Camera pitch in degrees, positive = looking down.
        public float Pitch => Model.Pitch;
        public Camera Camera => ResolveCamera();
        public Transform FollowTarget => followTarget;
        // The numbers in use right now (the asset's, or the defaults when none is assigned).
        public CameraTuning Tuning => ActiveTuning;
        // What the player should pass to SetFovBoost while sprinting.
        public float SprintFovBoost => ActiveTuning.SprintFovBoost;
        // Flat forward direction of the camera, handy for camera-relative movement.
        public Vector3 PlanarForward => Directions.FromYaw(Model.Yaw).ToUnity();

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
            if (playing) Model.UpdateDistance(ProbeWalls(tuning), 0f);
            ApplyToCamera(cam, tuning, 0f, playing);
        }

        // Smoothly swings the camera behind the follow target (Elden Ring's "lock-on with nothing to lock").
        // Moving the camera yourself cancels it.
        public void RecenterBehindTarget()
        {
            Transform target = ResolveFollowTarget();
            if (target != null) Model.BeginRecenter(target.eulerAngles.y);
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

            // Real time for anything the player controls or feels (look, lock-on swing, wall easing, FOV),
            // so the camera stays responsive while hitstop or slow motion changes Time.timeScale.
            float realDt = Time.unscaledDeltaTime;

            PlayerInputReader reader = PlayerInputReader.Instance;
            PlayerInputFrame frame = reader != null ? reader.Frame : default(PlayerInputFrame);
            OrbitCameraInput input = new OrbitCameraInput { Look = frame.Look, LookIsMouse = frame.LookIsMouse };
            LockOnController lockOn = LockOnController.Instance;
            Combatant lockTarget = lockOn != null ? lockOn.Target : null;
            if (lockTarget != null)
            {
                input.HasLockTarget = true;
                input.LockTargetPoint = lockTarget.AimPoint.position.ToNumerics();
            }

            // Game time for following the player: see OrbitCameraModel.UpdatePivot for why.
            orbit.UpdatePivot(target.position.ToNumerics(), Time.deltaTime);
            orbit.UpdateOrientation(input, realDt);
            orbit.UpdateDistance(ProbeWalls(tuning), realDt);
            ApplyToCamera(cam, tuning, realDt, true);
        }

        // How far the camera can back away from the pivot before its collision sphere touches level geometry.
        // Only the environment layer counts, so fighters walking behind the player never shove the camera.
        float ProbeWalls(CameraTuning tuning)
        {
            OrbitCameraModel orbit = Model;
            float length = orbit.DesiredDistance;
            if (!(length > 0f)) return 0f;

            Vector3 pivot = orbit.Pivot.ToUnity();
            Vector3 back = -orbit.Forward.ToUnity();
            float radius = Mathf.Max(0f, tuning.CollisionRadius);
            RaycastHit hit;
            if (radius <= 0.001f)
            {
                return Physics.Raycast(pivot, back, out hit, length, Layers.EnvironmentMask, QueryTriggerInteraction.Ignore)
                    ? hit.distance
                    : length;
            }
            if (Physics.CheckSphere(pivot, radius, Layers.EnvironmentMask, QueryTriggerInteraction.Ignore))
            {
                // The pivot's sphere already touches something (e.g. it trailed round a pillar corner). SphereCast
                // can't see colliders it starts inside, so use a thin ray and stay one radius clear of the hit.
                return Physics.Raycast(pivot, back, out hit, length, Layers.EnvironmentMask, QueryTriggerInteraction.Ignore)
                    ? Mathf.Max(0f, hit.distance - radius)
                    : length;
            }
            return Physics.SphereCast(pivot, radius, back, out hit, length, Layers.EnvironmentMask, QueryTriggerInteraction.Ignore)
                ? hit.distance
                : length;
        }

        void ApplyToCamera(Camera cam, CameraTuning tuning, float realDt, bool withShake)
        {
            OrbitCameraModel orbit = Model;
            // Shake only rotates the view (a moved camera could end up inside a wall) and doesn't touch Yaw/Pitch,
            // so movement directions never wobble with it.
            Vector3 shake = withShake && Instance == this
                ? CameraShake.Advance(realDt, tuning.ShakeFrequency, tuning.ShakeMaxAngle)
                : Vector3.zero;
            Quaternion rotation = Quaternion.Euler(orbit.Pitch + shake.x, orbit.Yaw + shake.y, shake.z);
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
