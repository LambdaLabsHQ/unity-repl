using System.Collections.Generic;
using System.Threading.Tasks;
using NativeMcp.Editor.Helpers;
using Newtonsoft.Json.Linq;

namespace NativeMcp.Editor.Tools.Meta
{
    [McpForUnityTool("unity_test",
        Description =
            "Run and list Unity tests. Pass 'action' to select an operation.\n" +
            "Actions:\n" +
            "- list(testMode?) — list available tests (EditMode or PlayMode)\n" +
            "- run(testMode?, testNames?, categoryNames?, assemblyNames?, timeoutSeconds?) — run tests with optional filters")]
    public static class UnityTest
    {
        private static readonly Dictionary<string, string> ActionMap = new()
        {
            ["list"] = "list_tests",
            ["run"] = "run_tests",
        };

        public class Parameters
        {
            [ToolParameter("Action to perform: list, run")]
            public string action { get; set; }

            [ToolParameter("Test mode: 'EditMode' or 'PlayMode' (default 'EditMode')", Required = false, DefaultValue = "EditMode")]
            public string testMode { get; set; }

            [ToolParameter("Array of fully qualified test names to run", Required = false)]
            public string[] testNames { get; set; }

            [ToolParameter("Array of NUnit category names to filter by", Required = false)]
            public string[] categoryNames { get; set; }

            [ToolParameter("Array of test assembly names to filter by (without .dll extension)", Required = false)]
            public string[] assemblyNames { get; set; }

            [ToolParameter("Maximum time in seconds to wait for run completion (default 120)", Required = false, DefaultValue = "120")]
            public int? timeoutSeconds { get; set; }
        }

        public static async Task<object> HandleCommand(JObject @params)
        {
            return await MetaToolDispatcher.DispatchAsync(@params, ActionMap, "unity_test");
        }
    }
}
