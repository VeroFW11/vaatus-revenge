using VaatusRevenge.Core;

namespace VaatusRevenge
{
    // What a FireProjectile looks like in the grey box.
    public enum ProjectileVisual
    {
        Fire,      // glowing fireball with a flame trail; bursts into flame on impact
        Bolt,      // crossbow bolt: a thin dark shaft with a pale trail so it can be seen coming
        WaterOrb,  // Water's ice dart: a pale, icy sliver with a blue trail; splashes on impact
        Rock,      // Earth's boulder: a tumbling lit rock with a dust trail; breaks into rock and dust on impact
        AirBall    // Air's blast: a pale swirling ball with a white streak; bursts into a ring of wind on impact
    }

    // Which visual each element's projectile uses, and back.
    public static class ProjectileVisuals
    {
        public static ProjectileVisual ForElement(ElementId element)
        {
            switch (element)
            {
                case ElementId.Water: return ProjectileVisual.WaterOrb;
                case ElementId.Earth: return ProjectileVisual.Rock;
                case ElementId.Air: return ProjectileVisual.AirBall;
                default: return ProjectileVisual.Fire;
            }
        }

        // The element whose effects a projectile bursts with (a bolt has none: None).
        public static ElementId ElementOf(ProjectileVisual visual)
        {
            switch (visual)
            {
                case ProjectileVisual.Fire: return ElementId.Fire;
                case ProjectileVisual.WaterOrb: return ElementId.Water;
                case ProjectileVisual.Rock: return ElementId.Earth;
                case ProjectileVisual.AirBall: return ElementId.Air;
                default: return ElementId.None;
            }
        }
    }
}
