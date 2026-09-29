namespace VaatusRevenge.Core
{
    // The player's top-level state. Exactly one at a time; PlayerCombatModel documents every transition.
    public enum PlayerState
    {
        Locomotion,  // on the ground: idle, walking, running (free to act)
        Sprinting,   // Dodge button held while moving (free to act; light = sprint attack)
        Airborne,    // jumping or falling (light/heavy = plunge)
        Attacking,   // running a MoveData: light chain, heavy after release, sprint attack, skill
        Charging,    // heavy held: building up to the fa jin sweet spot
        Plunging,    // jump attack: hang, fall, landing recovery
        Dodging,     // dash with invincibility frames
        Guarding,    // guard raised (a fresh press also opens the deflect window)
        Healing,     // drinking spirit water: committed
        Staggered,   // stunned: poise broken, guard broken or deflected
        Dead
    }

    // Where the current move is in its frame data.
    public enum AttackPhase { None, Startup, Active, Recovery }

    // Button presses the input buffer can hold. Guard is the press that raises the guard or parries (see
    // GuardSettings.Style). New commands go at the end to keep the existing numbering.
    public enum PlayerCommand { None, Light, Heavy, Dodge, Jump, Skill, Heal, Guard, ZipStrike, AbilityNorth, AbilityEast }

    // How long the heavy was charged (see ChargeSettings).
    public enum ChargeTier { None, Quick, Partial, FaJin, Charged }

    // Which kind of attack is running while the player is Attacking or Plunging.
    // Launcher: the held-attack uppercut that throws the target up. Air: an air-combo strike. Ability: an ability-slot move.
    public enum PlayerAttackKind { None, Light, Heavy, Sprint, Skill, Plunge, ZipStrike, Launcher, Air, Ability }
}
