using System;
using System.Collections.Generic;
using NativeMcp.Editor.Helpers;
using NativeMcp.Editor.Tools.Scene;
using Newtonsoft.Json.Linq;

namespace NativeMcp.Editor.Tools
{
    /// <summary>
    /// Legacy facade that forwards scene management actions to individual scene_* tools.
    /// Kept for backward compatibility with clients that still send "manage_scene" commands.
    /// </summary>
    [Obsolete("Use individual scene_* tools instead.")]
    [McpForUnityTool("manage_scene", AutoRegister = false, Internal = true)]
    public static class ManageScene
    {
        private static readonly Dictionary<string, string> ActionToToolName = new()
        {
            { "create",             "scene_create" },
            { "load",               "scene_load" },
            { "save",               "scene_save" },
            { "get_hierarchy",      "scene_get_hierarchy" },
            { "get_active",         "scene_get_active" },
            { "get_build_settings", "scene_get_build_settings" },
            { "screenshot",         "scene_screenshot" },
        };

        /// <summary>
        /// Main handler that maps the legacy "action" parameter to the corresponding scene_* tool.
        /// </summary>
        public static object HandleCommand(JObject @params)
        {
            var p = @params ?? new JObject();
            string action = (p["action"]?.ToString() ?? string.Empty).Trim().ToLowerInvariant();

            if (string.IsNullOrEmpty(action))
                return new ErrorResponse("Action parameter is required.");

            if (!ActionToToolName.TryGetValue(action, out string toolName))
            {
                return new ErrorResponse(
                    $"Unknown action: '{action}'. Valid actions: create, load, save, get_hierarchy, get_active, get_build_settings, screenshot.");
            }

            // Forward to the individual tool, stripping the action parameter
            var forwarded = new JObject(p);
            forwarded.Remove("action");

            try
            {
                var task = CommandRegistry.InvokeCommandAsync(toolName, forwarded);
                // All scene tools are synchronous, so the task is already completed.
                return task.Result;
            }
            catch (AggregateException ae) when (ae.InnerException != null)
            {
                return new ErrorResponse($"Error forwarding to {toolName}: {ae.InnerException.Message}");
            }
            catch (Exception e)
            {
                return new ErrorResponse($"Error forwarding to {toolName}: {e.Message}");
            }
        }

        /// <summary>
        /// Public API for screenshot capture. Delegates to SceneScreenshot.
        /// Kept for backward compatibility.
        /// </summary>
        public static object ExecuteScreenshot(string fileName = null, int? superSize = null)
        {
            return SceneScreenshot.CaptureScreenshot(fileName, superSize);
        }
    }
}
