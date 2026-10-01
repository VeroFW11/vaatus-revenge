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

        // A clean hit's spark: is the attacker off the ground? (The air string, an air attack, or simply airborne.)
        public static bool IsAirborneAttacker(bool attackerGrounded, PlayerAttackKind kind)
        {
            return !attackerGrounded || kind == PlayerAttackKind.Air;
        }
    }
}
