using UnityEngine;
using VaatusRevenge.Core;

namespace VaatusRevenge
{
    // Builds the GameObject behind EnemyController.Spawn and TrainingDummy.Spawn, following the fighter
    // conventions every fighter shares: a 1.8 m CharacterController with its pivot at the feet, a Combatant
    // (Team.Enemy) with an aim point at chest height, a grey-box body in the archetype's colour, the Enemy layer.
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
        const string CrossbowName = "Crossbow";

        // Archetype colours (spec section 9), used unless the tuning asset's feedback picks a custom one.
        static readonly Color SoldierColor = new Color(0.44f, 0.52f, 0.62f);     // steel blue-grey
        static readonly Color CrossbowmanColor = new Color(0.42f, 0.5f, 0.2f);   // olive green
        static readonly Color DummyColor = new Color(0.84f, 0.72f, 0.46f);       // straw tan

        public static Color ColorFor(EnemyArchetype archetype, EnemyFeedbackSettings feedback)
        {
            if (feedback != null && feedback.UseCustomColor) return feedback.CustomColor;
            switch (archetype)
            {
                case EnemyArchetype.Ranged: return CrossbowmanColor;
                case EnemyArchetype.Dummy: return DummyColor;
                default: return SoldierColor;
            }
        }

        // Display names are data: the tuning's, or else the default tuning's for this kind of fighter.
        public static string NameFor(EnemyTuning tuning, EnemyTuning fallback)
        {
            if (tuning != null && !string.IsNullOrEmpty(tuning.DisplayName)) return tuning.DisplayName;
            return fallback != null ? fallback.DisplayName : "";
        }

        // Creates the fighter, still inactive: CharacterController, Combatant with aim point, built GreyboxRig.
        public static GameObject Create(Transform parent, Vector3 position, float yaw, string displayName, Color bodyColor, bool withWeapon)
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

            GreyboxRig rig = go.AddComponent<GreyboxRig>();
            rig.Build(bodyColor, withWeapon);
            return go;
        }

        // A small repeating crossbow (stock, magazine, bow arms) held in the right hand, pointing forward.
        // It shares the rig's limb material, so it glows with the aiming telegraph. Returns its pivot.
        public static Transform AddCrossbow(GreyboxRig rig)
        {
            Transform hand = rig.GetAnchor(Limb.RightFist);
            Transform pivot = GreyboxShapes.CreatePivot(CrossbowName, hand, Vector3.zero);
            Material material = rig.LimbMaterial;
            AddPart("Stock", pivot, new Vector3(0f, -0.01f, 0.12f), new Vector3(0.055f, 0.07f, 0.46f), material);
            AddPart("Magazine", pivot, new Vector3(0f, 0.075f, 0.13f), new Vector3(0.06f, 0.09f, 0.2f), material);
            AddPart("Prod", pivot, new Vector3(0f, 0f, 0.33f), new Vector3(0.6f, 0.035f, 0.045f), material);
            return pivot;
        }

        // Last step of a Spawn: the health bar, the Enemy layer on every part, then switch it on.
        public static void Finish(GameObject go)
        {
            if (go.GetComponent<EnemyHealthBar>() == null) go.AddComponent<EnemyHealthBar>();
            Layers.SetRecursively(go, Layers.Enemy);
            go.SetActive(true);
        }

        static void AddPart(string partName, Transform parent, Vector3 localPosition, Vector3 localScale, Material material)
        {
            GameObject part = GreyboxShapes.CreateVisual(partName, PrimitiveType.Cube, parent, material, true);
            part.transform.localPosition = localPosition;
            part.transform.localScale = localScale;
        }
    }
}
