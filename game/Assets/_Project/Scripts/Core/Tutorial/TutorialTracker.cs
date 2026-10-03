using System;

namespace VaatusRevenge.Core
{
    // Runs a TutorialScript: which step is showing, how far the player is through it, and when it's passed. Pure rules
    // with no Unity in them, so every step can be tested by driving a real PlayerCombatModel (TutorialTrackerTests).
    //
    // Feed it every player event (OnEvent, in the order the model raised them) and then Tick once per frame. Events are
    // judged against the snapshot from the previous Tick, which is the state they started from (a slip-in is measured
    // from where the dodge began, not a frame into it).
    //
    // "The same combo" means no ComboEnded in between. The combo count and MIX are followed from the events (ComboHit,
    // MixChanged, ComboEnded) even between steps and during the success flash, so a combo carried from one step into the
    // next still counts. Goal progress itself only moves while a step is live.
    //
    // A passed step shows its green flash for TutorialScript.SuccessPause (real time) and then moves on; Skip moves on at
    // once. The Unity side notices passes and progress through PassSerial and ProgressSerial (chime, flash, tick).
    public sealed class TutorialTracker
    {
        const double WindowSlack = 1e-6;   // float sums: a follow-up landing exactly on a window's end still counts
        const float MinFlash = 1e-4f;      // a passed step always shows its flash for at least one frame
        const float AlmostFull = 0.95f;    // the graduation bar stops here until the MIX is there too

        readonly TutorialScript script;

        bool running;
        bool finished;
        int stepIndex = -1;
        int progress;
        int passSerial;
        int progressSerial;
        float stepTime;                // real seconds the step has been showing
        float successPauseLeft;        // > 0: the step was passed, its flash is showing
        double clock;                  // game seconds since Start (the follow-up windows are game time, like the model's)
        TutorialSnapshot last;

        // Followed from the events, whatever the step.
        int comboCount;
        int mixLevel;
        int comboSerial;               // +1 at every ComboEnded: "the same combo" = the same serial
        int stringOnBeat;              // on-beat presses in the current main string

        // Per-step goal state (reset when a step begins).
        int armedCount = -1;           // DodgeKeepsCombo: the combo count when the dodge started
        int armedSerial;
        double armedUntil = -1.0;      // SlipInStrike / DangerResponse: the follow-up must come by then
        bool respondedParry, respondedDodge; // DangerResponse with NeedsParryAndDodge: which kinds have counted
        bool launched;                 // LaunchAndJuggle: the launcher landed in combo 'launchSerial'
        int launchSerial;
        bool switched;                 // SwitchStrikeTo: switched into the step's element in combo 'switchSerial'
        int switchSerial;
        int lastCountedInstance;       // a multi-hit move passes a step once, not once per sub-hit

        public TutorialTracker(TutorialScript script)
        {
            this.script = script ?? TutorialScript.CreateDefault();
        }

        public TutorialScript Script => script;
        public bool IsRunning => running;
        public bool IsFinished => finished;        // the last step was passed or skipped (until the next Start)
        public int StepIndex => stepIndex;
        public int StepCount => script.StepCount;
        public TutorialStepData Step => running ? script.StepAt(stepIndex) : null;
        public int Progress => progress;
        public int Required => Step != null ? Math.Max(1, Step.Count) : 1;
        public float StepTime => stepTime;
        public bool InSuccessPause => running && successPauseLeft > 0f;
        public int PassSerial => passSerial;       // +1 each time a step is passed
        public int ProgressSerial => progressSerial; // +1 each time a pip lights (including the last one)

        // 1 just after passing, falling to 0 as the next step comes up.
        public float SuccessPause01 => InSuccessPause && script.SuccessPause > 0f ? Math.Min(1f, successPauseLeft / script.SuccessPause) : 0f;

        // How close the current attempt is (0..1) for goals with a running total: on-beat presses for the beat step,
        // combo hits for the graduation, time for the timed step, distance from the partner for the slip-in step. 0 for
        // the others.
        public float AttemptProgress01
        {
            get
            {
                TutorialStepData step = Step;
                if (step == null) return 0f;
                switch (step.Goal)
                {
                    case TutorialGoal.Timed: return Fraction(stepTime, step.Duration);
                    // The slip-in step: how far back you are, full once you're far enough to slip in from (J3-S13).
                    case TutorialGoal.SlipInStrike: return Fraction(last.TargetDistance, step.MinStartDistance);
                    case TutorialGoal.OnBeatFinisher: return Fraction(stringOnBeat, step.MinOnBeat);
                    case TutorialGoal.BigMixedCombo:
                        // The hits fill the bar; it only reaches the end once enough elements are in the mix too.
                        float hits = Fraction(comboCount, step.MinComboCount);
                        return mixLevel >= step.MinMixLevel ? hits : Math.Min(hits, AlmostFull);
                    default: return 0f;
                }
            }
        }

