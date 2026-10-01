using System;
using System.Collections.Generic;
using UnityEngine;
using VaatusRevenge.Core;

namespace VaatusRevenge
{
    // A fighter's body: a real jointed skeleton (bones named exactly like Unity's HumanBodyBones, standing in a
    // T-pose, so Mecanim can build a Humanoid Avatar from it) wearing simple shapes: tapered limbs, a rounded
    // torso and pelvis, a head with a face band (so you can see which way it looks), fists, feet, and the
    // fighter's colours, headgear and weapon (see BodyLook).
    //
    // It doesn't move itself: BodyAnimatorDriver poses the bones every frame from the procedural animator, or a
    // pack-clip player (MecanimPoseSource) takes over by setting ExternalPoseActive. What this component does do
    // is the "look" layer: telegraph glows, hit flashes, the i-frame shimmer, the fa jin charge glow and dimming
    // on stagger and death, all through per-renderer property blocks (materials are never changed at runtime).
    //
    // Build() works in edit mode too: the sandbox builder saves fully built fighters into the scene.
    [DefaultExecutionOrder(50)]   // after the player (0) and enemies (10): shows this frame's glows
    [DisallowMultipleComponent]
    public class HumanoidBody : MonoBehaviour, IHumanoidRig
    {
        public const string RootName = "Body";
        const float TwoPi = Mathf.PI * 2f;

        enum PartGroup { Body, Limb, Fist, Weapon }

        [SerializeField] private BodyLook look = BodyLook.Player();
        [SerializeField] private BodyGlowStyle glow = new BodyGlowStyle();

        [Header("Built by Build (don't edit)")]
        [SerializeField] private Transform root;
        [SerializeField] private Transform[] bones = new Transform[BodyJoints.Count];
        [SerializeField] private Vector3[] restLocal = new Vector3[BodyJoints.Count];
        [SerializeField] private Transform leftFist, rightFist, leftFoot, rightFoot, chestAnchor, headAnchor;
        [SerializeField] private Transform weaponPivot, weaponTip, weaponBlade;
        [SerializeField] private Renderer[] parts = new Renderer[0];
        [SerializeField] private int[] partGroups = new int[0];
        [SerializeField] private Color[] partColors = new Color[0];
        [SerializeField] private Material[] materials = new Material[0];

        MaterialPropertyBlock block;
        BodyAnimatorDriver driver;
        float animTime;
        Color telegraphColor;
        float telegraphIntensity;
        Color flashColor;
        float flashDuration, flashRemaining;
        bool invulnerable, staggered, dead;
        float chargeLevel;
        bool chargeSweetSpot;
        bool coloursDirty = true;
        bool anyOverride;
        Color lastBodyEmission, lastLimbEmission, lastFistExtra;
        float lastDim = 1f;

        static int[] humanToJoint;

        // ---------------------------------------------------------------- IHumanoidRig

        public Transform Root => root;
        public bool IsBuilt => root != null && bones != null && bones.Length == BodyJoints.Count && bones[0] != null;
        public bool ExternalPoseActive { get; set; }
        public AnimationCue CurrentActionCue => Driver != null ? Driver.ActionCue : default;
        public AnimationCue CurrentLocomotionCue => Driver != null ? Driver.LocomotionCue : default;

        public Transform GetBone(HumanBodyBones bone)
        {
            int joint = JointOf(bone);
            return joint >= 0 && IsBuilt ? bones[joint] : null;
        }

        // Every bone's rest rotation is identity: the rest pose is built as a T-pose.
        public Quaternion GetRestLocalRotation(HumanBodyBones bone)
        {
            return Quaternion.identity;
        }

        public Vector3 GetRestLocalPosition(HumanBodyBones bone)
        {
            int joint = JointOf(bone);
            return joint >= 0 && restLocal != null && joint < restLocal.Length ? restLocal[joint] : Vector3.zero;
        }

        // ---------------------------------------------------------------- read-only

        public BodyLook Look => look;
        public BodyGlowStyle Glow => glow;
        public float Scale => look != null ? look.Scale : 1f;
        public string AnimationStyle => look != null ? look.AnimationStyle ?? "" : "";
        public bool IsDead => dead;
        public Transform ChestAnchor => chestAnchor != null ? chestAnchor : transform;
        public Transform HeadAnchor => headAnchor != null ? headAnchor : transform;
        public Transform WeaponPivot => weaponPivot;
        public bool HasWeapon => weaponTip != null;
        // Metres from the grip to the weapon's tip (0 = no weapon); the animator keeps that tip above the floor.
        public float WeaponReach => weaponTip != null ? weaponTip.localPosition.z : 0f;
        public Transform GetBone(BodyJoint joint) => IsBuilt ? bones[(int)joint] : null;

        // The transform a strike comes from (trails, sparks, flames). Never null. BothFists = the right fist;
        // Weapon = the blade tip (or the right fist when unarmed).
        public Transform GetAnchor(Limb limb)
        {
            if (!IsBuilt) return transform;
            switch (limb)
            {
                case Limb.LeftFist: return leftFist;
                case Limb.RightFoot: return rightFoot;
                case Limb.LeftFoot: return leftFoot;
                case Limb.Weapon: return weaponTip != null ? weaponTip : rightFist;
                default: return rightFist;
            }
        }

