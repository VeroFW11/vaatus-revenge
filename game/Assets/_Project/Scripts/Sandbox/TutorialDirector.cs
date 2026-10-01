using UnityEngine;
using VaatusRevenge.Core;

namespace VaatusRevenge
{
    // Runs the sandbox's combat tutorial: eleven short lessons against a sparring partner who can't be hurt, from the
    // beat of the string to a 20-hit, three-element graduation combo. The rules of each step (what counts as passing)
    // are pure C# in TutorialTracker, tested headless; this component is the Unity side around them:
    //   * input: tap View (or F7) to start; while it runs, tap View (or F8) to skip a step, hold View for
    //     TutorialScript.QuitHoldTime (or F7) to quit;
    //   * the arena: it hides the soldiers and crossbowmen, wakes the partner (who only fights back in the steps that
    //     say so), forces the Fluid preset (locking F5 / F6), turns on the beat ring at the player's feet and makes
    //     TutorialStart the respawn point. Quitting or finishing puts all of it back and the player at the normal spawn;
    //   * presentation: the panel at the top (HudTutorialPanel), a chime and a green flash when a step is passed, a tick
    //     for each success short of that, and a toast the first times Play starts ("Press View ...").
    // Vaatu's Revenge > Play Combat Tutorial sets AutoStartPref, so the tutorial begins on the first frame of Play.
    //
    // Execution order: after the player (0), so this frame's events are in before the tracker ticks; before the enemies
    // (10), so the partner's Passive flag is right before its brain thinks. Setup from code (the sandbox builder):
    // AddComponent, then Configure(...).
    [DefaultExecutionOrder(5)]
    [DisallowMultipleComponent]
    public class TutorialDirector : MonoBehaviour
    {
        public const string AutoStartPref = "VR.TutorialAutoStart";
        const string CompletedPref = "VR.TutorialCompleted";
        const int GuiDepth = -5;                 // under CombatHud (-10): the F1 overlay and pause screen cover the panel
        const float MinQuitHold = 0.1f;          // a hold shorter than this would make every tap a quit

        // The chime and tick are made in code (no audio files): two rising notes for a pass, a short click per success.
        const int SampleRate = 44100;
        const float ChimeLength = 0.32f;
        const float ChimeLowHz = 784f;           // G5 ...
        const float ChimeHighHz = 1175f;         // ... to D6
        const float TickLength = 0.05f;
        const float TickHz = 660f;
        const float ToneDecay = 9f;              // per second: how fast each note fades

        public static TutorialDirector Instance { get; private set; }

        [Header("Tutorial")]
        [Tooltip("The steps (Assets/_Project/Tuning/Tutorial). Empty = the built-in default.")]
        [SerializeField] private TutorialScriptAsset script;
        [Tooltip("The sparring partner (Tutorial_Partner spawn): unkillable, launchable, inactive until the tutorial starts.")]
        [SerializeField] private EnemyController partner;
        [Tooltip("Where the player stands for each lesson, facing the partner. Also the respawn point while it runs.")]
        [SerializeField] private Transform tutorialStart;

        [Header("Presentation")]
        [Tooltip("Colour of the step number, lit pips and the bars.")]
        [SerializeField] private Color accentColor = new Color(1f, 0.76f, 0.32f);
        [Tooltip("Show \"Press View for the combat tutorial\" when Play starts, until the tutorial has been finished once.")]
        [SerializeField] private bool showStartHint = true;
        [Tooltip("Real seconds the start hint stays on screen.")]
        [SerializeField] private float startHintSeconds = 4f;
        [Tooltip("Real seconds the \"tutorial complete\" message stays on screen.")]
        [SerializeField] private float endMessageSeconds = 3.5f;
        [Range(0f, 1f)]
        [Tooltip("Volume of the step chime and the progress tick.")]
        [SerializeField] private float chimeVolume = 0.45f;

        readonly HudPainter painter = new HudPainter();
        readonly HudTutorialPanel panel = new HudTutorialPanel();
        TutorialTracker tracker;
        PlayerController hookedPlayer;
        bool autoStartPending;
        bool wasPunishing;              // the preset before the tutorial forced Fluid (put back when it ends)
        bool viewCounting;              // View went down while the tutorial ran: a tap skips, a hold quits
        float viewHeldTime;
        int seenPassSerial;
        int seenProgressSerial;
        AudioSource audioSource;
        AudioClip chimeClip;
        AudioClip tickClip;

