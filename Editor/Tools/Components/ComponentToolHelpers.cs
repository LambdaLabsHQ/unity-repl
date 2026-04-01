using System.Collections.Generic;
using NativeMcp.Editor.Helpers;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace NativeMcp.Editor.Tools.Components
{
    /// <summary>
    /// Shared helper methods for the individual component_* tools.
    /// </summary>
    internal static class ComponentToolHelpers
    {
        /// <summary>
        /// Resolves a target GameObject from a JToken using instance ID, name, or path.
        /// </summary>
        internal static GameObject FindTarget(JToken targetToken, string searchMethod)
        {
            if (targetToken == null)
                return null;

            // Try instance ID first
            if (targetToken.Type == JTokenType.Integer)
            {
                int instanceId = targetToken.Value<int>();
                return GameObjectLookup.FindById(instanceId);
            }

            string targetStr = targetToken.ToString();

            // Try parsing as instance ID
            if (int.TryParse(targetStr, out int parsedId))
            {
                var byId = GameObjectLookup.FindById(parsedId);
                if (byId != null)
                    return byId;
            }

            // Use GameObjectLookup for search
            return GameObjectLookup.FindByTarget(targetToken, searchMethod ?? "by_name", true);
        }

        /// <summary>
        /// Sets multiple properties on a component, logging warnings for any failures.
        /// </summary>
        internal static void SetPropertiesOnComponent(Component component, JObject properties)
        {
            if (component == null || properties == null)
                return;

            var errors = new List<string>();
            foreach (var prop in properties.Properties())
            {
                var error = TrySetProperty(component, prop.Name, prop.Value);
                if (error != null)
                    errors.Add(error);
            }

            if (errors.Count > 0)
            {
                McpLog.Warn($"[ComponentToolHelpers] Some properties failed to set on {component.GetType().Name}: {string.Join(", ", errors)}");
            }
        }

        /// <summary>
        /// Attempts to set a property or field on a component.
        /// Returns an error string on failure, or null on success.
        /// </summary>
        internal static string TrySetProperty(Component component, string propertyName, JToken value)
        {
            if (component == null || string.IsNullOrEmpty(propertyName))
                return "Invalid component or property name";

            if (ComponentOps.SetProperty(component, propertyName, value, out string error))
            {
                return null; // Success
            }

            McpLog.Warn($"[ComponentToolHelpers] {error}");
            return error;
        }
    }
}