        BodyAnimatorDriver Driver
        {
            get
            {
                if (driver == null) driver = GetComponent<BodyAnimatorDriver>();
                return driver;
            }
        }

        // ---------------------------------------------------------------- building

        // Creates (or recreates) the skeleton and its shapes under a "Body" child. Safe to call again.
        public void Build(BodyLook newLook)
        {
            look = newLook != null ? newLook.Clone() : BodyLook.Player();
            RemoveOld();
            HumanoidSkeleton skeleton = HumanoidSkeleton.Create(null, look.Scale);
            root = GreyboxShapes.CreatePivot(RootName, transform, Vector3.zero);
            bones = new Transform[BodyJoints.Count];
            restLocal = new Vector3[BodyJoints.Count];
            for (int j = 0; j < BodyJoints.Count; j++)
            {
                int parent = BodyJoints.Parent(j);
                Vector3 offset = skeleton.RestOffset(j).ToUnity();
                restLocal[j] = offset;
                bones[j] = GreyboxShapes.CreatePivot(BodyJoints.Name((BodyJoint)j), parent < 0 ? root : bones[parent], offset);
            }
            var partList = new List<Renderer>();
            var groupList = new List<int>();
            var colorList = new List<Color>();
            var materialByColor = new Dictionary<Color, Material>();
            var madeMaterials = new List<Material>();
            new PartBuilder(this, skeleton, partList, groupList, colorList, materialByColor, madeMaterials).BuildAll();
            parts = partList.ToArray();
            partGroups = groupList.ToArray();
            partColors = colorList.ToArray();
            materials = madeMaterials.ToArray();
            GreyboxShapes.StripColliders(root.gameObject);
            coloursDirty = true;
            ResetLook();
            BodyAnimatorDriver d = Driver;
            if (d != null) d.OnBodyRebuilt();
        }

        // Makes the blade (or stick) this long from the hand to the tip. Safe to call any time.
        public void SetWeaponLength(float length)
        {
            if (weaponBlade == null || weaponTip == null || !(length > 0f)) return;
            if (look != null) look.WeaponLength = length;
            weaponBlade.localScale = new Vector3(1f, 1f, length);
            weaponTip.localPosition = new Vector3(0f, 0f, length);
        }

        // Writes a pose to the bones (the driver calls this every LateUpdate unless an external pose is showing).
        public void ApplyPose(BodyPose pose)
        {
            if (!IsBuilt || pose == null) return;
            for (int j = 0; j < BodyJoints.Count; j++)
            {
                Transform bone = bones[j];
                if (bone == null) continue;
                bone.localPosition = restLocal[j];
                bone.localRotation = ToUnity(pose.Local[j]);
            }
            Transform hips = bones[(int)BodyJoint.Hips];
            hips.localPosition = restLocal[(int)BodyJoint.Hips] + pose.HipsOffset.ToUnity();
            hips.localRotation = Quaternion.Euler(0f, pose.RootYaw, 0f) * ToUnity(pose.Local[(int)BodyJoint.Hips]);
            ApplyProp(pose);
        }

        // Reads what the bones show right now (whoever posed them: us or a pack clip) into a pose buffer. The whole-body
        // yaw is left folded into the hips rotation (RootYaw = 0), which is how BlendBonesFrom expects it.
        public void CaptureBones(BodyPose into)
        {
            if (!IsBuilt || into == null) return;
            for (int j = 0; j < BodyJoints.Count; j++)
            {
                Transform bone = bones[j];
                if (bone == null) continue;
                Quaternion q = bone.localRotation;
                into.Local[j] = new System.Numerics.Quaternion(q.x, q.y, q.z, q.w);
            }
            Transform hips = bones[(int)BodyJoint.Hips];
            into.HipsOffset = hips != null ? (hips.localPosition - restLocal[(int)BodyJoint.Hips]).ToNumerics() : System.Numerics.Vector3.Zero;
            into.RootYaw = 0f;
            if (weaponPivot != null)
            {
                Quaternion p = weaponPivot.localRotation;
                into.PropLocal = new System.Numerics.Quaternion(p.x, p.y, p.z, p.w);
            }
        }

        // Blends the bones from a captured pose (weight 0) to what they show now (weight 1). Used to fade between a
        // pack clip and the procedural animator instead of cutting: the new source poses the bones as usual, then
        // this pulls them back toward where the old one left them, less each frame.
        public void BlendBonesFrom(BodyPose from, float weight)
        {
            if (!IsBuilt || from == null || weight >= 1f) return;
            weight = Mathf.Clamp01(weight);
            for (int j = 0; j < BodyJoints.Count; j++)
            {
                Transform bone = bones[j];
                if (bone == null) continue;
                bone.localRotation = Quaternion.Slerp(ToUnity(from.Local[j]), bone.localRotation, weight);
            }
            Transform hips = bones[(int)BodyJoint.Hips];
            if (hips != null) hips.localPosition = Vector3.Lerp(restLocal[(int)BodyJoint.Hips] + from.HipsOffset.ToUnity(), hips.localPosition, weight);
            if (weaponPivot != null) weaponPivot.localRotation = Quaternion.Slerp(ToUnity(from.PropLocal), weaponPivot.localRotation, weight);
        }

