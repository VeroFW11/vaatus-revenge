using System;

namespace VaatusRevenge.Core
{
    // Rhythm combos, string memory and the pause branch.
    //
    // THE STRING: the light chain (X X X X X), the pause chain (X X, wait, X), the air chain and the dodge strike are
    //   "string moves". Each knows its slot (chainIndex) in its chain (chainBranch: Main, Pause or Air).
    // STRING MEMORY: a string interrupted by a dodge, zip, ability, skill, heavy, guard or jump isn't lost. RememberString
    //   (on the way out of a string move) notes the next slot: the same one if the move hadn't struck yet (retry it),
    //   else the one after (the ground chains loop, the air chain doesn't). The memory is held while that action runs and
    //   for StringMemoryAfterAction after it, so X X, dodge, X is hit 3. A string move that just finishes keeps it for its
    //   old grace (ComboWindowEnd past the end of the move) or its pause band. A clean hit taken, a stagger, a deflected
    //   attack, death and a preset change clear it.
    // THE BEAT: every string move's beat is the moment it strikes (attack start + ActiveStart / playback rate, on the game
    //   clock). The FIRST follow-up press during a string move is judged against it (BeatJudged):
    //     before the window      Early  (a mash): the next move plays at OffBeatPlaybackRate and costs MashStaminaSurcharge more
    //     inside the window      OnBeat: the next move plays at OnBeatPlaybackRate, hits harder and gets the element's perk;
    //                                    a second press before the next move starts downgrades it to Mashed (as Early)
    //     after it, in the combo window  Late: neutral, and the streak starts over
    //     after the combo window Pause, if this move opens the pause branch (see below); else the old restart rules
    //   A judged press is queued at once (InputBuffer.Lock with the move's cancel point), so a beat press early in a long
    //   move can never expire before it can run. The playback rate scales the move's whole timeline (AdvanceAction), so
    //   every cancel point, window and lunge follows it and no frame data changes. Poise is never scaled by rhythm.
    // THE PAUSE BRANCH: after main-chain hit ElementRhythm.PauseAfterIndex (the 2nd) with no follow-up press, a press in
    //   (ComboWindowEnd, end of move + PauseGrace] starts PauseChain[0]; after the pause chain the string loops to the
    //   main chain's first hit. Anything in between (a dodge, a zip...) makes that press a string-memory continue instead.
    // PERFECT STRING: if every follow-up of a string was on the beat, its finisher (main or pause) hits harder and, on a
    //   launchable foe, launches (PerfectString).
    // RhythmTuning.Enabled = false restores the old chain timing exactly (no judgement, no playback rate, no pause branch).
    public sealed partial class PlayerCombatModel
    {
        readonly RhythmTuning fallbackRhythm = new RhythmTuning { Enabled = false };
        readonly ElementRhythm fallbackElementRhythm = new ElementRhythm();

        // String memory.
        int stringNext = -1;          // the string's next slot after an interruption (-1 = none)
        ComboBranch stringBranch;     // ...in this chain (Main, Pause or Air)
        double stringMemoryUntil = double.NegativeInfinity;
        bool stringHeld;              // an action that keeps the string (dodge, zip, ability, skill, heavy, guard, jump) is running
        bool stringKeptByDodge;       // the last thing that held the string was a dodge (Air's DodgeKeepsBeat keeps the streak)
        bool streakKeptByDodge;       // ...and the string move about to start continues it that way

        // The beat of the running string move.
        bool beatActive;
        double beatClock;
        float beatEarly;
        float beatLate;
        bool beatWindowAnnounced;
        double cueFromClock;          // when the running beat's ring appeared (J3-01: with the previous move, if it queued this one)
        double pendingCueFrom = double.NegativeInfinity;   // a judged follow-up press: when the next move's ring appeared
        int followUpCount;            // presses made during the running move
        BeatGrade followUpGrade;      // the first one's grade (Mashed after a downgrade)
        bool earlyPressSeen;
        BeatGrade lastGrade;
        int onBeatStreak;
        bool stringPerfect;           // every follow-up of this string so far was on the beat
        int stringFollowUps;          // ...and how many there were

        // The pause band after an eligible move has finished (while still in it, the move itself judges the press).
        bool pauseBandOpen;
        double pauseBandUntil = double.NegativeInfinity;

