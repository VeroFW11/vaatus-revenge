using UnityEngine;

namespace VaatusRevenge
{
    // The one place that writes Time.timeScale. Gameplay asks for time effects through the static methods
    // and this resolves overlapping requests: pause beats everything, then hitstop, then the slowest active
    // slow motion (including the F2 debug slow motion).
    //
    // Hitstop is a freeze-frame of a few hundredths of a second when a hit lands. Nearly every action game
    // does it: the brief stop lets the eye register the contact and makes blows feel heavy instead of
    // passing through. Slow motion is the longer, gentler version used as the perfect-dodge reward.
    //
    // Timers count in real (unscaled) time, otherwise a freeze would never end. Creates itself on first use
    // in play mode only, never in an edited scene, and always puts time back to normal when play stops.
    [DefaultExecutionOrder(-200)]
    public class TimeScaleController : MonoBehaviour
    {
        // Hitstop slows almost to a stop rather than to 0, so code that skips work when deltaTime is 0
        // (menus, pause checks) doesn't mistake a hit for a pause.
        public const float HitstopScale = 0.02f;
        const int MaxSlowRequests = 4;
        // Safety net: a broken tuning value can't freeze the game for longer than this.
        const float MaxRequestSeconds = 5f;

        static TimeScaleController instance;
        static bool quitting;
        static float debugSlowScale = 0.25f;

        float hitstopRemaining;
        readonly float[] slowScale = new float[MaxSlowRequests];
        readonly float[] slowRemaining = new float[MaxSlowRequests];
        bool paused;
        bool debugSlow;

        public static bool IsHitstopActive => instance != null && instance.hitstopRemaining > 0f;
        public static bool IsPaused => instance != null && instance.paused;
        public static bool IsDebugSlowMotion => instance != null && instance.debugSlow;
        public static bool IsSlowMotionActive => instance != null && instance.SlowestRequest() < 1f;

        // Speed used by SetDebugSlowMotion (F2). Kept here rather than in player tuning because it's a
        // sandbox study tool, not part of the game's balance.
        public static float DebugSlowMotionScale
        {
            get => debugSlowScale;
            set
            {
                debugSlowScale = Mathf.Clamp(value, 0.01f, 1f);
                if (instance != null) instance.Apply();
            }
        }

        // Freeze-frame for this many real seconds. Overlapping hitstops don't add up (three enemies caught
        // in one explosion shouldn't freeze three times as long); the longest one wins.
        public static void Hitstop(float seconds)
        {
            if (!(seconds > 0f)) return; // also rejects NaN
            TimeScaleController controller = GetOrCreate();
            if (controller == null) return;
            controller.hitstopRemaining = Mathf.Max(controller.hitstopRemaining, Mathf.Min(seconds, MaxRequestSeconds));
            controller.Apply();
        }

        // Runs the game at 'scale' (e.g. 0.35) for this many real seconds.
        public static void SlowMotion(float seconds, float scale)
        {
            if (!(seconds > 0f) || !(scale < 1f)) return;
            TimeScaleController controller = GetOrCreate();
            if (controller == null) return;
            controller.AddSlowRequest(Mathf.Min(seconds, MaxRequestSeconds), Mathf.Clamp(scale, 0.01f, 1f));
            controller.Apply();
        }

        // Pause stops time completely and also stops hitstop/slow-motion timers from running out meanwhile.
        public static void SetPaused(bool paused)
        {
            TimeScaleController controller = paused ? GetOrCreate() : instance;
            if (controller == null) return;
            controller.paused = paused;
            controller.Apply();
        }

        public static void SetDebugSlowMotion(bool on)
        {
            TimeScaleController controller = on ? GetOrCreate() : instance;
            if (controller == null) return;
            controller.debugSlow = on;
            controller.Apply();
        }

        // Cancels hitstop and slow motion (keeps pause and debug slow motion). Useful on respawn or reset.
        public static void ClearEffects()
        {
            if (instance == null) return;
            instance.hitstopRemaining = 0f;
            for (int i = 0; i < MaxSlowRequests; i++) instance.slowRemaining[i] = 0f;
            instance.Apply();
        }

        static TimeScaleController GetOrCreate()
        {
            if (instance != null) return instance;
            if (!Application.isPlaying || quitting) return null;
            var go = new GameObject("TimeScaleController");
            DontDestroyOnLoad(go);
            return go.AddComponent<TimeScaleController>(); // Awake sets instance
        }

        // With domain reload turned off (this project's Enter Play Mode settings) statics survive between
        // play sessions, so they are reset by hand each time play starts.
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics()
        {
            instance = null;
            quitting = false;
            debugSlowScale = 0.25f;
            Time.timeScale = 1f;
            Application.quitting -= OnQuitting;
            Application.quitting += OnQuitting;
        }

        static void OnQuitting()
        {
            quitting = true;
            Time.timeScale = 1f;
        }

        void Awake()
        {
            if (instance != null && instance != this)
            {
                Destroy(gameObject);
                return;
            }
            instance = this;
        }

        void Update()
        {
            float dt = Time.unscaledDeltaTime;
            if (!paused && dt > 0f)
            {
                hitstopRemaining -= dt;
                // Round to the nearest frame: a new time scale only takes effect next frame, so without this a
                // 2-frame hitstop would last 3.
                if (hitstopRemaining <= dt * 0.5f) hitstopRemaining = 0f;
                for (int i = 0; i < MaxSlowRequests; i++)
                {
                    if (slowRemaining[i] > 0f) slowRemaining[i] = Mathf.Max(0f, slowRemaining[i] - dt);
                }
            }
            Apply();
        }

        void OnDisable()
        {
            if (instance == this) Time.timeScale = 1f;
        }

        void OnDestroy()
        {
            if (instance != this) return;
            Time.timeScale = 1f;
            instance = null;
        }

        void OnApplicationQuit()
        {
            quitting = true;
            Time.timeScale = 1f;
        }

        void AddSlowRequest(float seconds, float scale)
        {
            // Reuse a free slot; if all are busy, replace the one closest to finishing.
            int slot = 0;
            for (int i = 0; i < MaxSlowRequests; i++)
            {
                if (slowRemaining[i] <= 0f)
                {
                    slot = i;
                    break;
                }
                if (slowRemaining[i] < slowRemaining[slot]) slot = i;
            }
            slowScale[slot] = scale;
            slowRemaining[slot] = seconds;
        }

        float SlowestRequest()
        {
            float slowest = 1f;
            for (int i = 0; i < MaxSlowRequests; i++)
            {
                if (slowRemaining[i] > 0f && slowScale[i] < slowest) slowest = slowScale[i];
            }
            return slowest;
        }

        void Apply()
        {
            float scale;
            if (paused) scale = 0f;
            else if (hitstopRemaining > 0f) scale = HitstopScale;
            else
            {
                scale = SlowestRequest();
                if (debugSlow) scale = Mathf.Min(scale, debugSlowScale);
            }
            if (Time.timeScale != scale) Time.timeScale = scale;
        }
    }
}