        // The held weapon's angle in the hand (also while a pack clip drives the bones).
        public void ApplyProp(BodyPose pose)
        {
            if (weaponPivot != null && pose != null) weaponPivot.localRotation = ToUnity(pose.PropLocal);
        }

        public static Quaternion ToUnity(System.Numerics.Quaternion q)
        {
            return new Quaternion(q.X, q.Y, q.Z, q.W);
        }

        void RemoveOld()
        {
            for (int i = transform.childCount - 1; i >= 0; i--)
            {
                Transform child = transform.GetChild(i);
                if (child.name != RootName) continue;
                child.gameObject.SetActive(false);   // Destroy is deferred in play mode: hide it now
                GreyboxShapes.SafeDestroy(child.gameObject);
            }
            if (materials != null)
            {
                for (int i = 0; i < materials.Length; i++) GreyboxShapes.SafeDestroy(materials[i]);
            }
            materials = new Material[0];
            parts = new Renderer[0];
            partGroups = new int[0];
            partColors = new Color[0];
            root = null;
            weaponPivot = weaponTip = weaponBlade = null;
        }

        // ---------------------------------------------------------------- the look layer (glows and flashes)

        // Steady glow while an attack winds up (0 = off). Hands, feet and weapon glow fully, the torso partly.
        public void SetTelegraph(Color color, float intensity)
        {
            intensity = Mathf.Max(0f, intensity);
            if (color == telegraphColor && intensity == telegraphIntensity) return;
            telegraphColor = color;
            telegraphIntensity = intensity;
            coloursDirty = true;
        }

        // A burst of light that fades out (hit taken, deflect, sweet spot).
        public void Flash(Color color, float duration)
        {
            if (!(duration > 0f)) return;
            flashColor = color;
            flashDuration = duration;
            flashRemaining = duration;
            coloursDirty = true;
        }

        // Cool shimmer while invincible (dodge i-frames).
        public void SetInvulnerableLook(bool on)
        {
            if (on == invulnerable) return;
            invulnerable = on;
            coloursDirty = true;
        }

        public void SetStaggered(bool on)
        {
            if (on == staggered) return;
            staggered = on;
            if (on) ClearCharge();
            coloursDirty = true;
        }

        public void SetDead(bool on)
        {
            if (on == dead) return;
            dead = on;
            if (on)
            {
                telegraphIntensity = 0f;
                ClearCharge();
            }
            coloursDirty = true;
        }

        // Fa jin charge: the fists glow brighter with level01; in the sweet spot they burn white-gold, and
        // entering it flashes the whole body (the release cue).
        public void SetCharge(float level01, bool sweetSpot)
        {
            if (dead) return;
            if (sweetSpot && !chargeSweetSpot) Flash(glow.SweetSpotColor, glow.SweetSpotFlashTime);
            level01 = Mathf.Clamp01(level01);
            if (level01 == chargeLevel && sweetSpot == chargeSweetSpot) return;
            chargeLevel = level01;
            chargeSweetSpot = sweetSpot;
            coloursDirty = true;
        }

        // Every glow off, alive again (respawn, reset).
        public void ResetLook()
        {
            telegraphIntensity = 0f;
            flashRemaining = 0f;
            invulnerable = false;
            staggered = false;
            dead = false;
            ClearCharge();
            coloursDirty = true;
            ApplyColours();
        }

        void ClearCharge()
        {
            chargeLevel = 0f;
            chargeSweetSpot = false;
        }

        void Update()
        {
            float dt = Time.deltaTime;
            if (dt > 0f)
            {
                animTime += dt;
                if (flashRemaining > 0f)
                {
                    flashRemaining = Mathf.Max(0f, flashRemaining - dt);
                    coloursDirty = true;
                }
                if (invulnerable || chargeSweetSpot) coloursDirty = true;   // pulsing
            }
            if (coloursDirty) ApplyColours();
        }

