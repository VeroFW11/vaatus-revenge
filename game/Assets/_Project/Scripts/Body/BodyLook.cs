using System;
using UnityEngine;

namespace VaatusRevenge
{
    // What a fighter's grey-box body looks like: its colours, headgear and what it carries. Purely visual.
    // Presets for each fighter live here (data), so a new enemy type is a new preset, not new code.
    public enum BodyWeapon { None, Dao, PracticeStick, Crossbow }
    public enum BodyHeadgear { Topknot, Helmet, Hood, None }

    // How the clothes are cut. Plain: a tunic with sleeves, trousers, wraps and a sash in Trim (every enemy). CrossCollar:
    // the Avatar's outfit from the chosen character sheet (docs/Art/Character-Sheets/Player-Avatar-Style-B-chosen.webp): a
    // sleeveless cross-collar tunic over an undershirt, bare arms with leather bracers, a wide sash with trimmed edges and
    // a hanging tail, baggy trousers gathered at the calves, shin wraps, cloth shoes, a tied topknot and a pendant.
    public enum BodyOutfit { Plain, CrossCollar }

    [Serializable]
    public class BodyLook
    {
        public string Name = "Fighter";
        [Tooltip("Tunic / jacket.")]
        public Color Cloth = new Color(0.62f, 0.12f, 0.08f);
        [Tooltip("Trousers.")]
        public Color Pants = new Color(0.28f, 0.08f, 0.06f);
        [Tooltip("Sash, arm and shin wraps: the accent colour.")]
        public Color Trim = new Color(0.95f, 0.68f, 0.16f);
        public Color Skin = new Color(0.86f, 0.64f, 0.46f);
        [Tooltip("Hair, belt and the face band that shows which way the fighter looks.")]
        public Color Dark = new Color(0.12f, 0.07f, 0.05f);
        [Tooltip("Shoulder guards and helmet (soldiers).")]
        public bool Armour;
        public Color ArmourColor = new Color(0.35f, 0.07f, 0.06f);
        public BodyHeadgear Headgear = BodyHeadgear.Topknot;
        public BodyWeapon Weapon = BodyWeapon.None;
        public Color WeaponColor = new Color(0.78f, 0.8f, 0.84f);
        [Tooltip("Hand to tip, metres (dao blade or practice stick). The soldier's blade must show its attacks' reach.")]
        public float WeaponLength = 1.3f;
        [Tooltip("Body size: 1 = a 1.8 m adult. Soldiers are a touch bigger.")]
        public float Scale = 1f;
        [Tooltip("Limb thickness multiplier (a stocky soldier, a slim crossbowman).")]
        public float Build = 1f;
        [Tooltip("Animation style: which clip variants the body uses ('' = unarmed, 'sword', 'crossbow', 'dummy').")]
        public string AnimationStyle = "";

        [Header("Outfit (CrossCollar only)")]
        public BodyOutfit Outfit = BodyOutfit.Plain;
        [Tooltip("The undershirt showing in the tunic's V (charcoal).")]
        public Color Undershirt = new Color(0.16f, 0.15f, 0.16f);
        [Tooltip("The tunic's collar and hem edging (a darker shade of the tunic).")]
        public Color TunicEdge = new Color(0.3f, 0.06f, 0.09f);
        [Tooltip("The wide sash and its hanging tail.")]
        public Color Sash = new Color(0.08f, 0.075f, 0.075f);
        [Tooltip("The sash's and the hem's trim (gold).")]
        public Color SashTrim = new Color(0.78f, 0.58f, 0.22f);
        [Tooltip("Leather bracers on the forearms.")]
        public Color Bracers = new Color(0.1f, 0.085f, 0.085f);
        [Tooltip("The bracers' straps.")]
        public Color BracerStraps = new Color(0.3f, 0.17f, 0.12f);
        [Tooltip("Cloth wraps on the shins.")]
        public Color Wraps = new Color(0.22f, 0.16f, 0.15f);
        public Color Shoes = new Color(0.07f, 0.065f, 0.065f);
        [Tooltip("The tie round the topknot (and its ribbon).")]
        public Color HairTie = new Color(0.4f, 0.07f, 0.11f);
        [Tooltip("The small pendant on a cord at the throat.")]
        public Color Pendant = new Color(0.72f, 0.42f, 0.2f);

        public BodyLook Clone()
        {
            return (BodyLook)MemberwiseClone();
        }

