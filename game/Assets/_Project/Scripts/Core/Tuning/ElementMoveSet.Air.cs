namespace VaatusRevenge.Core
{
    // Air's move set. STUB (Build 05 package A): a copy of Fire under Air's name and style, so the loadout, element
    // switching and the tests work before the real Air data lands (package B fills it from the Build 05 spec, section 3).
    public partial class ElementMoveSet
    {
        public static ElementMoveSet CreateAirFluid()
        {
            ElementMoveSet set = CreateFireFluid();
            set.Element = ElementId.Air;
            set.DisplayName = "Air";
            set.AnimationStyle = "air";
            set.Momentum.Enabled = false;   // only Fire has an identity meter in this build
            return set;
        }

        public static ElementMoveSet CreateAirPunishing()
        {
            ElementMoveSet set = CreateAirFluid();
            ApplyPunishing(set);
            return set;
        }
    }
}
