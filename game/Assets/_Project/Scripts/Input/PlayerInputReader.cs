using UnityEngine;
using UnityEngine.InputSystem;
using VaatusRevenge.Core;

namespace VaatusRevenge
{
    // Reads the gamepad, keyboard and mouse once per frame, before any gameplay script runs (execution order
    // -100), and packs everything into a PlayerInputFrame. Gameplay code only ever reads Frame, never a device,
    // which is what lets the headless harness play the game with scripted inputs.
    //
    // Bindings are defined in code (BuildActions) instead of an .inputactions asset, so they're easy to read
    // and can't get out of sync. The mouse only counts while the cursor is captured: click the Game view to
    // capture it (that click is not an attack), Esc or switching windows releases it. The gamepad and keyboard
    // work either way.
    //
    // Needs no references: just add it to any GameObject in the scene.
    [DefaultExecutionOrder(-100)]
    [DisallowMultipleComponent]
    public class PlayerInputReader : MonoBehaviour
    {
        public static PlayerInputReader Instance { get; private set; }

        // Slots for remembering each button's held state from last frame.
        const int LightSlot = 0, HeavySlot = 1, DodgeSlot = 2, JumpSlot = 3, GuardSlot = 4, SkillSlot = 5, HealSlot = 6, LockOnSlot = 7;
        const int SwapShoulderSlot = 8;
        const int SlotCount = 9;
        // Mouse movement is ignored for this many frames after the cursor is captured: some platforms report the
        // cursor's jump to the window centre as one huge movement, which would whip the camera round.
        const int MouseSettleFrames = 2;
        // Smallest wheel movement that counts as a notch.
        const float ScrollDeadZone = 0.01f;
        // Right-stick tilt that counts as "the player is using the gamepad now".
        const float GamepadActivityTilt = 0.25f;

        [Tooltip("Element picked by each D-pad direction / number key, in the order Up or 1, Right or 2, Down or 3, Left or 4.")]
        [SerializeField] private ElementId[] elementSlots = { ElementId.Fire, ElementId.Water, ElementId.Earth, ElementId.Air };
        [Tooltip("Capture the mouse when the Game view is clicked (Esc releases it).")]
        [SerializeField] private bool lockCursorOnClick = true;

        InputActionMap map;
        InputAction move, lookStick, lookMouse;
        InputAction light, lightMouse, heavy, heavyMouse, dodge, jump, guard, skill, heal, lockOn, lockOnMouse, swapShoulder;
        InputAction switchLeft, switchRight, switchScroll, releaseCursor;
        readonly InputAction[] elementActions = new InputAction[4];
        InputAction[] sharedActions;
        readonly bool[] heldLastFrame = new bool[SlotCount];

        PlayerInputFrame frame;
        bool gameplayEnabled = true;
        bool usingGamepad;
        bool mouseButtonsBlocked;
        int mouseSettleFrames;

        // This frame's input. Empty while gameplay input is disabled (paused, menus).
        public PlayerInputFrame Frame => frame;
        // True when the last thing the player touched was a gamepad (for button prompts and rumble).
        public bool UsingGamepad => usingGamepad;
        public bool GameplayEnabled => gameplayEnabled;
        public bool CursorLocked => Cursor.lockState == CursorLockMode.Locked;

        public bool LockCursorOnClick
        {
            get { return lockCursorOnClick; }
            set { lockCursorOnClick = value; }
        }

        // Turns gameplay input off (Frame reads as empty: paused, death screen, menus) or back on.
        // Takes effect immediately, even for scripts that read Frame later this frame.
        public void SetGameplayEnabled(bool enabled)
        {
            if (gameplayEnabled == enabled) return;
            gameplayEnabled = enabled;
            if (!enabled) frame = default(PlayerInputFrame);
            // The click that closed a menu must not turn into an attack: ignore mouse buttons until released.
            else mouseButtonsBlocked = true;
        }

