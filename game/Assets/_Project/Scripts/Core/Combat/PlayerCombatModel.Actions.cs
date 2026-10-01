using System;
using System.Numerics;

namespace VaatusRevenge.Core
{
    // Attacks, charge, plunge, skill, heal and stagger: starting them, running their frame data and
    // ending them cleanly.
    //
    // WHEN CAN A BUFFERED PRESS RUN? ("the earliest legal point")
    //   Light / Heavy / Skill / ZipStrike: when free (Locomotion, Sprinting, Guarding); during an attack from its
    //     ChainCancelAt; during a dodge from its kind's attack cancel point (Dodge.SlipInAttackCancelAt for a slip-in,
    //     else Dodge.EvadeAttackCancelAt); after a plunge landing from its ChainCancelAt.
    //     In the air, Light/Heavy become the plunge once you've been off the ground for Plunge.MinAirTime
    //     (earlier presses are dropped, not saved for later). Skill waits for the ground.
    //   Dodge: when free on the ground; during an attack from DodgeCancelAt; during a dodge from NextDodgeAt;
    //     while charging if Charge.CanDodgeCancelCharge; after a plunge landing from its DodgeCancelAt.
    //   Jump: like Dodge (a dodge allows it from its attack cancel point), and only with ground under you or within coyote time.
    //   Guard (the press): like Dodge, but not out of a charge or in the air. Held guard raises it when free; a
    //     buffered press only raises it if guard is still held or its deflect window (timed from the real press)
    //     hasn't run out.
    //   Heal: only when free on the ground (drinking is a committed action, never a cancel).
    //   Nothing runs while staggered, healing, mid-plunge, or dead; presses there still wait in the buffer.
    // LIGHT CHAIN: a light press arriving (or still buffered) while the current light move is inside its combo
    //   window is queued as the next move and fires at the cancel point (or is dropped once it's older than
    //   QueuedPressMaxAge). With rhythm on, every follow-up press is judged against the move's beat and queued at once
    //   (see .Rhythm). A press after the window restarts the chain from the first move (or takes the pause branch).
    //   After the last move the chain loops. Which move a press starts (ResolveNextStringMove), first match wins:
    //   1. pressed during a dodge (or DodgeStrikeGrace after it) with a target near: the dodge strike, in the string's
    //      next slot (a finisher slot plays the finisher: a dodge never skips your finisher)
    //   2. queued during the running string move: the next slot of its chain
    //   3. the pause press: PauseChain[0]
    //   4. string memory live (after a dodge, zip, ability...): the remembered slot, in the ACTIVE element's chain
    //   5. otherwise the first move
    //   A switch strike (RB + an element's button mid-string) resolves the same way in the new element, which it
    //   switches to as the move starts; the slot carries over (a shorter chain plays its finisher).
    // FREE-FLOW LUNGE: a light attack aimed at a target (lock-on, else soft lock) that's out of reach lunges further
    //   than the move's own LungeDistance, by up to PlayerTuning.GapCloseDistance, stopping short of the target.
    // ZIP STRIKE: needs PlayerWorldState.HasZipTarget; without one the press is dropped and costs nothing. The dash
    //   covers the whole gap to the target during the move's startup and active frames. Not in the air.
    // LAUNCHER: keep holding the attack press that started a ground-string move (any hit of the main chain, Spider-Man's
    //   hold-square works at any point in a combo) for AerialSettings.LauncherHoldTime and that move turns into the launcher:
    //   the hit throws the target up and SelfLift carries you after it when the strike lands.
    // AIR STRING: attack while in the air (jumping, launched, after a zip) walks the AirChain, which does not loop and is
    //   capped at AerialSettings.AirAttacksPerJump strikes before touching down. Air strikes lift you (SelfLift) and
    //   gravity is scaled by AirAttackGravityScale while one runs, so you hang as you strike. Heavy in the air = plunge.
    // AIR DASH: dodge in the air, up to AirDashesPerJump times before landing: a flat dash with no gravity.
    // ABILITIES: AbilityNorth / AbilityEast run like the skill (grounded, from the usual cancel points).
    // MULTI-HIT MOVES (MoveData.HitCount > 1): sub-hit k goes live at ActiveStart + k x HitInterval and stays live until
    //   the next one; each has its own AttackId (one Active start/end pair each) and shares the first one's MoveInstanceId.
    // ORBIT (MoveData.OrbitDegrees): the lunge curves round the target, ending that many degrees round it (Air).
    // SPRINT ATTACK: light while sprinting for SprintAttackMinSprintTime, or within SprintAttackGrace after such
    //   a sprint ends while still moving at full running speed (strafe speed when locked on).
    public sealed partial class PlayerCombatModel
    {
        void TryRunBufferedCommand(in PlayerWorldState world)
        {
            if (state == PlayerState.Dead)
            {
                buffer.Clear();
                return;
            }
            TryLauncherHold(world);
            switch (buffer.Command)
            {
                case PlayerCommand.Light:
                case PlayerCommand.SwitchStrike: TryLight(world); break;
                case PlayerCommand.AbilityNorth: TryAbility(moveSet.AbilityNorth, world); break;
                case PlayerCommand.AbilityEast: TryAbility(moveSet.AbilityEast, world); break;
                case PlayerCommand.Heavy: TryHeavy(world); break;
                case PlayerCommand.Skill: TrySkill(world); break;
                case PlayerCommand.Dodge: TryDodge(world); break;
                case PlayerCommand.Jump: TryJump(); break;
                case PlayerCommand.Heal: TryHeal(); break;
                case PlayerCommand.Guard: TryGuardPress(); break;
                case PlayerCommand.ZipStrike: TryZipStrike(world); break;
            }
        }

