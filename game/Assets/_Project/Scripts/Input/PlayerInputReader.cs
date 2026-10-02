using System.Collections.Generic;
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
    // and can't get out of sync. The layout is Spider-Man 2's (see the spec's controls table): face buttons
    // attack / zip / dodge / jump, a tapped LB parries and a tapped RB fires the ranged skill. The shoulders are
    // also modifiers, the way Spider-Man 2 does abilities and gadgets:
    //   hold LB + X (Q + left mouse)  = Heavy (charge the fa jin palm, release on the flash; in the air: plunge)
    //   hold LB + Y (Q + F)            = AbilityNorth (Fire: Fire Whip, mid range)
    //   hold LB + B (Q + Left Shift)   = AbilityEast (Fire: Flame Wheel, close, all round)
    //   LB + A is an empty slot and still jumps.
    //   hold RB, then Y / B / A / X    = pick an element (ElementButtonLayout, colour-matched: Y Air, B Fire, A Earth,
    //                                  X Water; 1-4 on the keyboard are Fire, Water, Earth, Air). RB then fires nothing:
    //                                  the Fire Blast comes from a quick RB tap on its own, on release. Modifier first, like
    //                                  Spider-Man 2: a face pressed before RB does its own job (nothing is ever held back).
    // View (the small left button) and F7 / F8 drive the sandbox's combat tutorial (TutorialButton, TutorialKey,
    // TutorialSkipKey). They are not gameplay actions, so they live outside the PlayerInputFrame. Menu (Start) pauses
    // and resumes like Esc, and while paused Y pages through the F1 overlay (MenuButton, OverlayPageButton): an
    // Xbox-first player never needs the keyboard.
    // The mouse only counts while the cursor is captured: click the Game view to capture it (that click is not an
    // attack), Esc or switching windows releases it. The gamepad and keyboard work either way.
    //
    // Needs no references: just add it to any GameObject in the scene.
    [DefaultExecutionOrder(-100)]
    [DisallowMultipleComponent]
    public class PlayerInputReader : MonoBehaviour
    {
        public static PlayerInputReader Instance { get; private set; }

        // Slots for remembering each button's held state from last frame.
        const int AttackSlot = 0, ZipSlot = 1, DodgeSlot = 2, JumpSlot = 3, GuardSlot = 4, SkillPadSlot = 5, HealSlot = 6;
        const int LockOnSlot = 7, SwapShoulderSlot = 8, SkillMouseSlot = 9;
        const int TutorialPadSlot = 10, TutorialKeySlot = 11, TutorialSkipSlot = 12;
        const int MenuPadSlot = 13, OverlayPagePadSlot = 14;
        const int SlotCount = 15;
        // Mouse movement is ignored for this many frames after the cursor is captured: some platforms report the
        // cursor's jump to the window centre as one huge movement, which would whip the camera round.
        const int MouseSettleFrames = 2;
        // Smallest wheel movement that counts as a notch.
        const float ScrollDeadZone = 0.01f;
        // Right-stick tilt that counts as "the player is using the gamepad now".
        const float GamepadActivityTilt = 0.25f;

        [Tooltip("Element picked by RB + each face button (colour-matched: Y Air, B Fire, A Earth, X Water) and by the number "
                 + "keys 1-4 (Fire, Water, Earth, Air).")]
        [SerializeField] private ElementButtonLayout elementLayout = new ElementButtonLayout();
        [Tooltip("Capture the mouse when the Game view is clicked (Esc releases it).")]
        [SerializeField] private bool lockCursorOnClick = true;
        [Tooltip("RB (R1) held no longer than this, with no face button pressed, fires the ranged skill when let go. "
                 + "Held longer, it's treated as a cancelled element pick and fires nothing.")]
        [SerializeField] private float skillTapMaxTime = 0.35f;
        [Tooltip("A face button counts as an LB (Q) ability chord only this long after LB went down. Held longer (a parry "
                 + "you're still holding), B and Y dodge and zip as normal again, so a panic parry-then-dodge isn't eaten.")]
        [SerializeField] private float abilityChordWindow = 0.5f;
        [Tooltip("A face pressed at most this long before RB (so it did its own job: the chord is RB first, then the face) "
                 + "still stops RB's release firing the ranged skill (held longer, e.g. sprinting on B, an RB tap is a deliberate skill).")]
        [SerializeField] private float chordSkillGuard = 0.15f;

        InputActionMap map;
        InputAction move, lookStick, lookMouse;
        InputAction attack, attackMouse, zip, dodge, jump, guard, skillPad, skillMouse, heal, lockOn, lockOnMouse, swapShoulder;
        InputAction switchLeft, switchRight, switchScroll, releaseCursor;
        InputAction tutorialPad, tutorialKey, tutorialSkip;
        InputAction menuPad, overlayPagePad;
        readonly InputAction[] elementActions = new InputAction[4];
        InputAction[] sharedActions;
        readonly bool[] heldLastFrame = new bool[SlotCount];

        PlayerInputFrame frame;
        ButtonState tutorialButton, tutorialKeyButton, tutorialSkipButton;
        ButtonState menuButton, overlayPageButton;
        // The shoulder chords (RB + face = element, RB tap = skill, LB + face = ability) are pure C# so tests can cover
        // exact frame orders (PadChordReader).
        readonly PadChordReader chords = new PadChordReader();
        readonly ElementId[] padSlots = new ElementId[ElementButtonLayout.SlotCount];   // ElementSlots, refreshed each frame
        bool gameplayEnabled = true;
        bool usingGamepad;
        bool mouseButtonsBlocked;
        int mouseSettleFrames;

        // This frame's input. Empty while gameplay input is disabled (paused, menus).
        public PlayerInputFrame Frame => frame;
        // True when the last thing the player touched was a gamepad (for button prompts and rumble).
        public bool UsingGamepad => usingGamepad;
        // The element each face button picks with RB held, in the order Y, B, A, X (the HUD's element wheel).
        public IReadOnlyList<ElementId> ElementSlots
        {
            get
            {
                RefreshPadSlots();
                return padSlots;
            }
        }
        // RB (R1) is held: a face button now picks an element (the wheel grows and shows "RB +").
        public bool ElementModifierHeld => gameplayEnabled && skillPad != null && skillPad.IsPressed();
        public ElementButtonLayout ElementLayout => elementLayout;
        // The tutorial's buttons, read every frame even while gameplay input is off (the tutorial decides what a press
        // means when paused). View on the gamepad: tap = start / skip a step, hold = quit. F7 starts or quits, F8 skips.
        public ButtonState TutorialButton => tutorialButton;
        public ButtonState TutorialKey => tutorialKeyButton;
        public ButtonState TutorialSkipKey => tutorialSkipButton;
        // Menu (Start) on the gamepad: pause / resume. Read every frame, gameplay input on or off.
        public ButtonState MenuButton => menuButton;
        // Y on the gamepad, as a menu button: pages the F1 overlay while paused (gameplay input is off then).
        public ButtonState OverlayPageButton => overlayPageButton;
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

        // With domain reload off (this project's Enter Play Mode settings), statics survive between Play sessions, so
        // they are cleared by hand when play starts.
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics()
        {
            Instance = null;
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
            tutorialButton = default(ButtonState);
            tutorialKeyButton = default(ButtonState);
            tutorialSkipButton = default(ButtonState);
            menuButton = default(ButtonState);
            overlayPageButton = default(ButtonState);
            System.Array.Clear(heldLastFrame, 0, heldLastFrame.Length);
            ResetChords();
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
            ButtonState attackButton = ReadButton(AttackSlot, attack, mouseButtonsLive ? attackMouse : null);
            ButtonState zipButton = ReadButton(ZipSlot, zip, null);
            ButtonState dodgeButton = ReadButton(DodgeSlot, dodge, null);
            ButtonState jumpButton = ReadButton(JumpSlot, jump, null);
            ButtonState guardButton = ReadButton(GuardSlot, guard, null);
            ButtonState skillPadButton = ReadButton(SkillPadSlot, skillPad, null);
            ButtonState skillMouseButton = ReadButton(SkillMouseSlot, mouseButtonsLive ? skillMouse : null, null);
            ButtonState healButton = ReadButton(HealSlot, heal, null);
            ButtonState lockOnButton = ReadButton(LockOnSlot, lockOn, mouseButtonsLive ? lockOnMouse : null);
            ButtonState swapShoulderButton = ReadButton(SwapShoulderSlot, swapShoulder, null);
            tutorialButton = ReadButton(TutorialPadSlot, tutorialPad, null);
            tutorialKeyButton = ReadButton(TutorialKeySlot, tutorialKey, null);
            tutorialSkipButton = ReadButton(TutorialSkipSlot, tutorialSkip, null);
            menuButton = ReadButton(MenuPadSlot, menuPad, null);
            overlayPageButton = ReadButton(OverlayPagePadSlot, overlayPagePad, null);

            Vector2 moveValue = Vector2.ClampMagnitude(move.ReadValue<Vector2>(), 1f);
            Vector2 stickLook = lookStick.ReadValue<Vector2>();
            Vector2 mouseLook = mouseLookLive ? lookMouse.ReadValue<Vector2>() : Vector2.zero;
            float scroll = cursorLocked ? switchScroll.ReadValue<float>() : 0f;
            UpdateActiveDevice(stickLook, mouseLook, mouseButtonsLive && AnyMouseButtonPressedThisFrame());

            // Shoulder chords (see the top of the file). Worked out even while gameplay is off, so a chord that
            // began before a pause can't leak out as a stray press after it.
            chords.SkillTapMaxTime = skillTapMaxTime;
            chords.AbilityChordWindow = abilityChordWindow;
            chords.ChordSkillGuard = chordSkillGuard;
            PadChordReader.Result chord = chords.Read(ref zipButton, ref dodgeButton, ref jumpButton, ref attackButton,
                skillPadButton, guardButton, Time.unscaledDeltaTime, elementLayout);
            ElementId elementPick = chord.ElementSelect;
            ButtonState skillButton = chord.Skill;
            if (skillMouseButton.Pressed) skillButton.Pressed = true;
            ButtonState heavyButton = chord.Heavy;
            ButtonState abilityNorthButton = chord.AbilityNorth;
            ButtonState abilityEastButton = chord.AbilityEast;

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
                Light = attackButton,
                Heavy = heavyButton,
                ZipStrike = zipButton,
                AbilityNorth = abilityNorthButton,
                AbilityEast = abilityEastButton,
                Dodge = dodgeButton,
                Jump = jumpButton,
                Guard = guardButton,
                Skill = skillButton,
                Heal = healButton,
                LockOn = lockOnButton,
                SwapShoulder = swapShoulderButton,
                SwitchTargetDelta = ReadSwitchDelta(scroll),
                ElementSelect = elementPick != ElementId.None ? elementPick : ReadElementSelect(),
                ElementSelectOffAttack = elementPick != ElementId.None && chord.ElementSelectOffAttack,
            };
        }

        void ResetChords()
        {
            chords.Reset();
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
        // primary may be null (a mouse-only button while the cursor is free): it then reads as not held.
        ButtonState ReadButton(int slot, InputAction primary, InputAction mouse)
        {
            bool held = primary != null && primary.IsPressed();
            bool pressed = primary != null && primary.WasPressedThisFrame();
            bool released = primary != null && primary.WasReleasedThisFrame();
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
                return elementLayout != null ? elementLayout.KeySlot(i) : ElementId.None;
            }
            return ElementId.None;
        }

        void RefreshPadSlots()
        {
            for (int i = 0; i < padSlots.Length; i++) padSlots[i] = elementLayout != null ? elementLayout.PadSlot(i) : ElementId.None;
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
            return attackMouse.IsPressed() || skillMouse.IsPressed() || lockOnMouse.IsPressed();
        }

        bool AnyMouseButtonPressedThisFrame()
        {
            return attackMouse.WasPressedThisFrame() || skillMouse.WasPressedThisFrame() || lockOnMouse.WasPressedThisFrame();
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
            // Face buttons: buttonWest = X (Square), buttonNorth = Y (Triangle), buttonEast = B (Circle),
            // buttonSouth = A (Cross).
            attack = AddButton("Attack", "<Gamepad>/buttonWest", null);
            attackMouse = AddButton("AttackMouse", "<Mouse>/leftButton", null);
            zip = AddButton("ZipStrike", "<Gamepad>/buttonNorth", "<Keyboard>/f");
            dodge = AddButton("Dodge", "<Gamepad>/buttonEast", "<Keyboard>/leftShift");
            jump = AddButton("Jump", "<Gamepad>/buttonSouth", "<Keyboard>/space");
            guard = AddButton("Guard", "<Gamepad>/leftShoulder", "<Keyboard>/q");
            skillPad = AddButton("Skill", "<Gamepad>/rightShoulder", null);
            skillMouse = AddButton("SkillMouse", "<Mouse>/rightButton", null);
            heal = AddButton("Heal", "<Gamepad>/dpad/down", "<Keyboard>/r");
            lockOn = AddButton("LockOn", "<Gamepad>/rightStickPress", "<Keyboard>/tab");
            lockOnMouse = AddButton("LockOnMouse", "<Mouse>/middleButton", null);
            // Camera only: held for a moment, flips the over-the-shoulder view to the other side. The hold time is
            // the camera's rule (CameraTuning.ShoulderSwapHoldTime); this just reports the button as usual.
            swapShoulder = AddButton("SwapShoulder", "<Gamepad>/leftStickPress", "<Keyboard>/v");

            // Right-stick flicks are detected by LockOnController from Look; these are the keyboard/mouse ways.
            switchLeft = AddButton("SwitchTargetLeft", "<Keyboard>/z", null);
            switchRight = AddButton("SwitchTargetRight", "<Keyboard>/c", null);
            switchScroll = map.AddAction("SwitchTargetScroll", InputActionType.Value, "<Mouse>/scroll/y", expectedControlLayout: "Axis");

            // On the gamepad an element is picked with RB + a face button (ReadElementChord); these are the number keys.
            elementActions[0] = AddButton("Element1", "<Keyboard>/1", null);
            elementActions[1] = AddButton("Element2", "<Keyboard>/2", null);
            elementActions[2] = AddButton("Element3", "<Keyboard>/3", null);
            elementActions[3] = AddButton("Element4", "<Keyboard>/4", null);

            releaseCursor = AddButton("ReleaseCursor", "<Keyboard>/escape", null);

            // The sandbox tutorial: View (Back / Share / Select) on the gamepad, F7 and F8 on the keyboard.
            tutorialPad = AddButton("Tutorial", "<Gamepad>/select", null);
            tutorialKey = AddButton("TutorialKey", "<Keyboard>/f7", null);
            tutorialSkip = AddButton("TutorialSkip", "<Keyboard>/f8", null);

            // Menu buttons: Start pauses (like Esc); Y pages the F1 overlay while paused (gameplay input is off then).
            menuPad = AddButton("Menu", "<Gamepad>/start", null);
            overlayPagePad = AddButton("OverlayPage", "<Gamepad>/buttonNorth", null);

            sharedActions = new[]
            {
                move, attack, zip, dodge, jump, guard, skillPad, heal, lockOn, swapShoulder, switchLeft, switchRight,
                elementActions[0], elementActions[1], elementActions[2], elementActions[3], tutorialPad, tutorialKey, tutorialSkip,
                menuPad, overlayPagePad,
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