        public bool IsRunning => tracker != null && tracker.IsRunning;

        // The bottom of the tutorial panel on screen (pixels from the top) while it shows, else 0: the HUD's lock-on target
        // panel moves below it so the two never overlap.
        public float PanelBottom => IsRunning ? panel.Bottom : 0f;
        public TutorialTracker Tracker => tracker;
        public EnemyController Partner => partner;

        // Edit-time wiring (the sandbox builder).
        public void Configure(TutorialScriptAsset script, EnemyController partner, Transform tutorialStart)
        {
            this.script = script;
            this.partner = partner;
            this.tutorialStart = tutorialStart;
        }

        // With domain reload off (this project's Enter Play Mode settings), statics survive between Play sessions, so
        // they are cleared by hand when play starts.
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics()
        {
            Instance = null;
        }

        void Awake()
        {
            useGUILayout = false;   // only GUI.* calls: skip Unity's extra layout pass
            tracker = new TutorialTracker(script != null ? script.Script : null);
        }

        void OnEnable()
        {
            if (Instance != null && Instance != this)
            {
                Debug.LogWarning("TutorialDirector: another one ('" + Instance.name + "') is already active, so this one on '"
                                 + name + "' switches itself off. Keep a single tutorial director in the scene.", this);
                enabled = false;
                return;
            }
            Instance = this;
        }

        void OnDisable()
        {
            UnhookPlayer();
            if (Instance != this) return;
            // Switched off mid-tutorial (or Play stopped): stop tracking and give the sandbox its keys and spawn back.
            // Fighters aren't touched here, since Play may be tearing the scene down.
            if (IsRunning) tracker.Stop();
            SandboxDirector director = SandboxDirector.Instance;
            if (director != null)
            {
                director.PresetsLocked = false;
                director.SpawnOverride = null;
            }
            Instance = null;
        }

        void OnDestroy()
        {
            painter.Dispose();
            if (chimeClip != null) Destroy(chimeClip);
            if (tickClip != null) Destroy(tickClip);
        }

        void Start()
        {
            if (partner != null && partner.gameObject.activeSelf) partner.gameObject.SetActive(false);
            if (PlayerPrefs.GetInt(AutoStartPref, 0) == 1)
            {
                // Set by Vaatu's Revenge > Play Combat Tutorial. Cleared at once, so the next Play is a normal one.
                PlayerPrefs.DeleteKey(AutoStartPref);
                PlayerPrefs.Save();
                autoStartPending = true;   // on the first Update, once the player has run its first frame
                return;
            }
            if (showStartHint && PlayerPrefs.GetInt(CompletedPref, 0) == 0)
            {
                SandboxDirector director = SandboxDirector.Instance;
                if (director != null) director.ShowToast("Press View for the combat tutorial (F7 on the keyboard)", startHintSeconds);
            }
        }

        void Update()
        {
            if (Instance != this) return;
            HookPlayer(PlayerController.Instance);
            if (autoStartPending)
            {
                autoStartPending = false;
                StartTutorial();
            }
            ReadInput();
            if (!IsRunning) return;

            UpdatePartner();
            tracker.Tick(Time.deltaTime, Time.unscaledDeltaTime, Snapshot());
            PlayFeedback();
            if (!tracker.IsRunning) EndTutorial(tracker.IsFinished);   // the last step's flash just ended
        }

        // ---------------------------------------------------------------- start, skip, quit

