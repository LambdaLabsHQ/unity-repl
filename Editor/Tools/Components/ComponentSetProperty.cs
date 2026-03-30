using System;
using System.Collections.Generic;
using NativeMcp.Editor.Helpers;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine;

namespace NativeMcp.Editor.Tools.Components
{
    /// <summary>
    /// MCP tool that sets one or more properties on a component attached to a GameObject.
    /// </summary>
    [McpForUnityTool("component_set_property", Description = "Set one or more properties on a component.")]
    public static class ComponentSetProperty
    {
        public class Parameters
        {
            [ToolParameter("Target GameObject name, path, or instance ID", Required = true)]
            public string target { get; set; }

            [ToolParameter("Search method: by_name, by_path, by_tag (default: by_name)", Required = false)]
            public string searchMethod { get; set; }

            [ToolParameter("Component type name (e.g. Rigidbody, BoxCollider)", Required = true)]
            public string componentType { get; set; }

            [ToolParameter("Single property name to set", Required = false)]
            public string property { get; set; }

            [ToolParameter("Value for the single property", Required = false)]
            public string value { get; set; }

            [ToolParameter("Multiple properties as a JSON object (e.g. {\"mass\": 2, \"drag\": 0.5})", Required = false)]
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
                    return new ErrorResponse($"Component type '{componentTypeName}' not found.");

                Component component = targetGo.GetComponent(type);
                if (component == null)
                    return new ErrorResponse($"Component '{componentTypeName}' not found on '{targetGo.name}'.");

                // Get property and value
                string propertyName = ParamCoercion.CoerceString(@params["property"], null);
                JToken valueToken = @params["value"];

                // Support both single property or properties object
                JObject properties = @params["properties"] as JObject;

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

                if (string.IsNullOrEmpty(propertyName) && (properties == null || !properties.HasValues))
                    return new ErrorResponse("Either 'property'+'value' or 'properties' object is required.");

                var errors = new List<string>();

                Undo.RecordObject(component, $"Set property on {componentTypeName}");

                if (!string.IsNullOrEmpty(propertyName) && valueToken != null)
                {
                    // Single property mode
                    var error = ComponentToolHelpers.TrySetProperty(component, propertyName, valueToken);
                    if (error != null)
                        errors.Add(error);
                }

                if (properties != null && properties.HasValues)
                {
                    // Multiple properties mode
                    foreach (var prop in properties.Properties())
                    {
                        var error = ComponentToolHelpers.TrySetProperty(component, prop.Name, prop.Value);
                        if (error != null)
                            errors.Add(error);
                    }
                }

                EditorUtility.SetDirty(component);

                if (errors.Count > 0)
                {
                    return new
                    {
                        success = false,
                        message = $"Some properties failed to set on '{componentTypeName}'.",
                        data = new
                        {
                            instanceID = targetGo.GetInstanceID(),
                            errors = errors
                        }
                    };
                }

                return new
                {
                    success = true,
                    message = $"Properties set on component '{componentTypeName}' on '{targetGo.name}'.",
                    data = new
                    {
                        instanceID = targetGo.GetInstanceID()
                    }
                };
            }
            catch (Exception e)
            {
                McpLog.Error($"[ComponentSetProperty] Failed: {e}");
                return new ErrorResponse($"Internal error setting properties: {e.Message}");
            }
        }
    }
}
