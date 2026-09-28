// Compile-only stand-in for the parts of Unity's Input System package (com.unity.inputsystem 1.x)
// that Vaatu's Revenge uses. Unity never sees this file: it lives outside game/Assets and exists
// so a machine without Unity can check that the game's scripts compile.
//
// Every member here mirrors a real public member of the Input System with the same name and
// signature. Only add a member if you are certain it exists in the real package; if you need
// something that is missing, check the Input System docs first.
using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine.InputSystem.Controls;
using UnityEngine.InputSystem.Utilities;

namespace UnityEngine.InputSystem.Utilities
{
    public struct ReadOnlyArray<TValue> : IReadOnlyList<TValue>
    {
        public int Count => 0;
        public TValue this[int index] => default;
        public IEnumerator<TValue> GetEnumerator() { yield break; }
        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }
}

namespace UnityEngine.InputSystem
{
    public enum InputActionType { Value, Button, PassThrough }

    public enum InputActionPhase { Disabled, Waiting, Started, Performed, Canceled }

    public enum InputDeviceChange
    {
        Added, Removed, Disconnected, Reconnected, Enabled, Disabled, UsageChanged, ConfigurationChanged, SoftReset, HardReset
    }

    public sealed class InputAction : IDisposable
    {
        public InputAction(string name = null, InputActionType type = default, string binding = null,
            string interactions = null, string processors = null, string expectedControlType = null) { }

        public string name => null;
        public InputActionType type => default;
        public bool enabled => false;
        public bool triggered => false;
        public InputActionPhase phase => default;
        public InputControl activeControl => null;
        public InputActionMap actionMap => null;

        public event Action<CallbackContext> started;
        public event Action<CallbackContext> performed;
        public event Action<CallbackContext> canceled;

        public void Enable() { }
        public void Disable() { }
        public void Dispose() { }

        public TValue ReadValue<TValue>() where TValue : struct => default;
        public object ReadValueAsObject() => null;
        public bool IsPressed() => false;
        public bool IsInProgress() => false;
        public bool WasPressedThisFrame() => false;
        public bool WasReleasedThisFrame() => false;
        public bool WasPerformedThisFrame() => false;

        public struct CallbackContext
        {
            public InputAction action => null;
            public InputControl control => null;
            public InputActionPhase phase => default;
            public bool started => false;
            public bool performed => false;
            public bool canceled => false;
            public double time => 0;
            public double startTime => 0;
            public TValue ReadValue<TValue>() where TValue : struct => default;
            public bool ReadValueAsButton() => false;
        }
    }

    public sealed class InputActionMap : IDisposable, IEnumerable<InputAction>
    {
        public InputActionMap(string name = null) { }

        public string name => null;
        public bool enabled => false;
        public ReadOnlyArray<InputAction> actions => default;
        public InputAction this[string actionNameOrId] => null;

        public InputAction FindAction(string actionNameOrId, bool throwIfNotFound = false) => null;
        public void Enable() { }
        public void Disable() { }
        public void Dispose() { }

        public IEnumerator<InputAction> GetEnumerator() { yield break; }
        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }

    public static class InputActionSetupExtensions
    {
        public static InputAction AddAction(this InputActionMap map, string name, InputActionType type = default,
            string binding = null, string interactions = null, string processors = null, string groups = null,
            string expectedControlLayout = null) => null;

        public static BindingSyntax AddBinding(this InputAction action, string path, string interactions = null,
            string processors = null, string groups = null) => default;

        public static CompositeSyntax AddCompositeBinding(this InputAction action, string composite,
            string interactions = null, string processors = null) => default;

        public struct BindingSyntax
        {
            public BindingSyntax WithName(string name) => this;
            public BindingSyntax WithPath(string path) => this;
            public BindingSyntax WithGroup(string group) => this;
            public BindingSyntax WithGroups(string groups) => this;
            public BindingSyntax WithInteraction(string interaction) => this;
            public BindingSyntax WithInteractions(string interactions) => this;
            public BindingSyntax WithProcessor(string processor) => this;
            public BindingSyntax WithProcessors(string processors) => this;
        }

