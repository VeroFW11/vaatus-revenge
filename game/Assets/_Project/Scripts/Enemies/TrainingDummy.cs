using System.Collections.Generic;
using System.Globalization;
using UnityEngine;
using VaatusRevenge.Core;

namespace VaatusRevenge
{
    // A sparring dummy for practising combos and timing. It can't die (its tuning is Unkillable), refills its
    // health after a few seconds without being hit, and counts the current combo: hits, damage, damage per
    // second and the last hit's damage, shown under its health bar. With SwingEnabled it turns to face you and
    // swings its practice stick on a fixed beat, with the same wind-up glow and pose as real enemies, so you
    // can drill dodge and deflect timing without being chased.
    //
    // It's a post: it turns but never moves or gets knocked back, so a combo can be repeated at the same spacing.
    // Make one with TrainingDummy.Spawn (edit mode or runtime); every dummy registers in TrainingDummy.All.
    [DefaultExecutionOrder(10)]
    [DisallowMultipleComponent]
    [RequireComponent(typeof(CharacterController), typeof(Combatant), typeof(GreyboxRig))]
    public class TrainingDummy : EnemyFighter
    {
        static readonly List<TrainingDummy> all = new List<TrainingDummy>();

        [Tooltip("Swing on a fixed rhythm (the tuning's AttackIntervalMin) while the player is within its aggro range.")]
        [SerializeField] private bool swingEnabled;

        string statusText;

        public static IReadOnlyList<TrainingDummy> All => all;

        // Builds a complete dummy at position/yaw: layer Enemy, CharacterController (so you bump into it),
        // Combatant (Team.Enemy, named from the tuning), straw-tan grey-box body with a practice stick, and a
        // health bar with the combo readout. Works in edit mode and at runtime.
        public static TrainingDummy Spawn(Transform parent, Vector3 position, float yaw, EnemyTuningAsset tuning, bool swings)
        {
            EnemyTuning data = tuning != null ? tuning.Tuning : null;
            EnemyFeedbackSettings feedback = tuning != null ? tuning.Feedback : null;
            string displayName = EnemyBuilder.NameFor(data, EnemyTuning.CreateSparringDummy());
            GameObject go = EnemyBuilder.Create(parent, position, yaw, displayName, EnemyBuilder.ColorFor(EnemyArchetype.Dummy, feedback), true);
            TrainingDummy dummy = go.AddComponent<TrainingDummy>();
            dummy.Configure(tuning, swings);
            EnemyBuilder.Finish(go);
            return dummy;
        }

        // Resets every dummy.
        public static void ResetAll()
        {
            for (int i = 0; i < all.Count; i++)
            {
                if (all[i] != null) all[i].ResetDummy();
            }
        }

        // Edit-time wiring: stores the tuning and whether it swings.
        public void Configure(EnemyTuningAsset tuning, bool swings)
        {
            SetTuningAsset(tuning);
            SwingEnabled = swings;
        }

        // Full health, combo stats cleared, facing its spawn direction, swing rhythm restarted.
        public void ResetDummy()
        {
            ResetFighter();
            RefreshStatusText();
        }

        public bool SwingEnabled
        {
            get => swingEnabled;
            set
            {
                swingEnabled = value;
                SparringDummyBrain dummyBrain = DummyBrain;
                if (dummyBrain != null) dummyBrain.SwingEnabled = value;
            }
        }

        public int ComboHits => DummyBrain != null ? DummyBrain.ComboHits : 0;
        public float ComboDamage => DummyBrain != null ? DummyBrain.ComboDamage : 0f;
        public float ComboDps => DummyBrain != null ? DummyBrain.ComboDps : 0f;
        public float LastHitDamage => DummyBrain != null ? DummyBrain.LastHitDamage : 0f;

        public override string StatusText => statusText;

        SparringDummyBrain DummyBrain => Brain as SparringDummyBrain;

        protected override bool Planted => true;

        protected override EnemyBrain CreateBrain(EnemyTuning tuning, AttackTokenPool tokens, int ownerId, int seed, float yaw)
        {
            // Always the dummy brain (whatever the tuning's archetype), so the combo stats always work.
            return new SparringDummyBrain(tuning ?? EnemyTuning.CreateSparringDummy(), tokens, ownerId, seed, yaw) { SwingEnabled = swingEnabled };
        }

        // Dummies only share the encounter's pool if there is one (their tuning normally doesn't use tokens).
        protected override AttackTokenPool ResolveTokens(EnemyTuning tuning)
        {
            EnemyEncounter encounter = EnemyEncounter.Instance;
            return encounter != null ? encounter.Tokens : null;
        }

        // The readout only changes when it's hit, refilled or reset, so the text is rebuilt then, not every frame.
        protected override void OnBrainEvent(in EnemyEvent e)
        {
            if (e.Type == EnemyEventType.Damaged || e.Type == EnemyEventType.HealthRefilled || e.Type == EnemyEventType.Reset)
                RefreshStatusText();
        }

        // Editor only: ticking "Swing Enabled" in the Inspector while playing takes effect straight away.
        void OnValidate()
        {
            SparringDummyBrain dummyBrain = DummyBrain;
            if (dummyBrain != null) dummyBrain.SwingEnabled = swingEnabled;
        }

        protected override void OnEnable()
        {
            base.OnEnable();
            if (!all.Contains(this)) all.Add(this);
        }

        protected override void OnDisable()
        {
            all.Remove(this);
            base.OnDisable();
        }

        void RefreshStatusText()
        {
            SparringDummyBrain dummyBrain = DummyBrain;
            if (dummyBrain == null || (dummyBrain.ComboHits == 0 && dummyBrain.LastHitDamage <= 0f))
            {
                statusText = null;
                return;
            }
            statusText = string.Format(CultureInfo.InvariantCulture, "Combo {0} hits  {1:0} dmg  {2:0.0} DPS  (last {3:0})",
                dummyBrain.ComboHits, dummyBrain.ComboDamage, dummyBrain.ComboDps, dummyBrain.LastHitDamage);
        }

        // Domain reload is off in this project, so statics survive between play sessions: start each one clean.
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics()
        {
            all.Clear();
        }
    }
}
