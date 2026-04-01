using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using NativeMcp.Editor.Helpers;
using Newtonsoft.Json.Linq;

namespace NativeMcp.Editor.Tools.Meta
{
    /// <summary>
    /// Shared dispatcher logic for meta-tools. Routes an 'action' parameter
    /// to the corresponding internal tool via <see cref="CommandRegistry"/>.
    /// </summary>
    internal static class MetaToolDispatcher
    {
        public static async Task<object> DispatchAsync(
            JObject @params,
            Dictionary<string, string> actionMap,
            string metaToolName)
        {
            string action = @params?["action"]?.ToString()?.ToLowerInvariant();

            if (string.IsNullOrWhiteSpace(action))
            {
                return new ErrorResponse(
                    $"'{metaToolName}' requires an 'action' parameter. " +
                    $"Available actions: {string.Join(", ", actionMap.Keys)}");
            }

            if (!actionMap.TryGetValue(action, out string internalToolName))
            {
                return new ErrorResponse(
                    $"Unknown action '{action}' for '{metaToolName}'. " +
                    $"Available actions: {string.Join(", ", actionMap.Keys)}");
            }

            return await CommandRegistry.InvokeCommandAsync(internalToolName, @params);
        }
    }
}