        bool IsFree => state == PlayerState.Locomotion || state == PlayerState.Sprinting || state == PlayerState.Guarding;
        bool OnGroundish => grounded || airTime <= tuning.CoyoteTime;
        float PlungeTimeSinceLanding => plungeLanded ? action.Time - landingTime : -1f;

        bool CanStartAttack()
        {
            switch (state)
            {
                case PlayerState.Locomotion:
                case PlayerState.Sprinting:
                case PlayerState.Guarding:
                    return true;
                case PlayerState.Attacking:
                    return action.Time >= currentMove.ChainCancelAt;
                case PlayerState.Dodging:
                    return action.Time >= DodgeAttackCancelAt;
                case PlayerState.Plunging:
                    return plungeLanded && PlungeTimeSinceLanding >= currentMove.ChainCancelAt;
                default:
                    return false;
            }
        }

        // A slip-in is already close: it can strike sooner than the other dodge kinds.
        float DodgeAttackCancelAt => dodgeKind == DodgeKind.SlipIn ? Dodge.SlipInAttackCancelAt : Dodge.EvadeAttackCancelAt;

        // Dodge, jump and guard can cut other actions short from these points.
        bool CanDefensiveCancel(bool isDodge)
        {
            switch (state)
            {
                case PlayerState.Locomotion:
                case PlayerState.Sprinting:
                case PlayerState.Guarding:
                    return OnGroundish;
                case PlayerState.Attacking:
                    return action.Time >= currentMove.DodgeCancelAt;
                case PlayerState.Dodging:
                    return action.Time >= (isDodge ? Dodge.NextDodgeAt : DodgeAttackCancelAt);
                case PlayerState.Charging:
                    return isDodge && Charge.CanDodgeCancelCharge;
                case PlayerState.Plunging:
                    return plungeLanded && PlungeTimeSinceLanding >= currentMove.DodgeCancelAt;
                default:
                    return false;
            }
        }

        // In the air for the purpose of choosing moves: airborne, or attacking / dashing with no ground under us.
        bool Aloft => state == PlayerState.Airborne
            || (!grounded && (state == PlayerState.Attacking || state == PlayerState.Dodging || state == PlayerState.Plunging));

        void TryLight(in PlayerWorldState world)
        {
            bool switching = buffer.Command == PlayerCommand.SwitchStrike && IsSwitchUsable(pendingSwitchElement);
            if (Aloft)
            {
                TryAirAttack(world, switching);
                return;
            }
            if (!CanStartAttack()) return;

            bool stringRunning = state == PlayerState.Attacking && (attackKind == PlayerAttackKind.Light || attackKind == PlayerAttackKind.DodgeStrike);
            // Still inside (or before) the combo window and not queued: wait, the window decides chain vs restart.
            if (stringRunning && !buffer.Locked && action.Time <= currentMove.ComboWindowEnd) return;
            if (!stamina.CanAct) return;

            if (!switching && !stringRunning && moveSet.SprintAttack != null && WantsSprintAttack(world))
            {
                buffer.Clear();
                StartAttack(moveSet.SprintAttack, PlayerAttackKind.Sprint, -1, ChargeTier.None, ConsumeCounterWindow(), true, world);
                return;
            }
            ElementMoveSet set = switching ? loadout.Get(pendingSwitchElement) : moveSet;
            if (!ResolveNextStringMove(set, world, stringRunning, out MoveData move, out PlayerAttackKind kind, out int index,
                    out ComboBranch chain, out ComboBranch branch, out BeatGrade grade))
            {
                buffer.Clear();
                return;
            }
            buffer.Clear();
            StartStringMove(move, kind, index, chain, branch, grade, switching ? pendingSwitchElement : ElementId.None, world);
        }

        // Which string move a ground press starts (see the top of the file). False = nothing to start (no chain).
        bool ResolveNextStringMove(ElementMoveSet set, in PlayerWorldState world, bool stringRunning, out MoveData move,
            out PlayerAttackKind kind, out int index, out ComboBranch chain, out ComboBranch branch, out BeatGrade grade)
        {
            kind = PlayerAttackKind.Light;
            grade = BeatGrade.None;
            ElementRhythm rhythm = RhythmOf(set);

            // 1. The dodge strike: takes the string's next slot (the first one when there's no string).
            if (WantsDodgeStrike(world))
            {
                chain = IsStringMemoryLive && stringBranch != ComboBranch.Air ? stringBranch : ComboBranch.Main;
                int slot = IsStringMemoryLive && stringBranch != ComboBranch.Air ? stringNext : 0;
                MoveData[] slots = ChainOf(set, chain);
                index = Math.Min(slot, Math.Max(0, LengthOf(slots) - 1));
                grade = IsCounterWindowOpen || rhythm.DodgeKeepsBeat ? BeatGrade.Auto : BeatGrade.None;
                if (LengthOf(slots) > 0 && index == slots.Length - 1)
                {
                    move = slots[index];               // a dodge never skips your finisher
                    branch = chain;
                    return move != null;
                }
                if (set.DodgeStrike != null)
                {
                    move = set.DodgeStrike;
                    kind = PlayerAttackKind.DodgeStrike;
                    branch = ComboBranch.DodgeStrike;
                    return true;
                }
            }

            if (stringRunning && buffer.Locked)
            {
                // 2. Queued during the running string move: its chain's next slot.
                NextSlot(chainBranch, chainIndex, moveIsChainFinisher, out chain, out index);
                grade = followUpGrade;
            }
            else if (followUpGrade == BeatGrade.Pause && (stringRunning || IsPauseBandLive) && LengthOf(set.PauseChain) > 0)
            {
                // 3. The pause press.
                chain = ComboBranch.Pause;
                index = 0;
                grade = BeatGrade.Pause;
            }
            else if (!stringRunning && IsStringMemoryLive && stringBranch != ComboBranch.Air)
            {
                // 4. String memory: carry on where the string was. A dodge in between breaks the beat (Air keeps it).
                chain = stringBranch;
                index = stringNext;
                if (stringKeptByDodge && rhythm.DodgeKeepsBeat) streakKeptByDodge = true;
                else BreakStreak();
            }
            else
            {
                // 5. A fresh string.
                chain = ComboBranch.Main;
                index = 0;
            }
            MoveData[] moves = ChainOf(set, chain);
            if (LengthOf(moves) == 0 && chain == ComboBranch.Pause)
            {
                chain = ComboBranch.Main;              // no pause chain: just the first hit
                index = 0;
                moves = ChainOf(set, chain);
            }
            int length = LengthOf(moves);
            branch = chain;
            if (length == 0)
            {
                move = null;
                return false;
            }
            index = Math.Min(Math.Max(0, index), length - 1);   // a shorter chain: its finisher
            move = moves[index];
            return move != null;
        }

