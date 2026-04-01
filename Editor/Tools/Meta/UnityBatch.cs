using System.Threading.Tasks;
using Newtonsoft.Json.Linq;

namespace NativeMcp.Editor.Tools.Meta
{
    [McpForUnityTool("unity_batch",
        Description =
            "Execute multiple tool commands sequentially in a single request.\n" +
            "Parameters:\n" +
            "- commands (array, required): Array of {tool, params} objects. 'tool' is the internal tool name " +
            "(e.g. 'gameobject_create', 'component_add'). Max 25 commands per batch.\n" +
            "- failFast (bool, optional): Stop on first failure (default false).\n" +
            "- parallel (bool, optional): Accepted for compatibility; execution remains sequential.\n" +
            "- maxParallelism (int, optional): Accepted for compatibility; execution remains sequential.\n" +
            "Commands run on the Unity main thread for API safety.")]
    public static class UnityBatch
    {
        public class Parameters
        {
            [ToolParameter("Array of command objects: [{tool, params}, ...]")]
            public object[] commands { get; set; }

            [ToolParameter("Stop on first failure", Required = false)]
            public bool? failFast { get; set; }

            [ToolParameter("Compatibility flag only; commands still run sequentially", Required = false)]
            public bool? parallel { get; set; }

            [ToolParameter("Compatibility field only; commands still run sequentially", Required = false)]
            public int? maxParallelism { get; set; }
        }

        public static async Task<object> HandleCommand(JObject @params)
        {
            // Pass through directly to batch_execute
            return await CommandRegistry.InvokeCommandAsync("batch_execute", @params ?? new JObject());
        }
    }
}
