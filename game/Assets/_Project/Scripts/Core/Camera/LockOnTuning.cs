using System;

namespace VaatusRevenge.Core
{
    // Every number lock-on uses: which enemy gets picked, how switching works and when the lock breaks.
    // Lives next to CameraTuning inside a CameraTuningAsset. Defaults are the prototype spec's starting numbers.
    [Serializable]
    public class LockOnTuning
    {
        // Picking a target when lock-on is pressed.
        public float AcquireRange = 22f;           // metres from the player
        public float AcquireMaxAngle = 60f;        // degrees from the centre of the screen; wider enemies are ignored
        public float CentreWeight = 1f;            // how much being near the screen centre matters...
        public float DistanceWeight = 0.35f;       // ...compared with being close to the player (lower score wins)

        // Keeping the lock.
        public float BreakRange = 30f;             // metres: further than this and the lock drops
        public float LineOfSightGraceTime = 1.2f;  // seconds a wall may hide the target before the lock drops, so
                                                   // running past a pillar doesn't lose it
        public bool AutoRetargetOnKill = true;     // when the target dies, jump to the next one instead of unlocking

        // Switching target (right-stick flick, mouse wheel, or Z / C).
        public float SwitchMaxAngle = 90f;         // degrees either side of the camera's facing that can be switched to
        public float FlickThreshold = 0.75f;       // how far the stick must be pushed sideways to count as a flick
        public float FlickRearmRadius = 0.35f;     // the stick must come back inside this before the next flick counts
        public float SwitchCooldown = 0.3f;        // seconds between two switches, so one flick or wheel roll moves one step
    }
}
