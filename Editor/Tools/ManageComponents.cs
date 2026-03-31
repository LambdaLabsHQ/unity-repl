using System;
using NativeMcp.Editor.Helpers;
using NativeMcp.Editor.Tools.Components;
using Newtonsoft.Json.Linq;

namespace NativeMcp.Editor.Tools
{
    /// <summary>
    /// Legacy tool for managing components on GameObjects.
    /// Forwards to individual component_add, component_remove, and component_set_property tools.
    ///
    /// Kept for backward compatibility. New callers should use the individual tools directly.
    /// </summary>
    [Obsolete("Use individual component_* tools instead.")]
    [McpForUnityTool("manage_components", AutoRegister = false, Internal = true)]
    public static class ManageComponents
    {
        /// <summary>
        /// Handles the manage_components command by forwarding to the appropriate individual tool.
        /// </summary>
        /// <param name="params">Command parameters</param>
        /// <returns>Result of the component operation</returns>
        public static object HandleCommand(JObject @params)
        {
            if (@params == null)
            {
                return new ErrorResponse("Parameters cannot be null.");
            }

            string action = ParamCoercion.CoerceString(@params["action"], null)?.ToLowerInvariant();
            if (string.IsNullOrEmpty(action))
            {
                return new ErrorResponse("'action' parameter is required (add, remove, set_property).");
            }

            return action switch
            {
                "add" => ComponentAdd.HandleCommand(@params),
                "remove" => ComponentRemove.HandleCommand(@params),
                "set_property" => ComponentSetProperty.HandleCommand(@params),
                _ => new ErrorResponse($"Unknown action: '{action}'. Supported actions: add, remove, set_property")
            };
        }
    }
}
