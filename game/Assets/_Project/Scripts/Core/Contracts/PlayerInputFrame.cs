using System.Numerics;

namespace VaatusRevenge.Core
{
    // One button's state for a single frame.
    public struct ButtonState
    {
        public bool Held;      // down this frame
        public bool Pressed;   // went down this frame
        public bool Released;  // went up this frame

        public static ButtonState From(bool heldNow, bool heldLastFrame)
        {
            return new ButtonState { Held = heldNow, Pressed = heldNow && !heldLastFrame, Released = !heldNow && heldLastFrame };
        }
    }

    // Everything the player pressed this frame, with the device details stripped out.
    // The Unity input reader fills it in; the core combat and movement code only ever sees this,
    // which is what lets the headless playtest harness "play" the game with scripted inputs.
    // Buttons are raw: deciding tap vs hold, buffering, combos etc. is the core's job.
    //
    // Default bindings, Spider-Man 2 style (Xbox / PlayStation / keyboard + mouse). The input reader turns
    // button combinations into these plain fields, so the rules never see which physical button it was:
    //   Move       left stick / WASD                 Look       right stick / mouse
    //   Light      X (Square) / left mouse            attack: aims at the enemy your stick points at
    //   ZipStrike  Y (Triangle) / F                   dash across to a far enemy and hit it
    //   Dodge      B (Circle) / Left Shift            tap = dodge, hold = sprint
    //   Jump       A (Cross) / Space
    //   Guard      tap LB (L1) / Q                    parry (or block, if the element's DefenseStyle has one)
    //   Light held on the ground = launcher; Light in the air = air combo
    //   Heavy      hold LB + X (L1 + Square) / hold Q + left mouse    ability slot: charge the fa jin palm (in the air: plunge)
    //   AbilityNorth  hold LB + Y (L1 + Triangle) / hold Q + F        ability slot (Fire: Fire Whip, mid range)
    //   AbilityEast   hold LB + B (L1 + Circle) / hold Q + Left Shift ability slot (Fire: Flame Wheel, close all round)
    //   Skill      tap RB (R1) / right mouse          Fire Blast
    //   ElementSelect  hold RB, then press a face button / 1-4 (ElementButtonLayout: B Fire, X Water, A Earth, Y Air; keys 1-4 Fire,
    //                  Water, Earth, Air). Mid-string it is a switch strike: the next hit comes out in that element
    //   Heal       D-pad down / R
    //   LockOn     R3 / middle mouse or Tab (optional: off unless you press it)
    //   SwitchTargetDelta  mouse wheel / Z,C (stick flicks are detected by the lock-on code from Look)
    //   SwapShoulder  L3 (click left stick) / V       flip the over-the-shoulder camera to the other side
    public struct PlayerInputFrame
    {
        public Vector2 Move;          // x = right, y = forward, magnitude 0..1 (keyboard is normalised)
        public Vector2 Look;          // stick: -1..1 per axis. Mouse: pixels moved this frame (see LookIsMouse)
        public bool LookIsMouse;      // true when Look is a mouse delta (never scale a mouse delta by deltaTime)

        public ButtonState Light;
        public ButtonState Heavy;
        public ButtonState Dodge;
        public ButtonState Jump;
        public ButtonState Guard;
        public ButtonState Skill;
        public ButtonState Heal;
        public ButtonState LockOn;
        public ButtonState SwapShoulder; // camera only: the gameplay rules ignore it
        public ButtonState ZipStrike;
        public ButtonState AbilityNorth; // ability slot: hold LB + Y (Triangle) / hold Q + F
        public ButtonState AbilityEast;  // ability slot: hold LB + B (Circle) / hold Q + Left Shift

        public int SwitchTargetDelta; // -1 = previous/left, +1 = next/right, 0 = none (edge-triggered)
        public ElementId ElementSelect; // None unless an element was picked this frame (edge-triggered; see ElementButtonLayout)
    }
}
