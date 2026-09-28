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
    // Default bindings (gamepad / keyboard + mouse):
    //   Move      left stick / WASD            Look      right stick / mouse
    //   Light     RB (R1) / left mouse         Heavy     RT (R2) / right mouse (hold to charge)
    //   Dodge     B (Circle) / Left Shift      tap = dodge, hold = sprint
    //   Jump      A (Cross) / Space            Guard     LB (L1) / Q (tap on time = deflect)
    //   Skill     LT (L2) / E                  Heal      X (Square) / R
    //   LockOn    R3 / middle mouse or Tab     SwitchTargetDelta  mouse wheel / Z,C (stick flicks are
    //                                                              detected by the lock-on code from Look)
    //   ElementSelect  D-pad / 1-4
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

        public int SwitchTargetDelta; // -1 = previous/left, +1 = next/right, 0 = none (edge-triggered)
        public ElementId ElementSelect; // None unless a direction was pressed this frame (edge-triggered)
    }
}