        public struct CompositeSyntax
        {
            public CompositeSyntax With(string name, string binding, string groups = null, string processors = null) => this;
        }
    }

    public abstract class InputControl
    {
        public string name => null;
        public string displayName => null;
        public string path => null;
        public InputDevice device => null;
    }

    public abstract class InputControl<TValue> : InputControl where TValue : struct
    {
        public TValue ReadValue() => default;
    }

    public class InputDevice : InputControl
    {
        public int deviceId => 0;
        public bool added => false;
        public bool enabled => false;
        public double lastUpdateTime => 0;
    }

    public class Pointer : InputDevice
    {
        public static Pointer current => null;
        public Vector2Control position => null;
        public DeltaControl delta => null;
        public ButtonControl press => null;
    }

    public class Mouse : Pointer
    {
        public static new Mouse current => null;
        public DeltaControl scroll => null;
        public ButtonControl leftButton => null;
        public ButtonControl rightButton => null;
        public ButtonControl middleButton => null;
        public ButtonControl forwardButton => null;
        public ButtonControl backButton => null;
    }

    public class Gamepad : InputDevice
    {
        public static Gamepad current => null;
        public static ReadOnlyArray<Gamepad> all => default;

        public ButtonControl buttonSouth => null;
        public ButtonControl buttonEast => null;
        public ButtonControl buttonWest => null;
        public ButtonControl buttonNorth => null;
        public ButtonControl aButton => null;
        public ButtonControl bButton => null;
        public ButtonControl xButton => null;
        public ButtonControl yButton => null;
        public ButtonControl crossButton => null;
        public ButtonControl circleButton => null;
        public ButtonControl squareButton => null;
        public ButtonControl triangleButton => null;
        public ButtonControl leftShoulder => null;
        public ButtonControl rightShoulder => null;
        public ButtonControl leftTrigger => null;
        public ButtonControl rightTrigger => null;
        public ButtonControl leftStickButton => null;
        public ButtonControl rightStickButton => null;
        public ButtonControl startButton => null;
        public ButtonControl selectButton => null;
        public StickControl leftStick => null;
        public StickControl rightStick => null;
        public DpadControl dpad => null;

        public void SetMotorSpeeds(float lowFrequency, float highFrequency) { }
        public void PauseHaptics() { }
        public void ResumeHaptics() { }
        public void ResetHaptics() { }
    }

    public class Keyboard : InputDevice
    {
        public static Keyboard current => null;
        public KeyControl this[Key key] => null;
        public AnyKeyControl anyKey => null;

        public KeyControl spaceKey => null;
        public KeyControl enterKey => null;
        public KeyControl tabKey => null;
        public KeyControl backquoteKey => null;
        public KeyControl quoteKey => null;
        public KeyControl semicolonKey => null;
        public KeyControl commaKey => null;
        public KeyControl periodKey => null;
        public KeyControl slashKey => null;
        public KeyControl backslashKey => null;
        public KeyControl leftBracketKey => null;
        public KeyControl rightBracketKey => null;
        public KeyControl minusKey => null;
        public KeyControl equalsKey => null;
        public KeyControl aKey => null;
        public KeyControl bKey => null;
        public KeyControl cKey => null;
        public KeyControl dKey => null;
        public KeyControl eKey => null;
        public KeyControl fKey => null;
        public KeyControl gKey => null;
        public KeyControl hKey => null;
        public KeyControl iKey => null;
        public KeyControl jKey => null;
        public KeyControl kKey => null;
        public KeyControl lKey => null;
        public KeyControl mKey => null;
        public KeyControl nKey => null;
        public KeyControl oKey => null;
        public KeyControl pKey => null;
        public KeyControl qKey => null;
        public KeyControl rKey => null;
        public KeyControl sKey => null;
        public KeyControl tKey => null;
        public KeyControl uKey => null;
        public KeyControl vKey => null;
        public KeyControl wKey => null;
        public KeyControl xKey => null;
        public KeyControl yKey => null;
        public KeyControl zKey => null;
        public KeyControl digit1Key => null;
        public KeyControl digit2Key => null;
        public KeyControl digit3Key => null;
        public KeyControl digit4Key => null;
        public KeyControl digit5Key => null;
        public KeyControl digit6Key => null;
        public KeyControl digit7Key => null;
        public KeyControl digit8Key => null;
        public KeyControl digit9Key => null;
        public KeyControl digit0Key => null;
        public KeyControl leftShiftKey => null;
        public KeyControl rightShiftKey => null;
        public KeyControl leftAltKey => null;
        public KeyControl rightAltKey => null;
        public KeyControl leftCtrlKey => null;
        public KeyControl rightCtrlKey => null;
        public KeyControl escapeKey => null;
        public KeyControl leftArrowKey => null;
        public KeyControl rightArrowKey => null;
        public KeyControl upArrowKey => null;
        public KeyControl downArrowKey => null;
        public KeyControl backspaceKey => null;
        public KeyControl pageDownKey => null;
        public KeyControl pageUpKey => null;
        public KeyControl homeKey => null;
        public KeyControl endKey => null;
        public KeyControl insertKey => null;
        public KeyControl deleteKey => null;
        public KeyControl f1Key => null;
        public KeyControl f2Key => null;
        public KeyControl f3Key => null;
        public KeyControl f4Key => null;
        public KeyControl f5Key => null;
        public KeyControl f6Key => null;
        public KeyControl f7Key => null;
        public KeyControl f8Key => null;
        public KeyControl f9Key => null;
        public KeyControl f10Key => null;
        public KeyControl f11Key => null;
        public KeyControl f12Key => null;
    }