        // A press during a ground dodge, or within DodgeStrikeGrace after one, with a target near enough to hit.
        bool WantsDodgeStrike(in PlayerWorldState world)
        {
            bool duringDodge = state == PlayerState.Dodging && !dodgeInAir;
            bool justAfter = IsFree && !lastDodgeWasAir && buffer.PressTime >= dodgeStartClock - Epsilon
                && clock <= lastDodgeEndClock + Math.Max(0f, tuning.DodgeStrikeGrace) + 1e-6;
            if (!duringDodge && !justAfter) return false;
            if (!TryGetFocusTarget(world, out FocusTarget focus)) return false;
            float distance = Directions.Flatten(focus.Position - world.Position).Length() - Math.Max(0f, focus.Radius);
            return distance <= tuning.SoftLockRange;
        }

        // Starts a string move (light, pause, air chain or the dodge strike) with everything the press earned.
        void StartStringMove(MoveData move, PlayerAttackKind kind, int index, ComboBranch chain, ComboBranch branch, BeatGrade grade,
            ElementId switchTo, in PlayerWorldState world)
        {
            nextMove = new NextMoveInfo { ChainBranch = chain, Branch = branch, Grade = grade, SwitchTo = switchTo };
            StartAttack(move, kind, index, ChargeTier.None, ConsumeCounterWindow(), true, world);
        }

        bool WantsSprintAttack(in PlayerWorldState world)
        {
            float minSprint = moveSet.SprintAttackMinSprintTime;
            if (state == PlayerState.Sprinting) return sprintTime >= minSprint;
            if (!IsFree || lastSprintDuration < minSprint || clock - sprintEndedClock > moveSet.SprintAttackGrace + Epsilon) return false;
            // Still carrying the sprint: at least full running speed. (Deceleration settles exactly on run speed, so a
            // strict "faster than" would shrink the grace to the few frames it takes to slow down.)
            float fullSpeed = world.HasLockTarget ? tuning.LockOnStrafeSpeed : tuning.RunSpeed;
            return Directions.Flatten(moveVelocity).Length() >= fullSpeed - Epsilon;
        }

        void TryAirAttack(in PlayerWorldState world, bool switching)
        {
            if (state == PlayerState.Dodging) return;          // after the air dash (the press waits in the buffer)
            bool airRunning = state == PlayerState.Attacking && attackKind == PlayerAttackKind.Air;
            if (state != PlayerState.Airborne && !CanStartAttack()) return;
            // Still inside (or before) the combo window and not queued: wait, the window decides.
            if (airRunning && !buffer.Locked && action.Time <= currentMove.ComboWindowEnd) return;

            ElementMoveSet set = switching ? loadout.Get(pendingSwitchElement) : moveSet;
            MoveData[] chain = set.AirChain;
            int next = 0;
            BeatGrade grade = BeatGrade.None;
            if (airRunning && buffer.Locked)
            {
                NextSlot(ComboBranch.Air, chainIndex, moveIsChainFinisher, out _, out next);
                grade = followUpGrade;
            }
            else if (IsStringMemoryLive && stringBranch == ComboBranch.Air)
            {
                next = stringNext;                       // an air dash or a zip in between keeps the air string
            }
            // Another element's shorter air chain plays its finisher; after a finisher the air string is spent.
            if (next >= 0 && LengthOf(chain) > 0) next = Math.Min(next, chain.Length - 1);
            // The cap is shared by every element (switching can't stretch a juggle): the active element's limit.
            AerialSettings aerial = set.Aerial ?? FallbackAerial;
            if (LengthOf(chain) == 0 || next < 0 || chain[next] == null || airAttacksUsed >= Math.Max(0, aerial.AirAttacksPerJump))
            {
                buffer.Clear();                              // the air string is spent until you land
                return;
            }
            if (!stamina.CanAct) return;
            buffer.Clear();
            airAttacksUsed++;
            StartStringMove(chain[next], PlayerAttackKind.Air, next, ComboBranch.Air, ComboBranch.Air, grade,
                switching ? pendingSwitchElement : ElementId.None, world);
        }

        // Holding the press that started a ground-string move (any hit of the main chain) turns it into the launcher.
        void TryLauncherHold(in PlayerWorldState world)
        {
            if (state != PlayerState.Attacking || attackKind != PlayerAttackKind.Light || chainBranch != ComboBranch.Main || moveSet.Launcher == null) return;
            if (!lightHeld || !grounded) return;
            if (clock - lightPressClock < Aerial.LauncherHoldTime - Epsilon) return;
            // The held press must be the one that started this move (buffered presses count from a little earlier).
            if (lightPressClock < attackBeganClock - tuning.InputBufferWindow - Epsilon) return;
            if (!stamina.CanAct) return;
            if (buffer.Command == PlayerCommand.Light) buffer.Clear();
            StartAttack(moveSet.Launcher, PlayerAttackKind.Launcher, -1, ChargeTier.None, isCounter || ConsumeCounterWindow(), true, world);
            lightPressClock = double.NegativeInfinity;       // one launcher per hold
        }

