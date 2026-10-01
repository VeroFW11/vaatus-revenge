using System;

namespace VaatusRevenge.Core
{
    // The combat tutorial: its steps in order and the few timings around them. Lives in a TutorialScriptAsset in Unity;
    // CreateDefault is the Build 05 tutorial (spec 5 D): eleven steps, step 8 in three parts (Water, Earth, Air), taught
    // on Xbox buttons with the colour-matched element layout (hold RB + B Fire, X Water, A Earth, Y Air; keys 1-4).
    [Serializable]
    public class TutorialScript
    {
        public TutorialStepData[] Steps = new TutorialStepData[0];
        public string DisplayTotal = "11";       // the "/ 11" after the step number (8b and 8c share step 8)
        public float SuccessPause = 1.0f;        // seconds the green flash shows before the next step
        public float QuitHoldTime = 1.0f;        // hold View this long to quit (a tap skips)

        // Bumped when the default steps change, so the sandbox builder can offer to update an old saved tutorial.
        // A field missing from an old asset keeps its initialiser, so it reads 0.
        public int DataVersion = 0;
        public const int CurrentDataVersion = 3;   // 2: Build 05 verify (pause finisher X X wait X X, beat taught as anticipation)
                                                   // 3: verify round 2 (shorter step 2 hint; hints wrap and stay under HintBudget)
        // Longest hint, in characters (a {button} counts as 3): the panel wraps hints, and this keeps any of them to two
        // or three short lines. TutorialTrackerTests checks every default hint against it.
        public const int HintBudget = 140;

        public int StepCount => Steps != null ? Steps.Length : 0;

        public TutorialStepData StepAt(int index)
        {
            return Steps != null && index >= 0 && index < Steps.Length ? Steps[index] : null;
        }

