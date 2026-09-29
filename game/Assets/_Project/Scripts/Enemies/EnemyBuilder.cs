using UnityEngine;
using VaatusRevenge.Core;

namespace VaatusRevenge
{
    // Builds the GameObject behind EnemyController.Spawn and TrainingDummy.Spawn, following the fighter
    // conventions every fighter shares: a 1.8 m CharacterController with its pivot at the feet, a Combatant
    // (Team.Enemy) with an aim point at chest height, a jointed HumanoidBody dressed for the archetype (dao
    // soldier, crossbowman, straw dummy) animated by a BodyAnimatorDriver, on the Enemy layer.
    // Works in edit mode (the sandbox builder saves the result into the scene) and at runtime.
    //
    // The object is built inactive and switched on at the end (Finish), so Awake/OnEnable only run once
    // everything is in place.
    internal static class EnemyBuilder
    {
        // Fighter conventions (spec section 9): body size, and step offset 0.3 so the arena stairs work.
        const float BodyHeight = 1.8f;
        const float BodyRadius = 0.4f;
        const float StepOffset = 0.3f;
        const float SlopeLimit = 50f;
        const float AimHeight = 1.3f;

        // The body for each kind of enemy (BodyLook presets), with the tuning asset's custom colour if it picks one.
        public static BodyLook LookFor(EnemyArchetype archetype, EnemyFeedbackSettings feedback)
        {
            BodyLook look;
            switch (archetype)
            {
                case EnemyArchetype.Ranged: look = BodyLook.Crossbowman(); break;
                case EnemyArchetype.Dummy: look = BodyLook.Dummy(); break;
                default: look = BodyLook.Soldier(); break;
            }
            if (feedback != null && feedback.UseCustomColor) look.Cloth = feedback.CustomColor;
            if (look.Weapon == BodyWeapon.Dao || look.Weapon == BodyWeapon.PracticeStick) look.WeaponLength = WeaponLengthFor(feedback);
            return look;
        }

        // Display names are data: the tuning's, or else the default tuning's for this kind of fighter.
        public static string NameFor(EnemyTuning tuning, EnemyTuning fallback)
        {
            if (tuning != null && !string.IsNullOrEmpty(tuning.DisplayName)) return tuning.DisplayName;
            return fallback != null ? fallback.DisplayName : "";
        }

        // The weapon length the tuning asks for (dao or practice stick, hand to tip).
        public static float WeaponLengthFor(EnemyFeedbackSettings feedback)
        {
            return feedback != null && feedback.Poses != null ? feedback.Poses.WeaponLength : EnemyPoseSettings.DefaultWeaponLength;
        }

        // Creates the fighter, still inactive: CharacterController, Combatant with aim point, built HumanoidBody.
        public static GameObject Create(Transform parent, Vector3 position, float yaw, string displayName, BodyLook look)
        {
            var go = new GameObject(string.IsNullOrEmpty(displayName) ? "Enemy" : displayName);
            go.SetActive(false);
            Transform root = go.transform;
            root.SetParent(parent, false);
            root.SetPositionAndRotation(position, Quaternion.Euler(0f, yaw, 0f));

            CharacterController controller = go.AddComponent<CharacterController>();
            controller.height = BodyHeight;
            controller.radius = BodyRadius;
            controller.center = new Vector3(0f, BodyHeight * 0.5f, 0f); // pivot at the feet
            controller.stepOffset = StepOffset;
            controller.slopeLimit = SlopeLimit;
            controller.minMoveDistance = 0f; // tiny hitstop-slowed moves must still count, or grounding flickers

            var aim = new GameObject("AimPoint").transform;
            aim.SetParent(root, false);
            aim.localPosition = new Vector3(0f, AimHeight, 0f);
            Combatant combatant = go.AddComponent<Combatant>();
            combatant.Configure(Team.Enemy, aim, BodyRadius, BodyHeight, displayName);

            HumanoidBody body = go.AddComponent<HumanoidBody>();
            go.AddComponent<BodyAnimatorDriver>();
            body.Build(look);
            return go;
        }

        // Last step of a Spawn: the health bar, the Enemy layer on every part, then switch it on.
        public static void Finish(GameObject go)
        {
            if (go.GetComponent<EnemyHealthBar>() == null) go.AddComponent<EnemyHealthBar>();
            Layers.SetRecursively(go, Layers.Enemy);
            go.SetActive(true);
        }
    }
}