        void TryAbility(MoveData move, in PlayerWorldState world)
        {
            if (Aloft || !CanStartAttack() || !stamina.CanAct) return;   // grounded only: waits in the buffer for the landing
            buffer.Clear();
            if (move != null) StartAttack(move, PlayerAttackKind.Ability, -1, ChargeTier.None, ConsumeCounterWindow(), true, world);
        }

        void TryHeavy(in PlayerWorldState world)
        {
            if (Aloft)
            {
                TryPlunge(world);
                return;
            }
            if (!CanStartAttack() || !stamina.CanAct) return;
            buffer.Clear();
            if (moveSet.Heavy != null) StartCharge(world);
        }

        void TrySkill(in PlayerWorldState world)
        {
            if (Aloft || !CanStartAttack() || !stamina.CanAct) return;   // grounded only: waits in the buffer for the landing
            buffer.Clear();
            if (moveSet.Skill != null) StartAttack(moveSet.Skill, PlayerAttackKind.Skill, -1, ChargeTier.None, ConsumeCounterWindow(), true, world);
        }

        void TryZipStrike(in PlayerWorldState world)
        {
            bool canStart = state == PlayerState.Airborne || CanStartAttack();   // a zip works from the air too
            if (!canStart || !stamina.CanAct) return;
            buffer.Clear();
            if (moveSet.ZipStrike == null || !world.HasZipTarget) return;
            StartAttack(moveSet.ZipStrike, PlayerAttackKind.ZipStrike, -1, ChargeTier.None, ConsumeCounterWindow(), true, world);
        }

        void TryPlunge(in PlayerWorldState world)
        {
            // Mid air strike or air dash: wait for its cancel point (the press stays buffered).
            if (state == PlayerState.Dodging) return;
            if (state == PlayerState.Attacking && !CanStartAttack()) return;
            // Too soon after leaving the ground: the press does nothing, and isn't saved to plunge later either.
            if (airTime < Plunge.MinAirTime)
            {
                buffer.Clear();
                return;
            }
            if (!stamina.CanAct) return;
            buffer.Clear();
            if (moveSet.PlungeAttack != null) StartPlunge(world);
        }

        void TryHeal()
        {
            if (!IsFree || !OnGroundish) return;
            buffer.Clear();
            if (healCharges <= 0)
            {
                Emit(PlayerEventType.HealFailed);
                return;
            }
            ExitAction(true);
            state = PlayerState.Healing;
            healApplied = false;
            action.Begin();
            Emit(PlayerEventType.HealStarted);
        }

        // After a perfect dodge, the first attack started inside the window is a counter (bonus damage).
        bool ConsumeCounterWindow()
        {
            if (clock > counterWindowUntil) return false;
            counterWindowUntil = double.NegativeInfinity;
            return true;
        }

        // ---------------------------------------------------------------- attacks

        // What a string press earned for the move it starts (set by StartStringMove, read once by StartAttack).
        struct NextMoveInfo
        {
            public ComboBranch ChainBranch;
            public ComboBranch Branch;
            public BeatGrade Grade;
            public ElementId SwitchTo;            // a switch strike: the element to switch to as the move starts
        }

        NextMoveInfo nextMove;

        void StartAttack(MoveData move, PlayerAttackKind kind, int chain, ChargeTier tier, bool counter, bool payStamina,
            in PlayerWorldState world)
        {
            NextMoveInfo info = nextMove;
            nextMove = default;
            ExitAction(true);
            if (info.SwitchTo != ElementId.None) ApplySwitchStrike(info.SwitchTo, info.Branch);
            state = PlayerState.Attacking;
            currentMove = move;
            attackKind = kind;
            chainIndex = chain;
            chargeTier = tier;
            isCounter = counter;
            activeOpen = false;
            bool stringMove = IsStringKind(kind);
            chainBranch = stringMove ? info.ChainBranch : ComboBranch.Other;
            currentBranch = stringMove ? info.Branch : kind == PlayerAttackKind.Launcher ? ComboBranch.Launcher : ComboBranch.Other;
            BeatGrade grade = stringMove ? (counter ? BeatGrade.Auto : info.Grade) : BeatGrade.None;
            moveGrade = grade;
            MoveData[] chainMoves = stringMove ? ChainOf(actionSet, chainBranch) : null;
            moveIsChainFinisher = stringMove && kind != PlayerAttackKind.DodgeStrike && chain >= 0 && chain == LengthOf(chainMoves) - 1;
            moveIsSwitchStrike = info.SwitchTo != ElementId.None;
            attackPlaybackRate = stringMove ? PlaybackRateFor(grade, actionSet) : 1f;
            attackStartClock = clock;
            attackBeganClock = clock;
            if (payStamina)
            {
                bool mashed = grade == BeatGrade.Early || grade == BeatGrade.Mashed;
                SpendStamina(move.StaminaCost + (mashed && RhythmOn ? Math.Max(0f, RhythmRules.MashStaminaSurcharge) : 0f));
            }
            // The string: a string move is the string now; an action that holds it keeps it waiting; anything else ends it.
            if (stringMove) ClearStringMemory();
            else if (HoldsString(state, kind)) HoldStringIfLive(false);
            else ClearStringMemory();
            ApplyBeatRewards(grade, stringMove);
            // Free-flow snap (Spider-Man): a string, air or launcher strike at a target turns you to face it at once, so an
            // enemy behind you gets hit instead of the swing going wide (report 04, W-02). Startup tracking does the rest.
            if ((stringMove || kind == PlayerAttackKind.Launcher) && TryGetWorldLungeTarget(world, out Vector3 snapTarget, out _))
            {
                facingYaw = YawTowards(world.Position, snapTarget);
            }
            lungeDistance = PlanLunge(move, kind, world);
            PlanOrbit(move, world);
            PlanArrival(move, kind, world);
            // Air strikes lift you as they start (you hang while striking); the launcher lifts you when its kick lands.
            // An air strike stalls you: your rise is replaced by its own small lift (or none, over a standing foe), so a
            // jump's speed can't carry you sky-high under the lighter air-strike gravity.
            if (kind == PlayerAttackKind.Air && !grounded) verticalVelocity = Math.Min(verticalVelocity, 0f);
            if (move.SelfLift > 0f && kind != PlayerAttackKind.Launcher && !AboveGroundedTarget(world)) ApplySelfLift(move.SelfLift);
            currentAttackId = CombatIds.Next();
            moveInstanceId = currentAttackId;
            subHitIndex = 0;
            RememberAttack(currentAttackId, tier == ChargeTier.FaJin ? Charge.FaJinMomentumGain : move.MomentumGain, true);
            moveVelocity = Vector3.Zero;
            action.Begin();
            EmitMoveEvent(PlayerEventType.AttackStarted);
            BeginBeat(actionSet);
            UpdateAttack(world);   // moments at time 0 (a move with no startup is active at once)
        }

