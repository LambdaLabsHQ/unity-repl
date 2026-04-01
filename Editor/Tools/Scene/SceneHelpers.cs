using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using NativeMcp.Editor.Helpers;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace NativeMcp.Editor.Tools.Scene
{
    /// <summary>
    /// Shared helper methods extracted from ManageScene for use by individual scene_* tools.
    /// </summary>
    internal static class SceneHelpers
    {
        /// <summary>
        /// Force-opens a scene from disk.
        /// When the target scene is already the active scene (e.g. after domain reload from
        /// recompilation), EditorSceneManager.OpenScene may skip re-reading from disk, leaving
        /// rootCount=0 and all scene-placed GameObjects missing. This method detects that
        /// situation and forces a full reload by first creating a temporary empty scene.
        /// </summary>
        internal static void ForceOpenScene(string scenePath)
        {
            UnityEngine.SceneManagement.Scene active = EditorSceneManager.GetActiveScene();
            bool sameScene = active.IsValid() && active.path == scenePath;

            if (sameScene)
            {
                EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            }

            EditorSceneManager.OpenScene(scenePath, OpenSceneMode.Single);
        }

        /// <summary>
        /// Resolves a GameObject from a JToken that may be an instanceID (int), a path (string with '/'),
        /// or a name (plain string).
        /// </summary>
        internal static GameObject ResolveGameObject(JToken targetToken, UnityEngine.SceneManagement.Scene activeScene)
        {
            if (targetToken == null || targetToken.Type == JTokenType.Null) return null;

            try
            {
                if (targetToken.Type == JTokenType.Integer || int.TryParse(targetToken.ToString(), out _))
                {
                    if (int.TryParse(targetToken.ToString(), out int id))
                    {
                        var obj = EditorUtility.InstanceIDToObject(id);
                        if (obj is GameObject go) return go;
                        if (obj is Component c) return c.gameObject;
                    }
                }
            }
            catch { }

            string s = targetToken.ToString();
            if (string.IsNullOrEmpty(s)) return null;

            // Path-based find (e.g., "Root/Child/GrandChild")
            if (s.Contains("/"))
            {
                try
                {
                    var ids = GameObjectLookup.SearchGameObjects("by_path", s, includeInactive: true, maxResults: 1);
                    if (ids.Count > 0)
                    {
                        var byPath = GameObjectLookup.FindById(ids[0]);
                        if (byPath != null) return byPath;
                    }
                }
                catch { }
            }

            // Name-based find (first match, includes inactive)
            try
            {
                var all = activeScene.GetRootGameObjects();
                foreach (var root in all)
                {
                    if (root == null) continue;
                    if (root.name == s) return root;
                    var trs = root.GetComponentsInChildren<Transform>(includeInactive: true);
                    foreach (var t in trs)
                    {
                        if (t != null && t.gameObject != null && t.gameObject.name == s) return t.gameObject;
                    }
                }
            }
            catch { }

            return null;
        }

        /// <summary>
        /// Builds a lightweight summary dictionary for a single GameObject (no recursive children).
        /// </summary>
        internal static object BuildGameObjectSummary(GameObject go, bool includeTransform, int maxChildrenPerNode)
        {
            if (go == null) return null;

            int childCount = 0;
            try { childCount = go.transform != null ? go.transform.childCount : 0; } catch { }
            bool childrenTruncated = childCount > 0;

            var componentTypes = new List<string>();
            try
            {
                var components = go.GetComponents<Component>();
                if (components != null)
                {
                    foreach (var c in components)
                    {
                        if (c != null)
                        {
                            componentTypes.Add(c.GetType().Name);
                        }
                    }
                }
            }
            catch { }

            var d = new Dictionary<string, object>
            {
                { "name", go.name },
                { "instanceID", go.GetInstanceID() },
                { "activeSelf", go.activeSelf },
                { "activeInHierarchy", go.activeInHierarchy },
                { "tag", go.tag },
                { "layer", go.layer },
                { "isStatic", go.isStatic },
                { "path", GetGameObjectPath(go) },
                { "childCount", childCount },
                { "childrenTruncated", childrenTruncated },
                { "childrenCursor", childCount > 0 ? "0" : null },
                { "childrenPageSizeDefault", maxChildrenPerNode },
                { "componentTypes", componentTypes },
            };

            if (includeTransform && go.transform != null)
            {
                var t = go.transform;
                d["transform"] = new
                {
                    position = new[] { t.localPosition.x, t.localPosition.y, t.localPosition.z },
                    rotation = new[] { t.localRotation.eulerAngles.x, t.localRotation.eulerAngles.y, t.localRotation.eulerAngles.z },
                    scale = new[] { t.localScale.x, t.localScale.y, t.localScale.z },
                };
            }

            return d;
        }

        /// <summary>
        /// Returns the full hierarchy path of a GameObject (e.g. "Root/Child/GrandChild").
        /// </summary>
        internal static string GetGameObjectPath(GameObject go)
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

        /// <summary>
        /// Recursively builds a data representation of a GameObject and its children.
        /// </summary>
        internal static object GetGameObjectDataRecursive(GameObject go)
        {
            if (go == null)
                return null;

            var childrenData = new List<object>();
            foreach (Transform child in go.transform)
            {
                childrenData.Add(GetGameObjectDataRecursive(child.gameObject));
            }

            var gameObjectData = new Dictionary<string, object>
            {
                { "name", go.name },
                { "activeSelf", go.activeSelf },
                { "activeInHierarchy", go.activeInHierarchy },
                { "tag", go.tag },
                { "layer", go.layer },
                { "isStatic", go.isStatic },
                { "instanceID", go.GetInstanceID() },
                {
                    "transform",
                    new
                    {
                        position = new
                        {
                            x = go.transform.localPosition.x,
                            y = go.transform.localPosition.y,
                            z = go.transform.localPosition.z,
                        },
                        rotation = new
                        {
                            x = go.transform.localRotation.eulerAngles.x,
                            y = go.transform.localRotation.eulerAngles.y,
                            z = go.transform.localRotation.eulerAngles.z,
                        },
                        scale = new
                        {
                            x = go.transform.localScale.x,
                            y = go.transform.localScale.y,
                            z = go.transform.localScale.z,
                        },
                    }
                },
                { "children", childrenData },
            };

            return gameObjectData;
        }

        /// <summary>
        /// Sanitizes and resolves scene path components from user-provided name and path.
        /// Converts path to relative form, ensures "Assets/" prefix, applies default directory for create.
        /// </summary>
        /// <param name="name">Scene name without .unity extension.</param>
        /// <param name="path">Directory path relative to Assets/ (may be null).</param>
        /// <param name="relativeDir">Output: sanitized relative directory (e.g. "Scenes").</param>
        /// <param name="fullPathDir">Output: full system path to directory.</param>
        /// <param name="fullPath">Output: full system path to the .unity file (null if name is empty).</param>
        /// <param name="relativePath">Output: Assets-relative path to .unity file (null if name is empty).</param>
        /// <param name="applyDefaultDir">If true, uses "Scenes" as default when path is empty.</param>
        internal static void SanitizeScenePath(
            string name, string path,
            out string relativeDir, out string fullPathDir,
            out string fullPath, out string relativePath,
            bool applyDefaultDir = false)
        {
            relativeDir = path ?? string.Empty;
            if (!string.IsNullOrEmpty(relativeDir))
            {
                relativeDir = relativeDir.Replace('\\', '/').Trim('/');
                if (relativeDir.StartsWith("Assets/", StringComparison.OrdinalIgnoreCase))
                {
                    relativeDir = relativeDir.Substring("Assets/".Length).TrimStart('/');
                }
            }

            if (string.IsNullOrEmpty(path) && applyDefaultDir)
            {
                relativeDir = "Scenes";
            }

            string sceneFileName = string.IsNullOrEmpty(name) ? null : $"{name}.unity";
            fullPathDir = Path.Combine(Application.dataPath, relativeDir);
            fullPath = string.IsNullOrEmpty(sceneFileName)
                ? null
                : Path.Combine(fullPathDir, sceneFileName);
            relativePath = string.IsNullOrEmpty(sceneFileName)
                ? null
                : Path.Combine("Assets", relativeDir, sceneFileName).Replace('\\', '/');
        }

        /// <summary>
        /// Parses an optional integer from a JObject, supporting snake_case and camelCase keys.
        /// </summary>
        internal static int? ParseInt(JObject p, params string[] keys)
        {
            JToken t = null;
            foreach (var k in keys)
            {
                t = p[k];
                if (t != null) break;
            }
            if (t == null || t.Type == JTokenType.Null) return null;
            var s = t.ToString().Trim();
            if (s.Length == 0) return null;
            if (int.TryParse(s, out var i)) return i;
            if (double.TryParse(s, out var d)) return (int)d;
            return t.Type == JTokenType.Integer ? t.Value<int>() : (int?)null;
        }

        /// <summary>
        /// Parses an optional boolean from a JObject, supporting snake_case and camelCase keys.
        /// </summary>
        internal static bool? ParseBool(JObject p, params string[] keys)
        {
            JToken t = null;
            foreach (var k in keys)
            {
                t = p[k];
                if (t != null) break;
            }
            if (t == null || t.Type == JTokenType.Null) return null;
            try
            {
                if (t.Type == JTokenType.Boolean) return t.Value<bool>();
                var s = t.ToString().Trim();
                if (s.Length == 0) return null;
                if (bool.TryParse(s, out var b)) return b;
                if (s == "1") return true;
                if (s == "0") return false;
            }
            catch { }
            return null;
        }

        /// <summary>
        /// Reads a string value from a JObject, returning null if empty.
        /// </summary>
        internal static string ParseString(JObject p, params string[] keys)
        {
            foreach (var k in keys)
            {
                var val = p[k]?.ToString();
                if (!string.IsNullOrEmpty(val)) return val;
            }
            return null;
        }
    }
}
