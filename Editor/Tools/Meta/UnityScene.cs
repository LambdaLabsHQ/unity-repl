using System.Collections.Generic;
using System.Threading.Tasks;
using NativeMcp.Editor.Helpers;
using Newtonsoft.Json.Linq;

namespace NativeMcp.Editor.Tools.Meta
{
    [McpForUnityTool("unity_scene",
        Description =
            "Observe the Unity scene. Pass 'action' to select an operation.\n" +
            "Actions:\n" +
            "- get_tree(maxDepth?, includeInactive?, componentFilter?, nameFilter?, sceneIndex?, includePath?, includeTransform?) " +
              "— full hierarchy as compact recursive tree\n" +
            "- find(searchMethod?, searchTerm?, includeInactive?, pageSize?, cursor?) " +
              "— search GameObjects by name/tag/component/path\n" +
            "- get_hierarchy(parent?, pageSize?, cursor?, maxNodes?, maxDepth?, maxChildrenPerNode?, includeTransform?) " +
              "— paged hierarchy listing\n" +
            "- get_active() — active scene info\n" +
            "- get_build_settings() — scenes in Build Settings\n" +
            "- screenshot(fileName?, superSize?) — capture camera view as image")]
    public static class UnityScene
    {
        private static readonly Dictionary<string, string> ActionMap = new()
        {
            ["get_tree"] = "get_scene_tree",
            ["find"] = "find_gameobjects",
            ["get_hierarchy"] = "scene_get_hierarchy",
            ["get_active"] = "scene_get_active",
            ["get_build_settings"] = "scene_get_build_settings",
            ["screenshot"] = "scene_screenshot",
        };

        public class Parameters
        {
            [ToolParameter("Action to perform: get_tree, find, get_hierarchy, get_active, get_build_settings, screenshot")]
            public string action { get; set; }

            [ToolParameter("Maximum recursion depth for get_tree/get_hierarchy", Required = false)]
            public int? maxDepth { get; set; }

            [ToolParameter("Include inactive GameObjects", Required = false)]
            public bool? includeInactive { get; set; }

            [ToolParameter("Filter objects by component type name substring", Required = false)]
            public string componentFilter { get; set; }

            [ToolParameter("Filter objects by name substring", Required = false)]
            public string nameFilter { get; set; }

            [ToolParameter("Loaded scene index to query (-1 for active/all, action-dependent)", Required = false)]
            public int? sceneIndex { get; set; }

            [ToolParameter("Include hierarchy path in get_tree output", Required = false)]
            public bool? includePath { get; set; }

            [ToolParameter("Include transform data in output", Required = false)]
            public bool? includeTransform { get; set; }

            [ToolParameter("Search method for find action", Required = false)]
            public string searchMethod { get; set; }

            [ToolParameter("Search term for find action", Required = false)]
            public string searchTerm { get; set; }

            [ToolParameter("Alias for searchTerm when finding objects", Required = false)]
            public object target { get; set; }

            [ToolParameter("Page size for paginated responses", Required = false)]
            public int? pageSize { get; set; }

            [ToolParameter("Cursor for paginated responses", Required = false)]
            public int? cursor { get; set; }

            [ToolParameter("Parent GameObject (name/path/id) for get_hierarchy", Required = false)]
            public object parent { get; set; }

            [ToolParameter("Maximum total nodes for get_hierarchy", Required = false)]
            public int? maxNodes { get; set; }

            [ToolParameter("Maximum child summary count per node for get_hierarchy", Required = false)]
            public int? maxChildrenPerNode { get; set; }

            [ToolParameter("Output screenshot file name", Required = false)]
            public string fileName { get; set; }

            [ToolParameter("Screenshot supersampling multiplier", Required = false)]
            public int? superSize { get; set; }
        }

        public static async Task<object> HandleCommand(JObject @params)
        {
            return await MetaToolDispatcher.DispatchAsync(@params, ActionMap, "unity_scene");
        }
    }
}
