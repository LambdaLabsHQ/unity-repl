#if UNITY_REPL_HAS_INPUT_SYSTEM
using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Controls;
using UnityEngine.InputSystem.LowLevel;

namespace LambdaLabs.UnityRepl.Editor.Helpers
{
    /// <summary>
    /// Simulate keyboard and mouse input from Editor scripts / REPL during Play Mode.
    /// Press/release state persists across frames until explicitly released.
    ///
    /// Why this utility exists (tricks not discoverable from Unity docs):
    /// 1. <c>InputState.Change()</c> called mid-frame gets overwritten when InputSystem
    ///    processes the hardware event buffer that same frame. We hook
    ///    <c>InputSystem.onAfterUpdate</c> to re-inject held keys AFTER the event buffer
    ///    has been fully processed, ensuring hardware events can never override synthetic state.
    /// 2. Mouse coordinates use screenshot space (top-left origin). InputSystem uses
    ///    bottom-left. We Y-flip via <c>Camera.main.pixelHeight</c> because
    ///    <c>Screen.height</c> is broken in Editor Play Mode on macOS (returns the
    ///    focused Editor window's height, often 20px).
    /// 3. <c>editorInputBehaviorInPlayMode = AllDeviceInputAlwaysGoesToGameView</c>
    ///    routes input to the game regardless of which Editor panel has focus.
    /// 4. Static state is reset on domain reload via <c>[InitializeOnLoad] ReloadWatcher</c>
    ///    since event hooks become stale after recompilation.
    ///
    /// Example usage from REPL:
    /// <code>
    /// using LambdaLabs.UnityRepl.Editor.Helpers;
    /// using UnityEngine.InputSystem;
    /// InputUtility.PressKey(Key.W);   // character starts moving forward
    /// // (advance frames via EditorApplication.Step / play_for_frames)
    /// InputUtility.ReleaseKey(Key.W); // character stops
    /// </code>
    /// </summary>
    public static class InputUtility
    {
        // ── State ────────────────────────────────────────────────────

        private static readonly HashSet<Key> s_heldKeys = new HashSet<Key>();
        private static readonly HashSet<int> s_heldMouseButtons = new HashSet<int>();
        private static bool s_onAfterUpdateHooked;
        private static bool s_inputRouteConfigured;
        private static Type s_gameViewType;

        private static Type GameViewType => s_gameViewType ??=
            typeof(EditorWindow).Assembly.GetType("UnityEditor.GameView");

        private static bool HasSyntheticInput =>
            s_heldKeys.Count > 0 || s_heldMouseButtons.Count > 0;

        // ── Domain reload safety ─────────────────────────────────────

        [InitializeOnLoad]
        private static class ReloadWatcher
        {
            static ReloadWatcher()
            {
                s_heldKeys.Clear();
                s_heldMouseButtons.Clear();
                s_onAfterUpdateHooked = false;
                s_inputRouteConfigured = false;
            }
        }

        // ── Public API ───────────────────────────────────────────────

        /// <summary>Press a keyboard key. State persists until <see cref="ReleaseKey"/>.</summary>
        public static void PressKey(Key key) => SetKeyState(key, true);

        /// <summary>Release a keyboard key.</summary>
        public static void ReleaseKey(Key key) => SetKeyState(key, false);

        /// <summary>Press a mouse button. Button: "left", "right", "middle", "forward", "back" (or 0-4).</summary>
        public static void PressMouseButton(string button) => SetMouseButtonState(ResolveMouseButton(button), true);

        /// <summary>Release a mouse button.</summary>
        public static void ReleaseMouseButton(string button) => SetMouseButtonState(ResolveMouseButton(button), false);

        /// <summary>
        /// Set mouse position in screenshot space (top-left origin, as seen in
        /// <see cref="GameViewCaptureUtility"/> output). Internally Y-flips to
        /// InputSystem screen space using <c>Camera.main.pixelHeight</c>.
        /// </summary>
        public static void SetMousePosition(float screenshotX, float screenshotY)
        {
            var mouse = Mouse.current;
            if (mouse == null) return;

            EnsureInputRoutesToGameView();
            EnsureOnAfterUpdateHook();
            FocusGameView();

            float inputY = ConvertScreenshotY(screenshotY);

            using (StateEvent.From(mouse, out var eventPtr))
            {
                mouse.position.WriteValueIntoEvent(new Vector2(screenshotX, inputY), eventPtr);
                InputState.Change(mouse, eventPtr);
            }
        }

        /// <summary>Release everything: all keys, all mouse buttons, all held state.</summary>
        public static void ClearAllInput()
        {
            s_heldKeys.Clear();
            s_heldMouseButtons.Clear();

            var keyboard = Keyboard.current;
            if (keyboard != null)
            {
                using (StateEvent.From(keyboard, out var eventPtr))
                {
                    foreach (Key key in Enum.GetValues(typeof(Key)))
                    {
                        if (key == Key.None || key == Key.IMESelected) continue;
                        try { keyboard[key].WriteValueIntoEvent(0f, eventPtr); }
                        catch (ArgumentException) { /* enum values without a physical key */ }
                    }
                    InputState.Change(keyboard, eventPtr);
                }
            }

            var mouse = Mouse.current;
            if (mouse != null)
            {
                using (StateEvent.From(mouse, out var eventPtr))
                {
                    mouse.leftButton.WriteValueIntoEvent(0f, eventPtr);
                    mouse.rightButton.WriteValueIntoEvent(0f, eventPtr);
                    mouse.middleButton.WriteValueIntoEvent(0f, eventPtr);
                    mouse.forwardButton.WriteValueIntoEvent(0f, eventPtr);
                    mouse.backButton.WriteValueIntoEvent(0f, eventPtr);
                    InputState.Change(mouse, eventPtr);
                }
            }
        }

