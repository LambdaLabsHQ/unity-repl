#if NATIVE_MCP_HAS_INPUT_SYSTEM
using System;
using System.Collections.Generic;
using System.Reflection;
using NativeMcp.Editor.Helpers;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Controls;
using UnityEngine.InputSystem.LowLevel;

namespace NativeMcp.Editor.Tools.Input
{
    [McpForUnityTool("simulate_input", Internal = true,
        Description = "Simulate keyboard and mouse input in Play Mode via InputSystem low-level events.")]
    public static class SimulateInput
    {
        // ── Native input interception ─────────────────────────────────
        // Each frame, NativeInputRuntime delivers hardware events that overwrite
        // any state we injected via InputState.Change. We hook onUpdate via
        // reflection, let hardware events through, then overwrite keyboard/mouse
        // state with our synthetic values. Same technique as InputTestFixture.

        private static readonly HashSet<Key> s_heldKeys = new HashSet<Key>();
        private static readonly HashSet<int> s_heldMouseButtons = new HashSet<int>();
        private static bool s_nativeHookInstalled;
        private static Delegate s_originalOnUpdate;
        private static MethodInfo s_originalOnUpdateMethod;
        private static object s_originalOnUpdateTarget;
        private static readonly object[] s_interceptorArgs = new object[2];
        private static PropertyInfo s_onUpdateProp;
        private static object s_nativeRuntimeInstance;

        // ── Domain reload safety ─────────────────────────────────────
        // Static fields survive domain reload but become stale (reflection
        // targets point to dead instances). Reset everything so the hook
        // is lazily re-installed on the next unity_input call.
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

        private static bool HasSyntheticInput => s_heldKeys.Count > 0 || s_heldMouseButtons.Count > 0;

        /// <summary>
        /// Hook into NativeInputRuntime.instance.onUpdate to intercept and
        /// discard hardware input events while MCP synthetic input is active.
        /// Uses the onUpdate property setter which wraps our delegate into the
        /// native callback chain. Our delegate discards hardware events when
        /// synthetic input is held, then re-injects our state after the update.
        /// </summary>
        private static void EnsureNativeInputHook()
        {
            if (s_nativeHookInstalled) return;

            try
            {
                // Resolve NativeInputRuntime.instance via reflection (it's internal)
                var isAssembly = typeof(InputSystem).Assembly;
                var nativeRuntimeType = isAssembly.GetType(
                    "UnityEngine.InputSystem.LowLevel.NativeInputRuntime");
                if (nativeRuntimeType == null)
                {
                    McpLog.Warn("[SimulateInput] Cannot find NativeInputRuntime type.");
                    return;
                }

                var instanceField = nativeRuntimeType.GetField("instance",
                    BindingFlags.Public | BindingFlags.Static);
                s_nativeRuntimeInstance = instanceField?.GetValue(null);
                if (s_nativeRuntimeInstance == null)
                {
                    McpLog.Warn("[SimulateInput] NativeInputRuntime.instance is null.");
                    return;
                }

                // Get the onUpdate property — its type is the internal InputUpdateDelegate
                s_onUpdateProp = nativeRuntimeType.GetProperty("onUpdate",
                    BindingFlags.Public | BindingFlags.Instance | BindingFlags.NonPublic);
                if (s_onUpdateProp == null)
                {
                    McpLog.Warn("[SimulateInput] Cannot find onUpdate property.");
                    return;
                }

                // Save the original delegate (InputUpdateDelegate)
                s_originalOnUpdate = s_onUpdateProp.GetValue(s_nativeRuntimeInstance) as Delegate;

                // InputUpdateDelegate is internal, so we need to get the Type and use
                // Delegate.CreateDelegate with our method that has matching signature
                var delegateType = isAssembly.GetType(
                    "UnityEngine.InputSystem.LowLevel.InputUpdateDelegate");
                if (delegateType == null)
                {
                    McpLog.Warn("[SimulateInput] Cannot find InputUpdateDelegate type.");
                    return;
                }

                var interceptMethod = typeof(SimulateInput).GetMethod(
                    nameof(NativeInputInterceptor), BindingFlags.NonPublic | BindingFlags.Static);
                var interceptDelegate = Delegate.CreateDelegate(delegateType, interceptMethod);

                s_onUpdateProp.SetValue(s_nativeRuntimeInstance, interceptDelegate);

                // Cache method/target for fast invocation in the per-frame interceptor
                s_originalOnUpdateMethod = s_originalOnUpdate?.Method;
                s_originalOnUpdateTarget = s_originalOnUpdate?.Target;

                EditorApplication.playModeStateChanged += OnPlayModeChanged;
                s_nativeHookInstalled = true;

                McpLog.Info("[SimulateInput] Native input hook installed successfully.");
            }
            catch (Exception ex)
            {
                McpLog.Error($"[SimulateInput] Failed to install native input hook: {ex}");
            }
        }