        // What the press that started a string move earned: the on-beat damage, the element's perk, the streak, and the
        // perfect-string finisher. The per-move scales go into this move's attack record (BuildDamage reads them).
        void ApplyBeatRewards(BeatGrade grade, bool stringMove)
        {
            moveDamageScale = 1f;
            movePoiseScale = 1f;
            moveLaunchSpeed = 0f;
            moveOnBeatArmor = false;
            if (!stringMove) return;
            ElementSwitchTuning switchRules = SwitchRules;
            if (moveIsSwitchStrike)
            {
                moveDamageScale *= Math.Max(0f, switchRules.SwitchStrikeDamageMultiplier);
                movePoiseScale *= Math.Max(0f, switchRules.SwitchStrikePoiseMultiplier);
            }
            bool freshString = chainBranch == ComboBranch.Main && chainIndex == 0 && attackKind == PlayerAttackKind.Light;
            // A move nobody judged (a fresh string, a continue after a dodge) starts the streak over, unless the element's
            // step is its beat (Air).
            if (grade == BeatGrade.None && !streakKeptByDodge) onBeatStreak = 0;
            streakKeptByDodge = false;
            if (freshString)
            {
                stringPerfect = true;
                stringFollowUps = 0;
            }
            else if (grade != BeatGrade.Pause)
            {
                stringFollowUps++;
                if (!IsOnBeat(grade)) stringPerfect = false;
            }
            if (!RhythmOn) return;
            if (IsOnBeat(grade))
            {
                RhythmTuning rules = RhythmRules;
                ElementRhythm element = RhythmOf(actionSet);
                if (grade == BeatGrade.Auto) onBeatStreak++;
                moveDamageScale *= Math.Max(0f, rules.OnBeatDamageMultiplier + element.OnBeatDamageBonus);
                if (element.OnBeatMomentumBonus > 0f) MeterOf(actionSet.Element).Gain(element.OnBeatMomentumBonus, MomentumRulesOf(actionSet.Element));
                if (element.OnBeatStaminaRefund > 0f) stamina.Set(stamina.Current + element.OnBeatStaminaRefund, MaxStamina);
                moveOnBeatArmor = element.OnBeatHyperArmor;
            }
            bool finisher = moveIsChainFinisher && (chainBranch == ComboBranch.Main || chainBranch == ComboBranch.Pause);
            if (finisher && stringPerfect && stringFollowUps > 0)
            {
                RhythmTuning rules = RhythmRules;
                moveDamageScale *= Math.Max(0f, rules.PerfectStringDamageMultiplier);
                moveLaunchSpeed = Math.Max(moveLaunchSpeed, rules.PerfectStringLaunchSpeed);
                Emit(new PlayerEvent { Type = PlayerEventType.PerfectString, Move = currentMove, Element = actionSet.Element });
            }
        }

        // How far this attack travels forward. LimitApproach still stops every step short of the target's body.
        float PlanLunge(MoveData move, PlayerAttackKind kind, in PlayerWorldState world)
        {
            float own = Math.Max(0f, move.LungeDistance);
            lungeHoming = false;
            if (kind == PlayerAttackKind.ZipStrike)
            {
                lungeHoming = world.HasZipTarget;
                if (world.HasZipTarget)
                {
                    lungeTargetFeet = world.ZipTargetPosition;
                    lungeTargetRadius = world.ZipTargetRadius;
                }
                return world.HasZipTarget ? GapTo(world.ZipTargetPosition, world.ZipTargetRadius, world) : 0f;
            }
            // Free-flow gap close: string moves only (the dodge strike too: evade out, then X dashes back in).
            bool closes = kind == PlayerAttackKind.Light || kind == PlayerAttackKind.Air || kind == PlayerAttackKind.DodgeStrike;
            if (!closes || !(tuning.GapCloseDistance > 0f)) return own;
            float gap;
            if (world.HasLockTarget)
            {
                gap = GapTo(world.LockTargetPosition, world.LockTargetRadius, world);
                lungeTargetFeet = world.LockTargetPosition;
                lungeTargetRadius = world.LockTargetRadius;
            }
            else if (world.HasSoftTarget)
            {
                gap = GapTo(world.SoftTargetPosition, world.SoftTargetRadius, world);
                lungeTargetFeet = world.SoftTargetPosition;
                lungeTargetRadius = world.SoftTargetRadius;
            }
            else return own;
            // Only a stretched lunge homes in; a normal step keeps following the facing as it always has.
            lungeHoming = gap > own;
            return Math.Max(own, Math.Min(own + tuning.GapCloseDistance, gap));
        }