        void ApplyColours()
        {
            coloursDirty = false;
            if (parts == null) return;
            float dim = 1f;
            if (staggered) dim *= glow.StaggerDim;
            if (dead) dim *= glow.DeadDim;
            Color body = Color.black, limb = Color.black, fist = Color.black;
            if (!dead)
            {
                if (telegraphIntensity > 0f)
                {
                    Color g = telegraphColor * telegraphIntensity;
                    limb += g;
                    body += g * glow.TelegraphBodyShare;
                }
                if (invulnerable)
                {
                    float pulse = 0.5f + 0.5f * Mathf.Sin(animTime * glow.InvulnerablePulseRate * TwoPi);
                    Color g = glow.InvulnerableTint * (glow.InvulnerableGlow * pulse);
                    body += g;
                    limb += g;
                }
                if (chargeSweetSpot)
                {
                    float pulse = 0.75f + 0.25f * Mathf.Sin(animTime * glow.SweetSpotPulseRate * TwoPi);
                    fist = glow.SweetSpotColor * (glow.SweetSpotIntensity * pulse);
                }
                else if (chargeLevel > 0f)
                {
                    fist = glow.ChargeColor * (glow.ChargeIntensity * chargeLevel);
                }
            }
            if (flashRemaining > 0f && flashDuration > 0f)
            {
                float k = flashRemaining / flashDuration;
                Color g = flashColor * (glow.FlashIntensity * k * k);
                body += g;
                limb += g;
            }

            bool neutral = dim >= 0.999f && Max(body) <= 0f && Max(limb) <= 0f && Max(fist) <= 0f;
            if (neutral && !anyOverride) return;
            if (!neutral && body == lastBodyEmission && limb == lastLimbEmission && fist == lastFistExtra && dim == lastDim && anyOverride) return;
            if (block == null) block = new MaterialPropertyBlock();
            for (int i = 0; i < parts.Length; i++)
            {
                Renderer r = parts[i];
                if (r == null) continue;
                if (neutral)
                {
                    r.SetPropertyBlock(null);
                    continue;
                }
                var group = (PartGroup)(i < partGroups.Length ? partGroups[i] : 0);
                Color emission = group == PartGroup.Body ? body : limb;
                if (group == PartGroup.Fist) emission += fist;
                Color baseColor = (i < partColors.Length ? partColors[i] : Color.white) * dim;
                baseColor.a = 1f;
                emission.r = Mathf.Max(emission.r, GreyboxShapes.EmissionFloor);
                emission.g = Mathf.Max(emission.g, GreyboxShapes.EmissionFloor);
                emission.b = Mathf.Max(emission.b, GreyboxShapes.EmissionFloor);
                emission.a = 1f;
                block.SetColor(GreyboxShapes.BaseColorId, baseColor);
                block.SetColor(GreyboxShapes.EmissionColorId, emission);
                r.SetPropertyBlock(block);
            }
            anyOverride = !neutral;
            lastBodyEmission = body;
            lastLimbEmission = limb;
            lastFistExtra = fist;
            lastDim = dim;
        }

        static float Max(Color c)
        {
            return Mathf.Max(c.r, Mathf.Max(c.g, c.b));
        }

        void OnDestroy()
        {
            // Materials made in play mode must be freed by hand; edit-mode ones are part of the saved scene.
            if (!Application.isPlaying) return;
            for (int i = 0; materials != null && i < materials.Length; i++)
            {
                if (materials[i] != null && materials[i].name.EndsWith(RuntimeSuffix, StringComparison.Ordinal)) Destroy(materials[i]);
            }
        }

        const string RuntimeSuffix = " (runtime)";

        // ---------------------------------------------------------------- HumanBodyBones <-> BodyJoint

        static int JointOf(HumanBodyBones bone)
        {
            if (humanToJoint == null)
            {
                var map = new int[(int)HumanBodyBones.LastBone];
                for (int i = 0; i < map.Length; i++) map[i] = -1;
                for (int j = 0; j < BodyJoints.Count; j++)
                {
                    if (Enum.TryParse(BodyJoints.Name((BodyJoint)j), out HumanBodyBones human)) map[(int)human] = j;
                }
                humanToJoint = map;
            }
            int index = (int)bone;
            return index >= 0 && index < humanToJoint.Length ? humanToJoint[index] : -1;
        }

        // Domain reload is off: rebuild the lookup table each Play session (it never changes, but stay tidy).
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics()
        {
            humanToJoint = null;
        }

        // ---------------------------------------------------------------- the shapes

        // Builds the visible shapes on the bones. Sizes are for a 1.8 m fighter and scale with the look.
        sealed class PartBuilder
        {
            readonly HumanoidBody body;
            readonly HumanoidSkeleton skeleton;
            readonly BodyLook look;
            readonly List<Renderer> parts;
            readonly List<int> groups;
            readonly List<Color> colors;
            readonly Dictionary<Color, Material> materialByColor;
            readonly List<Material> made;
            readonly float k;   // size scale
            readonly float b;   // limb thickness

            public PartBuilder(HumanoidBody body, HumanoidSkeleton skeleton, List<Renderer> parts, List<int> groups, List<Color> colors,
                Dictionary<Color, Material> materialByColor, List<Material> made)
            {
                this.body = body;
                this.skeleton = skeleton;
                look = body.look;
                this.parts = parts;
                this.groups = groups;
                this.colors = colors;
                this.materialByColor = materialByColor;
                this.made = made;
                k = look.Scale;
                b = look.Scale * Mathf.Max(0.5f, look.Build);
            }

            Transform Bone(BodyJoint j) => body.bones[(int)j];