        RhythmTuning RhythmRules => tuning.Rhythm ?? fallbackRhythm;
        bool RhythmOn => RhythmRules.Enabled;

        ElementRhythm RhythmOf(ElementMoveSet set)
        {
            return set != null && set.Rhythm != null ? set.Rhythm : fallbackElementRhythm;
        }

        bool IsStringMemoryLive => stringNext >= 0 && (stringHeld || clock <= stringMemoryUntil + 1e-6);

        static bool IsStringKind(PlayerAttackKind kind)
        {
            return kind == PlayerAttackKind.Light || kind == PlayerAttackKind.Air || kind == PlayerAttackKind.DodgeStrike;
        }

        bool IsStringMoveRunning => state == PlayerState.Attacking && IsStringKind(attackKind);

        // Actions that keep the string alive while they run (and StringMemoryAfterAction after).
        static bool HoldsString(PlayerState state, PlayerAttackKind kind)
        {
            switch (state)
            {
                case PlayerState.Dodging:
                case PlayerState.Charging:
                case PlayerState.Guarding:
                case PlayerState.Airborne:
                    return true;
                case PlayerState.Attacking:
                    return kind == PlayerAttackKind.ZipStrike || kind == PlayerAttackKind.Ability || kind == PlayerAttackKind.Skill
                        || kind == PlayerAttackKind.Heavy;
                default:
                    return false;
            }
        }

        static MoveData[] ChainOf(ElementMoveSet set, ComboBranch chainBranch)
        {
            if (set == null) return null;
            switch (chainBranch)
            {
                case ComboBranch.Pause: return set.PauseChain;
                case ComboBranch.Air: return set.AirChain;
                default: return set.LightChain;
            }
        }

        static int LengthOf(MoveData[] chain)
        {
            return chain == null ? 0 : chain.Length;
        }

        void ClearStringMemory()
        {
            stringNext = -1;
            stringBranch = ComboBranch.Main;
            stringMemoryUntil = double.NegativeInfinity;
            stringHeld = false;
            stringKeptByDodge = false;
            pauseBandOpen = false;
        }

        void ResetRhythm()
        {
            ClearStringMemory();
            beatActive = false;
            followUpCount = 0;
            followUpGrade = BeatGrade.None;
            earlyPressSeen = false;
            lastGrade = BeatGrade.None;
            onBeatStreak = 0;
            stringPerfect = false;
            stringFollowUps = 0;
            attackPlaybackRate = 1f;
            pendingCueFrom = double.NegativeInfinity;
        }

        // ---------------------------------------------------------------- string memory

        // On the way out of a string move (finished or cut short): note where the string goes next.
        void RememberString()
        {
            MoveData move = currentMove;
            if (move == null || chainIndex < 0) return;
            if (action.Time < move.ActiveStart)
            {
                stringNext = chainIndex;                 // it never struck: retry the same hit
                stringBranch = chainBranch;
            }
            else
            {
                NextSlot(chainBranch, chainIndex, moveIsChainFinisher, out stringBranch, out stringNext);
            }
            // A move that runs its course keeps the string for its old grace: a combo window reaching past its end.
            float grace = Math.Max(0f, move.ComboWindowEnd - move.TotalDuration) / Math.Max(attackPlaybackRate, Epsilon);
            stringMemoryUntil = clock + grace;
            stringHeld = false;
        }

        // The slot after 'index' in 'chainBranch': the main chain loops after its finisher, the pause chain goes back to
        // the main chain's first hit, the air chain is spent (-1).
        static void NextSlot(ComboBranch chainBranch, int index, bool wasFinisher, out ComboBranch nextBranch, out int next)
        {
            nextBranch = chainBranch;
            if (!wasFinisher)
            {
                next = index + 1;
                return;
            }
            switch (chainBranch)
            {
                case ComboBranch.Air:
                    next = -1;
                    break;
                case ComboBranch.Pause:
                    nextBranch = ComboBranch.Main;
                    next = 0;
                    break;
                default:
                    next = 0;
                    break;
            }
        }

        // An action that keeps the string just started: the string waits for it.
        void HoldStringIfLive(bool byDodge)
        {
            if (!IsStringMemoryLive) return;
            stringHeld = true;
            stringKeptByDodge = byDodge;
        }