        public void StartTutorial()
        {
            if (IsRunning) return;
            PlayerController player = PlayerController.Instance;
            if (partner == null || player == null)
            {
                Debug.LogWarning("TutorialDirector: no sparring partner or no player, so the tutorial can't start. Rebuild the "
                                 + "scene with Vaatu's Revenge > Build Combat Sandbox.", this);
                Toast("The tutorial isn't set up in this scene (see Console)", endMessageSeconds);
                return;
            }
            if (tracker.Script != CurrentScript()) tracker = new TutorialTracker(CurrentScript());

            // Clear the arena and bring the partner in, then put the player at the start, on Fluid.
            SandboxDirector director = SandboxDirector.Instance;
            wasPunishing = director != null && director.IsPunishing;
            partner.gameObject.SetActive(true);
            partner.ResetEnemy();
            if (director != null)
            {
                director.TutorialPartner = partner;
                director.SetEnemiesActive(false);
                if (wasPunishing) director.ApplyFluidPreset(false);
                director.PresetsLocked = true;
                director.SpawnOverride = tutorialStart;
                director.PlacePlayerAtSpawn();
            }
            else if (tutorialStart != null)
            {
                player.Respawn(tutorialStart.position, tutorialStart.eulerAngles.y);
            }
            feetRingBefore = FeetBeatRing;
            SetFeetBeatRing(true);
            // The lessons are written for the script's start element (Fire): the beat step is timed and hinted on its string,
            // so a player who was in Air when they opened the tutorial is put back in Fire (J3-01).
            player.Model.SetElementAtRest(tracker.Script.StartElement);

            tracker.Start();
            seenPassSerial = tracker.PassSerial;
            seenProgressSerial = tracker.ProgressSerial;
            viewCounting = false;
            UpdatePartner();
        }

        public void SkipStep()
        {
            if (!IsRunning) return;
            tracker.Skip();
            seenPassSerial = tracker.PassSerial;
            seenProgressSerial = tracker.ProgressSerial;
            if (!tracker.IsRunning) EndTutorial(true);
        }

        public void QuitTutorial()
        {
            if (!IsRunning) return;
            tracker.Stop();
            EndTutorial(false);
        }

        void EndTutorial(bool completed)
        {
            viewCounting = false;
            SetFeetBeatRing(feetRingBefore);        // back to the HUD's own setting
            if (partner != null) partner.gameObject.SetActive(false);
            SandboxDirector director = SandboxDirector.Instance;
            if (director != null)
            {
                director.SpawnOverride = null;
                director.PresetsLocked = false;
                director.SetEnemiesActive(true);
                if (wasPunishing) director.ApplyPunishingPreset(false);
                director.PlacePlayerAtSpawn();
            }
            if (completed)
            {
                PlayerPrefs.SetInt(CompletedPref, 1);
                PlayerPrefs.Save();
                Toast("Tutorial complete! The arena is yours: View or F7 runs it again", endMessageSeconds);
            }
            else
            {
                Toast("Tutorial ended", endMessageSeconds);
            }
        }

        void ReadInput()
        {
            PlayerInputReader reader = PlayerInputReader.Instance;
            SandboxDirector director = SandboxDirector.Instance;
            if (reader == null || (director != null && director.IsPaused))
            {
                viewCounting = false;
                return;
            }
            if (reader.TutorialKey.Pressed)
            {
                if (IsRunning) QuitTutorial();
                else StartTutorial();
            }
            if (reader.TutorialSkipKey.Pressed) SkipStep();

            // View: a press starts the tutorial; once it runs, a tap (let go before the quit time) skips and a hold quits.
            ButtonState view = reader.TutorialButton;
            if (view.Pressed)
            {
                if (IsRunning)
                {
                    viewCounting = true;
                    viewHeldTime = 0f;
                }
                else
                {
                    StartTutorial();
                    viewCounting = false;   // the press that started it is neither a skip nor a quit
                }
            }
            if (!viewCounting) return;
            if (!IsRunning)
            {
                viewCounting = false;
                return;
            }
            if (view.Held)
            {
                viewHeldTime += Time.unscaledDeltaTime;
                if (viewHeldTime >= QuitHoldTime) QuitTutorial();
            }
            else
            {
                viewCounting = false;
                SkipStep();
            }
        }

        float QuitHoldTime => Mathf.Max(MinQuitHold, tracker.Script.QuitHoldTime);

        // ---------------------------------------------------------------- the partner, the snapshot, feedback

        void UpdatePartner()
        {
            if (partner == null) return;
            EnemyBrain brain = partner.Brain;
            if (brain == null) return;
            TutorialStepData step = tracker.Step;
            brain.Passive = step == null || !step.PartnerAttacks;
        }

        TutorialSnapshot Snapshot()
        {
            PlayerController player = PlayerController.Instance;
            PlayerCombatModel model = player != null ? player.Model : null;
            float distance = -1f;
            if (player != null && partner != null && partner.isActiveAndEnabled)
            {
                Vector3 gap = partner.transform.position - player.transform.position;
                gap.y = 0f;
                distance = gap.magnitude;
            }
            return TutorialSnapshot.From(model, distance);
        }

