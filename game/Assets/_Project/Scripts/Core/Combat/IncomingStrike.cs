using System.Numerics;

namespace VaatusRevenge.Core
{
    // One enemy strike that is on its way to the player (danger sense). DangerSenseRelay registers it when the
    // enemy's wind-up starts; the player model raises DangerWarning / DangerNow at its own preset's lead times and
    // DangerCleared when it has landed, missed or been called off.
    public struct IncomingStrike
    {
        public int AttackerId;       // the attacker's CombatIds id
        public int AttackKey;        // the attack's id (the telegraph's AttackId; a bolt's own AttackId once it flies)
        public int HitIndex;         // which strike of a multi-hit attack (0 = first)
        public double ImpactClock;   // when it lands, on the player model's clock
        public Vector3 AttackerFeet;
        public Vector3 StrikeForward; // flat direction the strike travels (attacker -> player at registration)
        public bool Parryable;
        public bool Unblockable;
        public bool Ranged;
        public bool Hidden;          // EnemyAttackData.HideDangerSense: tracked, never shown
        public float LeadScale;      // EnemyAttackData.DangerLeadScale: scales the warning lead times (0 = 1)
        public bool Warned;          // set by the model: DangerWarning has been raised
        public bool NowFired;        // set by the model: DangerNow has been raised

        // Gold (parry it) or red (dodge it).
        public bool MustDodge => !Parryable || Unblockable;
    }
}
