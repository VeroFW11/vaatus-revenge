using System;

namespace VaatusRevenge.Core
{
    // The anti-mash rule (report 02, NEW-02 "open"). Souls-like enemies can't just stand there and absorb a
    // stream of light hits: if they could, pressing light over and over would beat every fight. So when an
    // enemy takes HitsToTrigger clean hits within HitWindow seconds while it isn't staggered, it answers with a
    // "break-out": its own armoured counter (Attack) that shoves the player away.
    //
    // It's a normal enemy attack in every other way, so it stays FAIR: it has a readable telegraph (its own
    // glow colour, Attack.Move.Startup long, which should stay >= 0.4 s so a person can react), it takes an
    // attack token like any other swing, and it can be dodged, blocked or deflected. Only a player who keeps
    // pressing light without watching the enemy gets caught. Staggering the enemy (breaking its poise) wipes
    // the count: a stagger is earned, so it's never punished.
    [Serializable]
    public class EnemyBreakOutRule
    {
        public bool Enabled = false;                 // off = the enemy never breaks out (crossbowmen, dummies)
        public int HitsToTrigger = 3;                // clean hits needed... (1-8)
        public float HitWindow = 1.2f;               // ...within this many seconds
        public float MaxWait = 0.6f;                 // once triggered and free to act (its own swing over), how long it waits for the player
                                                     // to be in reach and a token to be free before giving up
        public bool RestoresPoise = true;            // starting the break-out refills its poise: it has shrugged the combo off, so the
                                                     // next stagger must be built up again
        public bool CountsTradedRecoveryHits = true; // hits on its recovery (after its own swing) count only if that swing LANDED on the
                                                     // player (they traded blows instead of defending). A punish after a dodge, block or
                                                     // deflect never counts: punishing an opening with a full chain is good play
        public bool WaitsForOtherAttackers = true;   // only breaks out while no other enemy is attacking (holds a token), so in a group it
                                                     // never adds a second blade to a fight that already has one swinging
        public bool CutsWindUp = true;               // may drop its own wind-up (one with no armour yet) to break out at once
        public float Cooldown = 5f;
        public float FollowUpDelay = 0f;             // if the break-out LANDED, its next normal attack may start this soon (instead of the
                                                     // usual AttackIntervalMin-Max pause). Dodge, block or deflect it and the pause is normal                  // seconds after a break-out starts before hits count towards the next one
        public EnemyAttackData Attack = new EnemyAttackData();   // the counter itself (Telegraph = BreakOut for its own glow). It only
                                                                 // starts when the player is within Attack.MaxRange; Weight is ignored

        // The ring buffer in EnemyBrain holds this many hit times, so HitsToTrigger is clamped to it.
        public const int MaxTrackedHits = 8;
    }
}