        // That action ended: the string waits StringMemoryAfterAction more.
        void ReleaseHeldString()
        {
            if (!stringHeld) return;
            stringHeld = false;
            stringMemoryUntil = Math.Max(stringMemoryUntil, clock + Math.Max(0f, tuning.StringMemoryAfterAction));
        }

        // Landing ends an air string; a ground string that was only held by the jump waits a moment more.
        void OnLandedForString()
        {
            if (stringNext >= 0 && stringBranch == ComboBranch.Air)
            {
                ClearStringMemory();
                return;
            }
            ReleaseHeldString();
        }

        // ---------------------------------------------------------------- the beat

        // Called when a string move starts: works out its beat window.
        void BeginBeat(ElementMoveSet set)
        {
            followUpCount = 0;
            followUpGrade = BeatGrade.None;
            earlyPressSeen = false;
            beatWindowAnnounced = false;
            beatActive = RhythmOn && IsStringKind(attackKind);
            // The ring of a move queued by a judged press has been closing since that press (the predicted next beat);
            // any other string move's ring appears as it starts.
            bool queued = moveGrade == BeatGrade.OnBeat || moveGrade == BeatGrade.Early || moveGrade == BeatGrade.Late
                || moveGrade == BeatGrade.Mashed;
            cueFromClock = queued && pendingCueFrom > double.NegativeInfinity && pendingCueFrom <= clock ? pendingCueFrom : attackBeganClock;
            pendingCueFrom = double.NegativeInfinity;
            if (!beatActive) return;
            RhythmTuning rules = RhythmRules;
            ElementRhythm element = RhythmOf(set);
            beatEarly = Math.Max(0f, rules.BeatEarly + element.BeatEarlyDelta);
            beatLate = Math.Max(0f, rules.BeatLate + element.BeatLateDelta);
            beatClock = attackStartClock + currentMove.ActiveStart / Math.Max(attackPlaybackRate, Epsilon);
            AnnounceBeatWindow();
        }

        // ComboBeatOpened as the window opens (the HUD's ring has been closing on the beat since the move started).
        void AnnounceBeatWindow()
        {
            if (!beatActive || beatWindowAnnounced || clock < beatClock - beatEarly - 1e-6) return;
            beatWindowAnnounced = true;
            Emit(new PlayerEvent
            {
                Type = PlayerEventType.ComboBeatOpened, Move = currentMove, AttackId = currentAttackId, ChainIndex = chainIndex,
                Branch = currentBranch, Element = actionSet.Element, Duration = beatEarly + beatLate, Amount = beatEarly
            });
        }

        // A string press (Light, or a switch strike) the buffer accepted. Judged against the running string move's beat,
        // or taken as the pause press after an eligible move has ended.
        void OnStringPress(bool switchStrike)
        {
            if (!RhythmOn) return;
            double pressClock = clock + RhythmRules.BeatInputOffset;
            if (IsStringMoveRunning && beatActive)
            {
                if (pressClock <= attackBeganClock) return;   // can't be a follow-up of a move that hadn't started
                JudgeFollowUp(pressClock, switchStrike);
                return;
            }
            if (IsPauseBandLive)
            {
                followUpGrade = BeatGrade.Pause;
                EmitBeatJudged(BeatGrade.Pause, (float)(pressClock - pauseBandUntil), actionSet.Element);
            }
        }

        bool IsPauseBandLive => pauseBandOpen && clock <= pauseBandUntil + 1e-6
            && (state == PlayerState.Locomotion || state == PlayerState.Sprinting);