        // ARRIVING LUNGES (Build 05 verify J-06): a lunge stretched to close a gap (free-flow, the dodge strike dashing back
        // in) and an own lunge longer than ArriveLungeMinDistance (sprint attacks) end as the strike goes active, like the
        // zip strike, so the hit lands with the limb on the target instead of from metres away followed by a glide in. To
        // keep the dash believable it never goes faster than ArriveLungeMaxSpeed: the startup is stretched (played slower)
        // by up to ArriveLungeMaxExtraStartup to make room, and attackStartClock moves by that much so the beat, the combo
        // window and every later mark keep their place relative to the strike. A circling (orbit) lunge keeps circling through
        // its active frames, but closes in on the target by the strike.
        void PlanArrival(MoveData move, PlayerAttackKind kind, in PlayerWorldState world)
        {
            lungeArrives = false;
            approachScale = 1f;
            if (kind == PlayerAttackKind.ZipStrike || !(lungeDistance > 0f)) return;
            float minOwn = tuning.ArriveLungeMinDistance;
            bool longOwn = minOwn > 0f && move.LungeDistance > minOwn;
            if (!lungeHoming && !longOwn) return;
            lungeArrives = true;
            float distance = lungeDistance;
            if (orbiting) distance = Math.Max(0f, orbitStartRadius - orbitEndRadius);
            else if (TryGetLungeTarget(world, out Vector3 target, out float targetRadius)) distance = Math.Min(distance, GapTo(target, targetRadius, world));
            float rate = Math.Max(attackPlaybackRate, Epsilon);
            float startup = move.ActiveStart / rate;
            float maxSpeed = tuning.ArriveLungeMaxSpeed;
            if (!(maxSpeed > 0f) || !(startup > 0f)) return;
            float extra = Angles.Clamp(distance / maxSpeed - startup, 0f, Math.Max(0f, tuning.ArriveLungeMaxExtraStartup));
            if (!(extra > 1e-4f)) return;
            approachScale = startup / (startup + extra);
            attackStartClock += extra;
        }

        // How far the running attack's clock moves this frame: dt x its playback rate, slower before the strike while a
        // stretched startup runs (PlanArrival).
        float AttackAdvance(float dt)
        {
            float rate = attackPlaybackRate;
            if (!(approachScale < 1f) || currentMove == null || action.Time >= currentMove.ActiveStart) return dt * rate;
            float slow = rate * approachScale;
            float left = currentMove.ActiveStart - action.Time;
            float toStrike = left / Math.Max(slow, Epsilon);
            return dt <= toStrike ? dt * slow : left + (dt - toStrike) * rate;
        }

        // An air strike only lifts you when there's something up here to hit: against a foe standing on the ground (you
        // jumped at it), rising above its head would be silly (report 03, V-10). No target at all: it lifts (air practice).
        bool AboveGroundedTarget(in PlayerWorldState world)
        {
            Vector3 target;
            if (world.HasLockTarget) target = world.LockTargetPosition;
            else if (world.HasSoftTarget) target = world.SoftTargetPosition;
            else return false;
            return world.Position.Y > target.Y + Aerial.AirLiftMaxHeightAboveTarget;
        }

        // Flat distance we could travel toward a target before reaching LungeStopGap from its body.
        float GapTo(Vector3 target, float targetRadius, in PlayerWorldState world)
        {
            float distance = Directions.Flatten(target - world.Position).Length();
            return Math.Max(0f, distance - Math.Max(0f, world.SelfRadius) - Math.Max(0f, targetRadius) - tuning.LungeStopGap);
        }

        void UpdateAttack(in PlayerWorldState world)
        {
            MoveData move = currentMove;
            int hits = Math.Max(1, move.HitCount);
            // Each sub-hit opens as the one before closes; the last closes when the active frames end.
            for (int k = subHitIndex; k < hits; k++)
            {
                if (!action.Crossed(SubHitStart(move, k))) break;
                if (activeOpen) CloseActive();
                if (k > 0)
                {
                    currentAttackId = CombatIds.Next();
                    RememberAttack(currentAttackId, 0f, false);   // Momentum is earned once per move, by its first sub-hit
                }
                subHitIndex = k + 1;
                if (move.LaunchesProjectile) LaunchProjectile(world);
                else OpenActive(world);
            }
            if (activeOpen && subHitIndex >= hits && action.Crossed(move.ActiveEnd)) CloseActive();
            action.MarkChecked();
            AnnounceBeatWindow();

            // A light press inside the combo window is accepted as the chain follow-up (see top of file). With rhythm on,
            // the press was already judged and queued when it was made.
            bool stringMove = IsStringKind(attackKind);
            if (stringMove && !RhythmOn && buffer.Command == PlayerCommand.Light && !buffer.Locked
                && action.Time >= move.ComboWindowStart && action.Time <= move.ComboWindowEnd)
            {
                buffer.Lock();
            }

            if (action.Time >= move.TotalDuration)
            {
                // The move ran its course. RememberString (in ExitAction) keeps the string for the move's grace; a pause-
                // eligible move with no press opens the pause band.
                // A press judged Pause on this very frame counts too (it was made while the move still ran), or it would fall
                // through to main hit 3 (verify S-07).
                bool pauseEligible = stringMove && (followUpCount == 0 || followUpGrade == BeatGrade.Pause) && IsPauseEligible;
                double startClock = attackStartClock;
                float rate = attackPlaybackRate;
                FinishAction();
                OpenPauseBandIfEligible(startClock, move, rate, pauseEligible);
            }
        }

        static float SubHitStart(MoveData move, int k)
        {
            return move.ActiveStart + k * Math.Max(0f, move.HitInterval);
        }

        void OpenActive(in PlayerWorldState world)
        {
            activeOpen = true;
            if (attackKind == PlayerAttackKind.Launcher && currentMove.SelfLift > 0f && subHitIndex <= 1) ApplySelfLift(currentMove.SelfLift);
            PlayerEvent e = MoveEvent(PlayerEventType.AttackActiveStart);
            e.Origin = GetStrikeOrigin(world.Position);
            e.Direction = Forward;
            Emit(e);
        }

