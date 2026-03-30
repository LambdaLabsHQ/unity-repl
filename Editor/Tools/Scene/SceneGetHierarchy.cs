using System;
using System.Collections.Generic;
using System.Linq;
using NativeMcp.Editor.Helpers;
using Newtonsoft.Json.Linq;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace NativeMcp.Editor.Tools.Scene
{
    /// <summary>
    /// Get a paged hierarchy of GameObjects in the active scene or prefab stage.
    /// </summary>
    [McpForUnityTool("scene_get_hierarchy", Description = "Get a paged hierarchy of GameObjects in the active scene or prefab stage.")]
    public static class SceneGetHierarchy
    {
        public class Parameters
        {
            [ToolParameter("Parent GameObject name, path, or instanceID to list children of (omit for roots)", Required = false)]
            public string parent { get; set; }

            [ToolParameter("Number of items per page (default 50, max 500)", Required = false)]
            public int? pageSize { get; set; }

            [ToolParameter("Cursor position to start from (default 0)", Required = false)]
            public int? cursor { get; set; }

            [ToolParameter("Maximum total nodes to return (default 1000, max 5000)", Required = false)]
            public int? maxNodes { get; set; }

            [ToolParameter("Maximum recursion depth (reserved for future use)", Required = false)]
            public int? maxDepth { get; set; }

            [ToolParameter("Maximum children summary per node (default 200, max 2000)", Required = false)]
            public int? maxChildrenPerNode { get; set; }

            [ToolParameter("Include transform data in summary (default false)", Required = false)]
            public bool? includeTransform { get; set; }
        }

        public static object HandleCommand(JObject @params)
        {
            var p = @params ?? new JObject();

            JToken parentToken = p["parent"];
            int? pageSize = SceneHelpers.ParseInt(p, "pageSize", "page_size");
            int? cursor = SceneHelpers.ParseInt(p, "cursor");
            int? maxNodes = SceneHelpers.ParseInt(p, "maxNodes", "max_nodes");
            int? maxChildrenPerNode = SceneHelpers.ParseInt(p, "maxChildrenPerNode", "max_children_per_node");
            bool? includeTransform = SceneHelpers.ParseBool(p, "includeTransform", "include_transform");

            try
            {
                // Check Prefab Stage first
                var prefabStage = PrefabStageUtility.GetCurrentPrefabStage();
                UnityEngine.SceneManagement.Scene activeScene;

                if (prefabStage != null)
                {
                    activeScene = prefabStage.scene;
                }
                else
                {
                    activeScene = EditorSceneManager.GetActiveScene();
                }

                if (!activeScene.IsValid() || !activeScene.isLoaded)
                {
                    return new ErrorResponse("No valid and loaded scene is active to get hierarchy from.");
                }

                int resolvedPageSize = Mathf.Clamp(pageSize ?? 50, 1, 500);
                int resolvedCursor = Mathf.Max(0, cursor ?? 0);
                int resolvedMaxNodes = Mathf.Clamp(maxNodes ?? 1000, 1, 5000);
                int effectiveTake = Mathf.Min(resolvedPageSize, resolvedMaxNodes);
                int resolvedMaxChildrenPerNode = Mathf.Clamp(maxChildrenPerNode ?? 200, 0, 2000);
                bool resolvedIncludeTransform = includeTransform ?? false;

                List<GameObject> nodes;
                string scope;

                GameObject parentGo = SceneHelpers.ResolveGameObject(parentToken, activeScene);
                if (parentToken == null || parentToken.Type == JTokenType.Null)
                {
                    nodes = activeScene.GetRootGameObjects().Where(go => go != null).ToList();
                    scope = "roots";
                }
                else
                {
                    if (parentGo == null)
                        return new ErrorResponse($"Parent GameObject ('{parentToken}') not found.");

                    nodes = new List<GameObject>(parentGo.transform.childCount);
                    foreach (Transform child in parentGo.transform)
                    {
                        if (child != null) nodes.Add(child.gameObject);
                    }
                    scope = "children";
                }

                int total = nodes.Count;
                if (resolvedCursor > total) resolvedCursor = total;
                int end = Mathf.Min(total, resolvedCursor + effectiveTake);

                var items = new List<object>(Mathf.Max(0, end - resolvedCursor));
                for (int i = resolvedCursor; i < end; i++)
                {
                    var go = nodes[i];
                    if (go == null) continue;
                    items.Add(SceneHelpers.BuildGameObjectSummary(go, resolvedIncludeTransform, resolvedMaxChildrenPerNode));
                }

                bool truncated = end < total;
                string nextCursor = truncated ? end.ToString() : null;

                var payload = new
                {
                    scope = scope,
                    cursor = resolvedCursor,
                    pageSize = effectiveTake,
                    next_cursor = nextCursor,
                    truncated = truncated,
                    total = total,
                    items = items,
                };

                return new SuccessResponse($"Retrieved hierarchy page for scene '{activeScene.name}'.", payload);
            }
            catch (Exception e)
            {
                return new ErrorResponse($"Error getting scene hierarchy: {e.Message}");
            }
        }
    }
}
