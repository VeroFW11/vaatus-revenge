namespace VaatusRevenge.Core
{
    // Lore rules for how an element's effects may look (pure C#, so the Unity effects and the headless trace agree, and
    // tests can pin them). Canon, spec 8.1 item 2: an earthbender draws stone from the ground, so Earth OFF the ground
    // throws no rock: every Earth effect made in the air (a strike, a hit spark, a dash, a switch, a perfect dodge) is
    // dust and a push of pressure instead of rock pieces.
    public static class ElementFxRules
    {
        // True when this effect must be dust only (no rock or gravel pieces).
        public static bool DustOnly(ElementId element, bool airborne)
        {
            return element == ElementId.Earth && airborne;
        }

        // A player event made off the ground: anything with InAir, or a strike of the air string / an air attack.
        public static bool IsAirborne(in PlayerEvent e)
        {
            return e.InAir || e.Branch == ComboBranch.Air || e.AttackKind == PlayerAttackKind.Air;
        }

        // J3-06: the MIX accent burst on a switch strike's first hit. An airborne switch strike into Earth (the air string)
        // bursts dust, never rock, like every other effect made off the ground.
        public static bool MixAccentIsDust(ElementId element, bool attackerAirborne)
        {
            return DustOnly(element, attackerAirborne);
        }

        // J3-07, spec 7 ("no rock on the body"; round 2: "stone comes from the ground"): Earth's rock never comes out of a
        // body. An Earth effect made at a point on a body (a strike's fist or foot, a hit on a foe, the chest on a perfect
        // dodge or a parry, a charged fist, a launched foe) is dust at that point; any rock rises out of the floor under it.
        public static bool StoneFromFloor(ElementId element)
        {
            return element == ElementId.Earth;
        }

        // How high above the floor a point may be for its rock to rise from the floor under it; higher (a juggled foe), the
        // dust at the point is all there is (presentation, not gameplay).
        public const float FloorStoneMaxHeight = 1.6f;

        // Does an Earth effect at this height above the floor throw rock up from the floor under it?
        public static bool FloorStoneUnder(ElementId element, float heightAboveFloor)
        {
            return StoneFromFloor(element) && heightAboveFloor <= FloorStoneMaxHeight;
        }

        // A clean hit's spark: is the attacker off the ground? (The air string, an air attack, or simply airborne.)
        public static bool IsAirborneAttacker(bool attackerGrounded, PlayerAttackKind kind)
        {
            return !attackerGrounded || kind == PlayerAttackKind.Air;
        }
    }
}