        // Captures the mouse for camera control. The click (if any) that caused it is not counted as input.
        public void LockCursor()
        {
            Cursor.lockState = CursorLockMode.Locked;
            Cursor.visible = false;
            mouseButtonsBlocked = true;
            mouseSettleFrames = MouseSettleFrames;
        }

        public void ReleaseCursor()
        {
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
        }

        void OnEnable()
        {
            if (Instance != null && Instance != this)
            {
                Debug.LogWarning("PlayerInputReader: another one ('" + Instance.name + "') is already active, so this one on '"
                                 + name + "' switches itself off. Keep a single input reader in the scene.", this);
                enabled = false;
                return;
            }
            Instance = this;
            if (map == null) BuildActions();
            map.Enable();
        }

        void OnDisable()
        {
            if (map != null) map.Disable();
            frame = default(PlayerInputFrame);
            System.Array.Clear(heldLastFrame, 0, heldLastFrame.Length);
            if (Instance != this) return;
            if (CursorLocked) ReleaseCursor();
            Instance = null;
        }

        void OnDestroy()
        {
            if (map == null) return;
            map.Dispose();
            map = null;
        }

        void OnApplicationFocus(bool hasFocus)
        {
            // Alt-tabbing away must give the mouse back to the desktop.
            if (!hasFocus && Instance == this) ReleaseCursor();
        }

        void Update()
        {
            if (map == null) return;
            UpdateCursor();

            bool cursorLocked = CursorLocked;
            bool mouseButtonsLive = cursorLocked && !mouseButtonsBlocked;
            bool mouseLookLive = cursorLocked && mouseSettleFrames <= 0;
            if (mouseSettleFrames > 0) mouseSettleFrames--;

            // Buttons are read even while gameplay is disabled, so their held state stays up to date and
            // re-enabling doesn't invent a press for a button that was already down.
            ButtonState lightButton = ReadButton(LightSlot, light, mouseButtonsLive ? lightMouse : null);
            ButtonState heavyButton = ReadButton(HeavySlot, heavy, mouseButtonsLive ? heavyMouse : null);
            ButtonState dodgeButton = ReadButton(DodgeSlot, dodge, null);
            ButtonState jumpButton = ReadButton(JumpSlot, jump, null);
            ButtonState guardButton = ReadButton(GuardSlot, guard, null);
            ButtonState skillButton = ReadButton(SkillSlot, skill, null);
            ButtonState healButton = ReadButton(HealSlot, heal, null);
            ButtonState lockOnButton = ReadButton(LockOnSlot, lockOn, mouseButtonsLive ? lockOnMouse : null);
            ButtonState swapShoulderButton = ReadButton(SwapShoulderSlot, swapShoulder, null);

            Vector2 moveValue = Vector2.ClampMagnitude(move.ReadValue<Vector2>(), 1f);
            Vector2 stickLook = lookStick.ReadValue<Vector2>();
            Vector2 mouseLook = mouseLookLive ? lookMouse.ReadValue<Vector2>() : Vector2.zero;
            float scroll = cursorLocked ? switchScroll.ReadValue<float>() : 0f;
            UpdateActiveDevice(stickLook, mouseLook, mouseButtonsLive && AnyMouseButtonPressedThisFrame());

            if (!gameplayEnabled)
            {
                frame = default(PlayerInputFrame);
                return;
            }

            // One look source per frame: the mouse when it moved, unless the stick is clearly tilted. A stick
            // resting just past its dead zone (worn or drifting) must not block the mouse.
            bool stickTilted = stickLook.sqrMagnitude > GamepadActivityTilt * GamepadActivityTilt;
            bool useMouse = mouseLook.sqrMagnitude > 0f && !stickTilted;
            frame = new PlayerInputFrame
            {
                Move = moveValue.ToNumerics(),
                Look = (useMouse ? mouseLook : stickLook).ToNumerics(),
                LookIsMouse = useMouse,
                Light = lightButton,
                Heavy = heavyButton,
                Dodge = dodgeButton,
                Jump = jumpButton,
                Guard = guardButton,
                Skill = skillButton,
                Heal = healButton,
                LockOn = lockOnButton,
                SwapShoulder = swapShoulderButton,
                SwitchTargetDelta = ReadSwitchDelta(scroll),
                ElementSelect = ReadElementSelect(),
            };
        }

