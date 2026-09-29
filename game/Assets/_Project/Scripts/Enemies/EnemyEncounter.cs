using UnityEngine;
using VaatusRevenge.Core;

namespace VaatusRevenge
{
    // One per scene: owns the AttackTokenPool the enemies share, so only a few attack at once (spec: 2) while the
    // others circle and wait their turn. Enemies pick the pool up when their brains are created (in Start, after
    // every object's OnEnable has run, so it doesn't matter which object loads first).
    // Without an encounter in the scene, enemies fall back to a shared pool of
    // EncounterTuningAsset.DefaultMaxSimultaneousAttackers and log a warning once.
    //
    // Setup from code (e.g. the sandbox builder): AddComponent, then Configure(encounterTuningAsset).
    [DisallowMultipleComponent]
    public class EnemyEncounter : MonoBehaviour
    {
        public static EnemyEncounter Instance { get; private set; }

        [Tooltip("How many enemies may attack at the same time. Empty = EncounterTuningAsset's default (2).")]
        [SerializeField] private EncounterTuningAsset tuningAsset;

        AttackTokenPool tokens;

        // Created on first use, so an enemy asking before this object's own Awake still gets the real pool.
        public AttackTokenPool Tokens
        {
            get
            {
                if (tokens == null) tokens = new AttackTokenPool(MaxAttackers);
                return tokens;
            }
        }

        public EncounterTuningAsset TuningAsset => tuningAsset;

        // One-call setup that works at edit time as well as in play mode.
        public void Configure(EncounterTuningAsset tuning)
        {
            tuningAsset = tuning;
            if (tokens != null) tokens.MaxTokens = MaxAttackers;
        }

        int MaxAttackers => tuningAsset != null ? Mathf.Max(0, tuningAsset.MaxSimultaneousAttackers) : EncounterTuningAsset.DefaultMaxSimultaneousAttackers;

        // Domain reload is off in this project, so statics survive between play sessions: start each one clean.
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics()
        {
            Instance = null;
        }

        void OnEnable()
        {
            if (Instance != null && Instance != this)
            {
                Debug.LogWarning("EnemyEncounter: more than one is active. Only the first one ('" + Instance.name
                                 + "') hands out attack tokens.", this);
                return;
            }
            Instance = this;
        }

        void OnDisable()
        {
            if (Instance == this) Instance = null;
        }

        void Update()
        {
            // Read the limit live, so changing it in the Inspector while playing takes effect straight away.
            if (tokens != null) tokens.MaxTokens = MaxAttackers;
        }
    }
}