        /// <summary>
        /// The interceptor that replaces NativeInputRuntime.onUpdate.
        /// Signature must match: delegate void InputUpdateDelegate(InputUpdateType, ref InputEventBuffer)
        /// When MCP synthetic input is active, we discard hardware events and re-inject ours.
        /// </summary>
        private static void NativeInputInterceptor(InputUpdateType updateType, ref InputEventBuffer eventBuffer)
        {
            // Do NOT call eventBuffer.Reset() — that would discard ALL native events
            // including window resize, display config changes, etc., which breaks
            // Screen.width/height and other system state.
            // Instead, let all events through normally, then overwrite keyboard/mouse
            // state with our synthetic values AFTER the update processes them.

            // Call the original handler (triggers InputSystem.Update internally).
            // Uses cached MethodInfo/target to avoid per-frame reflection overhead.
            if (s_originalOnUpdateMethod != null)
            {
                s_interceptorArgs[0] = updateType;
                s_interceptorArgs[1] = eventBuffer;
                s_originalOnUpdateMethod.Invoke(s_originalOnUpdateTarget, s_interceptorArgs);
                eventBuffer = (InputEventBuffer)s_interceptorArgs[1];
            }

            if (HasSyntheticInput)
            {
                // Overwrite keyboard/mouse state with our held keys/buttons
                // after hardware events have been processed
                ReapplySyntheticState();
            }
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

        // ── MCP parameter schema ───────────────────────────────────────

        public class Parameters
        {
            [ToolParameter("Action: key_down, key_up, type_text, mouse_button_down, mouse_button_up, " +
                           "mouse_move, mouse_scroll, click, release_all")]
            public string action { get; set; }

            [ToolParameter("Key name (e.g. 'w', 'space', 'shift', 'escape'). Case-insensitive.", Required = false)]
            public string key { get; set; }

            [ToolParameter("Mouse button: 'left', 'right', 'middle' (or 0, 1, 2)", Required = false)]
            public string button { get; set; }

            [ToolParameter("Relative mouse X movement in pixels", Required = false)]
            public float? delta_x { get; set; }

            [ToolParameter("Relative mouse Y movement in pixels", Required = false)]
            public float? delta_y { get; set; }

            [ToolParameter("Absolute mouse X position in screen pixels", Required = false)]
            public float? position_x { get; set; }

            [ToolParameter("Absolute mouse Y position in screen pixels", Required = false)]
            public float? position_y { get; set; }

            [ToolParameter("Horizontal scroll amount (120 = one notch)", Required = false)]
            public float? scroll_x { get; set; }

            [ToolParameter("Vertical scroll amount (120 = one notch)", Required = false)]
            public float? scroll_y { get; set; }

            [ToolParameter("Key name or JSON array of key names for type_text", Required = false)]
            public string keys { get; set; }
        }

        // ── Maps ───────────────────────────────────────────────────────

        private static readonly Dictionary<string, Key> KeyNameMap = BuildKeyNameMap();

        private static readonly Dictionary<string, int> MouseButtonMap = new(StringComparer.OrdinalIgnoreCase)
        {
            { "left", 0 }, { "right", 1 }, { "middle", 2 }, { "forward", 3 }, { "back", 4 },
        };

        // ====================================================================
        //  Main handler
        // ====================================================================

        public static object HandleCommand(JObject @params)
        {
            if (!EditorApplication.isPlaying)
                return new ErrorResponse(
                    "Input simulation requires Play Mode. Use unity_editor action 'play' first.");

            // Force all device input to route to Game View regardless of focus
            EnsureInputRoutesToGameView();

            // Install native input hook to prevent hardware from overwriting synthetic state
            EnsureNativeInputHook();

            string action = @params["action"]?.ToString();
            if (string.IsNullOrEmpty(action))
                return new ErrorResponse(
                    "Missing required parameter 'action'. " +
                    "Supported: key_down, key_up, type_text, mouse_button_down, mouse_button_up, " +
                    "mouse_move, mouse_scroll, click, release_all");

            try
            {
                switch (action.ToLowerInvariant())
                {
                    case "key_down":            return HandleKeyDown(@params);
                    case "key_up":              return HandleKeyUp(@params);
                    case "type_text":           return HandleTypeText(@params);
                    case "mouse_button_down":   return HandleMouseButtonDown(@params);
                    case "mouse_button_up":     return HandleMouseButtonUp(@params);
                    case "mouse_move":          return HandleMouseMove(@params);
                    case "mouse_scroll":        return HandleMouseScroll(@params);
                    case "click":               return HandleClick(@params);
                    case "release_all":         return HandleReleaseAll();
                    default:
                        return new ErrorResponse(
                            $"Unknown action: '{action}'. " +
                            "Supported: key_down, key_up, type_text, mouse_button_down, mouse_button_up, " +
                            "mouse_move, mouse_scroll, click, release_all");
                }
            }
            catch (Exception ex)
            {
                return new ErrorResponse($"Input simulation error: {ex.Message}");
            }
        }

        // ====================================================================
        //  Keyboard actions
        // ====================================================================

        private static object HandleKeyDown(JObject @params)
        {
            if (Keyboard.current == null)
                return new ErrorResponse("No keyboard device found.");
            var key = ResolveKey(@params["key"]?.ToString());
            if (key == null) return KeyError(@params["key"]?.ToString());
            QueueKeyState(key.Value, true);
            return new SuccessResponse($"Key '{key.Value}' pressed (down).");
        }

        private static object HandleKeyUp(JObject @params)
        {
            if (Keyboard.current == null)
                return new ErrorResponse("No keyboard device found.");
            var key = ResolveKey(@params["key"]?.ToString());
            if (key == null) return KeyError(@params["key"]?.ToString());
            QueueKeyState(key.Value, false);
            return new SuccessResponse($"Key '{key.Value}' released (up).");
        }

        private static object HandleTypeText(JObject @params)
        {
            if (Keyboard.current == null)
                return new ErrorResponse("No keyboard device found.");

            var keysToken = @params["keys"];
            if (keysToken == null)
                return new ErrorResponse("'keys' parameter is required for type_text.");

            var keyNames = new List<string>();
            if (keysToken is JArray arr)
            {
                foreach (var item in arr)
                    keyNames.Add(item.ToString());
            }
            else
            {
                keyNames.Add(keysToken.ToString());
            }

            var resolved = new List<Key>();
            foreach (var name in keyNames)
            {
                var key = ResolveKey(name);
                if (key == null) return KeyError(name);
                resolved.Add(key.Value);
            }

            foreach (var key in resolved)
            {
                QueueKeyState(key, true);
                QueueKeyState(key, false);
            }

            return new SuccessResponse($"Typed {resolved.Count} key(s): [{string.Join(", ", resolved)}]");
        }

        // ====================================================================
        //  Mouse button actions
        // ====================================================================

        private static object HandleMouseButtonDown(JObject @params)
        {
            if (Mouse.current == null)
                return new ErrorResponse("No mouse device found.");
            int button = ResolveMouseButton(@params["button"]?.ToString());
            if (button < 0)
                return new ErrorResponse("'button' parameter is required (0/left, 1/right, 2/middle, 3/forward, 4/back).");
            QueueMouseButtonState(button, true);
            return new SuccessResponse($"Mouse button {button} pressed (down).");
        }

        private static object HandleMouseButtonUp(JObject @params)
        {
            if (Mouse.current == null)
                return new ErrorResponse("No mouse device found.");
            int button = ResolveMouseButton(@params["button"]?.ToString());
            if (button < 0)
                return new ErrorResponse("'button' parameter is required (0/left, 1/right, 2/middle, 3/forward, 4/back).");
            QueueMouseButtonState(button, false);
            return new SuccessResponse($"Mouse button {button} released (up).");
        }

        // ====================================================================
        //  Mouse movement
        // ====================================================================

        private static object HandleMouseMove(JObject @params)
        {
            var mouse = Mouse.current;
            if (mouse == null)
                return new ErrorResponse("No mouse device found.");

            float? dx = @params["delta_x"]?.ToObject<float>();
            float? dy = @params["delta_y"]?.ToObject<float>();
            float? x  = @params["position_x"]?.ToObject<float>();
            float? y  = @params["position_y"]?.ToObject<float>();

            if (dx == null && dy == null && x == null && y == null)
                return new ErrorResponse(
                    "At least one of 'delta_x'/'delta_y' (relative) or 'position_x'/'position_y' (absolute) is required.");

            // Focus Game View so Screen.width/height return correct values.
            // UGUI's GraphicRaycaster depends on Screen dimensions for hit testing.
            FocusGameView();

            // Convert Y from screenshot space (top-left origin) to
            // InputSystem screen space (bottom-left origin).
            if (y.HasValue)
                y = ConvertScreenshotY(y.Value);

            using (StateEvent.From(mouse, out var eventPtr))
            {
                if (dx.HasValue || dy.HasValue)
                    mouse.delta.WriteValueIntoEvent(new Vector2(dx ?? 0f, dy ?? 0f), eventPtr);

                if (x.HasValue || y.HasValue)
                {
                    var currentPos = mouse.position.ReadValue();
                    mouse.position.WriteValueIntoEvent(
                        new Vector2(x ?? currentPos.x, y ?? currentPos.y), eventPtr);
                }

                InputState.Change(mouse, eventPtr);
            }

            string details = "";
            if (dx.HasValue || dy.HasValue) details += $"delta=({dx ?? 0},{dy ?? 0}) ";
            if (x.HasValue || y.HasValue) details += $"position=({x},{y})";
            return new SuccessResponse($"Mouse moved: {details.Trim()}");
        }

        // ====================================================================
        //  Mouse scroll
        // ====================================================================

        private static object HandleMouseScroll(JObject @params)
        {
            var mouse = Mouse.current;
            if (mouse == null)
                return new ErrorResponse("No mouse device found.");

            float scrollX = @params["scroll_x"]?.ToObject<float>() ?? 0f;
            float scrollY = @params["scroll_y"]?.ToObject<float>() ?? 0f;

            if (Math.Abs(scrollX) < 0.001f && Math.Abs(scrollY) < 0.001f)
                return new ErrorResponse("At least one of 'scroll_x' or 'scroll_y' must be non-zero.");

            using (StateEvent.From(mouse, out var eventPtr))
            {
                mouse.scroll.WriteValueIntoEvent(new Vector2(scrollX, scrollY), eventPtr);
                InputState.Change(mouse, eventPtr);
            }

            return new SuccessResponse($"Mouse scrolled: ({scrollX}, {scrollY})");
        }

        // ====================================================================
        //  Click — convenience: move (optional) + press + release
        // ====================================================================

        private static object HandleClick(JObject @params)
        {
            var mouse = Mouse.current;
            if (mouse == null)
                return new ErrorResponse("No mouse device found.");

            string buttonRaw = @params["button"]?.ToString() ?? "left";
            int buttonIdx = ResolveMouseButton(buttonRaw);
            if (buttonIdx < 0)
                return new ErrorResponse($"Unknown mouse button: '{buttonRaw}'.");

            float? x = @params["position_x"]?.ToObject<float>();
            float? y = @params["position_y"]?.ToObject<float>();

            if (x.HasValue || y.HasValue)
            {
                FocusGameView();
                if (y.HasValue)
                    y = ConvertScreenshotY(y.Value);

                using (StateEvent.From(mouse, out var eventPtr))
                {
                    var currentPos = mouse.position.ReadValue();
                    mouse.position.WriteValueIntoEvent(
                        new Vector2(x ?? currentPos.x, y ?? currentPos.y), eventPtr);
                    InputState.Change(mouse, eventPtr);
                }
            }

            QueueMouseButtonState(buttonIdx, true);
            QueueMouseButtonState(buttonIdx, false);

            string pos = (x.HasValue || y.HasValue) ? $" at ({x}, {y})" : " at current position";
            return new SuccessResponse($"Clicked {buttonRaw} button{pos}.");
        }

        // ====================================================================
        //  Release all — safety valve
        // ====================================================================

        private static object HandleReleaseAll()
        {
            s_heldKeys.Clear();
            s_heldMouseButtons.Clear();
            int released = 0;

            var keyboard = Keyboard.current;
            if (keyboard != null)
            {
                using (StateEvent.From(keyboard, out var eventPtr))
                {
                    foreach (Key key in Enum.GetValues(typeof(Key)))
                    {
                        if (key == Key.None || key == Key.IMESelected) continue;
                        try
                        {
                            keyboard[key].WriteValueIntoEvent(0f, eventPtr);
                            released++;
                        }
                        catch (ArgumentException) { /* Some enum values may not map to physical keys */ }
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
                    released += 5;
                }
            }

            return new SuccessResponse($"Released all input devices ({released} controls zeroed).");
        }

        // ====================================================================
        //  Low-level helpers
        // ====================================================================

        private static bool s_inputRouteConfigured;

        /// <summary>
        /// Ensure InputSystem routes all device input to the Game View,
        /// even when the Game View doesn't have editor focus.
        /// Also focus the Game View window as a belt-and-suspenders measure.
        /// </summary>
        private static void EnsureInputRoutesToGameView()
        {
            if (!s_inputRouteConfigured)
            {
                var settings = InputSystem.settings;
                if (settings != null)
                {
                    settings.editorInputBehaviorInPlayMode =
                        InputSettings.EditorInputBehaviorInPlayMode.AllDeviceInputAlwaysGoesToGameView;
                }
                s_inputRouteConfigured = true;
            }

            // NOTE: Do NOT call FocusGameView() here globally.
            // It interferes with screenshot capture. Mouse actions call it explicitly.
        }

        private static Type s_gameViewType;
        private static Type GameViewType => s_gameViewType ??=
            typeof(EditorWindow).Assembly.GetType("UnityEditor.GameView");

        private static void FocusGameView()
        {
            if (GameViewType != null)
                EditorWindow.FocusWindowIfItsOpen(GameViewType);
        }

        /// <summary>
        /// Convert Y from screenshot space (top-left origin) to
        /// InputSystem screen space (bottom-left origin).
        /// Uses Camera.pixelHeight which is always correct, unlike Screen.height
        /// which returns the focused Editor window's height (known Unity Editor bug).
        /// </summary>
        private static float ConvertScreenshotY(float y)
        {
            var cam = Camera.main;
            return cam != null ? cam.pixelHeight - y : y;
        }

        private static void QueueKeyState(Key key, bool pressed)
        {
            var keyboard = Keyboard.current;
            if (keyboard == null) return;

            EnsureNativeInputHook();

            // Track held state so NativeInputInterceptor can re-inject every frame
            if (pressed)
                s_heldKeys.Add(key);
            else
                s_heldKeys.Remove(key);

            // Also apply immediately for the current frame
            using (StateEvent.From(keyboard, out var eventPtr))
            {
                keyboard[key].WriteValueIntoEvent(pressed ? 1f : 0f, eventPtr);
                InputState.Change(keyboard, eventPtr);
            }
        }

        private static void QueueMouseButtonState(int button, bool pressed)
        {
            var mouse = Mouse.current;
            var control = GetMouseButtonControl(mouse, button);
            if (control == null) return;

            EnsureNativeInputHook();

            if (pressed)
                s_heldMouseButtons.Add(button);
            else
                s_heldMouseButtons.Remove(button);

            using (StateEvent.From(mouse, out var eventPtr))
            {
                control.WriteValueIntoEvent(pressed ? 1f : 0f, eventPtr);
                InputState.Change(mouse, eventPtr);
            }
        }

        private static ButtonControl GetMouseButtonControl(Mouse mouse, int button)
        {
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

        // ====================================================================
        //  Key resolution
        // ====================================================================

        private static Key? ResolveKey(string keyName)
        {
            if (string.IsNullOrEmpty(keyName)) return null;
            if (KeyNameMap.TryGetValue(keyName, out var mapped)) return mapped;
            if (Enum.TryParse<Key>(keyName, true, out var parsed) && parsed != Key.None) return parsed;
            return null;
        }

        private static int ResolveMouseButton(string raw)
        {
            if (string.IsNullOrEmpty(raw)) return -1;
            if (MouseButtonMap.TryGetValue(raw, out int idx)) return idx;
            if (int.TryParse(raw, out int num) && num >= 0 && num <= 4) return num;
            return -1;
        }

        private static ErrorResponse KeyError(string provided)
        {
            return new ErrorResponse(
                $"Unknown key: '{provided ?? "(none)"}'. Use key names like " +
                "'w', 'space', 'shift', 'ctrl', 'alt', 'escape', 'enter', 'tab', " +
                "'up', 'down', 'left', 'right', 'f1'-'f12', '0'-'9', etc.");
        }

        // ====================================================================
        //  Key name aliases
        // ====================================================================

        private static Dictionary<string, Key> BuildKeyNameMap()
        {
            return new Dictionary<string, Key>(StringComparer.OrdinalIgnoreCase)
            {
                // Letters
                { "a", Key.A }, { "b", Key.B }, { "c", Key.C }, { "d", Key.D },
                { "e", Key.E }, { "f", Key.F }, { "g", Key.G }, { "h", Key.H },
                { "i", Key.I }, { "j", Key.J }, { "k", Key.K }, { "l", Key.L },
                { "m", Key.M }, { "n", Key.N }, { "o", Key.O }, { "p", Key.P },
                { "q", Key.Q }, { "r", Key.R }, { "s", Key.S }, { "t", Key.T },
                { "u", Key.U }, { "v", Key.V }, { "w", Key.W }, { "x", Key.X },
                { "y", Key.Y }, { "z", Key.Z },

                // Digits
                { "0", Key.Digit0 }, { "1", Key.Digit1 }, { "2", Key.Digit2 },
                { "3", Key.Digit3 }, { "4", Key.Digit4 }, { "5", Key.Digit5 },
                { "6", Key.Digit6 }, { "7", Key.Digit7 }, { "8", Key.Digit8 },
                { "9", Key.Digit9 },

                // Common aliases
                { "space", Key.Space },
                { "spacebar", Key.Space },
                { "enter", Key.Enter },
                { "return", Key.Enter },
                { "esc", Key.Escape },
                { "escape", Key.Escape },
                { "tab", Key.Tab },
                { "backspace", Key.Backspace },
                { "delete", Key.Delete },
                { "del", Key.Delete },

                // Modifiers
                { "shift", Key.LeftShift },
                { "leftshift", Key.LeftShift },
                { "lshift", Key.LeftShift },
                { "rightshift", Key.RightShift },
                { "rshift", Key.RightShift },
                { "ctrl", Key.LeftCtrl },
                { "leftctrl", Key.LeftCtrl },
                { "lctrl", Key.LeftCtrl },
                { "rightctrl", Key.RightCtrl },
                { "rctrl", Key.RightCtrl },
                { "control", Key.LeftCtrl },
                { "alt", Key.LeftAlt },
                { "leftalt", Key.LeftAlt },
                { "lalt", Key.LeftAlt },
                { "rightalt", Key.RightAlt },
                { "ralt", Key.RightAlt },
                { "cmd", Key.LeftMeta },
                { "command", Key.LeftMeta },
                { "meta", Key.LeftMeta },
                { "leftmeta", Key.LeftMeta },
                { "rightmeta", Key.RightMeta },
                { "win", Key.LeftMeta },
                { "windows", Key.LeftMeta },
                { "capslock", Key.CapsLock },

                // Arrow keys
                { "up", Key.UpArrow },
                { "down", Key.DownArrow },
                { "left", Key.LeftArrow },
                { "right", Key.RightArrow },
                { "uparrow", Key.UpArrow },
                { "downarrow", Key.DownArrow },
                { "leftarrow", Key.LeftArrow },
                { "rightarrow", Key.RightArrow },
                { "arrowup", Key.UpArrow },
                { "arrowdown", Key.DownArrow },
                { "arrowleft", Key.LeftArrow },
                { "arrowright", Key.RightArrow },

                // Function keys
                { "f1", Key.F1 }, { "f2", Key.F2 }, { "f3", Key.F3 },
                { "f4", Key.F4 }, { "f5", Key.F5 }, { "f6", Key.F6 },
                { "f7", Key.F7 }, { "f8", Key.F8 }, { "f9", Key.F9 },
                { "f10", Key.F10 }, { "f11", Key.F11 }, { "f12", Key.F12 },

                // Navigation
                { "home", Key.Home },
                { "end", Key.End },
                { "pageup", Key.PageUp },
                { "pagedown", Key.PageDown },
                { "insert", Key.Insert },

                // Punctuation / symbols
                { "minus", Key.Minus },
                { "equals", Key.Equals },
                { "leftbracket", Key.LeftBracket },
                { "rightbracket", Key.RightBracket },
                { "backslash", Key.Backslash },
                { "semicolon", Key.Semicolon },
                { "quote", Key.Quote },
                { "comma", Key.Comma },
                { "period", Key.Period },
                { "slash", Key.Slash },
                { "backquote", Key.Backquote },
                { "tilde", Key.Backquote },

                // Numpad
                { "numpad0", Key.Numpad0 }, { "numpad1", Key.Numpad1 },
                { "numpad2", Key.Numpad2 }, { "numpad3", Key.Numpad3 },
                { "numpad4", Key.Numpad4 }, { "numpad5", Key.Numpad5 },
                { "numpad6", Key.Numpad6 }, { "numpad7", Key.Numpad7 },
                { "numpad8", Key.Numpad8 }, { "numpad9", Key.Numpad9 },
                { "numpadenter", Key.NumpadEnter },
                { "numpadplus", Key.NumpadPlus },
                { "numpadminus", Key.NumpadMinus },
                { "numpadtimes", Key.NumpadMultiply },
                { "numpaddivide", Key.NumpadDivide },
                { "numpadperiod", Key.NumpadPeriod },
                { "numlock", Key.NumLock },

                // Misc
                { "printscreen", Key.PrintScreen },
                { "scrolllock", Key.ScrollLock },
                { "pause", Key.Pause },
            };
        }
    }
}
#endif
