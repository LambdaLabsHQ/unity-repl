#nullable disable
using System;
using System.Collections.Generic;
using NativeMcp.Editor.Helpers;
using Newtonsoft.Json.Linq;

namespace NativeMcp.Editor.Tools.GameObjects
{
    /// <summary>
    /// Deprecated stub that forwards to individual gameobject_* tools.
    /// </summary>
    [McpForUnityTool("manage_gameobject", AutoRegister = false)]
    [Obsolete("Use individual gameobject_* tools instead.")]
    public static class ManageGameObject
    {
        private static readonly Dictionary<string, string> ActionToToolName = new Dictionary<string, string>
        {
            { "create", "gameobject_create" },
            { "modify", "gameobject_modify" },
            { "delete", "gameobject_delete" },
            { "duplicate", "gameobject_duplicate" },
            { "move_relative", "gameobject_move_relative" }
        };

        public static object HandleCommand(JObject @params)
        {
            if (@params == null)
            {
                return new ErrorResponse("Parameters cannot be null.");
            }

            string action = @params["action"]?.ToString()?.ToLower();
            if (string.IsNullOrEmpty(action))
            {
                return new ErrorResponse("Action parameter is required.");
            }

            // --- Usability Improvement: Alias 'name' to 'target' for modification actions ---
            JToken targetToken = @params["target"];
            string name = @params["name"]?.ToString();
            if (targetToken == null && !string.IsNullOrEmpty(name) && action != "create")
            {
                @params["target"] = name;
            }

            // Coerce string JSON to JObject for 'componentProperties' if provided as a JSON string
            var componentPropsToken = @params["componentProperties"];
            if (componentPropsToken != null && componentPropsToken.Type == JTokenType.String)
            {
                try
                {
                    var parsed = JObject.Parse(componentPropsToken.ToString());
                    @params["componentProperties"] = parsed;
                }
                catch (Exception e)
                {
                    McpLog.Warn($"[ManageGameObject] Could not parse 'componentProperties' JSON string: {e.Message}");
                }
            }

            // --- Prefab Asset Check ---
            string targetPath = @params["target"]?.Type == JTokenType.String ? @params["target"].ToString() : null;
            if (
                !string.IsNullOrEmpty(targetPath)
                && targetPath.EndsWith(".prefab", StringComparison.OrdinalIgnoreCase)
                && action != "create"
            )
            {
                return new ErrorResponse(
                    $"Target '{targetPath}' is a prefab asset. " +
                    $"Use 'manage_asset' with action='modify' for prefab asset modifications, " +
                    $"or 'manage_prefabs' with action='open_stage' to edit the prefab in isolation mode."
                );
            }

            if (!ActionToToolName.TryGetValue(action, out string toolName))
            {
                return new ErrorResponse($"Unknown action: '{action}'.");
            }

            try
            {
                return CommandRegistry.InvokeCommandAsync(toolName, @params).GetAwaiter().GetResult();
            }
            catch (Exception e)
            {
                McpLog.Error($"[ManageGameObject] Forwarding action '{action}' to '{toolName}' failed: {e}");
                return new ErrorResponse($"Internal error processing action '{action}': {e.Message}");
            }
        }
    }
}