        void CloseActive()
        {
            activeOpen = false;
            EmitMoveEvent(PlayerEventType.AttackActiveEnd);
        }

        // Projectiles fly along the committed facing, pitched up or down toward the target's chest.
        void LaunchProjectile(in PlayerWorldState world)
        {
            Vector3 origin = GetStrikeOrigin(world.Position);
            Vector3 direction = Forward;
            if (TryGetAimTarget(world, out Vector3 aimPoint))
            {
                float pitch = Directions.PitchOf(aimPoint - origin);
                direction = Directions.FromYawPitch(facingYaw, pitch);
            }
            PlayerEvent e = MoveEvent(PlayerEventType.ProjectileLaunched);
            e.Origin = origin;
            e.Direction = direction;
            Emit(e);
        }

        bool TryGetAimTarget(in PlayerWorldState world, out Vector3 aimPoint)
        {
            if (world.HasLockTarget)
            {
                aimPoint = world.LockTargetAimPoint != Vector3.Zero ? world.LockTargetAimPoint : world.LockTargetPosition;
                return true;
            }
            if (world.HasSoftTarget)
            {
                aimPoint = world.SoftTargetAimPoint != Vector3.Zero ? world.SoftTargetAimPoint : world.SoftTargetPosition;
                return true;
            }
            aimPoint = Vector3.Zero;
            return false;
        }

        // ---------------------------------------------------------------- charged heavy

        void StartCharge(in PlayerWorldState world)
        {
            ExitAction(true);
            state = PlayerState.Charging;
            HoldStringIfLive(false);
            currentMove = moveSet.Heavy;
            attackKind = PlayerAttackKind.Heavy;
            chainIndex = -1;
            chargeTier = ChargeTier.None;
            chargeTime = HeldHeavyCredit();
            sweetSpotAnnounced = false;
            readyCueAnnounced = false;
            isCounter = ConsumeCounterWindow();   // committing to the heavy inside the window makes it the counter
            currentAttackId = 0;
            SpendStamina(currentMove.StaminaCost);
            moveVelocity = Vector3.Zero;
            action.Begin();
            EmitMoveEvent(PlayerEventType.ChargeStarted);
            UpdateCharge(world);                  // a tap already released: quick heavy straight away
        }

        // A heavy pressed during another move starts charging only at that move's cancel point. If the button has
        // been held all along, that time counts, capped just before the ready cue so the cue still shows.
        float HeldHeavyCredit()
        {
            if (!heavyHeld) return 0f;
            ChargeSettings charge = Charge;
            float held = (float)(realClock - heavyPressRealClock);
            float cap = Math.Max(0f, charge.SweetSpotStart - charge.ReadyCueLead - Epsilon);
            return Angles.Clamp(held, 0f, cap);
        }

        // The charge clock is advanced (on real time) by AdvanceAction before this runs.
        void UpdateCharge(in PlayerWorldState world)
        {
            ChargeSettings charge = Charge;
            if (!readyCueAnnounced && chargeTime >= charge.SweetSpotStart - charge.ReadyCueLead)
            {
                readyCueAnnounced = true;
                EmitMoveEvent(PlayerEventType.ChargeReadyCue);
            }
            if (!sweetSpotAnnounced && chargeTime >= charge.SweetSpotStart)
            {
                sweetSpotAnnounced = true;
                EmitMoveEvent(PlayerEventType.ChargeSweetSpot);
            }
            action.MarkChecked();
            if (!heavyHeld || chargeTime >= charge.MaxChargeTime) ReleaseCharge(world);
        }

        void ReleaseCharge(in PlayerWorldState world)
        {
            ChargeTier tier = EvaluateChargeTier(chargeTime);
            releasingCharge = true;
            StartAttack(currentMove, PlayerAttackKind.Heavy, -1, tier, isCounter, false, world);
            releasingCharge = false;
        }

        ChargeTier EvaluateChargeTier(float time)
        {
            ChargeSettings charge = Charge;
            if (time < charge.QuickReleaseTime) return ChargeTier.Quick;
            if (time < charge.SweetSpotStart) return ChargeTier.Partial;
            if (time <= charge.SweetSpotEnd + Epsilon) return ChargeTier.FaJin;
            return ChargeTier.Charged;
        }

        // ---------------------------------------------------------------- plunge (jump attack)

        void StartPlunge(in PlayerWorldState world)
        {
            ExitAction(true);
            state = PlayerState.Plunging;
            ClearStringMemory();                  // a plunge ends the string (the landing starts a new one)
            currentMove = moveSet.PlungeAttack;
            attackKind = PlayerAttackKind.Plunge;
            chainIndex = -1;
            chargeTier = ChargeTier.None;
            isCounter = ConsumeCounterWindow();
            plungeFalling = false;
            plungeLanded = false;
            landingTime = 0f;
            SpendStamina(currentMove.StaminaCost);
            currentAttackId = CombatIds.Next();
            moveInstanceId = currentAttackId;
            RememberAttack(currentAttackId, currentMove.MomentumGain, true);
            moveVelocity = Vector3.Zero;
            action.Begin();
            EmitMoveEvent(PlayerEventType.AttackStarted);
            UpdatePlunge(world);
        }

