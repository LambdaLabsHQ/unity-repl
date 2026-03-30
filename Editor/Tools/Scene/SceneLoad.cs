using System;
using System.IO;
using NativeMcp.Editor.Helpers;
using Newtonsoft.Json.Linq;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace NativeMcp.Editor.Tools.Scene
{
    /// <summary>
    /// Load an existing scene by name/path or build index.
    /// </summary>
    [McpForUnityTool("scene_load", Description = "Load an existing scene by name/path or build index.")]
    public static class SceneLoad
    {
        public class Parameters
        {
            [ToolParameter("Scene name without .unity extension", Required = false)]
            public string name { get; set; }

            [ToolParameter("Directory path relative to Assets/", Required = false)]
            public string path { get; set; }

            [ToolParameter("Build index of the scene to load", Required = false)]
            public int? buildIndex { get; set; }
        }

        public static object HandleCommand(JObject @params)
        {
            var p = @params ?? new JObject();
            string name = SceneHelpers.ParseString(p, "name");
            string path = SceneHelpers.ParseString(p, "path");
            int? buildIndex = SceneHelpers.ParseInt(p, "buildIndex", "build_index");

            SceneHelpers.SanitizeScenePath(name, path,
                out _, out _, out _, out string relativePath);

            if (!string.IsNullOrEmpty(relativePath))
                return LoadByPath(relativePath);
            else if (buildIndex.HasValue)
                return LoadByBuildIndex(buildIndex.Value);
            else
                return new ErrorResponse("Either 'name'/'path' or 'buildIndex' must be provided for scene_load.");
        }

        private static object LoadByPath(string relativePath)
        {
            if (!File.Exists(
                Path.Combine(
                    Application.dataPath.Substring(0, Application.dataPath.Length - "Assets".Length),
                    relativePath)))
            {
                return new ErrorResponse($"Scene file not found at '{relativePath}'.");
            }

            if (EditorSceneManager.GetActiveScene().isDirty)
            {
                return new ErrorResponse(
                    "Current scene has unsaved changes. Please save or discard changes before loading a new scene.");
            }

            try
            {
                SceneHelpers.ForceOpenScene(relativePath);
                return new SuccessResponse(
                    $"Scene '{relativePath}' loaded successfully.",
                    new
                    {
                        path = relativePath,
                        name = Path.GetFileNameWithoutExtension(relativePath),
                    }
                );
            }
            catch (Exception e)
            {
                return new ErrorResponse($"Error loading scene '{relativePath}': {e.Message}");
            }
        }

        private static object LoadByBuildIndex(int buildIndex)
        {
            if (buildIndex < 0 || buildIndex >= SceneManager.sceneCountInBuildSettings)
            {
                return new ErrorResponse(
                    $"Invalid build index: {buildIndex}. Must be between 0 and {SceneManager.sceneCountInBuildSettings - 1}.");
            }

            if (EditorSceneManager.GetActiveScene().isDirty)
            {
                return new ErrorResponse(
                    "Current scene has unsaved changes. Please save or discard changes before loading a new scene.");
            }

            try
            {
                string scenePath = SceneUtility.GetScenePathByBuildIndex(buildIndex);
                SceneHelpers.ForceOpenScene(scenePath);
                return new SuccessResponse(
                    $"Scene at build index {buildIndex} ('{scenePath}') loaded successfully.",
                    new
                    {
                        path = scenePath,
                        name = Path.GetFileNameWithoutExtension(scenePath),
                        buildIndex = buildIndex,
                    }
                );
            }
            catch (Exception e)
            {
                return new ErrorResponse($"Error loading scene with build index {buildIndex}: {e.Message}");
            }
        }
    }
}
