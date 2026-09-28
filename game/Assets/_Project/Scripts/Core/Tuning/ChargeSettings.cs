using System;

namespace VaatusRevenge.Core
{
    // Hold-to-charge heavy attack (Fire: Fa Jin Palm). Fa jin is the martial-arts idea of explosive
    // whole-body power released over a short distance; here it's a timing reward: release inside the
    // sweet spot (the fighter flashes) for a burst of damage and stagger. Times are seconds of holding.
    //   released before QuickReleaseTime       -> Quick heavy   (normal damage)
    //   QuickReleaseTime .. SweetSpotStart     -> Partial charge (normal damage)
    //   SweetSpotStart .. SweetSpotEnd         -> Fa jin        (FaJin multipliers)
    //   after SweetSpotEnd (auto at MaxChargeTime) -> Charged   (ChargedDamageMultiplier)
    [Serializable]
    public class ChargeSettings
    {
        public float MaxChargeTime = 1.2f;           // holding this long releases automatically as Charged
        public float QuickReleaseTime = 0.2f;
        public float SweetSpotStart = 0.70f;
        public float SweetSpotEnd = 0.90f;
        public float ChargedDamageMultiplier = 1.4f;
        public float FaJinDamageMultiplier = 1.8f;
        public float FaJinPoiseMultiplier = 2f;
        public float FaJinHitstopBonus = 0.12f;      // extra freeze-frame on a fa jin hit
        public float FaJinMomentumGain = 20f;        // replaces the heavy's MomentumGain when a fa jin lands
        public bool CanDodgeCancelCharge = true;     // dodge out of a charge you regret
    }
}