            public void BuildAll()
            {
                bool tunic = look.Outfit == BodyOutfit.CrossCollar;

                // Torso: pelvis with a sash, belly, chest, shoulders.
                Box("Pelvis", BodyJoint.Hips, new Vector3(0.33f * b, 0.22f * k, 0.22f * b), new Vector3(0f, -0.03f * k, 0f), look.Pants, PartGroup.Body, 0.4f);
                if (tunic) BuildSashAndHem();
                else Box("Sash", BodyJoint.Hips, new Vector3(0.35f * b, 0.075f * k, 0.235f * b), new Vector3(0f, 0.06f * k, 0f), look.Trim, PartGroup.Limb, 0.35f);
                Box("Belly", BodyJoint.Spine, new Vector3(0.29f * b, 0.18f * k, 0.19f * b), new Vector3(0f, 0.07f * k, 0f), look.Cloth, PartGroup.Body, 0.45f);
                Box("ChestShape", BodyJoint.Chest, new Vector3(0.35f * b, 0.2f * k, 0.22f * b), new Vector3(0f, 0.08f * k, 0.005f), look.Cloth, PartGroup.Body, 0.45f);
                Box("Shoulders", BodyJoint.UpperChest, new Vector3(0.41f * b, 0.15f * k, 0.21f * b), new Vector3(0f, 0.05f * k, -0.005f), look.Cloth, PartGroup.Body, 0.5f);
                if (tunic) BuildCollar();
                if (look.Armour)
                {
                    Box("Breastplate", BodyJoint.Chest, new Vector3(0.37f * b, 0.24f * k, 0.24f * b), new Vector3(0f, 0.1f * k, 0.01f), look.ArmourColor, PartGroup.Body, 0.3f);
                }

                // Neck and head, with a face band so the facing reads from any angle.
                Capsule("NeckShape", BodyJoint.Neck, Vector3.up, 0.052f * b, 0.048f * b, skeleton.RestOffset(BodyJoint.Head).Y, look.Skin, PartGroup.Body);
                Box("Skull", BodyJoint.Head, new Vector3(0.19f * k, 0.23f * k, 0.21f * k), new Vector3(0f, 0.11f * k, 0.01f * k), look.Skin, PartGroup.Body, 1f);
                Box("FaceBand", BodyJoint.Head, new Vector3(0.16f * k, 0.035f * k, 0.05f * k), new Vector3(0f, 0.13f * k, 0.1f * k), look.Dark, PartGroup.Body, 0.4f);
                switch (look.Headgear)
                {
                    case BodyHeadgear.Topknot:
                        Box("Hair", BodyJoint.Head, new Vector3(0.2f * k, 0.11f * k, 0.22f * k), new Vector3(0f, 0.19f * k, -0.01f * k), look.Dark, PartGroup.Body, 0.9f);
                        if (tunic) BuildTiedTopknot();
                        else Box("Topknot", BodyJoint.Head, new Vector3(0.07f * k, 0.08f * k, 0.07f * k), new Vector3(0f, 0.26f * k, -0.03f * k), look.Dark, PartGroup.Body, 1f);
                        break;
                    case BodyHeadgear.Helmet:
                        Box("Helmet", BodyJoint.Head, new Vector3(0.24f * k, 0.15f * k, 0.25f * k), new Vector3(0f, 0.2f * k, -0.005f * k), look.ArmourColor, PartGroup.Body, 0.7f);
                        Box("HelmetCrest", BodyJoint.Head, new Vector3(0.035f * k, 0.07f * k, 0.2f * k), new Vector3(0f, 0.29f * k, -0.01f * k), look.Trim, PartGroup.Body, 0.6f);
                        break;
                    case BodyHeadgear.Hood:
                        Box("Hood", BodyJoint.Head, new Vector3(0.23f * k, 0.25f * k, 0.22f * k), new Vector3(0f, 0.14f * k, -0.035f * k), look.ArmourColor, PartGroup.Body, 0.85f);
                        break;
                }

                for (int s = 0; s < 2; s++)
                {
                    BodySide side = s == 0 ? BodySide.Left : BodySide.Right;
                    float sign = (float)side;
                    string n = side == BodySide.Left ? "Left" : "Right";
                    Vector3 armAxis = new Vector3(sign, 0f, 0f);
                    Capsule(n + "Collar", BodyJoints.Shoulder(side), armAxis, 0.055f * b, 0.06f * b, skeleton.RestOffset(BodyJoints.UpperArm(side)).Length(), look.Cloth, PartGroup.Body);
                    if (look.Armour)
                        Box(n + "Pauldron", BodyJoints.Shoulder(side), new Vector3(0.15f * b, 0.07f * k, 0.16f * b), new Vector3(sign * 0.14f * k, 0.035f * k, 0f), look.ArmourColor, PartGroup.Body, 0.4f);
                    if (tunic)
                    {
                        // sleeveless: bare upper arm and forearm, a leather bracer from mid-forearm to the wrist
                        Capsule(n + "UpperArm", BodyJoints.UpperArm(side), armAxis, 0.056f * b, 0.046f * b, skeleton.UpperArmLength, look.Skin, PartGroup.Body);
                        Capsule(n + "Forearm", BodyJoints.LowerArm(side), armAxis, 0.046f * b, 0.036f * b, skeleton.ForearmLength, look.Skin, PartGroup.Limb);
                        BuildBracer(n, side, armAxis);
                    }
                    else
                    {
                        Capsule(n + "Sleeve", BodyJoints.UpperArm(side), armAxis, 0.058f * b, 0.047f * b, skeleton.UpperArmLength, look.Cloth, PartGroup.Body);
                        Capsule(n + "Forearm", BodyJoints.LowerArm(side), armAxis, 0.047f * b, 0.037f * b, skeleton.ForearmLength, look.Trim, PartGroup.Limb);
                    }
                    Box(n + "FistShape", BodyJoints.Hand(side), new Vector3(0.1f * k, 0.085f * k, 0.095f * k), armAxis * (0.055f * k), look.Skin, PartGroup.Fist, 0.4f);
                    Transform fist = GreyboxShapes.CreatePivot(n + "FistAnchor", Bone(BodyJoints.Hand(side)), armAxis * (0.08f * k));
                    if (side == BodySide.Left) body.leftFist = fist;
                    else body.rightFist = fist;

                    Vector3 down = Vector3.down;
                    if (tunic)
                    {
                        // baggy trousers billowing to the calf, gathered there into the shin wraps
                        Capsule(n + "Thigh", BodyJoints.UpperLeg(side), down, 0.097f * b, 0.08f * b, skeleton.ThighLength, look.Pants, PartGroup.Body);
                        Segment(n + "TrouserCuff", BodyJoints.LowerLeg(side), down, 0f, 0.42f * skeleton.ShinLength, 0.08f * b, 0.062f * b, look.Pants, PartGroup.Body);
                        Segment(n + "ShinWraps", BodyJoints.LowerLeg(side), down, 0.36f * skeleton.ShinLength, 0.97f * skeleton.ShinLength, 0.06f * b, 0.046f * b,
                            look.Wraps, PartGroup.Limb);
                    }
                    else
                    {
                        Capsule(n + "Thigh", BodyJoints.UpperLeg(side), down, 0.085f * b, 0.062f * b, skeleton.ThighLength, look.Pants, PartGroup.Body);
                        Capsule(n + "Shin", BodyJoints.LowerLeg(side), down, 0.062f * b, 0.046f * b, skeleton.ShinLength, look.Trim, PartGroup.Limb);
                    }
                    Box(n + "Shoe", BodyJoints.Foot(side), new Vector3(0.1f * k, 0.07f * k, 0.25f * k), new Vector3(0f, -0.045f * k, 0.06f * k),
                        tunic ? look.Shoes : look.Dark, PartGroup.Limb, 0.35f);
                    Transform foot = GreyboxShapes.CreatePivot(n + "FootAnchor", Bone(BodyJoints.Foot(side)), new Vector3(0f, -0.05f * k, 0.14f * k));
                    if (side == BodySide.Left) body.leftFoot = foot;
                    else body.rightFoot = foot;
                }

                body.chestAnchor = GreyboxShapes.CreatePivot("ChestAnchor", Bone(BodyJoint.UpperChest), new Vector3(0f, 0.02f * k, 0.14f * k));
                body.headAnchor = GreyboxShapes.CreatePivot("HeadAnchor", Bone(BodyJoint.Head), new Vector3(0f, 0.11f * k, 0f));
                BuildWeapon();
            }

