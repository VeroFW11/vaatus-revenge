namespace VaatusRevenge.Core
{
    // One button, two jobs: a tap dodges, a hold sprints. This decides which, per DodgeTrigger:
    //   OnPress   (Sekiro-style): every press is a dodge straight away; holding afterwards means sprint.
    //   OnRelease (Elden Ring-style): let go before the threshold = dodge (on release, so it has a tiny
    //             delay); keep holding past the threshold = sprint, and no dodge when you let go.
    public sealed class TapHoldResolver
    {
        bool pressSeen;      // we saw this press start (so a stray release can't fire a dodge)
        bool holdReached;    // this press lasted past the threshold

        public bool IsHeld { get; private set; }
        public float HeldTime { get; private set; }

        // Feed this frame's button edges. Returns true when the press counts as a tap (a dodge) this frame.
        // dt may be 0 (frozen frame): edges are still handled, the hold timer just doesn't advance.
        // pressAge: how long the button had already been down when the press was reported (a press the pad's chord
        // reader held back, ButtonState.PressDelay), so the hold is timed from the real press.
        public bool Update(bool pressed, bool released, bool held, float dt, DodgeTrigger trigger, float threshold, float pressAge = 0f)
        {
            bool tap = false;
            if (pressed)
            {
                pressSeen = true;
                holdReached = false;
                HeldTime = pressAge > 0f ? pressAge : 0f;
                if (trigger == DodgeTrigger.OnPress) tap = true;
            }
            else if (held && pressSeen && dt > 0f)
            {
                HeldTime += dt;
            }

            if (held && pressSeen && HeldTime >= threshold) holdReached = true;

            // Released (possibly in the same frame as the press: a very quick tap).
            if (!held && pressSeen && (released || IsHeld || pressed))
            {
                if (trigger == DodgeTrigger.OnRelease && !holdReached) tap = true;
                pressSeen = false;
                holdReached = false;
            }
            IsHeld = held;
            return tap;
        }

        // Should the character sprint (when it's free to)?
        public bool WantsSprint(DodgeTrigger trigger, float threshold)
        {
            if (!IsHeld || !pressSeen) return false;
            return trigger == DodgeTrigger.OnPress || HeldTime >= threshold;
        }

        public void Reset()
        {
            pressSeen = false;
            holdReached = false;
            IsHeld = false;
            HeldTime = 0f;
        }
    }
}
