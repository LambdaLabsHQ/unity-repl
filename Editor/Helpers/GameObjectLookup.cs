using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace NativeMcp.Editor.Helpers
{
    /// <summary>
    /// Utility class for finding and looking up GameObjects in the scene.
    /// Provides search functionality by name, tag, layer, component, path, and instance ID.
    /// </summary>
    public static class GameObjectLookup
    {
        private static bool _hasDontDestroyOnLoadSceneCache;
        private static Scene _cachedDontDestroyOnLoadScene;

        static GameObjectLookup()
        {
            EditorApplication.playModeStateChanged += _ => InvalidateDontDestroyOnLoadSceneCache();
        }

        /// <summary>
        /// Supported search methods for finding GameObjects.
        /// </summary>
        public enum SearchMethod
        {
            ByName,
            ByTag,
            ByLayer,
            ByComponent,
            ByPath,
            ById
        }

        /// <summary>
        /// Parses a search method string into the enum value.
        /// </summary>
        public static SearchMethod ParseSearchMethod(string method)
        {
            if (string.IsNullOrEmpty(method))
                return SearchMethod.ByName;

            return method.ToLowerInvariant() switch
            {
                "by_name" => SearchMethod.ByName,
                "by_tag" => SearchMethod.ByTag,
                "by_layer" => SearchMethod.ByLayer,
                "by_component" => SearchMethod.ByComponent,
                "by_path" => SearchMethod.ByPath,
                "by_id" => SearchMethod.ById,
                _ => throw new ArgumentException(
                    $"Unknown search method '{method}'. " +
                    "Valid: by_name, by_tag, by_layer, by_component, by_path, by_id.")
            };
        }

        /// <summary>
        /// Finds a single GameObject based on the target and search method.
        /// </summary>
        /// <param name="target">The target identifier (name, ID, path, etc.)</param>
        /// <param name="searchMethod">The search method to use</param>
        /// <param name="includeInactive">Whether to include inactive objects</param>
        /// <returns>The found GameObject or null</returns>
        public static GameObject FindByTarget(JToken target, string searchMethod, bool includeInactive = false)
        {
            if (target == null)
                return null;

            var results = SearchGameObjects(searchMethod, target.ToString(), includeInactive, 1);
            return results.Count > 0 ? FindById(results[0]) : null;
        }

        /// <summary>
        /// Finds a GameObject by its instance ID.
        /// </summary>
        public static GameObject FindById(int instanceId)
        {
            return EditorUtility.InstanceIDToObject(instanceId) as GameObject;
        }

        /// <summary>
        /// Searches for GameObjects and returns their instance IDs.
        /// </summary>
        /// <param name="searchMethod">The search method string (by_name, by_tag, etc.)</param>
        /// <param name="searchTerm">The term to search for</param>
        /// <param name="includeInactive">Whether to include inactive objects</param>
        /// <param name="maxResults">Maximum number of results to return (0 = unlimited)</param>
        /// <returns>List of instance IDs</returns>
        public static List<int> SearchGameObjects(string searchMethod, string searchTerm, bool includeInactive = false, int maxResults = 0)
        {
            var method = ParseSearchMethod(searchMethod);
            return SearchGameObjects(method, searchTerm, includeInactive, maxResults);
        }

        /// <summary>
        /// Searches for GameObjects and returns their instance IDs.
        /// </summary>
        /// <param name="method">The search method</param>
        /// <param name="searchTerm">The term to search for</param>
        /// <param name="includeInactive">Whether to include inactive objects</param>
        /// <param name="maxResults">Maximum number of results to return (0 = unlimited)</param>
        /// <returns>List of instance IDs</returns>
        public static List<int> SearchGameObjects(SearchMethod method, string searchTerm, bool includeInactive = false, int maxResults = 0)
        {
            var results = new List<int>();

            switch (method)
            {
                case SearchMethod.ById:
                    if (int.TryParse(searchTerm, out int instanceId))
                    {
                        var obj = EditorUtility.InstanceIDToObject(instanceId) as GameObject;
                        if (obj != null && (includeInactive || obj.activeInHierarchy))
                        {
                            results.Add(instanceId);
                        }
                    }
                    break;

                case SearchMethod.ByName:
                    results.AddRange(SearchByName(searchTerm, includeInactive, maxResults));
                    break;

                case SearchMethod.ByPath:
                    results.AddRange(SearchByPath(searchTerm, includeInactive));
                    break;

                case SearchMethod.ByTag:
                    results.AddRange(SearchByTag(searchTerm, includeInactive, maxResults));
                    break;

                case SearchMethod.ByLayer:
                    results.AddRange(SearchByLayer(searchTerm, includeInactive, maxResults));
                    break;

                case SearchMethod.ByComponent:
                    results.AddRange(SearchByComponent(searchTerm, includeInactive, maxResults));
                    break;
            }

            return results;
        }

        private static IEnumerable<int> SearchByName(string name, bool includeInactive, int maxResults)
        {
            var allObjects = GetAllSceneObjects(includeInactive);
            var matching = allObjects.Where(go => go.name == name);

            if (maxResults > 0)
                matching = matching.Take(maxResults);

            return matching.Select(go => go.GetInstanceID());
        }

        private static IEnumerable<int> SearchByPath(string path, bool includeInactive)
        {
            // Check Prefab Stage first - GameObject.Find() doesn't work in Prefab Stage
            var prefabStage = PrefabStageUtility.GetCurrentPrefabStage();
            if (prefabStage != null)
            {
                // Use GetAllSceneObjects which already handles Prefab Stage
                var allObjects = GetAllSceneObjects(includeInactive);
                foreach (var go in allObjects)
                {
                    if (MatchesPath(go, path))
                    {
                        yield return go.GetInstanceID();
                    }
                }
                yield break;
            }

            // Normal scene mode
            // NOTE: Unity's GameObject.Find(path) only finds ACTIVE GameObjects.
            // If includeInactive=true, we need to search manually to find inactive objects.
            if (includeInactive)
            {
                // Search manually to support inactive objects
                var allObjects = GetAllSceneObjects(true);
                foreach (var go in allObjects)
                {
                    if (MatchesPath(go, path))
                    {
                        yield return go.GetInstanceID();
                    }
                }
            }
            else
            {
                // Use GameObject.Find for active objects only (Unity API limitation)
                var found = GameObject.Find(path);
                if (found != null)
                {
                    yield return found.GetInstanceID();
                }
            }
        }

        private static IEnumerable<int> SearchByTag(string tag, bool includeInactive, int maxResults)
        {
            GameObject[] taggedObjects;
            try
            {
                if (includeInactive)
                {
                    // FindGameObjectsWithTag doesn't find inactive, so we need to iterate all
                    var allObjects = GetAllSceneObjects(true);
                    taggedObjects = allObjects.Where(go => go.CompareTag(tag)).ToArray();
                }
                else
                {
                    taggedObjects = GameObject.FindGameObjectsWithTag(tag);
                }
            }
            catch (UnityException)
            {
                // Tag doesn't exist
                yield break;
            }

            var results = taggedObjects.AsEnumerable();
            if (maxResults > 0)
                results = results.Take(maxResults);

            foreach (var go in results)
            {
                yield return go.GetInstanceID();
            }
        }

        private static IEnumerable<int> SearchByLayer(string layerName, bool includeInactive, int maxResults)
        {
            int layer = LayerMask.NameToLayer(layerName);
            if (layer == -1)
            {
                // Try parsing as layer number
                if (!int.TryParse(layerName, out layer) || layer < 0 || layer > 31)
                {
                    yield break;
                }
            }

            var allObjects = GetAllSceneObjects(includeInactive);
            var matching = allObjects.Where(go => go.layer == layer);

            if (maxResults > 0)
                matching = matching.Take(maxResults);

            foreach (var go in matching)
            {
                yield return go.GetInstanceID();
            }
        }

        private static IEnumerable<int> SearchByComponent(string componentTypeName, bool includeInactive, int maxResults)
        {
            Type componentType = FindComponentType(componentTypeName);
            if (componentType == null)
            {
                McpLog.Warn($"[GameObjectLookup] Component type '{componentTypeName}' not found.");
                yield break;
            }

            var allObjects = GetAllSceneObjects(includeInactive);
            var count = 0;

            foreach (var go in allObjects)
            {
                if (go.GetComponent(componentType) != null)
                {
                    yield return go.GetInstanceID();
                    count++;

                    if (maxResults > 0 && count >= maxResults)
                        yield break;
                }
            }
        }

        /// <summary>
        /// Gets all GameObjects across all loaded scenes, including DontDestroyOnLoad.
        /// In Play Mode, searches all loaded scenes (not just active) to handle
        /// scene transitions and domain reload timing issues.
        /// </summary>
        public static IEnumerable<GameObject> GetAllSceneObjects(bool includeInactive)
        {
            // Check Prefab Stage first
            var prefabStage = PrefabStageUtility.GetCurrentPrefabStage();
            if (prefabStage != null && prefabStage.prefabContentsRoot != null)
            {
                // Use Prefab Stage's prefabContentsRoot
                foreach (var go in GetObjectAndDescendants(prefabStage.prefabContentsRoot, includeInactive))
                {
                    yield return go;
                }
                yield break;
            }

            // Search ALL loaded scenes (not just active scene)
            // This is critical for Play Mode where objects may be in different scenes
            // due to scene transitions, additive loading, or domain reload timing
            var visitedIds = new HashSet<int>();
            for (int i = 0; i < SceneManager.sceneCount; i++)
            {
                var scene = SceneManager.GetSceneAt(i);
                if (!scene.IsValid() || !scene.isLoaded) continue;

                var rootObjects = scene.GetRootGameObjects();
                foreach (var root in rootObjects)
                {
                    if (root == null) continue;
                    foreach (var go in GetObjectAndDescendants(root, includeInactive))
                    {
                        if (visitedIds.Add(go.GetInstanceID()))
                            yield return go;
                    }
                }
            }

            // In Play Mode, also search DontDestroyOnLoad scene
            // DontDestroyOnLoad objects are in a hidden scene not enumerated by SceneManager
            if (Application.isPlaying)
            {
                foreach (var go in GetDontDestroyOnLoadObjects(includeInactive))
                {
                    if (visitedIds.Add(go.GetInstanceID()))
                        yield return go;
                }
            }
        }

        /// <summary>
        /// Gets all GameObjects in the DontDestroyOnLoad scene.
        /// Uses a temporary helper object to discover the hidden scene.
        /// </summary>
        internal static IEnumerable<GameObject> GetDontDestroyOnLoadObjects(bool includeInactive)
        {
            if (!TryGetDontDestroyOnLoadScene(out var ddolScene))
                yield break;

            var rootObjects = ddolScene.GetRootGameObjects();
            foreach (var root in rootObjects)
            {
                if (root == null) continue;
                // Skip the temp object (already destroyed, but defensive)
                if (root.name == "__MCP_DDOL_Probe__") continue;
                foreach (var go in GetObjectAndDescendants(root, includeInactive))
                {
                    yield return go;
                }
            }
        }

        internal static bool TryGetDontDestroyOnLoadScene(out Scene scene)
        {
            scene = default;

            if (!Application.isPlaying)
            {
                InvalidateDontDestroyOnLoadSceneCache();
                return false;
            }

            if (_hasDontDestroyOnLoadSceneCache
                && _cachedDontDestroyOnLoadScene.IsValid()
                && _cachedDontDestroyOnLoadScene.isLoaded)
            {
                scene = _cachedDontDestroyOnLoadScene;
                return true;
            }

            GameObject temp = null;
            try
            {
                temp = new GameObject("__MCP_DDOL_Probe__");
                temp.hideFlags = HideFlags.HideAndDontSave;
                UnityEngine.Object.DontDestroyOnLoad(temp);
                scene = temp.scene;
            }
            finally
            {
                if (temp != null)
                    UnityEngine.Object.DestroyImmediate(temp);
            }

            if (!scene.IsValid())
            {
                InvalidateDontDestroyOnLoadSceneCache();
                return false;
            }

            _cachedDontDestroyOnLoadScene = scene;
            _hasDontDestroyOnLoadSceneCache = true;
            return true;
        }

        private static void InvalidateDontDestroyOnLoadSceneCache()
        {
            _cachedDontDestroyOnLoadScene = default;
            _hasDontDestroyOnLoadSceneCache = false;
        }

        private static IEnumerable<GameObject> GetObjectAndDescendants(GameObject obj, bool includeInactive)
        {
            if (!includeInactive && !obj.activeInHierarchy)
                yield break;

            yield return obj;

            foreach (Transform child in obj.transform)
            {
                foreach (var descendant in GetObjectAndDescendants(child.gameObject, includeInactive))
                {
                    yield return descendant;
                }
            }
        }

        /// <summary>
        /// Finds a component type by name, searching loaded assemblies.
        /// </summary>
        /// <remarks>
        /// Delegates to UnityTypeResolver.ResolveComponent() for unified type resolution.
        /// </remarks>
        public static Type FindComponentType(string typeName)
        {
            return UnityTypeResolver.ResolveComponent(typeName);
        }

        /// <summary>
        /// Checks whether a GameObject matches a path or trailing path segment.
        /// </summary>
        internal static bool MatchesPath(GameObject go, string path)
        {
            if (go == null || string.IsNullOrEmpty(path))
                return false;

            var goPath = GetGameObjectPath(go);
            return goPath == path || goPath.EndsWith("/" + path);
        }

        /// <summary>
        /// Gets the hierarchical path of a GameObject.
        /// </summary>
        public static string GetGameObjectPath(GameObject obj)
        {
            if (obj == null)
                return string.Empty;

            var path = obj.name;
            var parent = obj.transform.parent;

            while (parent != null)
            {
                path = parent.name + "/" + path;
                parent = parent.parent;
            }

            return path;
        }
    }
}