        public static TutorialScript CreateDefault()
        {
            return new TutorialScript
            {
                DataVersion = CurrentDataVersion,
                Steps = new[]
                {
                    new TutorialStepData
                    {
                        Id = "1", Title = "Move & look", Goal = TutorialGoal.Timed, Duration = 4f,
                        Prompt = "{LS} move, {RS} camera",
                        KeyboardPrompt = "W A S D move, mouse camera",
                        Hint = "Your sparring partner can't be hurt. Get a feel for the space round them."
                    },
                    new TutorialStepData
                    {
                        Id = "2", Title = "On the beat", Goal = TutorialGoal.OnBeatFinisher, MinOnBeat = 3,
                        Prompt = "{X} ×5, press as the ring touches the circle",
                        Hint = "Press as the gold ring touches the circle, not after the flash (that only confirms it). On the beat, the next hit is faster and harder."
                    },
                    new TutorialStepData
                    {
                        Id = "3", Title = "Pause finisher", Goal = TutorialGoal.PauseFinisher, Count = 2,
                        Prompt = "{X} {X} … wait … {X} {X}",
                        Hint = "Two hits, then wait until your hands are back in guard and the circle glows blue, then two more: a different finisher."
                    },
                    new TutorialStepData
                    {
                        Id = "4", Title = "Dodge keeps the combo", Goal = TutorialGoal.DodgeKeepsCombo, Count = 2, MinComboCount = 2,
                        PartnerAttacks = true,
                        Prompt = "Mid-combo, {B} when the mark flashes, keep pressing {X}",
                        Hint = "Your partner swings back now. Dodging never breaks your combo: the counter keeps climbing."
                    },
                    new TutorialStepData
                    {
                        Id = "5", Title = "Slip in", Goal = TutorialGoal.SlipInStrike, Count = 2, MinStartDistance = 4f,
                        FollowUpWindow = 0.6f,
                        Prompt = "From range, stick toward the target + {B}, then {X}",
                        KeyboardPrompt = "From range, W toward the target + {B}, then {X}",
                        Hint = "Back off about five steps first. Dodging toward an enemy carries you in close, ready to hit."
                    },
                    new TutorialStepData
                    {
                        Id = "6", Title = "Dodge strike", Goal = TutorialGoal.DodgeStrike, Count = 2,
                        Prompt = "{X} late in a dodge",
                        Hint = "Dodge with {B}, then press {X} as the dodge ends: you dash back in with a counter."
                    },
                    new TutorialStepData
                    {
                        Id = "7", Title = "Launch & juggle", Goal = TutorialGoal.LaunchAndJuggle,
                        Prompt = "Hold {X}, then {X}{X}{X} in the air",
                        Hint = "The launcher throws your partner up and you jump after them. Finish the air string before you land."
                    },
                    new TutorialStepData
                    {
                        Id = "8", Title = "Switch mid-combo: Water", Goal = TutorialGoal.SwitchStrikeTo, Element = ElementId.Water,
                        Prompt = "{X}{X}, {RB}+{X} (Water), keep going",
                        KeyboardPrompt = "{X}{X}, then 2 (Water), keep going",
                        Hint = "The button's colour is the element: blue X is Water. The string carries on in Water with its next hit.",
                        KeyboardHint = "The string carries on in Water with its next hit.",
                        AlreadyInElementHint = "You're already in Water: {RB}+{B} back to Fire first.",
                        KeyboardAlreadyInElementHint = "You're already in Water: press 1 to go back to Fire first."
                    },
                    new TutorialStepData
                    {
                        Id = "8b", Title = "Switch mid-combo: Earth", Goal = TutorialGoal.SwitchStrikeTo, Element = ElementId.Earth,
                        Prompt = "{X}{X}, {RB}+{A} (Earth), keep going",
                        KeyboardPrompt = "{X}{X}, then 3 (Earth), keep going",
                        Hint = "Green A is Earth: slow, heavy and rooted. Its hits push through enemy attacks.",
                        KeyboardHint = "Earth is slow, heavy and rooted. Its hits push through enemy attacks.",
                        AlreadyInElementHint = "You're already in Earth: {RB}+{X} to Water first.",
                        KeyboardAlreadyInElementHint = "You're already in Earth: press 2 for Water first."
                    },
                    new TutorialStepData
                    {
                        Id = "8c", Title = "Switch mid-combo: Air", Goal = TutorialGoal.SwitchStrikeTo, Element = ElementId.Air,
                        Prompt = "{X}{X}, {RB}+{Y} (Air), keep going",
                        KeyboardPrompt = "{X}{X}, then 4 (Air), keep going",
                        Hint = "Yellow Y is Air: the fastest, many little hits that circle round the enemy.",
                        KeyboardHint = "Air is the fastest: many little hits that circle round the enemy.",
                        AlreadyInElementHint = "You're already in Air: {RB}+{A} to Earth first.",
                        KeyboardAlreadyInElementHint = "You're already in Air: press 3 for Earth first."
                    },
                    new TutorialStepData
                    {
                        Id = "9", Title = "MIX finisher", Goal = TutorialGoal.MixFinisher, MinMixLevel = 2,
                        Prompt = "Finish a string after mixing two elements",
                        Hint = "Try {X}{X}, {RB} + a different colour, {X}{X}. Every element you land adds to MIX: two hit harder, three launch.",
                        KeyboardHint = "Try {X}{X}, a different element's number key (1-4), {X}{X}. Every element you land adds to MIX: two hit harder, three launch."
                    },
                    new TutorialStepData
                    {
                        Id = "10", Title = "Danger sense", Goal = TutorialGoal.DangerResponse, Count = 2, FollowUpWindow = 0.35f,
                        PartnerAttacks = true,
                        Prompt = "Gold = {LB} parry, red = {B} dodge, press when it turns white",
                        Hint = "Your partner attacks now. The mark above your head points at the attacker."
                    },
                    new TutorialStepData
                    {
                        Id = "11", Title = "Graduation", Goal = TutorialGoal.BigMixedCombo, MinComboCount = 20, MinMixLevel = 3,
                        Prompt = "20-hit combo using 3 elements",
                        Hint = "Keep the beat, switch mid-string, dodge when you need to: the combo lives as long as you keep landing hits."
                    },
                }
            };
        }
    }
}