        void PlayFeedback()
        {
            if (tracker.PassSerial != seenPassSerial)
            {
                seenPassSerial = tracker.PassSerial;
                seenProgressSerial = tracker.ProgressSerial;   // the last pip is the pass itself: one sound, the chime
                Play(chimeClip);
                return;
            }
            if (tracker.ProgressSerial == seenProgressSerial) return;
            seenProgressSerial = tracker.ProgressSerial;
            Play(tickClip);
        }

        void Play(AudioClip clip)
        {
            if (chimeVolume <= 0f) return;
            EnsureAudio();
            if (audioSource != null && clip != null) audioSource.PlayOneShot(clip, chimeVolume);
        }

        void EnsureAudio()
        {
            if (audioSource == null)
            {
                audioSource = gameObject.AddComponent<AudioSource>();
                audioSource.playOnAwake = false;
                audioSource.spatialBlend = 0f;   // a UI sound: the same in both ears wherever the camera is
                audioSource.ignoreListenerPause = true;
            }
            if (chimeClip == null) chimeClip = CreateTones("TutorialChime", ChimeLength, ChimeLowHz, ChimeHighHz);
            if (tickClip == null) tickClip = CreateTones("TutorialTick", TickLength, TickHz, TickHz);
        }

        // A short sine tone that steps from 'firstHz' to 'secondHz' half way, each half fading out.
        static AudioClip CreateTones(string clipName, float length, float firstHz, float secondHz)
        {
            int samples = Mathf.Max(1, Mathf.RoundToInt(length * SampleRate));
            var data = new float[samples];
            int half = samples / 2;
            for (int i = 0; i < samples; i++)
            {
                bool second = i >= half;
                float t = (second ? i - half : i) / (float)SampleRate;
                float hz = second ? secondHz : firstHz;
                data[i] = Mathf.Sin(2f * Mathf.PI * hz * t) * Mathf.Exp(-ToneDecay * t);
            }
            AudioClip clip = AudioClip.Create(clipName, samples, 1, SampleRate, false);
            clip.SetData(data, 0);
            return clip;
        }

        // The beat ring drawn on the floor round the player's feet is the HUD's.
        bool feetRingBefore;

        bool FeetBeatRing
        {
            get
            {
                CombatHud hud = GetComponent<CombatHud>();
                return hud != null && hud.ShowFeetBeatRing;
            }
        }

        void SetFeetBeatRing(bool on)
        {
            CombatHud hud = GetComponent<CombatHud>();
            if (hud != null) hud.ShowFeetBeatRing = on;
        }

        void Toast(string message, float seconds)
        {
            SandboxDirector director = SandboxDirector.Instance;
            if (director != null) director.ShowToast(message, seconds);
            else Debug.Log(message, this);
        }

        TutorialScript CurrentScript()
        {
            return script != null && script.Script != null ? script.Script : tracker.Script;
        }

        // ---------------------------------------------------------------- the player's events

        void HookPlayer(PlayerController current)
        {
            if (ReferenceEquals(hookedPlayer, current)) return;
            UnhookPlayer();
            hookedPlayer = current;
            if (current != null) current.GameplayEvent += OnGameplayEvent;
        }

        void UnhookPlayer()
        {
            // GameplayEvent is a plain C# event, so unsubscribing is safe even if the player's GameObject was destroyed.
            if (!ReferenceEquals(hookedPlayer, null)) hookedPlayer.GameplayEvent -= OnGameplayEvent;
            hookedPlayer = null;
        }

        void OnGameplayEvent(in PlayerEvent e)
        {
            if (IsRunning) tracker.OnEvent(e);
        }

        // ---------------------------------------------------------------- the panel

        void OnGUI()
        {
            if (!IsRunning) return;
            if (Event.current.type != EventType.Repaint) return;
            GUI.depth = GuiDepth;
            Color previous = GUI.color;
            painter.Begin();
            PlayerInputReader reader = PlayerInputReader.Instance;
            bool gamepad = reader != null && reader.UsingGamepad;
            float quitHold = viewCounting ? Mathf.Clamp01(viewHeldTime / QuitHoldTime) : 0f;
            panel.Draw(painter, tracker, gamepad, quitHold, accentColor);
            GUI.color = previous;
        }
    }
}
