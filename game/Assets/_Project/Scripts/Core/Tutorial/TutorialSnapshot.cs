namespace VaatusRevenge.Core
{
    // The little the tutorial needs from the world besides the player's events: where the sparring partner is and which
    // element and move sets the player has. Built once per frame by whoever drives the tracker (TutorialDirector, tests).
    public struct TutorialSnapshot
    {
        public ElementId ActiveElement;
        public float TargetDistance;      // flat distance from the player's feet to the partner's, metres (< 0 = unknown)
        public ElementLoadout Loadout;    // the player's move sets (tells the tracker how long each air string is)

        public static TutorialSnapshot From(PlayerCombatModel model, float targetDistance)
        {
            return new TutorialSnapshot
            {
                ActiveElement = model != null ? model.ActiveElement : ElementId.None,
                TargetDistance = targetDistance,
                Loadout = model != null ? model.Loadout : null
            };
        }
    }
}