        // The Avatar, as on the chosen character sheet: a deep crimson sleeveless cross-collar tunic over a charcoal
        // undershirt, bare warm-tan arms with black leather bracers, a wide black sash trimmed in gold with its tail at the
        // left hip, baggy dark maroon trousers gathered at the calves, dark shin wraps, black cloth shoes, black hair in a
        // high topknot tied in dark red, and a small bronze pendant. (PlayerBodySettings.BodyColor tints the tunic.)
        public static BodyLook Player()
        {
            return new BodyLook
            {
                Name = "Avatar", Outfit = BodyOutfit.CrossCollar,
                Cloth = new Color(0.5f, 0.1f, 0.15f), Pants = new Color(0.27f, 0.07f, 0.1f), Trim = new Color(0.78f, 0.58f, 0.22f),
                Skin = new Color(0.78f, 0.53f, 0.36f), Dark = new Color(0.06f, 0.05f, 0.05f)
            };
        }

        // Dao soldier: dark red lacquered armour, helmet, a dao in the right hand.
        public static BodyLook Soldier()
        {
            return new BodyLook
            {
                // Dark iron and slate lacquer with bronze trim: far from the player's crimson in hue, so friend and foe read
                // apart at a glance (verify S-04).
                Name = "Dao Soldier", Cloth = new Color(0.2f, 0.23f, 0.27f), Pants = new Color(0.12f, 0.13f, 0.15f),
                Trim = new Color(0.58f, 0.42f, 0.2f), Skin = new Color(0.78f, 0.58f, 0.44f), Dark = new Color(0.06f, 0.06f, 0.07f),
                Armour = true, ArmourColor = new Color(0.15f, 0.16f, 0.18f), Headgear = BodyHeadgear.Helmet,
                Weapon = BodyWeapon.Dao, WeaponLength = 1.3f, Scale = 1.04f, Build = 1.12f, AnimationStyle = "sword"
            };
        }

        // Crossbowman: lighter leather armour and a hood, a repeating crossbow.
        public static BodyLook Crossbowman()
        {
            return new BodyLook
            {
                Name = "Crossbowman", Cloth = new Color(0.62f, 0.5f, 0.34f), Pants = new Color(0.36f, 0.28f, 0.2f),
                Trim = new Color(0.55f, 0.16f, 0.1f), Skin = new Color(0.8f, 0.6f, 0.45f), Dark = new Color(0.2f, 0.14f, 0.09f),
                Armour = true, ArmourColor = new Color(0.46f, 0.34f, 0.2f), Headgear = BodyHeadgear.Hood,
                Weapon = BodyWeapon.Crossbow, WeaponColor = new Color(0.35f, 0.24f, 0.14f), Scale = 1f, Build = 0.95f,
                AnimationStyle = "crossbow"
            };
        }

        // Sparring dummy: straw body on a wooden frame, holding a practice stick.
        public static BodyLook Dummy()
        {
            return new BodyLook
            {
                Name = "Sparring Dummy", Cloth = new Color(0.82f, 0.68f, 0.4f), Pants = new Color(0.72f, 0.58f, 0.33f),
                Trim = new Color(0.45f, 0.3f, 0.16f), Skin = new Color(0.78f, 0.64f, 0.38f), Dark = new Color(0.36f, 0.24f, 0.12f),
                Headgear = BodyHeadgear.None, Weapon = BodyWeapon.PracticeStick, WeaponColor = new Color(0.45f, 0.3f, 0.15f),
                WeaponLength = 1.1f, Build = 1.2f, AnimationStyle = "dummy"
            };
        }
    }

    // How a body shows what's happening to it: telegraph glows, hit flashes, i-frames, charge, stagger and death.
    [Serializable]
    public class BodyGlowStyle
    {
        [Tooltip("Share of a telegraph glow the torso gets; hands, feet and the weapon always get all of it.")]
        public float TelegraphBodyShare = 0.35f;
        [Tooltip("Brightness of Flash(). Values above 1 bloom.")]
        public float FlashIntensity = 4f;
        public Color InvulnerableTint = new Color(0.75f, 0.9f, 1f);
        public float InvulnerableGlow = 0.6f;
        public float InvulnerablePulseRate = 12f;
        [Range(0f, 1f)] public float StaggerDim = 0.7f;
        [Range(0f, 1f)] public float DeadDim = 0.45f;
        public Color ChargeColor = new Color(1f, 0.45f, 0.1f);
        public float ChargeIntensity = 3f;
        public Color SweetSpotColor = new Color(1f, 0.92f, 0.6f);
        public float SweetSpotIntensity = 7f;
        public float SweetSpotPulseRate = 18f;
        public float SweetSpotFlashTime = 0.12f;
    }
}
