using System;
using NativeMcp.Editor.Helpers;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine;

namespace NativeMcp.Editor.Tools.Components
{
    /// <summary>
    /// MCP tool that removes a component from a GameObject.
    /// </summary>
    [McpForUnityTool("component_remove", Description = "Remove a component from a GameObject.")]
    public static class ComponentRemove
    {
        public class Parameters
        {
            [ToolParameter("Target GameObject name, path, or instance ID", Required = true)]
            public string target { get; set; }

            [ToolParameter("Search method: by_name, by_path, by_tag (default: by_name)", Required = false)]
            public string searchMethod { get; set; }

            [ToolParameter("Component type name (e.g. Rigidbody, BoxCollider)", Required = true)]
            public string componentType { get; set; }
        }

        public static object HandleCommand(JObject @params)
        {
            if (@params == null)
                return new ErrorResponse("Parameters cannot be null.");

            // Target resolution
            JToken targetToken = @params["target"];
            string searchMethod = ParamCoercion.CoerceString(@params["searchMethod"] ?? @params["search_method"], null);

            if (targetToken == null)
                return new ErrorResponse("'target' parameter is required.");

            try
            {
                GameObject targetGo = ComponentToolHelpers.FindTarget(targetToken, searchMethod);
                if (targetGo == null)
                    return new ErrorResponse($"Target GameObject ('{targetToken}') not found using method '{searchMethod ?? "default"}'.");

                string componentTypeName = ParamCoercion.CoerceString(@params["componentType"] ?? @params["component_type"], null);
                if (string.IsNullOrEmpty(componentTypeName))
                    return new ErrorResponse("'componentType' parameter is required.");

                // Resolve component type using unified type resolver
                Type type = UnityTypeResolver.ResolveComponent(componentTypeName);
                if (type == null)
                    return new ErrorResponse($"Component type '{componentTypeName}' not found.");

                // Use ComponentOps for the actual operation
                bool removed = ComponentOps.RemoveComponent(targetGo, type, out string error);
                if (!removed)
                    return new ErrorResponse(error ?? $"Failed to remove component '{componentTypeName}'.");

                EditorUtility.SetDirty(targetGo);

                return new
                {
                    success = true,
                    message = $"Component '{componentTypeName}' removed from '{targetGo.name}'.",
                    data = new
                    {
                        instanceID = targetGo.GetInstanceID()
                    }
                };
            }
            catch (Exception e)
            {
                McpLog.Error($"[ComponentRemove] Failed: {e}");
                return new ErrorResponse($"Internal error removing component: {e.Message}");
            }
        }
    }
}
