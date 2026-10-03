namespace VaatusRevenge.Core
{
    // What a fighter's body should be doing this frame, as the animation system sees it: the clip (by
    // AnimationKey), how far into it we are, and how long it should take. The procedural animator produces these
    // cues from the combat rules; a pack-clip player (Mecanim) can follow the same cues, so both stay in sync with
    // the frame data (the strike lands on ImpactTime, whatever the clip's own length).
    public struct AnimationCue
    {
        public string Key;          // AnimationKeys id ("" = none)
        public float Time;          // seconds since this cue started
        public float Duration;      // seconds the whole action lasts (<= 0: open-ended, e.g. locomotion or a held stance)
        public float ImpactTime;    // seconds from the start when the strike should land (move Startup; <= 0: no impact)
        public bool Loop;           // locomotion cycles loop; actions don't
        public float Speed;         // locomotion: ground speed in m/s (drives the cycle rate); 0 for actions
        public float Weight;        // 0..1: how much this cue should show (blending in or out)

        public bool IsValid => !string.IsNullOrEmpty(Key);
    }
}
