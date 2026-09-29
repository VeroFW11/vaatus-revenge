namespace VaatusRevenge.Core
{
    // An enemy brain's top-level state.
    public enum EnemyState
    {
        Idle,       // hasn't noticed the player (or lost them)
        Approach,   // closing distance (chasing, or walking back into its preferred band)
        Circle,     // holding its spacing, strafing, waiting for its next attack
        Retreat,    // stepping back (after an attack, or a ranged enemy keeping its distance)
        Attacking,  // running an attack: telegraph (startup), active frames, recovery
        Staggered,  // stunned: poise broken or deflected
        Dead
    }
}
