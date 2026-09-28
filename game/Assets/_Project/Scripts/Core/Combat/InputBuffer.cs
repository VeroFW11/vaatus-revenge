namespace VaatusRevenge.Core
{
    // Input buffering: a button pressed while the character is busy (mid-attack, mid-dodge) is remembered
    // for a short window and performed at the earliest moment it's allowed, instead of being thrown away.
    // Without it, players have to press on the exact frame an action ends and the controls feel "eaten".
    //
    // Holds ONE command: the latest press wins (if you press light and then dodge during a recovery, you
    // meant the dodge). Commands expire after the window so a stale press doesn't fire long afterwards.
    // "Locked" = the current light move accepted this press as its chain follow-up (it arrived inside the
    // combo window), so it waits for the cancel point instead of expiring. Locks end with the move.
    public sealed class InputBuffer
    {
        const double Epsilon = 1e-5;

        public PlayerCommand Command { get; private set; }
        public double PressTime { get; private set; }
        public bool Locked { get; private set; }
        public bool HasCommand => Command != PlayerCommand.None;

        public void Push(PlayerCommand command, double time)
        {
            if (command == PlayerCommand.None) return;
            // Mashing light after the chain follow-up is already queued must not un-queue it.
            if (command == PlayerCommand.Light && Command == PlayerCommand.Light && Locked) return;
            Command = command;
            PressTime = time;
            Locked = false;
        }

        public void Expire(double now, float window)
        {
            if (Command != PlayerCommand.None && !Locked && now - PressTime > window + Epsilon) Clear();
        }

        public void Lock()
        {
            if (Command != PlayerCommand.None) Locked = true;
        }

        public void Unlock()
        {
            Locked = false;
        }

        public void Clear()
        {
            Command = PlayerCommand.None;
            Locked = false;
        }

        public float Age(double now)
        {
            return Command == PlayerCommand.None ? 0f : (float)(now - PressTime);
        }
    }
}
