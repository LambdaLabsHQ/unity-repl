using System;
using NativeMcp.Editor.Helpers;
using Newtonsoft.Json.Linq;

namespace NativeMcp.Editor.Tools
{
    /// <summary>
    /// Legacy facade that forwards to individual editor_* tools.
    /// Kept for backward compatibility only.
    /// </summary>
    [Obsolete("Use individual editor_* tools instead.")]
    [McpForUnityTool("manage_editor", AutoRegister = false)]
    public static class ManageEditor
    {
        public static object HandleCommand(JObject @params)
        {
            string action = @params["action"]?.ToString()?.ToLowerInvariant();
            string mappedTool = action switch
            {
                "play" => "editor_play",
                "pause" => "editor_pause",
                "stop" => "editor_stop",
                "set_active_tool" => "editor_set_active_tool",
                "add_tag" => "editor_add_tag",
                "remove_tag" => "editor_remove_tag",
                "add_layer" => "editor_add_layer",
                "remove_layer" => "editor_remove_layer",
                _ => null
            };
            if (mappedTool == null)
                return new ErrorResponse($"Unknown action: '{action}'.");
            return CommandRegistry.InvokeCommandAsync(mappedTool, @params).GetAwaiter().GetResult();
        }
    }
}
