using UnityEngine;
using VaatusRevenge.Core;

namespace VaatusRevenge
{
    // One target an attack touched and what happened, handed back to the attacker so it can react:
    // hitstop and momentum on a clean hit, getting staggered itself when the target parried, etc.
    public struct HitReport
    {
        public Combatant Target;
        public HitResult Result;
        public Vector3 Point;
    }
}
