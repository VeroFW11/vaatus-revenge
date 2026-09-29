using System;
using System.Numerics;

namespace VaatusRevenge.Core
{
    // Turns right-stick movement into discrete "switch target" flicks while locked on.
    // A flick is the stick going from near the centre out past FlickThreshold sideways. After a flick the stick
    // must come back to the centre before another one counts, so holding the stick over switches exactly once,
    // and a cooldown stops a single wobbly flick from skipping past several enemies.
    public sealed class StickFlickDetector
    {
        LockOnTuning tuning;
        bool armed;      // the stick has been near the centre since the last flick
        float cooldown;  // seconds until another flick may fire

        public StickFlickDetector(LockOnTuning tuning)
        {
            Tuning = tuning;
        }

        public LockOnTuning Tuning
        {
            get { return tuning; }
            set { tuning = value ?? new LockOnTuning(); }
        }

        public bool IsArmed => armed;

        // Returns -1 (flicked left), +1 (flicked right) or 0. deltaTime should be real (unscaled) time: this is
        // input handling, and must behave the same during hitstop and slow motion.
        public int Update(Vector2 stick, float deltaTime)
        {
            cooldown = Math.Max(0f, cooldown - CameraMath.SafeDeltaTime(deltaTime));
            if (!CameraMath.IsFinite(stick)) return 0;

            float magnitude = stick.Length();
            if (magnitude <= Math.Max(0f, tuning.FlickRearmRadius))
            {
                armed = true;
                return 0;
            }
            if (!armed) return 0;

            // Only mostly-sideways pushes count; pushing up or down never switches target.
            float sideways = Math.Abs(stick.X);
            if (sideways < tuning.FlickThreshold || sideways < Math.Abs(stick.Y)) return 0;

            armed = false; // one flick per push
            if (cooldown > 0f) return 0; // too soon after the last switch: swallowed, not delayed
            cooldown = Math.Max(0f, tuning.SwitchCooldown);
            return stick.X > 0f ? 1 : -1;
        }

        // Forget any push in progress: the stick must return to the centre before the next flick. Call when a
        // lock starts (clicking the right stick in to lock on often tilts it) or when switching by other means.
        public void Reset()
        {
            armed = false;
        }
    }
}
