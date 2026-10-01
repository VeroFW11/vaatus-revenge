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
        public int Streak;           // OnBeatStreak: on-beat presses in a row in this string
        public BeatGrade LastGrade;
        public float PlaybackRate;   // of the running move (1 = as authored)
    }
}
