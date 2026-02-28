using System;
using System.Collections.Generic;
using NativeMcp.Editor.Helpers;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;

namespace NativeMcp.Editor.Tools
{
    /// <summary>
    /// Simulates keyboard and mouse input by injecting low-level InputSystem events.
    /// All actions require Play Mode – the InputSystem event loop only runs during play.
    ///
    /// Supported actions:
    ///   key_down / key_up       – press/release a keyboard key
    ///   mouse_button_down/up    – press/release a mouse button (0=left,1=right,2=middle)
    ///   mouse_move              – move the mouse by delta or to an absolute position
    ///   mouse_scroll            – scroll the mouse wheel
    ///   type_text               – convenience: press then release a sequence of keys
    /// </summary>
    [McpForUnityTool(
        Description = "Simulate keyboard and mouse input in Play Mode. " +
                      "Uses low-level InputSystem events so it works with both " +
                      "direct device reads (Keyboard.current[key].isPressed) and InputAction bindings. " +
                      "IMPORTANT: Only works when Play Mode is active. " +
                      "key_down events persist until a matching key_up is sent. " +
                      "For a quick tap, use type_text or send key_down followed by key_up in separate calls.")]
    public static class SimulateInput
    {
        // ── MCP parameter schema ───────────────────────────────────────
        public class Parameters
        {
            [ToolParameter("The input action to perform. One of: key_down, key_up, mouse_button_down, mouse_button_up, mouse_move, mouse_scroll, type_text, release_all")]
            public string action { get; set; }

            [ToolParameter("Key name for keyboard actions (key_down/key_up). " +
                           "Examples: 'W', 'Space', 'LeftShift', 'Escape', 'Return', 'F1'. " +
                           "Common aliases: 'enter', 'shift', 'ctrl', 'alt', 'esc', 'up/down/left/right'.",
                Required = false)]
            public string key { get; set; }

            [ToolParameter("Mouse button for mouse_button_down/up. " +
                           "Values: 0 or 'left', 1 or 'right', 2 or 'middle', 3 or 'forward', 4 or 'back'.",
                Required = false)]
            public string button { get; set; }

            [ToolParameter("Relative mouse movement X (pixels) for mouse_move.", Required = false)]
            public string delta_x { get; set; }

            [ToolParameter("Relative mouse movement Y (pixels) for mouse_move.", Required = false)]
            public string delta_y { get; set; }

            [ToolParameter("Absolute mouse position X (screen pixels) for mouse_move.", Required = false)]
            public string position_x { get; set; }

            [ToolParameter("Absolute mouse position Y (screen pixels) for mouse_move.", Required = false)]
            public string position_y { get; set; }

            [ToolParameter("Horizontal scroll amount for mouse_scroll (120 = one notch on Windows).", Required = false)]
            public string scroll_x { get; set; }

            [ToolParameter("Vertical scroll amount for mouse_scroll (120 = one notch on Windows).", Required = false)]
            public string scroll_y { get; set; }

            [ToolParameter("A single key name or JSON array of key names for type_text. " +
                           "Each key will be pressed and released in sequence within the same frame.",
                Required = false)]
            public string keys { get; set; }
        }

        // ── Map of friendly key names to InputSystem Key enum ──────────────
        private static readonly Dictionary<string, Key> KeyNameMap = BuildKeyNameMap();

        // ── Map of friendly mouse-button names to indices ──────────────────
        private static readonly Dictionary<string, int> MouseButtonMap = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
        {
            { "left",   0 },
            { "right",  1 },
            { "middle", 2 },
            { "forward", 3 },
            { "back",   4 },
        };

        // ====================================================================
        //  Main handler
        // ====================================================================

        public static object HandleCommand(JObject @params)
        {
            // ── Guard: must be in Play Mode ──────────────────────────────
            if (!EditorApplication.isPlaying)
            {
                return new ErrorResponse(
                    "Input simulation requires Play Mode. Use manage_editor with action 'play' first.");
            }

            string action = @params["action"]?.ToString();
            if (string.IsNullOrEmpty(action))
            {
                return new ErrorResponse(
                    "Missing required parameter 'action'. " +
                    "Supported: key_down, key_up, mouse_button_down, mouse_button_up, " +
                    "mouse_move, mouse_scroll, type_text, release_all");
            }

