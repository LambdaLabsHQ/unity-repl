using System;
using System.Collections.Generic;
using System.Linq;
using NativeMcp.Editor.Helpers;
using Newtonsoft.Json.Linq;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace NativeMcp.Editor.Tools
{
    /// <summary>
    /// MCP tool that returns the full in-memory scene hierarchy as a compact tree.
    /// Designed for agent use: quickly understand scene structure, locate objects
    /// by path/instanceID, and then invoke methods on them via invoke_dynamic.
    ///
    /// Output format per node (compact to minimise token usage):
    ///   { "n": name, "id": instanceID, "p": path, "a": activeInHierarchy,
    ///     "c": [componentTypeNames], "ch": [children...] }
    ///
    /// Supports optional filters:
    ///   - maxDepth        : limit recursion depth (default unlimited)
    ///   - includeInactive : include inactive GameObjects (default true)
    ///   - componentFilter : only return nodes that have at least one component
    ///                       whose type name contains this substring (case-insensitive)
    ///   - nameFilter      : only return nodes whose name contains this substring
    ///   - sceneIndex      : which loaded scene to query (default: active scene)
    ///   - includePath     : include full hierarchy path per node (default true)
    ///   - includeTransform: include local transform data (default false)
    /// </summary>
    [McpForUnityTool("get_scene_tree", Description =
        "Returns the full in-memory scene hierarchy as a compact tree. " +
        "Each node contains: name (n), instanceID (id), path (p), active (a), " +
        "components (c), and children (ch). Use instanceID with invoke_dynamic " +
        "or find_gameobjects for further operations. " +
        "Supports maxDepth, includeInactive, componentFilter, nameFilter, " +
        "sceneIndex, includePath, includeTransform parameters.")]
    public static class GetSceneTree
    {
        public class Parameters
        {
            [ToolParameter("Maximum recursion depth. -1 = unlimited (default)", Required = false, DefaultValue = "-1")]
            public int maxDepth { get; set; }

            [ToolParameter("Include inactive GameObjects (default true)", Required = false, DefaultValue = "true")]
            public bool includeInactive { get; set; }

            [ToolParameter("Only return nodes that have a component whose type name contains this substring (case-insensitive)", Required = false)]
            public string componentFilter { get; set; }

            [ToolParameter("Only return nodes whose name contains this substring (case-insensitive)", Required = false)]
            public string nameFilter { get; set; }

            [ToolParameter("Which loaded scene to query by index. -1 = active scene (default)", Required = false, DefaultValue = "-1")]
            public int sceneIndex { get; set; }

            [ToolParameter("Include full hierarchy path per node (default true)", Required = false, DefaultValue = "true")]
            public bool includePath { get; set; }

            [ToolParameter("Include local transform data (pos/rot/scl) per node (default false)", Required = false, DefaultValue = "false")]
            public bool includeTransform { get; set; }
        }

        public static object HandleCommand(JObject @params)
        {
            try
            {
                // ── Parse parameters ──────────────────────────────────────
                int maxDepth = ParamCoercion.CoerceInt(
                    @params?["maxDepth"] ?? @params?["max_depth"], -1);
                bool includeInactive = ParamCoercion.CoerceBool(
                    @params?["includeInactive"] ?? @params?["include_inactive"], true);
                string componentFilter = ParamCoercion.CoerceString(
                    @params?["componentFilter"] ?? @params?["component_filter"], null);
                string nameFilter = ParamCoercion.CoerceString(
                    @params?["nameFilter"] ?? @params?["name_filter"], null);
                int sceneIndex = ParamCoercion.CoerceInt(
                    @params?["sceneIndex"] ?? @params?["scene_index"], -1);
                bool includePath = ParamCoercion.CoerceBool(
                    @params?["includePath"] ?? @params?["include_path"], true);
                bool includeTransform = ParamCoercion.CoerceBool(
                    @params?["includeTransform"] ?? @params?["include_transform"], false);

                // ── Resolve target scene ──────────────────────────────────
                Scene scene;
                var prefabStage = PrefabStageUtility.GetCurrentPrefabStage();
                if (prefabStage != null)
                {
                    scene = prefabStage.scene;
                }
                else if (sceneIndex >= 0 && sceneIndex < SceneManager.sceneCount)
                {
                    scene = SceneManager.GetSceneAt(sceneIndex);
                }
                else
                {
                    scene = EditorSceneManager.GetActiveScene();
                }

                if (!scene.IsValid() || !scene.isLoaded)
                {
                    return new ErrorResponse("No valid loaded scene found.");
                }

                // ── Build tree ────────────────────────────────────────────
                var roots = scene.GetRootGameObjects();
                var tree = new List<object>();
                int nodeCount = 0;
                const int hardNodeLimit = 10000; // safety cap

                foreach (var root in roots)
                {
                    if (root == null) continue;
                    var node = BuildNode(root, 0, maxDepth, includeInactive,
                        componentFilter, nameFilter, includePath, includeTransform,
                        ref nodeCount, hardNodeLimit);
                    if (node != null) tree.Add(node);
                }

                return new SuccessResponse(
                    $"Scene tree for '{scene.name}' ({nodeCount} nodes)", new
                    {
                        scene = scene.name,
                        scenePath = scene.path,
                        nodeCount,
                        truncated = nodeCount >= hardNodeLimit,
                        tree
                    });
            }
            catch (Exception ex)
            {
                McpLog.Error($"[GetSceneTree] {ex.Message}");
                return new ErrorResponse($"Error building scene tree: {ex.Message}");
            }
        }

        // ────────────────────────────────────────────────────────────────────
        // Recursive node builder
        // ────────────────────────────────────────────────────────────────────
        private static object BuildNode(
            GameObject go, int depth, int maxDepth,
            bool includeInactive, string componentFilter, string nameFilter,
            bool includePath, bool includeTransform,
            ref int nodeCount, int hardNodeLimit)
        {
            if (go == null) return null;
            if (nodeCount >= hardNodeLimit) return null;
            if (!includeInactive && !go.activeInHierarchy) return null;

            // ── Collect component type names ──────────────────────────────
            var compNames = new List<string>();
            try
            {
                var components = go.GetComponents<Component>();
                foreach (var c in components)
                {
                    if (c != null)
                        compNames.Add(c.GetType().Name);
                }
            }
            catch { /* destroyed component */ }

            // ── Apply component filter (check self) ──────────────────────
            bool selfMatchesComponentFilter = string.IsNullOrEmpty(componentFilter)
                || compNames.Any(cn =>
                    cn.IndexOf(componentFilter, StringComparison.OrdinalIgnoreCase) >= 0);

            // ── Apply name filter (check self) ───────────────────────────
            bool selfMatchesNameFilter = string.IsNullOrEmpty(nameFilter)
                || go.name.IndexOf(nameFilter, StringComparison.OrdinalIgnoreCase) >= 0;

            // ── Recurse children ─────────────────────────────────────────
            List<object> children = null;
            bool atMaxDepth = maxDepth >= 0 && depth >= maxDepth;
            int directChildCount = go.transform.childCount;

            if (!atMaxDepth && directChildCount > 0)
            {
                children = new List<object>();
                foreach (Transform child in go.transform)
                {
                    if (nodeCount >= hardNodeLimit) break;
                    var childNode = BuildNode(child.gameObject, depth + 1, maxDepth,
                        includeInactive, componentFilter, nameFilter,
                        includePath, includeTransform,
                        ref nodeCount, hardNodeLimit);
                    if (childNode != null) children.Add(childNode);
                }
            }

            bool hasMatchingDescendants = children != null && children.Count > 0;

            // ── Filter logic: include this node if it matches, or has matching descendants ──
            bool hasFilter = !string.IsNullOrEmpty(componentFilter) || !string.IsNullOrEmpty(nameFilter);
            if (hasFilter)
            {
                bool selfMatches = selfMatchesComponentFilter && selfMatchesNameFilter;
                if (!selfMatches && !hasMatchingDescendants)
                    return null;
            }

            // ── Build output dictionary (compact keys) ───────────────────
            nodeCount++;
            var d = new Dictionary<string, object>
            {
                { "n", go.name },
                { "id", go.GetInstanceID() },
                { "a", go.activeInHierarchy },
                { "c", compNames }
            };

            if (includePath)
            {
                d["p"] = GetPath(go);
            }

            if (includeTransform)
            {
                var t = go.transform;
                d["t"] = new
                {
                    pos = new[] { t.localPosition.x, t.localPosition.y, t.localPosition.z },
                    rot = new[] { t.localEulerAngles.x, t.localEulerAngles.y, t.localEulerAngles.z },
                    scl = new[] { t.localScale.x, t.localScale.y, t.localScale.z }
                };
            }

            if (atMaxDepth && directChildCount > 0)
            {
                // Indicate truncated children
                d["ch"] = $"[{directChildCount} children, increase maxDepth to see]";
            }
            else if (children != null && children.Count > 0)
            {
                d["ch"] = children;
            }

            return d;
        }

        private static string GetPath(GameObject go)
        {
            if (go == null) return string.Empty;
            try
            {
                var names = new Stack<string>();
                Transform t = go.transform;
                while (t != null)
                {
                    names.Push(t.name);
                    t = t.parent;
                }
                return string.Join("/", names);
            }
            catch
            {
                return go.name;
            }
        }
    }
}
