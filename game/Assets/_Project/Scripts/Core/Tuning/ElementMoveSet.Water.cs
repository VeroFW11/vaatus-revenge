namespace VaatusRevenge.Core
{
    // Water's move set. STUB (Build 05 package A): a copy of Fire under Water's name and style, so the loadout, element
    // switching and the tests work before the real Water data lands (package B fills it from the Build 05 spec, section 3).
    public partial class ElementMoveSet
    {
        public static ElementMoveSet CreateWaterFluid()
        {
            ElementMoveSet set = CreateFireFluid();
            set.Element = ElementId.Water;
            set.DisplayName = "Water";
            set.AnimationStyle = "water";
            set.Momentum.Enabled = false;   // only Fire has an identity meter in this build
            return set;
        }

        public static ElementMoveSet CreateWaterPunishing()
        {
            ElementMoveSet set = CreateWaterFluid();
            ApplyPunishing(set);
            return set;
        }
    }
}
