using System.Collections.Generic;
using UnityEngine;

namespace VaatusRevenge
{
    // The pieces of a grey-box fighter. GreyboxRig.Build creates them and keeps this in a serialized field,
    // so fighters built in edit mode are saved into the scene with all their references intact.
    // Proportions suit a 1.8 m fighter whose pivot is at its feet (CharacterController centre at y 0.9).
    // No arms or legs on purpose: floating fists and feet read strikes more clearly than stiff limbs.
    [System.Serializable]
    public class GreyboxRigParts
    {
        public const string VisualName = "GreyboxVisual";

        // Rest pose. Fists hang off the torso (they lean with it); feet hang off the visual root (they stay planted).
        public static readonly Vector3 TorsoPivot = new Vector3(0f, 0.9f, 0f);
        public static readonly Vector3 RightFistRest = new Vector3(0.24f, 0.36f, 0.26f);
        public static readonly Vector3 LeftFistRest = new Vector3(-0.22f, 0.4f, 0.3f);
        public static readonly Vector3 RightFootRest = new Vector3(0.14f, 0f, 0.04f);
        public static readonly Vector3 LeftFootRest = new Vector3(-0.14f, 0f, -0.02f);
        public static readonly Vector3 FistScale = new Vector3(0.17f, 0.17f, 0.17f);

        public Transform Visual;         // the whole body: topples, wobbles and spins around the feet
        public Transform Torso;          // hip pivot: leans
        public Transform Head;
        public Transform Chest;          // front of the chest, handy for effects
        public Transform RightFist;
        public Transform LeftFist;
        public Transform RightFoot;
        public Transform LeftFoot;
        public Transform RightFistMesh;  // scaled child of the fist anchor (grows while charging)
        public Transform LeftFistMesh;
        public Transform Weapon;         // null when built without a weapon
        public Transform WeaponTip;
        public Renderer[] BodyRenderers = new Renderer[0];
        public Renderer[] LimbRenderers = new Renderer[0];
        public Renderer[] FistRenderers = new Renderer[0];

        public bool IsValid =>
            Visual != null && Torso != null && RightFist != null && LeftFist != null &&
            RightFoot != null && LeftFoot != null && RightFistMesh != null && LeftFistMesh != null;

        public static GreyboxRigParts Create(Transform owner, bool withWeapon, Material bodyMaterial, Material limbMaterial)
        {
            var parts = new GreyboxRigParts();
            var body = new List<Renderer>();
            var limbs = new List<Renderer>();

            parts.Visual = GreyboxShapes.CreatePivot(VisualName, owner, Vector3.zero);
            parts.Torso = GreyboxShapes.CreatePivot("Torso", parts.Visual, TorsoPivot);
            // Capsule from y 0.33 to 1.57, flattened front-to-back so the facing direction reads.
            body.Add(AddMesh("Body", PrimitiveType.Capsule, parts.Torso, new Vector3(0f, 0.05f, 0f), new Vector3(0.56f, 0.62f, 0.4f), bodyMaterial, true));

            parts.Head = GreyboxShapes.CreatePivot("Head", parts.Torso, new Vector3(0f, 0.75f, 0f));
            body.Add(AddMesh("HeadMesh", PrimitiveType.Sphere, parts.Head, Vector3.zero, new Vector3(0.3f, 0.3f, 0.3f), bodyMaterial, true));
            // A dark visor shows at a glance which way a fighter faces (and when an enemy stops turning).
            limbs.Add(AddMesh("Visor", PrimitiveType.Cube, parts.Head, new Vector3(0f, 0.02f, 0.13f), new Vector3(0.2f, 0.06f, 0.06f), limbMaterial, false));
            parts.Chest = GreyboxShapes.CreatePivot("Chest", parts.Torso, new Vector3(0f, 0.42f, 0.22f));

            parts.RightFist = GreyboxShapes.CreatePivot("RightFist", parts.Torso, RightFistRest);
            Renderer rightFist = AddMesh("RightFistMesh", PrimitiveType.Sphere, parts.RightFist, Vector3.zero, FistScale, limbMaterial, true);
            parts.RightFistMesh = rightFist.transform;
            parts.LeftFist = GreyboxShapes.CreatePivot("LeftFist", parts.Torso, LeftFistRest);
            Renderer leftFist = AddMesh("LeftFistMesh", PrimitiveType.Sphere, parts.LeftFist, Vector3.zero, FistScale, limbMaterial, true);
            parts.LeftFistMesh = leftFist.transform;
            limbs.Add(rightFist);
            limbs.Add(leftFist);
            parts.FistRenderers = new[] { rightFist, leftFist };

            parts.RightFoot = GreyboxShapes.CreatePivot("RightFoot", parts.Visual, RightFootRest);
            limbs.Add(AddMesh("RightFootMesh", PrimitiveType.Cube, parts.RightFoot, new Vector3(0f, 0.05f, 0.04f), new Vector3(0.14f, 0.1f, 0.28f), limbMaterial, true));
            parts.LeftFoot = GreyboxShapes.CreatePivot("LeftFoot", parts.Visual, LeftFootRest);
            limbs.Add(AddMesh("LeftFootMesh", PrimitiveType.Cube, parts.LeftFoot, new Vector3(0f, 0.05f, 0.04f), new Vector3(0.14f, 0.1f, 0.28f), limbMaterial, true));

            if (withWeapon)
            {
                // A dao: straight box blade plus a guard, pointing along the weapon anchor's +Z.
                parts.Weapon = GreyboxShapes.CreatePivot("Weapon", parts.RightFist, Vector3.zero);
                limbs.Add(AddMesh("Blade", PrimitiveType.Cube, parts.Weapon, new Vector3(0f, 0f, 0.47f), new Vector3(0.05f, 0.1f, 0.8f), limbMaterial, true));
                limbs.Add(AddMesh("Guard", PrimitiveType.Cube, parts.Weapon, new Vector3(0f, 0f, 0.07f), new Vector3(0.16f, 0.05f, 0.04f), limbMaterial, true));
                parts.WeaponTip = GreyboxShapes.CreatePivot("WeaponTip", parts.Weapon, new Vector3(0f, 0f, 0.87f));
            }

            parts.BodyRenderers = body.ToArray();
            parts.LimbRenderers = limbs.ToArray();
            return parts;
        }

        static Renderer AddMesh(string name, PrimitiveType type, Transform parent, Vector3 localPosition, Vector3 localScale,
            Material material, bool castShadows)
        {
            GameObject go = GreyboxShapes.CreateVisual(name, type, parent, material, castShadows);
            go.transform.localPosition = localPosition;
            go.transform.localScale = localScale;
            return go.GetComponent<Renderer>();
        }
    }
}