            try
            {
                switch (action.ToLowerInvariant())
                {
                    case "key_down":       return HandleKeyDown(@params);
                    case "key_up":         return HandleKeyUp(@params);
                    case "mouse_button_down": return HandleMouseButtonDown(@params);
                    case "mouse_button_up":   return HandleMouseButtonUp(@params);
                    case "mouse_move":     return HandleMouseMove(@params);
                    case "mouse_scroll":   return HandleMouseScroll(@params);
                    case "type_text":      return HandleTypeText(@params);
                    case "release_all":    return HandleReleaseAll();
                    default:
                        return new ErrorResponse(
                            $"Unknown action: '{action}'. " +
                            "Supported: key_down, key_up, mouse_button_down, mouse_button_up, " +
                            "mouse_move, mouse_scroll, type_text, release_all");
                }
            }
            catch (Exception ex)
            {
                McpLog.Error($"[SimulateInput] {action} failed: {ex}");
                return new ErrorResponse($"Input simulation error: {ex.Message}");
            }
        }

        // ====================================================================
        //  Keyboard actions
        // ====================================================================

        private static object HandleKeyDown(JObject @params)
        {
            var key = ResolveKey(@params);
            if (key == null)
                return new ErrorResponse(KeyErrorMessage(@params));

            QueueKeyState(key.Value, true);
            return new SuccessResponse($"Key '{key.Value}' pressed (down).");
        }

        private static object HandleKeyUp(JObject @params)
        {
            var key = ResolveKey(@params);
            if (key == null)
                return new ErrorResponse(KeyErrorMessage(@params));

            QueueKeyState(key.Value, false);
            return new SuccessResponse($"Key '{key.Value}' released (up).");
        }

        private static object HandleTypeText(JObject @params)
        {
            // "keys" can be a single key name or a JSON array of key names
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

            // For each key: queue press then release.
            // NOTE: Both press and release are queued in the same frame's event buffer.
            // InputSystem will process press first, then release, resulting in a single-frame tap.
            var resolved = new List<Key>();
            foreach (var name in keyNames)
            {
                var key = ResolveKeyByName(name);
                if (key == null)
                    return new ErrorResponse($"Unknown key: '{name}'. Use key names like 'W', 'Space', 'LeftShift', 'Escape', etc.");
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
            int button = ResolveMouseButton(@params);
            if (button < 0)
                return new ErrorResponse("'button' parameter is required (0/left, 1/right, 2/middle, 3/forward, 4/back).");

            QueueMouseButtonState(button, true);
            return new SuccessResponse($"Mouse button {button} pressed (down).");
        }

        private static object HandleMouseButtonUp(JObject @params)
        {
            int button = ResolveMouseButton(@params);
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

            using (StateEvent.From(mouse, out var eventPtr))
            {
                if (dx.HasValue || dy.HasValue)
                {
                    var delta = new Vector2(dx ?? 0f, dy ?? 0f);
                    mouse.delta.WriteValueIntoEvent(delta, eventPtr);
                }

                if (x.HasValue || y.HasValue)
                {
                    var currentPos = mouse.position.ReadValue();
                    var newPos = new Vector2(x ?? currentPos.x, y ?? currentPos.y);
                    mouse.position.WriteValueIntoEvent(newPos, eventPtr);
                }

                InputSystem.QueueEvent(eventPtr);
            }

            string details = "";
            if (dx.HasValue || dy.HasValue) details += $"delta=({dx ?? 0},{dy ?? 0}) ";
            if (x.HasValue || y.HasValue)   details += $"position=({x},{y})";

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

            // InputSystem scroll values are in pixels (120 = one notch on Windows)
            var scrollDelta = new Vector2(scrollX, scrollY);
            using (StateEvent.From(mouse, out var eventPtr))
            {
                mouse.scroll.WriteValueIntoEvent(scrollDelta, eventPtr);
                InputSystem.QueueEvent(eventPtr);
            }

            return new SuccessResponse($"Mouse scrolled: ({scrollX}, {scrollY})");
        }

        // ====================================================================
        //  Release all – safety valve
        // ====================================================================

        private static object HandleReleaseAll()
        {
            int released = 0;

            var keyboard = Keyboard.current;
            if (keyboard != null)
            {
                // Send a state event with all keys zeroed
                using (StateEvent.From(keyboard, out var eventPtr))
                {
                    // StateEvent.From copies the current state; we need to zero all keys.
                    // The simplest way is to write 0 for every key in the enum range.
                    foreach (Key key in Enum.GetValues(typeof(Key)))
                    {
                        if (key == Key.None || key == Key.IMESelected) continue;
                        try
                        {
                            keyboard[key].WriteValueIntoEvent(0f, eventPtr);
                            released++;
                        }
                        catch
                        {
                            // Some enum values may not map to physical keys
                        }
                    }
                    InputSystem.QueueEvent(eventPtr);
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
                    InputSystem.QueueEvent(eventPtr);
                    released += 5;
                }
            }