    public enum Key
    {
        None, Space, Enter, Tab, Backquote, Quote, Semicolon, Comma, Period, Slash, Backslash,
        LeftBracket, RightBracket, Minus, Equals,
        A, B, C, D, E, F, G, H, I, J, K, L, M, N, O, P, Q, R, S, T, U, V, W, X, Y, Z,
        Digit1, Digit2, Digit3, Digit4, Digit5, Digit6, Digit7, Digit8, Digit9, Digit0,
        LeftShift, RightShift, LeftAlt, RightAlt, LeftCtrl, RightCtrl, LeftMeta, RightMeta, ContextMenu,
        Escape, LeftArrow, RightArrow, UpArrow, DownArrow, Backspace, PageDown, PageUp, Home, End, Insert, Delete,
        CapsLock, NumLock, PrintScreen, ScrollLock, Pause,
        NumpadEnter, NumpadDivide, NumpadMultiply, NumpadPlus, NumpadMinus, NumpadPeriod, NumpadEquals,
        Numpad0, Numpad1, Numpad2, Numpad3, Numpad4, Numpad5, Numpad6, Numpad7, Numpad8, Numpad9,
        F1, F2, F3, F4, F5, F6, F7, F8, F9, F10, F11, F12
    }

    public static class InputSystem
    {
        public static event Action<InputDevice, InputDeviceChange> onDeviceChange;
        public static ReadOnlyArray<InputDevice> devices => default;
        public static void PauseHaptics() { }
        public static void ResumeHaptics() { }
        public static void ResetHaptics() { }
    }
}

namespace UnityEngine.InputSystem.Controls
{
    public class AxisControl : InputControl<float> { }

    public class ButtonControl : AxisControl
    {
        public float pressPoint => 0;
        public bool isPressed => false;
        public bool wasPressedThisFrame => false;
        public bool wasReleasedThisFrame => false;
    }

    public class KeyControl : ButtonControl
    {
        public Key keyCode => default;
    }

    public class AnyKeyControl : ButtonControl { }

    public class Vector2Control : InputControl<Vector2>
    {
        public AxisControl x => null;
        public AxisControl y => null;
    }

    public class DeltaControl : Vector2Control { }

    public class StickControl : Vector2Control
    {
        public ButtonControl up => null;
        public ButtonControl down => null;
        public ButtonControl left => null;
        public ButtonControl right => null;
    }

    public class DpadControl : Vector2Control
    {
        public ButtonControl up => null;
        public ButtonControl down => null;
        public ButtonControl left => null;
        public ButtonControl right => null;
    }
}