        // SwitchStrikeTo while the player is already in that element: show AlreadyInElementHint instead of the hint.
        public bool AlreadyInTargetElement
        {
            get
            {
                TutorialStepData step = Step;
                return step != null && step.Goal == TutorialGoal.SwitchStrikeTo && !switched && !InSuccessPause
                       && last.ActiveElement == step.Element;
            }
        }

        public int ComboCount => comboCount;
        public int MixLevel => mixLevel;

        public void Start()
        {
            running = script.StepCount > 0;
            finished = false;
            clock = 0.0;
            comboCount = 0;
            mixLevel = 0;
            stringOnBeat = 0;
            BeginStep(0);
        }

        // Quit: nothing more is tracked until the next Start.
        public void Stop()
        {
            running = false;
            stepIndex = -1;
            successPauseLeft = 0f;
        }

        // Move on without passing (tap View / F8). During the success flash it just ends the flash early.
        public void Skip()
        {
            if (!running) return;
            Advance();
        }

        public void Tick(float dt, float realDt, in TutorialSnapshot snapshot)
        {
            last = snapshot;
            if (!running) return;
            if (dt > 0f) clock += dt;
            if (realDt > 0f) stepTime += realDt;

            if (successPauseLeft > 0f)
            {
                successPauseLeft -= Math.Max(0f, realDt);
                if (successPauseLeft <= 0f) Advance();
                return;
            }
            TutorialStepData step = Step;
            if (step != null && step.Goal == TutorialGoal.Timed && stepTime >= step.Duration) Pass(Required - progress);
        }

        public void OnEvent(in PlayerEvent e)
        {
            Follow(e);
            if (!running || successPauseLeft > 0f) return;
            TutorialStepData step = Step;
            if (step == null) return;
            switch (step.Goal)
            {
                case TutorialGoal.OnBeatFinisher:
                    if (e.Type == PlayerEventType.ComboHit && e.IsFinisher && e.Branch == ComboBranch.Main
                        && stringOnBeat >= step.MinOnBeat && NewInstance(e)) Pass(1);
                    break;
                case TutorialGoal.PauseFinisher:
                    if (e.Type == PlayerEventType.ComboHit && e.IsFinisher && e.Branch == ComboBranch.Pause && NewInstance(e)) Pass(1);
                    break;
                case TutorialGoal.DodgeKeepsCombo:
                    if (e.Type == PlayerEventType.DodgeStarted && comboCount >= step.MinComboCount)
                    {
                        armedCount = comboCount;
                        armedSerial = comboSerial;
                    }
                    else if (e.Type == PlayerEventType.ComboHit && armedCount >= 0 && armedSerial == comboSerial && e.Count > armedCount)
                    {
                        armedCount = -1;
                        Pass(1);
                    }
                    break;
                case TutorialGoal.SlipInStrike:
                    if (e.Type == PlayerEventType.DodgeStarted)
                    {
                        bool fromRange = e.DodgeKind == DodgeKind.SlipIn && last.TargetDistance >= step.MinStartDistance;
                        armedUntil = fromRange ? clock + step.FollowUpWindow : -1.0;
                    }
                    else if (e.Type == PlayerEventType.ComboHit && armedUntil >= 0.0 && clock <= armedUntil + WindowSlack)
                    {
                        armedUntil = -1.0;
                        Pass(1);
                    }
                    break;
                case TutorialGoal.DodgeStrike:
                    if (e.Type == PlayerEventType.ComboHit && e.Branch == ComboBranch.DodgeStrike && NewInstance(e)) Pass(1);
                    break;
                case TutorialGoal.LaunchAndJuggle:
                    if (e.Type != PlayerEventType.ComboHit) break;
                    if (e.AttackKind == PlayerAttackKind.Launcher)
                    {
                        launched = true;
                        launchSerial = comboSerial;
                    }
                    else if (launched && launchSerial == comboSerial && e.Branch == ComboBranch.Air && e.InAir && IsLastAirHit(e))
                    {
                        launched = false;
                        Pass(1);
                    }
                    break;
                case TutorialGoal.SwitchStrikeTo:
                    // The switch strike, or a plain switch made while the combo is still going (a switch strike that waited
                    // too long in a dodge, J3-S03): either way the string carries on in the new element with the next hit.
                    if (e.Type == PlayerEventType.ElementSwitched && e.Element == step.Element && (e.IsSwitchStrike || comboCount > 0))
                    {
                        switched = true;
                        switchSerial = comboSerial;
                    }
                    else if (e.Type == PlayerEventType.ComboHit && switched && switchSerial == comboSerial && e.Element == step.Element)
                    {
                        switched = false;
                        Pass(1);
                    }
                    break;
                case TutorialGoal.MixFinisher:
                    if (e.Type == PlayerEventType.MixFinisher && e.Count >= step.MinMixLevel) Pass(1);
                    break;
                case TutorialGoal.DangerResponse:
                    if (e.Type == PlayerEventType.DangerNow) armedUntil = clock + step.FollowUpWindow;
                    else if ((e.Type == PlayerEventType.Deflected || e.Type == PlayerEventType.PerfectDodge)
                             && armedUntil >= 0.0 && clock <= armedUntil + WindowSlack)
                    {
                        bool parry = e.Type == PlayerEventType.Deflected;
                        if (step.NeedsParryAndDodge && (parry ? respondedParry : respondedDodge)) break;   // already have one of those
                        if (parry) respondedParry = true;
                        else respondedDodge = true;
                        armedUntil = -1.0;
                        Pass(1);
                    }
                    break;
                case TutorialGoal.BigMixedCombo:
                    if ((e.Type == PlayerEventType.ComboHit || e.Type == PlayerEventType.MixChanged)
                        && comboCount >= step.MinComboCount && mixLevel >= step.MinMixLevel) Pass(1);
                    break;
            }
        }