        void UpdateCursor()
        {
            // The click that captured the mouse (or closed a menu) stays blocked until every mouse button is up.
            if (mouseButtonsBlocked && !AnyMouseButtonHeld() && !AnyMouseButtonPressedThisFrame()) mouseButtonsBlocked = false;

            bool locked = CursorLocked;
            if (locked && releaseCursor.WasPressedThisFrame())
            {
                ReleaseCursor();
            }
            else if (!locked && lockCursorOnClick && gameplayEnabled && AnyMouseButtonPressedThisFrame())
            {
                // The Input System only passes clicks to the game while its window / Game view has focus, so a
                // click arriving here really was a click on the game.
                LockCursor();
            }
        }

        // Combines this frame's held state with the presses and releases the Input System saw between frames.
        // A tap shorter than one frame is pressed AND released with Held never true; it must still count.
        ButtonState ReadButton(int slot, InputAction primary, InputAction mouse)
        {
            bool held = primary.IsPressed();
            bool pressed = primary.WasPressedThisFrame();
            bool released = primary.WasReleasedThisFrame();
            if (mouse != null)
            {
                held |= mouse.IsPressed();
                pressed |= mouse.WasPressedThisFrame();
                released |= mouse.WasReleasedThisFrame();
            }

            bool heldBefore = heldLastFrame[slot];
            heldLastFrame[slot] = held;

            ButtonState state = ButtonState.From(held, heldBefore);
            // A new press: the button was up, or it was let go and pressed again within this one frame.
            if (pressed && (!heldBefore || released)) state.Pressed = true;
            // A release only counts once nothing is holding the button any more (the other device might be).
            if (released && !held) state.Released = true;
            return state;
        }

        // -1 = previous/left, +1 = next/right. Mouse wheel up (away from you) is left, down is right.
        int ReadSwitchDelta(float scroll)
        {
            int delta = 0;
            if (switchLeft.WasPressedThisFrame()) delta--;
            if (switchRight.WasPressedThisFrame()) delta++;
            if (scroll > ScrollDeadZone) delta--;
            else if (scroll < -ScrollDeadZone) delta++;
            return delta > 0 ? 1 : (delta < 0 ? -1 : 0);
        }

        ElementId ReadElementSelect()
        {
            for (int i = 0; i < elementActions.Length; i++)
            {
                if (!elementActions[i].WasPressedThisFrame()) continue;
                return elementSlots != null && i < elementSlots.Length ? elementSlots[i] : ElementId.None;
            }
            return ElementId.None;
        }

        void UpdateActiveDevice(Vector2 stickLook, Vector2 mouseLook, bool mouseClicked)
        {
            if (stickLook.sqrMagnitude > GamepadActivityTilt * GamepadActivityTilt)
            {
                usingGamepad = true;
                return;
            }
            if (mouseLook.sqrMagnitude > 0f || mouseClicked)
            {
                usingGamepad = false;
                return;
            }
            // Movement and buttons are shared between devices: ask which control drove a fresh press.
            for (int i = 0; i < sharedActions.Length; i++)
            {
                InputAction action = sharedActions[i];
                if (!action.WasPressedThisFrame()) continue;
                InputControl control = action.activeControl;
                if (control == null) continue;
                usingGamepad = control.device is Gamepad;
                return;
            }
        }

        bool AnyMouseButtonHeld()
        {
            return lightMouse.IsPressed() || heavyMouse.IsPressed() || lockOnMouse.IsPressed();
        }

        bool AnyMouseButtonPressedThisFrame()
        {
            return lightMouse.WasPressedThisFrame() || heavyMouse.WasPressedThisFrame() || lockOnMouse.WasPressedThisFrame();
        }

