using System.Collections.Generic;
using System.Threading.Tasks;
using NativeMcp.Editor.Helpers;
using Newtonsoft.Json.Linq;

namespace NativeMcp.Editor.Tools.Meta
{
    [McpForUnityTool("unity_invoke",
        Description =
            "Reflect-invoke any C# method or access any property in Unity. Two-step workflow:\n" +
            "1) action='resolve_method' with method='Type.Member' to inspect candidates.\n" +
            "2) action='call_method' with method='Type.ExactName' and args={...} to execute.\n" +
            "Supports static/instance methods and properties. Instance methods on MonoBehaviours are auto-located in the scene.\n" +
            "Also supports action='list'/'call'/'describe' for pre-registered dynamic tools.")]
    public static class UnityInvoke
    {
        private static readonly Dictionary<string, string> ActionMap = new()
        {
            ["_passthrough"] = "invoke_dynamic",
        };

        public class Parameters
        {
            [ToolParameter("Action: 'resolve_method', 'call_method', 'list', 'call', or 'describe'")]
            public string action { get; set; }

            [ToolParameter("Method/property path, e.g. 'MyClass.MyMethod'.", Required = false)]
            public string method { get; set; }

            [ToolParameter("Name of a registered dynamic function (for 'call'/'describe' actions)", Required = false)]
            public string function_name { get; set; }

            [ToolParameter("JSON object of arguments. Keys mapped to parameter names.", Required = false)]
            public object args { get; set; }
        }

        public static object HandleCommand(JObject @params)
        {
            // Pass through directly to invoke_dynamic — all actions are handled there
            return CommandRegistry.InvokeCommandAsync("invoke_dynamic", @params ?? new JObject())
                .GetAwaiter().GetResult();
        }
    }
}
