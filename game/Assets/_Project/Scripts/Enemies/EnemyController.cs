using System.Collections.Generic;
using UnityEngine;
using VaatusRevenge.Core;

namespace VaatusRevenge
{
    // A real enemy: the Dao Soldier (sword) or the Crossbowman, depending on its tuning's archetype. The brain
    // (MeleeEnemyBrain or RangedEnemyBrain) decides; EnemyFighter moves it, shows every wind-up (glow + pose)
    // and delivers its swings and bolts. Hits it lands freeze the frame briefly and shake the camera; hits it
    // takes flash, poise breaks stagger it, and at zero health it topples and stops blocking the way.
    //
    // Make one with EnemyController.Spawn (edit mode or runtime). Every enemy registers in EnemyController.All,
    // so the sandbox director can reset them all without searching the scene.
    [DefaultExecutionOrder(10)] // after the player (0), before projectiles (20)
    [DisallowMultipleComponent]
    [RequireComponent(typeof(CharacterController), typeof(Combatant), typeof(GreyboxRig))]
    public class EnemyController : EnemyFighter
    {
        static readonly List<EnemyController> all = new List<EnemyController>();

        // Every enabled enemy (dead ones included, until they're reset). Loop with for, not foreach.
        public static IReadOnlyList<EnemyController> All => all;

        // Builds a complete enemy at position/yaw: layer Enemy, CharacterController, Combatant (Team.Enemy, named
        // from the tuning), grey-box body in its archetype's colour with a dao for melee or a crossbow for ranged,
        // and a health bar. Works in edit mode (for the sandbox builder) and at runtime. Its spawn point for
        // ResetEnemy is wherever it stands when play starts.
        public static EnemyController Spawn(Transform parent, Vector3 position, float yaw, EnemyTuningAsset tuning)
        {
            EnemyTuning data = tuning != null ? tuning.Tuning : null;
            EnemyArchetype archetype = data != null ? data.Archetype : EnemyArchetype.Melee;
            EnemyFeedbackSettings feedback = tuning != null ? tuning.Feedback : null;
            string displayName = EnemyBuilder.NameFor(data, EnemyTuning.CreateDaoSoldier());
            bool melee = archetype != EnemyArchetype.Ranged;

            GameObject go = EnemyBuilder.Create(parent, position, yaw, displayName, EnemyBuilder.ColorFor(archetype, feedback), melee,
                EnemyBuilder.WeaponLengthFor(feedback));
            EnemyController enemy = go.AddComponent<EnemyController>();
            if (!melee) enemy.SetCrossbow(EnemyBuilder.AddCrossbow(go.GetComponent<GreyboxRig>()));
            enemy.Configure(tuning);
            EnemyBuilder.Finish(go);
            return enemy;
        }

        // Resets every enemy (sandbox T key).
        public static void ResetAll()
        {
            for (int i = 0; i < all.Count; i++)
            {
                if (all[i] != null) all[i].ResetEnemy();
            }
        }

        // Edit-time wiring: stores the tuning. At runtime a different asset rebuilds the brain next frame.
        public void Configure(EnemyTuningAsset tuning)
        {
            SetTuningAsset(tuning);
        }

        // Back to its spawn point, full health, alive, unaware of the player, with its random sequence restarted.
        public void ResetEnemy()
        {
            ResetFighter();
        }

        protected override EnemyBrain CreateBrain(EnemyTuning tuning, AttackTokenPool tokens, int ownerId, int seed, float yaw)
        {
            // Melee or ranged by the tuning's archetype (null tuning = Dao Soldier defaults).
            return EnemyBrain.Create(tuning, tokens, ownerId, seed, yaw);
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

        // Domain reload is off in this project, so statics survive between play sessions: start each one clean.
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics()
        {
            all.Clear();
        }
    }
}
