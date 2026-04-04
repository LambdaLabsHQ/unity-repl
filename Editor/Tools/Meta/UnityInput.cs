using NativeMcp.Editor.Helpers;
using Newtonsoft.Json.Linq;

namespace NativeMcp.Editor.Tools.Meta
{
    [McpForUnityTool("unity_input",
        Description =
            "Simulate keyboard and mouse input during Play Mode via low-level InputSystem events.\n" +
            "Actions:\n" +
            "- key_down(key) — press keyboard key (persists until key_up)\n" +
            "- key_up(key) — release keyboard key\n" +
            "- type_text(keys) — press+release key sequence in one frame\n" +
            "- mouse_button_down(button) — press mouse button\n" +
            "- mouse_button_up(button) — release mouse button\n" +
            "- mouse_move(delta_x?, delta_y?, position_x?, position_y?) — move mouse\n" +
            "- mouse_scroll(scroll_x?, scroll_y?) — scroll wheel\n" +
            "- click(button?, position_x?, position_y?) — move + click (single frame)\n" +
            "- release_all() — release all keys and buttons\n" +
            "NOTE: For UI clicks, 'click' may fail because UI needs pointer arrival before the press. " +
            "Split into: mouse_move → step_frame → mouse_button_down → step_frame → mouse_button_up. " +
            "Mouse coordinates are OS screen space, not Game View space.")]
    public static class UnityInput
    {
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

        public static object HandleCommand(JObject @params)
        {
#if !NATIVE_MCP_HAS_INPUT_SYSTEM
            return new ErrorResponse(
                "Input simulation requires the Unity Input System package (com.unity.inputsystem). " +
                "Install it via Window > Package Manager.");
#else
            return CommandRegistry.InvokeCommandAsync("simulate_input", @params ?? new JObject())
                .GetAwaiter().GetResult();
#endif
        }
    }
}
