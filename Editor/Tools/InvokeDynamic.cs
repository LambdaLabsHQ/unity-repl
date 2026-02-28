using System;
using System.Collections.Generic;
using System.Linq;
using NativeMcp.Editor.Helpers;
using NativeMcp.Runtime;
using Newtonsoft.Json.Linq;

namespace NativeMcp.Editor.Tools
{
    /// <summary>
    /// MCP tool that exposes dynamically registered functions.
    /// Game code registers functions via <see cref="DynamicToolRegistry"/>,
    /// and this tool makes them callable from the MCP client.
    ///
    /// Actions:
    ///   - list:     List all registered dynamic tools with descriptions and parameters.
    ///   - call:     Invoke a registered dynamic tool by name with arguments.
    ///   - describe: Get detailed info about a specific dynamic tool.
    /// </summary>
    [McpForUnityTool("invoke_dynamic",
        Description = "Invoke dynamically registered test functions. " +
                      "Use action='list' to discover available functions, " +
                      "action='call' with function_name and args to invoke one, " +
                      "action='describe' with function_name for details.")]
    public static class InvokeDynamic
    {
        public class Parameters
        {
            [ToolParameter("Action to perform: 'list', 'call', or 'describe'")]
            public string action { get; set; }

            [ToolParameter("Name of the dynamic function to call or describe", Required = false)]
            public string function_name { get; set; }

            [ToolParameter("JSON object of arguments to pass to the function (for 'call' action)", Required = false)]
            public string args { get; set; }
        }

        public static object HandleCommand(JObject @params)
        {
            string action = @params["action"]?.ToString()?.ToLowerInvariant();

            if (string.IsNullOrWhiteSpace(action))
            {
                return new ErrorResponse(
                    "Required parameter 'action' is missing. Use 'list', 'call', or 'describe'.");
            }

            switch (action)
            {
                case "list":
                    return HandleList();

                case "call":
                    return HandleCall(@params);

                case "describe":
                    return HandleDescribe(@params);

                default:
                    return new ErrorResponse(
                        $"Unknown action '{action}'. Supported: 'list', 'call', 'describe'.");
            }
        }

        // ────────────────────────────────────────────────────────────
        //  Actions
        // ────────────────────────────────────────────────────────────

        private static object HandleList()
        {
            var tools = DynamicToolRegistry.GetAll();

            if (tools.Length == 0)
            {
                return new SuccessResponse(
                    "No dynamic tools registered. " +
                    "Game code can register functions via DynamicToolRegistry.Register().",
                    new { count = 0, tools = Array.Empty<object>() });
            }

            var toolList = tools.Select(t => new
            {
                name = t.Name,
                description = t.Description,
                parameter_count = t.Parameters?.Length ?? 0,
                parameters = t.Parameters?.Select(p => new
                {
                    name = p.Name,
                    type = p.Type,
                    description = p.Description,
                    required = p.Required
                }).ToArray(),
                registered_at = t.RegisteredAt.ToString("O")
            }).ToArray();

            return new SuccessResponse(
                $"Found {tools.Length} registered dynamic tool(s).",
                new { count = tools.Length, tools = toolList });
        }

        private static object HandleCall(JObject @params)
        {
            string functionName = @params["function_name"]?.ToString();
            if (string.IsNullOrWhiteSpace(functionName))
            {
                return new ErrorResponse(
                    "Required parameter 'function_name' is missing for 'call' action.");
            }

            var toolInfo = DynamicToolRegistry.Get(functionName);
            if (toolInfo == null)
            {
                var available = DynamicToolRegistry.GetAll()
                    .Select(t => t.Name)
                    .ToArray();

                return new ErrorResponse(
                    $"No dynamic tool registered with name '{functionName}'. " +
                    $"Available: [{string.Join(", ", available)}]");
            }

            // Parse args
            Dictionary<string, object> args = new Dictionary<string, object>();
            var argsToken = @params["args"];
            if (argsToken != null)
            {
                JObject argsObj;
                if (argsToken.Type == JTokenType.String)
                {
                    // Args passed as a JSON string
                    try
                    {
                        argsObj = JObject.Parse(argsToken.ToString());
                    }
                    catch (Exception ex)
                    {
                        return new ErrorResponse(
                            $"Failed to parse 'args' as JSON: {ex.Message}");
                    }
                }
                else if (argsToken.Type == JTokenType.Object)
                {
                    argsObj = (JObject)argsToken;
                }
                else
                {
                    return new ErrorResponse(
                        $"Parameter 'args' must be a JSON object or JSON string, got: {argsToken.Type}");
                }

                foreach (var prop in argsObj.Properties())
                {
                    args[prop.Name] = ConvertJToken(prop.Value);
                }
            }

            // Invoke
            try
            {
                object result = DynamicToolRegistry.Invoke(functionName, args);
                return new SuccessResponse(
                    $"Successfully invoked dynamic tool '{functionName}'.",
                    new { function_name = functionName, result });
            }
            catch (Exception ex)
            {
                McpLog.Error($"[InvokeDynamic] Error invoking '{functionName}': {ex}");
                return new ErrorResponse(
                    $"Error invoking '{functionName}': {ex.Message}",
                    new { exception_type = ex.GetType().Name, stack_trace = ex.StackTrace });
            }
        }

        private static object HandleDescribe(JObject @params)
        {
            string functionName = @params["function_name"]?.ToString();
            if (string.IsNullOrWhiteSpace(functionName))
            {
                return new ErrorResponse(
                    "Required parameter 'function_name' is missing for 'describe' action.");
            }

            var toolInfo = DynamicToolRegistry.Get(functionName);
            if (toolInfo == null)
            {
                return new ErrorResponse(
                    $"No dynamic tool registered with name '{functionName}'.");
            }

            var parameterDescriptions = toolInfo.Parameters?.Select(p => new
            {
                name = p.Name,
                type = p.Type ?? "string",
                description = p.Description,
                required = p.Required,
                default_value = p.DefaultValue
            }).ToArray();

            return new SuccessResponse(
                $"Description of dynamic tool '{functionName}'.",
                new
                {
                    name = toolInfo.Name,
                    description = toolInfo.Description,
                    parameters = parameterDescriptions,
                    registered_at = toolInfo.RegisteredAt.ToString("O")
                });
        }

        // ────────────────────────────────────────────────────────────
        //  Helpers
        // ────────────────────────────────────────────────────────────

        /// <summary>
        /// Convert a JToken to a plain .NET object so handlers receive
        /// standard types (string, long, double, bool, etc.) instead of JTokens.
        /// </summary>
        private static object ConvertJToken(JToken token)
        {
            switch (token.Type)
            {
                case JTokenType.Integer:
                    return token.Value<long>();
                case JTokenType.Float:
                    return token.Value<double>();
                case JTokenType.String:
                    return token.Value<string>();
                case JTokenType.Boolean:
                    return token.Value<bool>();
                case JTokenType.Null:
                case JTokenType.Undefined:
                    return null;
                case JTokenType.Object:
                    // Convert nested object to Dictionary
                    var dict = new Dictionary<string, object>();
                    foreach (var prop in ((JObject)token).Properties())
                    {
                        dict[prop.Name] = ConvertJToken(prop.Value);
                    }
                    return dict;
                case JTokenType.Array:
                    return ((JArray)token).Select(ConvertJToken).ToArray();
                default:
                    return token.ToString();
            }
        }
    }
}