        // ---------------------------------------------------------------- internals

        // The combo, MIX and the main string's on-beat count, whatever the step.
        void Follow(in PlayerEvent e)
        {
            switch (e.Type)
            {
                case PlayerEventType.ComboHit:
                    comboCount = e.Count;
                    break;
                case PlayerEventType.MixChanged:
                    mixLevel = e.Count;
                    break;
                case PlayerEventType.ComboEnded:
                    comboCount = 0;
                    mixLevel = 0;
                    stringOnBeat = 0;
                    comboSerial++;
                    break;
                case PlayerEventType.BeatJudged:
                    if (e.Grade == BeatGrade.OnBeat) stringOnBeat++;
                    else if (e.Grade == BeatGrade.Mashed) stringOnBeat = Math.Max(0, stringOnBeat - 1); // a lucky mash after all
                    break;
                case PlayerEventType.AttackStarted:
                    // A fresh main string (its first hit is never judged: the press came before it started).
                    if (e.Branch == ComboBranch.Main && e.ChainIndex == 0 && e.AttackKind == PlayerAttackKind.Light) stringOnBeat = 0;
                    break;
            }
        }

        bool NewInstance(in PlayerEvent e)
        {
            int instance = e.MoveInstanceId != 0 ? e.MoveInstanceId : e.AttackId;
            if (instance != 0 && instance == lastCountedInstance) return false;
            lastCountedInstance = instance;
            return true;
        }

        bool IsLastAirHit(in PlayerEvent e)
        {
            ElementMoveSet set = last.Loadout != null ? last.Loadout.Get(e.Element) : null;
            MoveData[] chain = set != null ? set.AirChain : null;
            return chain != null && chain.Length > 0 && e.ChainIndex == chain.Length - 1;
        }

        void Pass(int amount)
        {
            if (amount <= 0) return;
            progress = Math.Min(Required, progress + amount);
            progressSerial++;
            if (progress < Required) return;
            passSerial++;
            successPauseLeft = Math.Max(MinFlash, script.SuccessPause);
        }

        void Advance()
        {
            if (stepIndex + 1 >= script.StepCount)
            {
                running = false;
                finished = true;
                stepIndex = -1;
                successPauseLeft = 0f;
                return;
            }
            BeginStep(stepIndex + 1);
        }

        void BeginStep(int index)
        {
            stepIndex = index;
            progress = 0;
            stepTime = 0f;
            successPauseLeft = 0f;
            armedCount = -1;
            armedUntil = -1.0;
            respondedParry = false;
            respondedDodge = false;
            launched = false;
            switched = false;
            lastCountedInstance = 0;
        }

        static float Fraction(float value, float of)
        {
            if (!(of > 0f)) return value > 0f ? 1f : 0f;
            return Math.Max(0f, Math.Min(1f, value / of));
        }
    }
}