        void UpdatePlunge(in PlayerWorldState world)
        {
            PlungeSettings plunge = Plunge;
            if (!plungeFalling && action.Time >= plunge.HangTime) plungeFalling = true;
            if (plungeFalling && !plungeLanded && grounded)
            {
                plungeLanded = true;
                landingTime = action.Time;
                    PlayerEvent impact = MoveEvent(PlayerEventType.PlungeImpact);
                impact.Origin = world.Position;
                impact.Radius = plunge.RingRadius;
                Emit(impact);
                Emit(new PlayerEvent { Type = PlayerEventType.Landed, Amount = plunge.FallSpeed });
            }
            action.MarkChecked();

            if (plungeLanded && PlungeTimeSinceLanding >= currentMove.Recovery)
            {
                FinishAction();
            }
            else if (!plungeLanded && plungeFalling && action.Time - plunge.HangTime > plunge.MaxFallTime)
            {
                // Safety net: never landed (a gap in the floor, a steep slope). Drop back to normal falling.
                FinishAction();
            }
        }

        // ---------------------------------------------------------------- heal, stagger, death

        void UpdateHeal()
        {
            float applyAt = Math.Min(tuning.HealApplyTime, tuning.HealDuration);
            if (!healApplied && action.Crossed(applyAt))
            {
                healApplied = true;
                if (healCharges > 0)
                {
                    healCharges--;
                    float before = health;
                    health = Math.Min(MaxHealth, health + Math.Max(0f, tuning.HealAmount));
                    Emit(new PlayerEvent { Type = PlayerEventType.HealApplied, Amount = health - before });
                }
            }
            action.MarkChecked();
            if (action.Time >= tuning.HealDuration) FinishAction();
        }

        void Stagger(float duration)
        {
            if (state == PlayerState.Dead) return;
            ExitAction(true);
            EndCombo(ComboEndReason.Staggered);
            ClearStringMemory();
            CancelPendingSwitch();
            state = PlayerState.Staggered;
            staggerDuration = Math.Max(0f, duration);
            moveVelocity = Vector3.Zero;
            buffer.Clear();
            action.Begin();
            Emit(new PlayerEvent { Type = PlayerEventType.Staggered, Duration = staggerDuration });
        }

        void UpdateStagger()
        {
            action.MarkChecked();
            if (action.Time >= staggerDuration) FinishAction();
        }

        void Die()
        {
            ExitAction(true);
            EndCombo(ComboEndReason.Died);
            ClearStringMemory();
            CancelPendingSwitch();
            state = PlayerState.Dead;
            health = 0f;
            moveVelocity = Vector3.Zero;
            knockback.Stop();
            buffer.Clear();
            Emit(PlayerEventType.Died);
        }

        // ---------------------------------------------------------------- running and ending actions

        void AdvanceAction(float dt, in PlayerWorldState world)
        {
            actionStep = Vector3.Zero;
            if (!action.IsRunning) return;
            // A string move plays at the speed its press earned (rhythm); every time in its MoveData scales with it.
            action.Advance(state == PlayerState.Attacking ? AttackAdvance(dt) : dt);
            // Worked out before the action gets a chance to end this frame, so its last slice of movement
            // (the end of a dash or lunge) is never lost, whatever the frame rate.
            actionStep = ActionStep(world);
            switch (state)
            {
                case PlayerState.Attacking: UpdateAttack(world); break;
                case PlayerState.Charging:
                    chargeTime += frameRealDt;   // real time: a practised hold isn't thrown off by slow motion
                    UpdateCharge(world);
                    break;
                case PlayerState.Plunging: UpdatePlunge(world); break;
                case PlayerState.Dodging: UpdateDodge(world); break;
                case PlayerState.Healing: UpdateHeal(); break;
                case PlayerState.Staggered: UpdateStagger(); break;
            }
        }

        void FinishAction()
        {
            ExitAction(false);
            EnterFreeState();
        }

        void EnterFreeState()
        {
            if (state == PlayerState.Dead) return;
            state = OnGroundish ? PlayerState.Locomotion : PlayerState.Airborne;
        }

        // Leaves the current state tidily, whatever the reason: every "start" event gets its "end" event
        // (an open active window always reports AttackActiveEnd), so the Unity side can never be left with a
        // lingering hitbox, glow or i-frame look.
        void ExitAction(bool interrupted)
        {
            bool heldString = HoldsString(state, attackKind);
            pauseBandOpen = false;                    // anything happening ends the pause band (the string memory stays)
            switch (state)
            {
                case PlayerState.Attacking:
                    if (activeOpen) CloseActive();
                    EmitMoveEvent(PlayerEventType.AttackEnded);
                    buffer.Unlock();
                    if (IsStringKind(attackKind)) RememberString();
                    beatActive = false;
                    break;
                case PlayerState.Charging:
                    if (!releasingCharge) EmitMoveEvent(PlayerEventType.ChargeCancelled);
                    break;
                case PlayerState.Plunging:
                    EmitMoveEvent(PlayerEventType.AttackEnded);
                    break;
                case PlayerState.Dodging:
                    EndDodgeIFrames();
                    EndDodgeChain();
                    Emit(PlayerEventType.DodgeEnded);
                    break;
                case PlayerState.Healing:
                    if (!healApplied) Emit(PlayerEventType.HealInterrupted);
                    break;
                case PlayerState.Staggered:
                    Emit(PlayerEventType.StaggerEnded);
                    break;
                case PlayerState.Guarding:
                    Emit(PlayerEventType.GuardEnded);
                    break;
                case PlayerState.Sprinting:
                    Emit(PlayerEventType.SprintEnded);
                    sprintEndedClock = clock;         // for the sprint attack's grace period
                    lastSprintDuration = sprintTime;
                    sprintTime = 0f;
                    break;
            }
            if (heldString) ReleaseHeldString();
            action.Stop();
            activeOpen = false;
            attackPlaybackRate = 1f;
            if (!releasingCharge)
            {
                currentMove = null;
                attackKind = PlayerAttackKind.None;
                chargeTier = ChargeTier.None;
                isCounter = false;
            }
            chainIndex = -1;
            if (interrupted && state == PlayerState.Charging) chargeTime = 0f;
            actionSet = moveSet;   // the next action (or none) uses the active element
        }
    }
}