        void JudgeFollowUp(double pressClock, bool switchStrike)
        {
            followUpCount++;
            double open = beatClock - beatEarly;
            if (pressClock < open) earlyPressSeen = true;
            float rate = Math.Max(attackPlaybackRate, Epsilon);
            double runnableAt = attackStartClock + currentMove.ChainCancelAt / rate;
            if (followUpCount > 1)
            {
                // A second press before the next move started: an on-beat press was a lucky mash after all.
                if (followUpGrade == BeatGrade.OnBeat)
                {
                    followUpGrade = BeatGrade.Mashed;
                    BreakStreak();
                    EmitBeatJudged(BeatGrade.Mashed, (float)(pressClock - beatClock), actionSet.Element);
                }
                if (buffer.HasCommand && !buffer.Locked && followUpGrade != BeatGrade.None && followUpGrade != BeatGrade.Pause)
                {
                    buffer.Lock(runnableAt);           // a switch strike replacing a queued press stays queued
                }
                return;
            }

            float late = beatLate + (switchStrike ? SwitchRules.SwitchStrikeBeatLateBonus : 0f);
            double comboWindowEnd = attackStartClock + currentMove.ComboWindowEnd / rate;
            BeatGrade grade;
            if (pressClock < open) grade = BeatGrade.Early;
            else if (pressClock <= beatClock + late + 1e-6) grade = BeatGrade.OnBeat;
            else if (pressClock <= comboWindowEnd + 1e-6) grade = BeatGrade.Late;
            else grade = IsPauseEligible ? BeatGrade.Pause : BeatGrade.None;
            followUpGrade = grade;
            if (grade == BeatGrade.None) return;      // after the combo window: the old restart rules
            if (grade == BeatGrade.OnBeat) onBeatStreak++;
            else if (grade != BeatGrade.Pause) BreakStreak();
            EmitBeatJudged(grade, (float)(pressClock - beatClock), actionSet.Element);
            if (grade != BeatGrade.Pause)
            {
                buffer.Lock(runnableAt);
                // The next hit's ring (J3-01) has been showing since this move started if the press came while it was
                // up (early or on the beat); a late press starts it now.
                pendingCueFrom = grade == BeatGrade.Late ? clock : attackBeganClock;
            }
        }

        void BreakStreak()
        {
            onBeatStreak = 0;
            stringPerfect = false;
        }

        void EmitBeatJudged(BeatGrade grade, float offset, ElementId element)
        {
            lastGrade = grade;
            Emit(new PlayerEvent { Type = PlayerEventType.BeatJudged, Grade = grade, Amount = offset, Count = onBeatStreak, Element = element });
        }

        // The pause branch opens after main-chain hit PauseAfterIndex, if the player hasn't pressed during it.
        bool IsPauseEligible
        {
            get
            {
                if (!RhythmOn || attackKind != PlayerAttackKind.Light || chainBranch != ComboBranch.Main || currentBranch != ComboBranch.Main) return false;
                if (followUpCount > 1 || earlyPressSeen || LengthOf(actionSet.PauseChain) == 0) return false;
                return chainIndex == RhythmOf(actionSet).PauseAfterIndex && !moveIsChainFinisher;
            }
        }

        // An eligible move ran its course without a press: the pause band stays open a little past its end.
        void OpenPauseBandIfEligible(double moveStartClock, MoveData move, float rate, bool eligible)
        {
            if (!eligible) return;
            pauseBandOpen = true;
            pauseBandUntil = moveStartClock + (move.TotalDuration + Math.Max(0f, RhythmRules.PauseGrace)) / Math.Max(rate, Epsilon);
            stringMemoryUntil = Math.Max(stringMemoryUntil, pauseBandUntil);   // a dodge in the band keeps the string
        }

        // The playback rate a grade gives the move it starts (1 = as authored).
        float PlaybackRateFor(BeatGrade grade, ElementMoveSet set)
        {
            if (!RhythmOn) return 1f;
            RhythmTuning rules = RhythmRules;
            float rate;
            switch (grade)
            {
                case BeatGrade.OnBeat:
                case BeatGrade.Auto:
                    ElementRhythm element = RhythmOf(set);
                    rate = element.OnBeatPlaybackRate > 0f ? element.OnBeatPlaybackRate : rules.OnBeatPlaybackRate;
                    break;
                case BeatGrade.Early:
                case BeatGrade.Mashed:
                    rate = rules.OffBeatPlaybackRate;
                    break;
                default:
                    rate = 1f;
                    break;
            }
            float min = Math.Max(Epsilon, rules.MinPlaybackRate);
            return Angles.Clamp(rate, min, Math.Max(min, rules.MaxPlaybackRate));
        }

        static bool IsOnBeat(BeatGrade grade)
        {
            return grade == BeatGrade.OnBeat || grade == BeatGrade.Auto;
        }

