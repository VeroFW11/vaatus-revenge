using System;

namespace VaatusRevenge.Core
{
    // The zip strike (Spider-Man 2's web strike, done with a fire-jet dash for Fire): press it and you shoot
    // across to the enemy you're aiming at and hit them on arrival. It only works with a target in reach;
    // with none, the press does nothing (and costs nothing).
    // The Unity side picks the target with SoftLockSelector using these limits and reports it in
    // PlayerWorldState.ZipTarget*. The strike itself (frame data, damage, cost) is ElementMoveSet.ZipStrike;
    // its dash covers the startup frames, arriving just short of the target as the kick goes active.
    [Serializable]
    public class ZipStrikeSettings
    {
        public float Range = 14f;                    // furthest target (feet to feet, flat)
        public float AngleDegrees = 50f;             // at most this far off the direction you're aiming (stick, or facing)
        public float MaxHeightDifference = 3f;       // targets further above or below than this are ignored
    }
}
