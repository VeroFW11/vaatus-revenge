using UnityEngine;

namespace VaatusRevenge
{
    // Numbers for a whole group fight rather than one enemy. For now just the attack-token limit: how many
    // enemies may attack at the same time while the rest circle and wait (see AttackTokenPool). It's why
    // souls-like groups feel dangerous but fair instead of everyone swinging at once.
    // Create one via Assets > Create > Vaatu's Revenge > Tuning > Encounter and give it to the EnemyEncounter.
    [CreateAssetMenu(menuName = "Vaatu's Revenge/Tuning/Encounter", fileName = "EncounterTuning")]
    public class EncounterTuningAsset : ScriptableObject
    {
        // Also used when a scene has no EnemyEncounter at all (enemies then share a fallback pool of this size).
        public const int DefaultMaxSimultaneousAttackers = 2;

        [Tooltip("How many enemies may attack at once. Enemies whose tuning has UsesAttackToken off (the crossbowman, dummies) ignore this. Read live.")]
        [Min(0)] public int MaxSimultaneousAttackers = DefaultMaxSimultaneousAttackers;
    }
}
