using System;
using NativeMcp.Editor.Helpers;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine;

namespace NativeMcp.Editor.Tools.Components
{
    /// <summary>
    /// MCP tool that adds a component to a GameObject.
    /// </summary>
    [McpForUnityTool("component_add", Description = "Add a component to a GameObject.")]
    public static class ComponentAdd
    {
        public class Parameters
        {
            [ToolParameter("Target GameObject name, path, or instance ID", Required = true)]
            public string target { get; set; }

            [ToolParameter("Search method: by_name, by_path, by_tag (default: by_name)", Required = false)]
            public string searchMethod { get; set; }

            [ToolParameter("Component type name (e.g. Rigidbody, BoxCollider)", Required = true)]
            public string componentType { get; set; }

            [ToolParameter("Initial property values as JSON object", Required = false)]
            public string properties { get; set; }
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
                    return new ErrorResponse($"Component type '{componentTypeName}' not found. Use a fully-qualified name if needed.");

                // Use ComponentOps for the actual operation
                Component newComponent = ComponentOps.AddComponent(targetGo, type, out string error);
                if (newComponent == null)
                    return new ErrorResponse(error ?? $"Failed to add component '{componentTypeName}'.");

                // Set properties if provided
                JObject properties = @params["properties"] as JObject ?? @params["componentProperties"] as JObject;

                // Also support properties passed as a JSON string
                if (properties == null)
                {
                    string propsStr = ParamCoercion.CoerceString(@params["properties"], null);
                    if (!string.IsNullOrEmpty(propsStr))
                    {
                        try { properties = JObject.Parse(propsStr); }
                        catch { /* ignore parse errors */ }
                    }
                }

                if (properties != null && properties.HasValues)
                {
                    Undo.RecordObject(newComponent, "Modify Component Properties");
                    ComponentToolHelpers.SetPropertiesOnComponent(newComponent, properties);
                }

                EditorUtility.SetDirty(targetGo);

                return new
                {
                    success = true,
                    message = $"Component '{componentTypeName}' added to '{targetGo.name}'.",
                    data = new
                    {
                        instanceID = targetGo.GetInstanceID(),
                        componentType = type.FullName,
                        componentInstanceID = newComponent.GetInstanceID()
                    }
                };
            }
            catch (Exception e)
            {
                McpLog.Error($"[ComponentAdd] Failed: {e}");
                return new ErrorResponse($"Internal error adding component: {e.Message}");
            }
        }
    }
}