        // The default controls (see the table in docs/Prototype/Fire-Combat-Prototype-Spec.md, section 1).
        void BuildActions()
        {
            map = new InputActionMap("Gameplay");

            // The keyboard composite is normalised, so diagonals aren't faster than straight lines.
            move = map.AddAction("Move", InputActionType.Value, "<Gamepad>/leftStick", expectedControlLayout: "Vector2");
            move.AddCompositeBinding("2DVector")
                .With("Up", "<Keyboard>/w")
                .With("Down", "<Keyboard>/s")
                .With("Left", "<Keyboard>/a")
                .With("Right", "<Keyboard>/d");

            // Stick and mouse look are separate actions so we always know which one moved (LookIsMouse).
            lookStick = map.AddAction("LookStick", InputActionType.Value, "<Gamepad>/rightStick", expectedControlLayout: "Vector2");
            lookMouse = map.AddAction("LookMouse", InputActionType.Value, "<Mouse>/delta", expectedControlLayout: "Vector2");

            // Mouse buttons get their own actions so they can be ignored while the cursor isn't captured.
            // Triggers work as buttons: pressed past halfway.
            light = AddButton("Light", "<Gamepad>/rightShoulder", null);
            lightMouse = AddButton("LightMouse", "<Mouse>/leftButton", null);
            heavy = AddButton("Heavy", "<Gamepad>/rightTrigger", null);
            heavyMouse = AddButton("HeavyMouse", "<Mouse>/rightButton", null);
            dodge = AddButton("Dodge", "<Gamepad>/buttonEast", "<Keyboard>/leftShift");
            jump = AddButton("Jump", "<Gamepad>/buttonSouth", "<Keyboard>/space");
            guard = AddButton("Guard", "<Gamepad>/leftShoulder", "<Keyboard>/q");
            skill = AddButton("Skill", "<Gamepad>/leftTrigger", "<Keyboard>/e");
            heal = AddButton("Heal", "<Gamepad>/buttonWest", "<Keyboard>/r");
            lockOn = AddButton("LockOn", "<Gamepad>/rightStickPress", "<Keyboard>/tab");
            lockOnMouse = AddButton("LockOnMouse", "<Mouse>/middleButton", null);
            // Camera only: held for a moment, flips the over-the-shoulder view to the other side. The hold time is
            // the camera's rule (CameraTuning.ShoulderSwapHoldTime); this just reports the button as usual.
            swapShoulder = AddButton("SwapShoulder", "<Gamepad>/leftStickPress", "<Keyboard>/v");

            // Right-stick flicks are detected by LockOnController from Look; these are the keyboard/mouse ways.
            switchLeft = AddButton("SwitchTargetLeft", "<Keyboard>/z", null);
            switchRight = AddButton("SwitchTargetRight", "<Keyboard>/c", null);
            switchScroll = map.AddAction("SwitchTargetScroll", InputActionType.Value, "<Mouse>/scroll/y", expectedControlLayout: "Axis");

            elementActions[0] = AddButton("ElementUp", "<Gamepad>/dpad/up", "<Keyboard>/1");
            elementActions[1] = AddButton("ElementRight", "<Gamepad>/dpad/right", "<Keyboard>/2");
            elementActions[2] = AddButton("ElementDown", "<Gamepad>/dpad/down", "<Keyboard>/3");
            elementActions[3] = AddButton("ElementLeft", "<Gamepad>/dpad/left", "<Keyboard>/4");

            releaseCursor = AddButton("ReleaseCursor", "<Keyboard>/escape", null);

            sharedActions = new[]
            {
                move, light, heavy, dodge, jump, guard, skill, heal, lockOn, swapShoulder, switchLeft, switchRight,
                elementActions[0], elementActions[1], elementActions[2], elementActions[3],
            };
        }

        InputAction AddButton(string actionName, string binding, string secondBinding)
        {
            InputAction action = map.AddAction(actionName, InputActionType.Button, binding);
            if (secondBinding != null) action.AddBinding(secondBinding);
            return action;
        }
    }
}
