using System.Collections.Generic;
using System.Threading.Tasks;
using NativeMcp.Editor.Helpers;
using Newtonsoft.Json.Linq;

namespace NativeMcp.Editor.Tools.Meta
{
    [McpForUnityTool("unity_editor",
        Description =
            "Control the Unity Editor. Pass 'action' to select an operation.\n" +
            "Actions:\n" +
            "- play() — enter play mode\n" +
            "- stop() — exit play mode\n" +
            "- pause() — toggle pause in play mode\n" +
            "- step_frame() — advance exactly one frame while paused in play mode\n" +
            "- play_for_frames(frames, timeout?) — play for N frames then pause. Supports domain reload recovery\n" +
            "- set_update_frequency(time_scale?, capture_framerate?) — get/set game update frequency. No args = getter\n" +
            "- add_tag(tagName) — add a project tag\n" +
            "- remove_tag(tagName) — remove a project tag\n" +
            "- add_layer(layerName) — add a project layer\n" +
            "- remove_layer(layerName) — remove a project layer\n" +
            "- set_active_tool(toolName) — set editor tool (View, Move, Rotate, Scale, Rect, Transform)\n" +
            "- refresh(mode?, scope?, compile?, wait_for_ready?) — refresh asset database")]
    public static class UnityEditorControl
    {
        private static readonly Dictionary<string, string> ActionMap = new()
        {
            ["play"] = "editor_play",
            ["stop"] = "editor_stop",
            ["pause"] = "editor_pause",
            ["step_frame"] = "editor_step_frame",
            ["play_for_frames"] = "editor_play_for_frames",
            ["set_update_frequency"] = "editor_set_update_frequency",
            ["add_tag"] = "editor_add_tag",
            ["remove_tag"] = "editor_remove_tag",
            ["add_layer"] = "editor_add_layer",
            ["remove_layer"] = "editor_remove_layer",
            ["set_active_tool"] = "editor_set_active_tool",
            ["refresh"] = "refresh_unity",
        };

        public class Parameters
        {
            [ToolParameter("Action to perform: play, stop, pause, step_frame, play_for_frames, set_update_frequency, add_tag, remove_tag, add_layer, remove_layer, set_active_tool, refresh")]
            public string action { get; set; }

            [ToolParameter("Number of frames to advance for play_for_frames (>= 1)", Required = false)]
            public int? frames { get; set; }

            [ToolParameter("Timeout in seconds for play_for_frames (default 30)", Required = false)]
            public int? timeout { get; set; }

            [ToolParameter("Time scale for set_update_frequency (0=frozen, 1=normal, range [0,100])", Required = false)]
            public float? time_scale { get; set; }

            [ToolParameter("Capture framerate for deterministic mode (0=off). deltaTime = timeScale/captureFramerate", Required = false)]
            public int? capture_framerate { get; set; }

            [ToolParameter("Tag name for add_tag/remove_tag actions", Required = false)]
            public string tagName { get; set; }

            [ToolParameter("Layer name for add_layer/remove_layer actions", Required = false)]
            public string layerName { get; set; }

            [ToolParameter("Editor tool name for set_active_tool", Required = false)]
            public string toolName { get; set; }

            [ToolParameter("Refresh mode: if_dirty or force", Required = false)]
            public string mode { get; set; }

            [ToolParameter("Refresh scope: all or scripts", Required = false)]
            public string scope { get; set; }

            [ToolParameter("Compilation mode: none or request", Required = false)]
            public string compile { get; set; }

            [ToolParameter("Wait for Unity ready state after refresh", Required = false)]
            public bool? wait_for_ready { get; set; }
        }

        public static async Task<object> HandleCommand(JObject @params)
        {
            return await MetaToolDispatcher.DispatchAsync(@params, ActionMap, "unity_editor");
        }
    }
}
