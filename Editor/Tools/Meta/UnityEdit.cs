using System.Collections.Generic;
using System.Threading.Tasks;
using NativeMcp.Editor.Helpers;
using Newtonsoft.Json.Linq;

namespace NativeMcp.Editor.Tools.Meta
{
    [McpForUnityTool("unity_edit",
        Description =
            "Modify the Unity scene. Pass 'action' to select an operation.\n" +
            "Actions:\n" +
            "— GameObject:\n" +
            "  - create_gameobject(name, primitiveType?, parent?, position?, rotation?, scale?, componentProperties?, saveAsPrefab?, prefabPath?)\n" +
            "  - modify_gameobject(target, name?, tag?, layer?, parent?, position?, rotation?, scale?, setActive?, searchMethod?)\n" +
            "  - delete_gameobject(target, searchMethod?)\n" +
            "  - duplicate_gameobject(target, new_name?, position?, offset?, parent?, searchMethod?)\n" +
            "  - move_gameobject(target, reference_object, direction?, distance?, offset?, world_space?, searchMethod?)\n" +
            "— Component:\n" +
            "  - add_component(target, componentType, properties?, searchMethod?)\n" +
            "  - remove_component(target, componentType, searchMethod?)\n" +
            "  - set_property(target, componentType, property?, value?, properties?, searchMethod?)\n" +
            "— Scene:\n" +
            "  - create_scene(name, path?)\n" +
            "  - load_scene(name?, path?, buildIndex?)\n" +
            "  - save_scene(name?, path?)\n" +
            "— Prefab:\n" +
            "  - open_prefab(prefabPath, mode?)\n" +
            "  - close_prefab(saveBeforeClose?)\n" +
            "  - save_prefab()\n" +
            "  - create_prefab(target, prefabPath, searchInactive?, allowOverwrite?)")]
    public static class UnityEdit
    {
        private static readonly Dictionary<string, string> ActionMap = new()
        {
            // GameObject
            ["create_gameobject"] = "gameobject_create",
            ["modify_gameobject"] = "gameobject_modify",
            ["delete_gameobject"] = "gameobject_delete",
            ["duplicate_gameobject"] = "gameobject_duplicate",
            ["move_gameobject"] = "gameobject_move_relative",
            // Component
            ["add_component"] = "component_add",
            ["remove_component"] = "component_remove",
            ["set_property"] = "component_set_property",
            // Scene
            ["create_scene"] = "scene_create",
            ["load_scene"] = "scene_load",
            ["save_scene"] = "scene_save",
            // Prefab
            ["open_prefab"] = "prefab_open_stage",
            ["close_prefab"] = "prefab_close_stage",
            ["save_prefab"] = "prefab_save_stage",
            ["create_prefab"] = "prefab_create_from_gameobject",
        };

        public class Parameters
        {
            [ToolParameter("Action to perform (see tool description for full list)")]
            public string action { get; set; }

            [ToolParameter("Object name (create_gameobject/create_scene/load_scene/save_scene)", Required = false)]
            public string name { get; set; }

            [ToolParameter("Target object (name/path/instanceID, action-dependent)", Required = false)]
            public object target { get; set; }

            [ToolParameter("Primitive type for create_gameobject", Required = false)]
            public string primitiveType { get; set; }

            [ToolParameter("Parent object (name/path/instanceID or null to unparent)", Required = false)]
            public object parent { get; set; }

            [ToolParameter("Position vector as {x,y,z}", Required = false)]
            public object position { get; set; }

            [ToolParameter("Rotation vector (Euler) as {x,y,z}", Required = false)]
            public object rotation { get; set; }

            [ToolParameter("Scale vector as {x,y,z}", Required = false)]
            public object scale { get; set; }

            [ToolParameter("Per-component property overrides as { ComponentType: { prop: value } }", Required = false)]
            public object componentProperties { get; set; }

            [ToolParameter("Save created GameObject as prefab", Required = false)]
            public bool? saveAsPrefab { get; set; }

            [ToolParameter("Prefab path for open/create/instantiate operations", Required = false)]
            public string prefabPath { get; set; }

            [ToolParameter("Tag name to assign", Required = false)]
            public string tag { get; set; }

            [ToolParameter("Layer name to assign", Required = false)]
            public string layer { get; set; }

            [ToolParameter("Set GameObject active/inactive", Required = false)]
            public bool? setActive { get; set; }

            [ToolParameter("Search strategy for target lookup", Required = false)]
            public string searchMethod { get; set; }

            [ToolParameter("Name for duplicated GameObject", Required = false)]
            public string new_name { get; set; }

            [ToolParameter("Offset vector as {x,y,z}", Required = false)]
            public object offset { get; set; }

            [ToolParameter("Reference object (name/path/instanceID) for move_gameobject", Required = false)]
            public object reference_object { get; set; }

            [ToolParameter("Direction for relative movement: right/left/up/down/forward/back", Required = false)]
            public string direction { get; set; }

            [ToolParameter("Distance for relative movement", Required = false)]
            public float? distance { get; set; }

            [ToolParameter("Use world space for relative movement", Required = false)]
            public bool? world_space { get; set; }

            [ToolParameter("Component type name", Required = false)]
            public string componentType { get; set; }

            [ToolParameter("Single property name for set_property", Required = false)]
            public string property { get; set; }

            [ToolParameter("Single property value for set_property", Required = false)]
            public object value { get; set; }

            [ToolParameter("Multiple properties object for set_property/add_component", Required = false)]
            public object properties { get; set; }

            [ToolParameter("Scene directory path relative to Assets/", Required = false)]
            public string path { get; set; }

            [ToolParameter("Build index for load_scene", Required = false)]
            public int? buildIndex { get; set; }

            [ToolParameter("Prefab open mode (InIsolation)", Required = false)]
            public string mode { get; set; }

            [ToolParameter("Save prefab before close_prefab", Required = false)]
            public bool? saveBeforeClose { get; set; }

            [ToolParameter("Search inactive objects when creating prefab", Required = false)]
            public bool? searchInactive { get; set; }

            [ToolParameter("Allow overwriting existing prefab asset", Required = false)]
            public bool? allowOverwrite { get; set; }
        }

        public static async Task<object> HandleCommand(JObject @params)
        {
            return await MetaToolDispatcher.DispatchAsync(@params, ActionMap, "unity_edit");
        }
    }
}