            // ---- The Avatar's outfit (BodyOutfit.CrossCollar) ----

            // The wide sash with gold-trimmed edges, its knot and tail hanging at the left hip, and the tunic's skirt below it
            // with a gold hem.
            void BuildSashAndHem()
            {
                Box("TunicSkirt", BodyJoint.Hips, new Vector3(0.35f * b, 0.13f * k, 0.235f * b), new Vector3(0f, -0.035f * k, 0f), look.Cloth, PartGroup.Body, 0.4f);
                Box("TunicHem", BodyJoint.Hips, new Vector3(0.356f * b, 0.018f * k, 0.24f * b), new Vector3(0f, -0.1f * k, 0f), look.SashTrim, PartGroup.Body, 0.3f);
                Box("Sash", BodyJoint.Hips, new Vector3(0.355f * b, 0.1f * k, 0.24f * b), new Vector3(0f, 0.07f * k, 0f), look.Sash, PartGroup.Limb, 0.35f);
                Box("SashTrimTop", BodyJoint.Hips, new Vector3(0.36f * b, 0.016f * k, 0.245f * b), new Vector3(0f, 0.12f * k, 0f), look.SashTrim, PartGroup.Limb, 0.3f);
                Box("SashTrimBottom", BodyJoint.Hips, new Vector3(0.36f * b, 0.016f * k, 0.245f * b), new Vector3(0f, 0.02f * k, 0f), look.SashTrim, PartGroup.Limb, 0.3f);
                float left = (float)BodySide.Left;
                Box("SashKnot", BodyJoint.Hips, new Vector3(0.07f * k, 0.07f * k, 0.04f * k), new Vector3(left * 0.09f * b, 0.07f * k, 0.125f * b), look.Sash, PartGroup.Limb, 0.6f);
                Box("SashTail", BodyJoint.Hips, new Vector3(0.075f * k, 0.3f * k, 0.02f * k), new Vector3(left * 0.115f * b, -0.1f * k, 0.125f * b), look.Sash, PartGroup.Limb, 0.25f);
                Box("SashTailTrim", BodyJoint.Hips, new Vector3(0.08f * k, 0.016f * k, 0.024f * k), new Vector3(left * 0.115f * b, -0.245f * k, 0.125f * b), look.SashTrim, PartGroup.Limb, 0.3f);
            }

