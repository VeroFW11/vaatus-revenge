using System;

namespace VaatusRevenge.Core
{
    // How an enemy's wind-up should be shown, so players learn to read it (souls-likes live on readable telegraphs).
    public enum TelegraphKind
    {
        Normal,   // e.g. yellow weapon glow
        Heavy,    // big hit: e.g. red glow. Usually hyper-armoured, so dodge or deflect it
        Delayed,  // a long, held wind-up that punishes panic dodging
        Aimed,    // ranged: taking aim
        BreakOut  // the armoured anti-mash counter (EnemyBreakOutRule): its own colour, so players learn "stop pressing, dodge"
    }

    // One attack in an enemy's list. The AI picks among the attacks that are off cooldown, weighted by
    // Weight, preferring ones usable at the current distance.
    [Serializable]
    public class EnemyAttackData
    {
        public MoveData Move = new MoveData();       // frame data, damage, reach, projectile. Move.Startup is the telegraph
        public TelegraphKind Telegraph = TelegraphKind.Normal;
        public float Weight = 1f;                    // relative chance of being picked
        public float MinRange = 0f;                  // usable from this distance (enemy centre to the target's body)...
        public float MaxRange = 2.4f;                // ...up to this one
        public float Cooldown = 0f;                  // seconds before this attack can be picked again
        public int HitCount = 1;                     // > 1 = a combo or burst of several strikes or bolts
        public float HitInterval = 0.35f;            // seconds between the starts of those strikes/bolts
        public bool HideDangerSense = false;         // true = the player's danger sense never shows it (bosses only, sparingly)
        public float DangerLeadScale = 1f;           // scales the danger sense's lead times for this attack (a feint, a slow one)
    }
}
