using System;
using System.IO;
using NativeMcp.Editor.Helpers;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace NativeMcp.Editor.Tools.Scene
{
    /// <summary>
    /// Create a new empty Unity scene at the specified path.
    /// </summary>
    [McpForUnityTool("scene_create", Description = "Create a new empty Unity scene at the specified path.")]
    public static class SceneCreate
    {
        public class Parameters
        {
            [ToolParameter("Scene name without .unity extension", Required = true)]
            public string name { get; set; }

            [ToolParameter("Directory path relative to Assets/ (default: Scenes)", Required = false)]
            public string path { get; set; }
        }

        public static object HandleCommand(JObject @params)
        {
            var p = @params ?? new JObject();
            string name = SceneHelpers.ParseString(p, "name");
            string path = SceneHelpers.ParseString(p, "path");

            if (string.IsNullOrEmpty(name))
                return new ErrorResponse("'name' parameter is required for scene_create.");

            SceneHelpers.SanitizeScenePath(name, path,
                out string relativeDir, out string fullPathDir,
                out string fullPath, out string relativePath,
                applyDefaultDir: true);

            if (string.IsNullOrEmpty(relativePath))
                return new ErrorResponse("'name' and 'path' parameters are required for scene_create.");

            // Ensure directory exists
            try
            {
                Directory.CreateDirectory(fullPathDir);
            }
            catch (Exception e)
            {
                return new ErrorResponse($"Could not create directory '{fullPathDir}': {e.Message}");
            }

            if (File.Exists(fullPath))
                return new ErrorResponse($"Scene already exists at '{relativePath}'.");

            try
            {
                UnityEngine.SceneManagement.Scene newScene = EditorSceneManager.NewScene(
                    NewSceneSetup.EmptyScene,
                    NewSceneMode.Single
                );
                bool saved = EditorSceneManager.SaveScene(newScene, relativePath);

                if (saved)
                {
                    AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
                    return new SuccessResponse(
                        $"Scene '{Path.GetFileName(relativePath)}' created successfully at '{relativePath}'.",
                        new { path = relativePath }
                    );
                }
                else
                {
                    return new ErrorResponse($"Failed to save new scene to '{relativePath}'.");
                }
            }
            catch (Exception e)
            {
                return new ErrorResponse($"Error creating scene '{relativePath}': {e.Message}");
            }
        }
    }
}