            // The cross collar: the undershirt showing in a V down the chest, the two lapels edging it (left over right), and
            // the pendant at the throat on its cord.
            void BuildCollar()
            {
                Box("Undershirt", BodyJoint.Chest, new Vector3(0.11f * b, 0.17f * k, 0.016f * k), new Vector3(0f, 0.1f * k, 0.116f * b), look.Undershirt, PartGroup.Body, 0.3f);
                Box("UndershirtNeck", BodyJoint.UpperChest, new Vector3(0.12f * b, 0.09f * k, 0.016f * k), new Vector3(0f, 0.05f * k, 0.105f * b), look.Undershirt, PartGroup.Body, 0.3f);
                Mesh lapel = BodyMeshes.RoundedBox(new Vector3(0.035f * b, 0.24f * k, 0.018f * k), 0.3f);
                Add("LeftLapel", Bone(BodyJoint.Chest), lapel, new Vector3(-0.045f * b, 0.11f * k, 0.12f * b), Quaternion.Euler(0f, 0f, 24f), look.TunicEdge, PartGroup.Body);
                Add("RightLapel", Bone(BodyJoint.Chest), lapel, new Vector3(0.045f * b, 0.11f * k, 0.121f * b), Quaternion.Euler(0f, 0f, -24f), look.TunicEdge, PartGroup.Body);
                Box("PendantCord", BodyJoint.UpperChest, new Vector3(0.008f * k, 0.06f * k, 0.008f * k), new Vector3(0f, 0.04f * k, 0.112f * b), look.Dark, PartGroup.Body, 0.5f);
                Box("Pendant", BodyJoint.Chest, new Vector3(0.042f * k, 0.048f * k, 0.014f * k), new Vector3(0f, 0.165f * k, 0.126f * b), look.Pendant, PartGroup.Body, 0.7f);
            }

            // A leather bracer from mid-forearm to the wrist, with two straps round it.
            void BuildBracer(string n, BodySide side, Vector3 armAxis)
            {
                float forearm = skeleton.ForearmLength;
                Segment(n + "Bracer", BodyJoints.LowerArm(side), armAxis, 0.38f * forearm, 0.97f * forearm, 0.052f * b, 0.046f * b, look.Bracers, PartGroup.Limb);
                Segment(n + "BracerStrapUpper", BodyJoints.LowerArm(side), armAxis, 0.5f * forearm, 0.56f * forearm, 0.055f * b, 0.054f * b, look.BracerStraps, PartGroup.Limb);
                Segment(n + "BracerStrapLower", BodyJoints.LowerArm(side), armAxis, 0.78f * forearm, 0.84f * forearm, 0.051f * b, 0.05f * b, look.BracerStraps, PartGroup.Limb);
            }

            // Black hair gathered into a high topknot, tied in dark red with the tie's ribbon hanging down the back.
            void BuildTiedTopknot()
            {
                Box("HairBack", BodyJoint.Head, new Vector3(0.19f * k, 0.15f * k, 0.07f * k), new Vector3(0f, 0.12f * k, -0.08f * k), look.Dark, PartGroup.Body, 0.8f);
                Box("Topknot", BodyJoint.Head, new Vector3(0.08f * k, 0.085f * k, 0.08f * k), new Vector3(0f, 0.3f * k, -0.035f * k), look.Dark, PartGroup.Body, 1f);
                Box("HairTie", BodyJoint.Head, new Vector3(0.086f * k, 0.024f * k, 0.086f * k), new Vector3(0f, 0.258f * k, -0.035f * k), look.HairTie, PartGroup.Body, 0.6f);
                Box("HairRibbon", BodyJoint.Head, new Vector3(0.03f * k, 0.12f * k, 0.012f * k), new Vector3(0.015f * k, 0.19f * k, -0.122f * k), look.HairTie, PartGroup.Body, 0.3f);
            }

