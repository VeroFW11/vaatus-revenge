using System;

namespace VaatusRevenge.Core
{
    // Input buffering: a button pressed while the character is busy (mid-attack, mid-dodge) is remembered
    // for a short window and performed at the earliest moment it's allowed, instead of being thrown away.
    // Without it, players have to press on the exact frame an action ends and the controls feel "eaten".
    //
    // Holds ONE command: normally the latest press wins (if you press light and then dodge during a recovery,
    // you meant the dodge). With defensivePressesWin, a buffered dodge or guard is not replaced by a later attack
    // press, so a nervous extra light press can't cancel your escape. Commands expire after the window so a stale
    // press doesn't fire long afterwards.
    // "Locked" = the current light move accepted this press as its chain follow-up (it arrived inside the
    // combo window, or it was judged against the move's beat), so it waits for the cancel point instead of the
    // normal window. Locks end with the move, and a locked press still expires once it's older than a (longer) cap,
    // so it can never fire late. A press locked before its move can even run (Lock(runnableAt): a beat press made
    // early in a long move) ages from when it could first run, so the cap never eats a press the player made in time.
    public sealed class InputBuffer
    {
        const double Epsilon = 1e-5;

        public PlayerCommand Command { get; private set; }
        public double PressTime { get; private set; }
        public bool Locked { get; private set; }
        public double RunnableAt { get; private set; }   // a locked press ages from max(PressTime, RunnableAt)
        public bool HasCommand => Command != PlayerCommand.None;

        public void Push(PlayerCommand command, double time)
        {
            Push(command, time, false);
        }

        public void Push(PlayerCommand command, double time, bool defensivePressesWin)
        {
            if (command == PlayerCommand.None) return;
            // Mashing light after the chain follow-up (or a switch strike) is already queued must not un-queue it.
            if (command == PlayerCommand.Light && (Command == PlayerCommand.Light || Command == PlayerCommand.SwitchStrike) && Locked) return;
            if (defensivePressesWin && IsDefensive(Command) && IsAttack(command)) return;
            Command = command;
            PressTime = time;
            Locked = false;
            RunnableAt = double.NegativeInfinity;
        }

        public static bool IsDefensive(PlayerCommand command)
        {
            return command == PlayerCommand.Dodge || command == PlayerCommand.Guard;
        }

        public static bool IsAttack(PlayerCommand command)
        {
            return command == PlayerCommand.Light || command == PlayerCommand.Heavy || command == PlayerCommand.Skill
                || command == PlayerCommand.ZipStrike || command == PlayerCommand.SwitchStrike;
        }

        // Unlocked presses expire after 'window'; locked (queued) ones never do.
        public void Expire(double now, float window)
        {
            if (Command != PlayerCommand.None && !Locked && now - PressTime > window + Epsilon) Clear();
        }

        // Unlocked presses expire after 'window', locked (queued) ones after 'lockedMaxAge'.
        public void Expire(double now, float window, float lockedMaxAge)
        {
            if (Command == PlayerCommand.None) return;
            double age = Locked ? now - Math.Max(PressTime, RunnableAt) : now - PressTime;
            if (age > (Locked ? lockedMaxAge : window) + Epsilon) Clear();
        }

        public void Lock()
        {
            Lock(double.NegativeInfinity);
        }

        // runnableAt: when the locked press can first run (its move's cancel point); it ages from then.
        public void Lock(double runnableAt)
        {
            if (Command == PlayerCommand.None) return;
            Locked = true;
            RunnableAt = runnableAt;
        }

        public void Unlock()
        {
            Locked = false;
        }

        public void Clear()
        {
            Command = PlayerCommand.None;
            Locked = false;
            RunnableAt = double.NegativeInfinity;
        }

        public float Age(double now)
        {
            return Command == PlayerCommand.None ? 0f : (float)(now - PressTime);
        }
    }
}
