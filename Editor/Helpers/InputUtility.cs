#if UNITY_REPL_HAS_INPUT_SYSTEM
using System;
using System.Collections.Generic;
using System.Reflection;
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
    /// 1. <c>InputState.Change()</c> alone doesn't work — every frame, NativeInputRuntime
    ///    delivers hardware events that overwrite injected state. We hook
    ///    <c>NativeInputRuntime.instance.onUpdate</c> via reflection (same technique as
    ///    Unity's <c>InputTestFixture</c>) and re-inject held keys/buttons after each
    ///    native update.
    /// 2. Mouse coordinates use screenshot space (top-left origin). InputSystem uses
    ///    bottom-left. We Y-flip via <c>Camera.main.pixelHeight</c> because
    ///    <c>Screen.height</c> is broken in Editor Play Mode on macOS (returns the
    ///    focused Editor window's height, often 20px).
    /// 3. <c>editorInputBehaviorInPlayMode = AllDeviceInputAlwaysGoesToGameView</c>
    ///    routes input to the game regardless of which Editor panel has focus.
    /// 4. Static state is reset on domain reload via <c>[InitializeOnLoad] ReloadWatcher</c>
    ///    since reflection hooks become stale after recompilation.
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
        private static bool s_nativeHookInstalled;
        private static Delegate s_originalOnUpdate;
        private static MethodInfo s_originalOnUpdateMethod;
        private static object s_originalOnUpdateTarget;
        private static readonly object[] s_interceptorArgs = new object[2];
        private static PropertyInfo s_onUpdateProp;
        private static object s_nativeRuntimeInstance;
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
                s_nativeHookInstalled = false;
                s_originalOnUpdate = null;
                s_originalOnUpdateMethod = null;
                s_originalOnUpdateTarget = null;
                s_onUpdateProp = null;
                s_nativeRuntimeInstance = null;
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
            EnsureNativeInputHook();
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
            EnsureNativeInputHook();

            if (pressed) s_heldKeys.Add(key);
            else s_heldKeys.Remove(key);

            using (StateEvent.From(keyboard, out var eventPtr))
            {
                keyboard[key].WriteValueIntoEvent(pressed ? 1f : 0f, eventPtr);
                InputState.Change(keyboard, eventPtr);
            }
        }

        private static void SetMouseButtonState(int button, bool pressed)
        {
            if (button < 0) return;
            var mouse = Mouse.current;
            var control = GetMouseButtonControl(mouse, button);
            if (control == null) return;

            EnsureInputRoutesToGameView();
            EnsureNativeInputHook();

            if (pressed) s_heldMouseButtons.Add(button);
            else s_heldMouseButtons.Remove(button);

            using (StateEvent.From(mouse, out var eventPtr))
            {
                control.WriteValueIntoEvent(pressed ? 1f : 0f, eventPtr);
                InputState.Change(mouse, eventPtr);
            }
        }

        // ── Native input hook ────────────────────────────────────────

        private static void EnsureNativeInputHook()
        {
            if (s_nativeHookInstalled) return;

            try
            {
                var isAssembly = typeof(InputSystem).Assembly;
                var nativeRuntimeType = isAssembly.GetType(
                    "UnityEngine.InputSystem.LowLevel.NativeInputRuntime");
                if (nativeRuntimeType == null)
                {
                    Debug.LogWarning("[InputUtility] Cannot find NativeInputRuntime type.");
                    return;
                }

                var instanceField = nativeRuntimeType.GetField("instance",
                    BindingFlags.Public | BindingFlags.Static);
                s_nativeRuntimeInstance = instanceField?.GetValue(null);
                if (s_nativeRuntimeInstance == null)
                {
                    Debug.LogWarning("[InputUtility] NativeInputRuntime.instance is null.");
                    return;
                }

                s_onUpdateProp = nativeRuntimeType.GetProperty("onUpdate",
                    BindingFlags.Public | BindingFlags.Instance | BindingFlags.NonPublic);
                if (s_onUpdateProp == null)
                {
                    Debug.LogWarning("[InputUtility] Cannot find onUpdate property.");
                    return;
                }

                s_originalOnUpdate = s_onUpdateProp.GetValue(s_nativeRuntimeInstance) as Delegate;

                var delegateType = isAssembly.GetType(
                    "UnityEngine.InputSystem.LowLevel.InputUpdateDelegate");
                if (delegateType == null)
                {
                    Debug.LogWarning("[InputUtility] Cannot find InputUpdateDelegate type.");
                    return;
                }

                var interceptMethod = typeof(InputUtility).GetMethod(
                    nameof(NativeInputInterceptor), BindingFlags.NonPublic | BindingFlags.Static);
                var interceptDelegate = Delegate.CreateDelegate(delegateType, interceptMethod);

                s_onUpdateProp.SetValue(s_nativeRuntimeInstance, interceptDelegate);

                s_originalOnUpdateMethod = s_originalOnUpdate?.Method;
                s_originalOnUpdateTarget = s_originalOnUpdate?.Target;

                EditorApplication.playModeStateChanged += OnPlayModeChanged;
                s_nativeHookInstalled = true;
            }
            catch (Exception ex)
            {
                Debug.LogError($"[InputUtility] Failed to install native input hook: {ex}");
            }
        }

        private static void NativeInputInterceptor(InputUpdateType updateType, ref InputEventBuffer eventBuffer)
        {
            // Let hardware events through (don't Reset — that breaks Screen.width/height etc.),
            // then overwrite keyboard/mouse state AFTER the update processes.
            if (s_originalOnUpdateMethod != null)
            {
                s_interceptorArgs[0] = updateType;
                s_interceptorArgs[1] = eventBuffer;
                s_originalOnUpdateMethod.Invoke(s_originalOnUpdateTarget, s_interceptorArgs);
                eventBuffer = (InputEventBuffer)s_interceptorArgs[1];
            }

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
