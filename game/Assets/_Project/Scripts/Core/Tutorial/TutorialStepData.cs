using System;

namespace VaatusRevenge.Core
{
    // One tutorial step: what the panel says and what counts as passing it. All of it is data (a TutorialScriptAsset in
    // Unity), so the wording, the order and every count, window and distance can be changed without touching code.
    //
    // Prompt and Hint are glyph text: "{RB}+{X}" draws the RB pill and a blue X (see HudGlyphText). The keyboard versions
    // are used when the player is on keyboard and mouse; leave them empty when the gamepad text reads right on both (the
    // glyphs swap to their keys: X is the left mouse button, B is Shift and so on).
    [Serializable]
    public class TutorialStepData
    {
        public string Id = "";                     // shown as "Id / total", e.g. "8b / 11"
        public string Title = "";
        public string Prompt = "";
        public string KeyboardPrompt = "";         // empty = Prompt
        public string Hint = "";                   // a smaller second line (optional)
        public string KeyboardHint = "";           // empty = Hint

        public TutorialGoal Goal = TutorialGoal.Timed;
        public int Count = 1;                      // successes needed (each one lights a pip)
        public float Duration;                     // Timed: seconds to play
        public ElementId Element = ElementId.None; // SwitchStrikeTo: the element to switch into
        public int MinOnBeat;                      // OnBeatFinisher: on-beat presses in the string
        public int MinComboCount;                  // DodgeKeepsCombo, BigMixedCombo: combo hits
        public int MinMixLevel;                    // MixFinisher, BigMixedCombo: distinct elements landed
        public float MinStartDistance;             // SlipInStrike: metres to the partner when the dodge starts
        public float FollowUpWindow;               // SlipInStrike, DangerResponse: seconds allowed for the follow-up
        public bool NeedsParryAndDodge;            // DangerResponse: at least one deflect AND one perfect dodge (a second of the
                                                   // same kind doesn't light a pip), so both colours of the mark get used

        // SwitchStrikeTo only: shown instead of Hint while the player is already in Element (a switch to the element you
        // are in does nothing, so they need to leave it first).
        public string AlreadyInElementHint = "";
        public string KeyboardAlreadyInElementHint = "";

        public bool PartnerAttacks;                // the sparring partner fights back during this step

        public string PromptFor(bool gamepad)
        {
            return gamepad || string.IsNullOrEmpty(KeyboardPrompt) ? Prompt ?? "" : KeyboardPrompt;
        }

        public string HintFor(bool gamepad)
        {
            return gamepad || string.IsNullOrEmpty(KeyboardHint) ? Hint ?? "" : KeyboardHint;
        }

        public string AlreadyInElementHintFor(bool gamepad)
        {
            return gamepad || string.IsNullOrEmpty(KeyboardAlreadyInElementHint) ? AlreadyInElementHint ?? "" : KeyboardAlreadyInElementHint;
        }
    }
}
