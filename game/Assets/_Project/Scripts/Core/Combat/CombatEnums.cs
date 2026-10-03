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
    // SwitchStrike: RB + an element's button while a string is live (the next string hit in that element, see
    // PlayerCombatModel.Switch). It is buffered and beat-judged exactly like a Light press.
    public enum PlayerCommand { None, Light, Heavy, Dodge, Jump, Skill, Heal, Guard, ZipStrike, AbilityNorth, AbilityEast, SwitchStrike }

    // How long the heavy was charged (see ChargeSettings).
    public enum ChargeTier { None, Quick, Partial, FaJin, Charged }

    // Which kind of attack is running while the player is Attacking or Plunging.
    // Launcher: the held-attack uppercut that throws the target up. Air: an air-combo strike. Ability: an ability-slot move.
    // DodgeStrike: the counter pressed late in a dodge (ElementMoveSet.DodgeStrike); it takes the string's next slot.
    public enum PlayerAttackKind { None, Light, Heavy, Sprint, Skill, Plunge, ZipStrike, Launcher, Air, Ability, DodgeStrike }

    // How a string press was timed against the running move's beat (the moment its strike lands, see
    // PlayerCombatModel.Rhythm). OnBeat speeds the next move up and hits harder; Early (a mash) and Mashed (a second
    // press before the next move started) slow it down; Late is neutral; Pause took the pause branch; Auto is a press
    // the game counts as on the beat by itself (a counter after a perfect dodge).
    public enum BeatGrade { None, OnBeat, Late, Early, Mashed, Pause, Auto }

    // Which branch of the string a move belongs to: the main chain, the pause chain (X X, wait, X X), the dodge strike,
    // the air chain, the launcher, or anything else (heavy, skill, ability, zip, sprint, plunge).
    public enum ComboBranch { Main, Pause, DodgeStrike, Air, Launcher, Other }

    // The shape of a dodge (Spider-Man 2 style; the stick against the direction to the focus target decides):
    //   Traverse  no enemy to face: dash along the stick, facing the dash
    //   EvadeOut  stick away from the target: hop back out of reach, still facing it
    //   SlipIn    stick toward the target: slip in close, ready to hit
    //   SideSlip  stick sideways: side-step round it (circle-strafe)
    //   Backstep  neutral stick, nothing about to hit you: a short hop back
    //   AutoEvade neutral stick while a strike is about to land: the game picks a safe side-step
    //   AirDash   in the air
    public enum DodgeKind { Traverse, EvadeOut, SlipIn, SideSlip, Backstep, AutoEvade, AirDash }

    // Why the hit counter (PlayerCombatModel.ComboCount) ended.
    public enum ComboEndReason { Timeout, TookHit, Staggered, Died, Respawned, PresetChanged }

    // Why an element switch was refused: still on cooldown, the element isn't learned, the player is busy for longer
    // than the switch buffer waits (charging, plunging, healing, staggered), or it's already the active element.
    public enum SwitchDeniedReason { Cooldown, NotLearned, Busy, SameElement }
}