            void BuildWeapon()
            {
                if (look.Weapon == BodyWeapon.None) return;
                Transform hand = Bone(BodyJoint.RightHand);
                body.weaponPivot = GreyboxShapes.CreatePivot("WeaponPivot", hand, new Vector3(skeleton.HandLength * 0.6f, 0f, 0f));
                float length = Mathf.Max(0.1f, look.WeaponLength);
                switch (look.Weapon)
                {
                    case BodyWeapon.Dao:
                        Box("Grip", body.weaponPivot, new Vector3(0.035f, 0.035f, 0.2f), new Vector3(0f, 0f, 0f), look.Dark, PartGroup.Weapon, 0.6f);
                        Box("Guard", body.weaponPivot, new Vector3(0.1f, 0.03f, 0.03f), new Vector3(0f, 0f, 0.1f), look.Trim, PartGroup.Weapon, 0.5f);
                        body.weaponBlade = GreyboxShapes.CreatePivot("Blade", body.weaponPivot, Vector3.zero);
                        // A dao: a single-edged, slightly widening blade, unit length (scaled to WeaponLength).
                        Box("BladeShape", body.weaponBlade, new Vector3(0.018f, 0.075f, 0.9f), new Vector3(0f, 0.012f, 0.56f), look.WeaponColor, PartGroup.Weapon, 0.25f);
                        body.weaponTip = GreyboxShapes.CreatePivot("WeaponTip", body.weaponPivot, new Vector3(0f, 0f, length));
                        body.SetWeaponLength(length);
                        break;
                    case BodyWeapon.PracticeStick:
                        body.weaponBlade = GreyboxShapes.CreatePivot("Blade", body.weaponPivot, Vector3.zero);
                        Box("Stick", body.weaponBlade, new Vector3(0.04f, 0.04f, 1.05f), new Vector3(0f, 0f, 0.45f), look.WeaponColor, PartGroup.Weapon, 0.9f);
                        body.weaponTip = GreyboxShapes.CreatePivot("WeaponTip", body.weaponPivot, new Vector3(0f, 0f, length));
                        body.SetWeaponLength(length);
                        break;
                    case BodyWeapon.Crossbow:
                        Box("Stock", body.weaponPivot, new Vector3(0.05f, 0.07f, 0.55f), new Vector3(0f, -0.01f, 0.12f), look.WeaponColor, PartGroup.Weapon, 0.4f);
                        Box("Magazine", body.weaponPivot, new Vector3(0.06f, 0.09f, 0.2f), new Vector3(0f, 0.075f, 0.13f), look.WeaponColor, PartGroup.Weapon, 0.4f);
                        Box("Prod", body.weaponPivot, new Vector3(0.62f, 0.035f, 0.045f), new Vector3(0f, 0f, 0.35f), look.Dark, PartGroup.Weapon, 0.5f);
                        body.weaponTip = GreyboxShapes.CreatePivot("WeaponTip", body.weaponPivot, new Vector3(0f, 0f, 0.42f));
                        break;
                }
            }

            Renderer Box(string name, BodyJoint joint, Vector3 size, Vector3 offset, Color color, PartGroup group, float roundness)
            {
                return Box(name, Bone(joint), size, offset, color, group, roundness);
            }

            Renderer Box(string name, Transform parent, Vector3 size, Vector3 offset, Color color, PartGroup group, float roundness)
            {
                return Add(name, parent, BodyMeshes.RoundedBox(size, roundness), offset, Quaternion.identity, color, group);
            }

            // A limb segment from the joint along 'axis' (the bone's rest direction), tapering from r0 to r1.
            void Capsule(string name, BodyJoint joint, Vector3 axis, float r0, float r1, float length, Color color, PartGroup group)
            {
                Add(name, Bone(joint), BodyMeshes.TaperedCapsule(r0, r1, length), Vector3.zero, Quaternion.FromToRotation(Vector3.up, axis), color, group);
            }

            // Part of a limb: a tapered capsule along 'axis' from 'from' to 'to' metres past the joint (a bracer, a cuff, wraps).
            void Segment(string name, BodyJoint joint, Vector3 axis, float from, float to, float r0, float r1, Color color, PartGroup group)
            {
                Add(name, Bone(joint), BodyMeshes.TaperedCapsule(r0, r1, Mathf.Max(0.001f, to - from)), axis * from, Quaternion.FromToRotation(Vector3.up, axis),
                    color, group);
            }

            Renderer Add(string name, Transform parent, Mesh mesh, Vector3 position, Quaternion rotation, Color color, PartGroup group)
            {
                GameObject go = GreyboxShapes.CreateVisual(name + "Mesh", mesh, parent, MaterialFor(color), true);
                go.transform.localPosition = position;
                go.transform.localRotation = rotation;
                Renderer r = go.GetComponent<Renderer>();
                parts.Add(r);
                groups.Add((int)group);
                colors.Add(color);
                return r;
            }

            Material MaterialFor(Color color)
            {
                if (materialByColor.TryGetValue(color, out Material m)) return m;
                string suffix = Application.isPlaying ? RuntimeSuffix : "";
                m = GreyboxShapes.CreateLit(look.Name + " body " + made.Count + suffix, color, true);
                if (m != null && m.HasProperty("_Smoothness")) m.SetFloat("_Smoothness", 0.35f);
                materialByColor[color] = m;
                made.Add(m);
                return m;
            }
        }
    }
}
