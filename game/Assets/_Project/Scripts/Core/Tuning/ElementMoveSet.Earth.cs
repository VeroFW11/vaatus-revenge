namespace VaatusRevenge.Core
{
    // Earth's move set. STUB (Build 05 package A): a copy of Fire under Earth's name and style, so the loadout, element
    // switching and the tests work before the real Earth data lands (package B fills it from the Build 05 spec, section 3).
    public partial class ElementMoveSet
    {
        public static ElementMoveSet CreateEarthFluid()
        {
            ElementMoveSet set = CreateFireFluid();
            set.Element = ElementId.Earth;
            set.DisplayName = "Earth";
            set.AnimationStyle = "earth";
            set.Momentum.Enabled = false;   // only Fire has an identity meter in this build
            return set;
        }

        public static ElementMoveSet CreateEarthPunishing()
        {
            ElementMoveSet set = CreateEarthFluid();
            ApplyPunishing(set);
            return set;
        }
    }
}
