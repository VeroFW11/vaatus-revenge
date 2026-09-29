namespace VaatusRevenge.Core
{
    // Runs the clock of one action (an attack, a dodge, a heal...) and answers "did we just pass this
    // moment?" exactly once per moment, even when one long frame jumps over several of them. That's what
    // guarantees an active window is never skipped at a low frame rate: both its start and its end are
    // reported, in order, in the same frame.
    //
    // Frame convention: an action begins at time 0 on the frame it starts; each later frame adds dt. So at
    // 60 fps a mark at 0.12 s is passed on the 8th frame after the start (7/60 = 0.117 < 0.12 <= 8/60).
    // Leftover time is not carried from one action into the next (keeps things simple; <= 1 frame error).
    public sealed class ActionTimeline
    {
        float checkedUntil = -1f;

        public float Time { get; private set; }
        public float PreviousTime { get; private set; }
        public bool IsRunning { get; private set; }

        public void Begin()
        {
            Time = 0f;
            PreviousTime = 0f;
            checkedUntil = -1f;
            IsRunning = true;
        }

        public void Stop()
        {
            IsRunning = false;
        }

        public void Advance(float dt)
        {
            PreviousTime = Time;
            if (IsRunning && dt > 0f) Time += dt;
        }

        // True the first time it's asked at or after 'mark' (call MarkChecked once all marks are checked).
        public bool Crossed(float mark)
        {
            return IsRunning && checkedUntil < mark && Time >= mark;
        }

        public void MarkChecked()
        {
            checkedUntil = Time;
        }

        public bool Reached(float mark)
        {
            return Time >= mark;
        }
    }
}
