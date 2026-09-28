using UnityEngine;

namespace VaatusRevenge
{
    // How a grey-box fighter looks and moves when it reacts. Purely visual (no gameplay numbers), kept in
    // one place so the feel of telegraphs and hit flashes can be tweaked in the Inspector.
    [System.Serializable]
    public class GreyboxRigStyle
    {
        [Tooltip("Hands, feet and weapon use the body colour times this, so strikes read against the body.")]
        public float LimbShade = 0.55f;

        [Header("Telegraph (attack wind-up glow)")]
        [Tooltip("Share of the telegraph glow the body gets. Hands and weapon always get all of it, so the striking part reads first.")]
        public float TelegraphBodyShare = 0.35f;

        [Header("Flash (hit or deflect)")]
        [Tooltip("Brightness of Flash(). Values above 1 bloom.")]
        public float FlashIntensity = 4f;

        [Header("Invulnerable (i-frames)")]
        public Color InvulnerableTint = new Color(0.75f, 0.9f, 1f);
        [Range(0f, 1f)] public float InvulnerableTintAmount = 0.55f;
        public float InvulnerableGlow = 0.6f;
        [Tooltip("Pulses per second of the i-frame glow.")]
        public float InvulnerablePulseRate = 12f;

        [Header("Stagger")]
        [Range(0f, 1f)] public float StaggerDim = 0.55f;
        public float StaggerWobbleDegrees = 7f;
        public float StaggerWobbleRate = 7f;
        [Tooltip("The wobble starts this much stronger and settles over StaggerSettleTime.")]
        public float StaggerKick = 2.2f;
        public float StaggerSettleTime = 0.35f;

        [Header("Death")]
        [Range(0f, 1f)] public float DeadDim = 0.3f;
        public float DeadToppleDegrees = 85f;
        public float DeadToppleTime = 0.55f;
        [Tooltip("Raises the body while it lies down so it doesn't sink into the floor.")]
        public float DeadLift = 0.2f;

        [Header("Charge (hold heavy)")]
        public Color ChargeColor = new Color(1f, 0.45f, 0.1f);
        public float ChargeIntensity = 3f;
        [Tooltip("Fists grow by this fraction at full charge.")]
        public float ChargeFistGrow = 0.45f;
        [Tooltip("The white-gold timing cue while the charge is in its sweet spot.")]
        public Color SweetSpotColor = new Color(1f, 0.92f, 0.6f);
        public float SweetSpotIntensity = 7f;
        public float SweetSpotPulseRate = 18f;
        public float SweetSpotFlashTime = 0.12f;

        [Header("Strikes")]
        [Tooltip("Seconds for limbs to pull back when a strike is interrupted (stagger, death).")]
        public float InterruptRetractTime = 0.12f;
        [Tooltip("Sideways gap between the fists for BothFists strikes, in metres.")]
        public float BothFistsSpread = 0.12f;

        [Header("Lean")]
        [Tooltip("Fraction of a Lean() spent leaning in; the rest eases back.")]
        [Range(0.05f, 0.95f)] public float LeanAttackShare = 0.25f;

        [Header("Weapon")]
        [Tooltip("Where the sword arm pivots (torso space). The blade points along shoulder -> fist, so raising the fist raises the blade.")]
        public Vector3 SwordShoulder = new Vector3(0.2f, 0.55f, -0.05f);
    }
}
