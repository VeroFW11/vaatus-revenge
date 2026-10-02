namespace VaatusRevenge.Core
{
    // What the HUD needs to draw the beat ring for the running string move (PlayerCombatModel.Rhythm). Read it every
    // frame; it's a copy, so holding on to it never changes the model.
    public struct RhythmView
    {
        public bool Active;          // a string move is running and its beat hasn't been judged yet
        public float TimeToBeat;     // game seconds; > 0 before the beat, < 0 after
        public float EarlyWindow;    // effective BeatEarly (preset + element)
        public float LateWindow;     // effective BeatLate (+ the switch bonus if a switch strike is pending)
        public int Streak;           // OnBeatStreak: on-beat presses in a row in this string (counted at the press)
        public int ConfirmedStreak;  // the same streak counting only presses whose move has started, so a press a second
                                     // press later downgrades to a mash never shows (the HUD's ON BEAT pips, J6-05)
        public BeatGrade LastGrade;
        public float PlaybackRate;   // of the running move (1 = as authored)
        public bool PauseReady;      // the pause band is open: an X now starts the pause chain (X X, wait, X X). The HUD lights
                                     // it so the wait is taught by a cue, not a number of seconds (it differs per element)

        // The beat ring (J3-01): what the HUD draws, so the press can be ANTICIPATED, not reacted to. While the running
        // move's beat is unjudged it is that beat (as TimeToBeat). Once a follow-up press is queued, it is the NEXT hit's
        // predicted beat (the running move's cancel point plus the next move's startup at the rate the press earned), and
        // that ring keeps closing across the move change. NextCue is the ring after the running one, shown from the
        // running move's start (assuming an on-beat press), so a ring is always closing well before its beat. Active / TimeToBeat above stay
        // the judged beat (bots, tutorials and tests read them).
        public bool Cue;             // a ring is closing on a beat
        public float CueTimeToBeat;  // game seconds to that beat (> 0 before it)
        public float CueLead;        // game seconds from when the ring appeared to that beat (the HUD runs its approach over it)
        public bool CueIsNextHit;    // the ring shows the queued next hit's predicted beat
        public bool NextCue;         // the hit after the running one, if you keep the beat: its ring appears as the running move
        public float NextCueTimeToBeat;   // starts, so every follow-up's ring is up for a whole beat-to-beat gap and more
        public float NextCueLead;
    }
}