            return new SuccessResponse($"Released all input devices ({released} controls zeroed).");
        }

        // ====================================================================
        //  Low-level helpers
        // ====================================================================

        private static void QueueKeyState(Key key, bool pressed)
        {
            var keyboard = Keyboard.current;
            if (keyboard == null)
            {
                McpLog.Warn("[SimulateInput] No keyboard device found; cannot queue key event.");
                return;
            }

            using (StateEvent.From(keyboard, out var eventPtr))
            {
                keyboard[key].WriteValueIntoEvent(pressed ? 1f : 0f, eventPtr);
                InputSystem.QueueEvent(eventPtr);
            }
        }

        private static void QueueMouseButtonState(int button, bool pressed)
        {
            var mouse = Mouse.current;
            if (mouse == null)
            {
                McpLog.Warn("[SimulateInput] No mouse device found; cannot queue button event.");
                return;
            }

            var control = GetMouseButtonControl(mouse, button);
            if (control == null)
            {
                McpLog.Warn($"[SimulateInput] Invalid mouse button index: {button}");
                return;
            }

            using (StateEvent.From(mouse, out var eventPtr))
            {
                control.WriteValueIntoEvent(pressed ? 1f : 0f, eventPtr);
                InputSystem.QueueEvent(eventPtr);
            }
        }

        private static UnityEngine.InputSystem.Controls.ButtonControl GetMouseButtonControl(Mouse mouse, int button)
        {
            switch (button)
            {
                case 0: return mouse.leftButton;
                case 1: return mouse.rightButton;
                case 2: return mouse.middleButton;
                case 3: return mouse.forwardButton;
                case 4: return mouse.backButton;
                default: return null;
            }
        }

        // ====================================================================
        //  Key resolution helpers
        // ====================================================================

        private static Key? ResolveKey(JObject @params)
        {
            string keyName = @params["key"]?.ToString();
            return ResolveKeyByName(keyName);
        }

        private static Key? ResolveKeyByName(string keyName)
        {
            if (string.IsNullOrEmpty(keyName)) return null;

            // 1. Try the friendly name map first (case-insensitive)
            if (KeyNameMap.TryGetValue(keyName, out var mapped))
                return mapped;

            // 2. Try direct enum parse (e.g. "W", "Space", "LeftShift")
            if (Enum.TryParse<Key>(keyName, true, out var parsed) && parsed != Key.None)
                return parsed;

            return null;
        }

        private static int ResolveMouseButton(JObject @params)
        {
            var token = @params["button"];
            if (token == null) return -1;

            string raw = token.ToString();

            // Name-based lookup
            if (MouseButtonMap.TryGetValue(raw, out int idx))
                return idx;

            // Numeric
            if (int.TryParse(raw, out int num) && num >= 0 && num <= 4)
                return num;

            return -1;
        }

        private static string KeyErrorMessage(JObject @params)
        {
            string provided = @params["key"]?.ToString() ?? "(none)";
            return $"Unknown key: '{provided}'. Use InputSystem key names such as " +
                   "'W', 'A', 'S', 'D', 'Space', 'LeftShift', 'Escape', 'Return', " +
                   "'LeftCtrl', 'LeftAlt', 'Tab', 'F1'–'F12', 'Digit0'–'Digit9', etc. " +
                   "Common aliases: 'enter'→Return, 'shift'→LeftShift, 'ctrl'→LeftCtrl, " +
                   "'alt'→LeftAlt, 'esc'→Escape, 'up/down/left/right'→arrow keys.";
        }

        // ====================================================================
        //  Friendly key-name aliases
        // ====================================================================

        private static Dictionary<string, Key> BuildKeyNameMap()
        {
            var map = new Dictionary<string, Key>(StringComparer.OrdinalIgnoreCase)
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
                { "rightshift", Key.RightShift },
                { "ctrl", Key.LeftCtrl },
                { "leftctrl", Key.LeftCtrl },
                { "rightctrl", Key.RightCtrl },
                { "control", Key.LeftCtrl },
                { "alt", Key.LeftAlt },
                { "leftalt", Key.LeftAlt },
                { "rightalt", Key.RightAlt },
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

            return map;
        }
    }
}