        // ── State application ────────────────────────────────────────

        private static void SetKeyState(Key key, bool pressed)
        {
            var keyboard = Keyboard.current;
            if (keyboard == null) return;

            EnsureInputRoutesToGameView();
            EnsureOnAfterUpdateHook();

            if (pressed)
            {
                s_heldKeys.Add(key);
                // Do NOT call InputState.Change here.
                // ReapplySyntheticState() fires on the next InputSystem.Update() call,
                // ensuring previousStatePtr is still 0 at that point so that
                // wasPressedThisFrame correctly returns true on the first press frame.
            }
            else
            {
                s_heldKeys.Remove(key);
                // Must clear immediately: no hardware "key up" event will ever arrive
                // for a synthetically-held key, so the bit would stay set indefinitely.
                using (StateEvent.From(keyboard, out var eventPtr))
                {
                    keyboard[key].WriteValueIntoEvent(0f, eventPtr);
                    InputState.Change(keyboard, eventPtr);
                }
            }
        }

        private static void SetMouseButtonState(int button, bool pressed)
        {
            if (button < 0) return;
            var mouse = Mouse.current;
            var control = GetMouseButtonControl(mouse, button);
            if (control == null) return;

            EnsureInputRoutesToGameView();
            EnsureOnAfterUpdateHook();

            if (pressed)
            {
                s_heldMouseButtons.Add(button);
                // Same reasoning as SetKeyState: defer first injection to
                // ReapplySyntheticState() so previousStatePtr is 0 on that frame,
                // allowing wasPressedThisFrame to correctly return true on the first press frame.
            }
            else
            {
                s_heldMouseButtons.Remove(button);
                // Must clear immediately: no hardware button-up event will ever arrive
                // for a synthetically-held button, so the bit would stay set indefinitely.
                using (StateEvent.From(mouse, out var eventPtr))
                {
                    control.WriteValueIntoEvent(0f, eventPtr);
                    InputState.Change(mouse, eventPtr);
                }
            }
        }

        // ── InputSystem.onAfterUpdate hook ───────────────────────────
        // Fires after InputSystem.Update() has fully processed the hardware
        // event buffer. Injecting state here ensures hardware events can't
        // overwrite our synthetic keys on the same frame.

        private static void EnsureOnAfterUpdateHook()
        {
            if (s_onAfterUpdateHooked) return;
            InputSystem.onAfterUpdate += OnAfterInputSystemUpdate;
            EditorApplication.playModeStateChanged += OnPlayModeChanged;
            s_onAfterUpdateHooked = true;
        }

        private static void OnAfterInputSystemUpdate()
        {
            if (HasSyntheticInput)
                ReapplySyntheticState();
        }

        private static void ReapplySyntheticState()
        {
            if (s_heldKeys.Count > 0)
            {
                var keyboard = Keyboard.current;
                if (keyboard != null)
                {
                    using (StateEvent.From(keyboard, out var eventPtr))
                    {
                        foreach (var key in s_heldKeys)
                            keyboard[key].WriteValueIntoEvent(1f, eventPtr);
                        InputState.Change(keyboard, eventPtr);
                    }
                }
            }

            if (s_heldMouseButtons.Count > 0)
            {
                var mouse = Mouse.current;
                if (mouse != null)
                {
                    using (StateEvent.From(mouse, out var eventPtr))
                    {
                        foreach (var btn in s_heldMouseButtons)
                        {
                            var control = GetMouseButtonControl(mouse, btn);
                            if (control != null)
                                control.WriteValueIntoEvent(1f, eventPtr);
                        }
                        InputState.Change(mouse, eventPtr);
                    }
                }
            }
        }

        private static void OnPlayModeChanged(PlayModeStateChange change)
        {
            if (change == PlayModeStateChange.ExitingPlayMode)
            {
                s_heldKeys.Clear();
                s_heldMouseButtons.Clear();
            }
        }

        // ── Helpers ──────────────────────────────────────────────────

        private static void EnsureInputRoutesToGameView()
        {
            if (s_inputRouteConfigured) return;
            var settings = InputSystem.settings;
            if (settings != null)
            {
                settings.editorInputBehaviorInPlayMode =
                    InputSettings.EditorInputBehaviorInPlayMode.AllDeviceInputAlwaysGoesToGameView;
            }
            s_inputRouteConfigured = true;
        }

        private static void FocusGameView()
        {
            if (GameViewType != null)
                EditorWindow.FocusWindowIfItsOpen(GameViewType);
        }

        private static float ConvertScreenshotY(float y)
        {
            // Camera.pixelHeight is reliable; Screen.height is broken in Editor Play Mode.
            var cam = Camera.main;
            return cam != null ? cam.pixelHeight - y : y;
        }

        private static ButtonControl GetMouseButtonControl(Mouse mouse, int button)
        {
            if (mouse == null) return null;
            return button switch
            {
                0 => mouse.leftButton,
                1 => mouse.rightButton,
                2 => mouse.middleButton,
                3 => mouse.forwardButton,
                4 => mouse.backButton,
                _ => null,
            };
        }

        private static int ResolveMouseButton(string name)
        {
            if (string.IsNullOrEmpty(name)) return -1;
            switch (name.ToLowerInvariant())
            {
                case "left": case "0": return 0;
                case "right": case "1": return 1;
                case "middle": case "2": return 2;
                case "forward": case "3": return 3;
                case "back": case "4": return 4;
                default:
                    return int.TryParse(name, out var n) && n >= 0 && n <= 4 ? n : -1;
            }
        }
    }
}
#endif
