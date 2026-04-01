using System.Collections.Generic;
using System.Threading.Tasks;
using NativeMcp.Editor.Helpers;
using Newtonsoft.Json.Linq;

namespace NativeMcp.Editor.Tools.Meta
{
    [McpForUnityTool("unity_console",
        Description =
            "Read or clear the Unity Editor console.\n" +
            "Actions:\n" +
            "- get(types?, count?, pageSize?, cursor?, filterText?, includeStacktrace?, format?) — read log entries\n" +
            "- clear() — clear all console entries\n" +
            "Pass action as 'action' parameter (default: 'get').")]
    public static class UnityConsole
    {
        public class Parameters
        {
            [ToolParameter("Action: 'get' (default) or 'clear'", Required = false)]
            public string action { get; set; }

            [ToolParameter("Log types to include: error, warning, log, all", Required = false)]
            public string[] types { get; set; }

            [ToolParameter("Maximum entries in non-paging mode", Required = false)]
            public int? count { get; set; }

            [ToolParameter("Page size for paged mode", Required = false)]
            public int? pageSize { get; set; }

            [ToolParameter("Cursor index for paged mode", Required = false)]
            public int? cursor { get; set; }

            [ToolParameter("Case-insensitive text filter", Required = false)]
            public string filterText { get; set; }

            [ToolParameter("Include stack traces in output", Required = false)]
            public bool? includeStacktrace { get; set; }

            [ToolParameter("Output format: plain, detailed, json", Required = false)]
            public string format { get; set; }
        }

        public static object HandleCommand(JObject @params)
        {
            // Pass through directly to read_console — it already handles the action parameter
            return CommandRegistry.InvokeCommandAsync("read_console", @params ?? new JObject())
                .GetAwaiter().GetResult();
        }
    }
}
