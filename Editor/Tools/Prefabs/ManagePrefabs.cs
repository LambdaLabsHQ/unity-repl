using System;
using System.Collections.Generic;
using NativeMcp.Editor.Helpers;
using Newtonsoft.Json.Linq;

namespace NativeMcp.Editor.Tools.Prefabs
{
    /// <summary>
    /// Deprecated stub that forwards to individual prefab_* tools.
    /// </summary>
    [McpForUnityTool("manage_prefabs", AutoRegister = false, Internal = true)]
    [Obsolete("Use individual prefab_* tools instead.")]
    public static class ManagePrefabs
    {
        private static readonly Dictionary<string, string> ActionToToolName = new Dictionary<string, string>
        {
            { "open_stage", "prefab_open_stage" },
            { "close_stage", "prefab_close_stage" },
            { "save_open_stage", "prefab_save_stage" },
            { "create_from_gameobject", "prefab_create_from_gameobject" }
        };

        public static object HandleCommand(JObject @params)
        {
            if (@params == null)
            {
                return new ErrorResponse("Parameters cannot be null.");
            }

            string action = @params["action"]?.ToString()?.ToLowerInvariant();
            if (string.IsNullOrEmpty(action))
            {
                return new ErrorResponse("Action parameter is required. Valid actions are: open_stage, close_stage, save_open_stage, create_from_gameobject.");
            }

            if (!ActionToToolName.TryGetValue(action, out string toolName))
            {
                return new ErrorResponse($"Unknown action: '{action}'. Valid actions are: open_stage, close_stage, save_open_stage, create_from_gameobject.");
            }

            try
            {
                return CommandRegistry.InvokeCommandAsync(toolName, @params).GetAwaiter().GetResult();
            }
            catch (Exception e)
            {
                McpLog.Error($"[ManagePrefabs] Forwarding action '{action}' to '{toolName}' failed: {e}");
                return new ErrorResponse($"Internal error: {e.Message}");
            }
        }
    }
}