        RhythmView BuildRhythmView()
        {
            bool pendingSwitch = buffer.Command == PlayerCommand.SwitchStrike;
            bool current = beatActive && IsStringMoveRunning && followUpCount == 0;
            double nextBeat = 0.0;
            bool next = !current && TryPredictQueuedBeat(out nextBeat);
            double cueBeat = next ? nextBeat : beatClock;
            double cueFrom = next ? pendingCueFrom : cueFromClock;
            // While the running beat is still to come (or inside its on-beat window), the hit after it is shown too,
            // assuming you keep the beat: its ring appears as this move starts.
            double afterBeat = 0.0;
            bool after = current && clock <= beatClock + beatLate + 1e-6 && TryPredictBeatAfter(moveSet, BeatGrade.OnBeat, out afterBeat);
            return new RhythmView
            {
                Cue = current || next,
                CueTimeToBeat = current || next ? (float)(cueBeat - clock) : 0f,
                CueLead = current || next ? (float)Math.Max(0.0, cueBeat - cueFrom) : 0f,
                CueIsNextHit = next,
                NextCue = after,
                NextCueTimeToBeat = after ? (float)(afterBeat - clock) : 0f,
                NextCueLead = after ? (float)Math.Max(0.0, afterBeat - attackBeganClock) : 0f,
                Active = beatActive && IsStringMoveRunning && followUpCount == 0,
                TimeToBeat = beatActive && IsStringMoveRunning ? (float)(beatClock - clock) : 0f,
                EarlyWindow = beatEarly,
                LateWindow = beatLate + (pendingSwitch ? SwitchRules.SwitchStrikeBeatLateBonus : 0f),
                Streak = onBeatStreak,
                LastGrade = lastGrade,
                PlaybackRate = state == PlayerState.Attacking ? attackPlaybackRate : 1f,
                PauseReady = IsPauseReady
            };
        }

        // J3-01: once a follow-up press is queued (locked) in the running string move, the next hit's beat is known: the
        // move starts at the running one's cancel point (or now, if that has passed) and strikes ActiveStart later at the
        // playback rate the press earned. Mirrors ResolveNextStringMove / TryAirAttack's queued branch. A stretched
        // arriving lunge can still push the real beat a little later; the ring then follows the real beat.
        bool TryPredictQueuedBeat(out double nextBeat)
        {
            nextBeat = 0.0;
            if (!RhythmOn || !beatActive || !IsStringMoveRunning || followUpCount == 0 || !buffer.Locked) return false;
            if (followUpGrade == BeatGrade.None || followUpGrade == BeatGrade.Pause || pendingCueFrom == double.NegativeInfinity) return false;
            PlayerCommand command = buffer.Command;
            bool switching = command == PlayerCommand.SwitchStrike && IsSwitchUsable(pendingSwitchElement);
            if (command != PlayerCommand.Light && !switching) return false;
            return TryPredictBeatAfter(switching ? loadout.Get(pendingSwitchElement) : moveSet, followUpGrade, out nextBeat);
        }

        // The beat of the running string move's next hit, from 'set', started by a press of this grade.
        bool TryPredictBeatAfter(ElementMoveSet set, BeatGrade grade, out double nextBeat)
        {
            nextBeat = 0.0;
            if (set == null || currentMove == null || !beatActive || !IsStringMoveRunning) return false;
            NextSlot(chainBranch, chainIndex, moveIsChainFinisher, out ComboBranch branch, out int index);
            MoveData[] moves = ChainOf(set, branch);
            int length = LengthOf(moves);
            if (index < 0 || length == 0) return false;
            MoveData move = moves[Math.Min(index, length - 1)];
            if (move == null) return false;
            float rate = Math.Max(attackPlaybackRate, Epsilon);
            double startAt = Math.Max(clock, attackStartClock + currentMove.ChainCancelAt / rate);
            nextBeat = startAt + move.ActiveStart / Math.Max(PlaybackRateFor(grade, set), Epsilon);
            return true;
        }

        // An X pressed now would take the pause branch: the eligible move is past its combo window with no press yet, or it
        // has ended and its pause band is still open.
        bool IsPauseReady
        {
            get
            {
                if (!RhythmOn) return false;
                if (IsPauseBandLive) return true;
                if (!IsStringMoveRunning || !beatActive || followUpCount != 0 || !IsPauseEligible) return false;
                float rate = Math.Max(attackPlaybackRate, Epsilon);
                return clock + RhythmRules.BeatInputOffset > attackStartClock + currentMove.ComboWindowEnd / rate + 1e-6;
            }
        }
    }
}
