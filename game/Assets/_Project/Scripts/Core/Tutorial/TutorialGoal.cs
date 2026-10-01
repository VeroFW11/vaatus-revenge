namespace VaatusRevenge.Core
{
    // What a tutorial step asks the player to do. TutorialTracker decides from the player's GameplayEvents (and a small
    // snapshot of the model) when it happened; the numbers each goal reads (counts, windows, distances) live in
    // TutorialStepData. New goals go at the end to keep the numbering of saved tutorial assets.
    public enum TutorialGoal
    {
        Timed,            // just play for TutorialStepData.Duration seconds (move and look around)
        OnBeatFinisher,   // land a main-string finisher in a string with at least MinOnBeat on-beat presses
        PauseFinisher,    // land the pause-chain finisher (X X, wait, X)
        DodgeKeepsCombo,  // dodge with a combo of at least MinComboCount going, then land another hit in the same combo
        SlipInStrike,     // slip in from at least MinStartDistance away, then land a hit within FollowUpWindow
        DodgeStrike,      // land a dodge strike (X late in a dodge)
        LaunchAndJuggle,  // land the launcher, then the air string's last hit on the airborne target, in the same combo
        SwitchStrikeTo,   // switch strike to Element mid-string, then land a hit in it, in the same combo
        MixFinisher,      // land a string finisher with MIX of at least MinMixLevel
        DangerResponse,   // deflect or perfect-dodge within FollowUpWindow of a danger sense "now" cue
        BigMixedCombo     // reach a combo of MinComboCount hits with MIX of at least MinMixLevel
    }
}
