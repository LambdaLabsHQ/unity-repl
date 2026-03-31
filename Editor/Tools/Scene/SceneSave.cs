using System;
using System.IO;
using NativeMcp.Editor.Helpers;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace NativeMcp.Editor.Tools.Scene
{
    /// <summary>
    /// Save the current scene, optionally to a new path.
    /// </summary>
    [McpForUnityTool("scene_save", Internal = true, Description = "Save the current scene, optionally to a new path.")]
    public static class SceneSave
    {
        public class Parameters
        {
            [ToolParameter("Scene name without .unity extension (for Save As)", Required = false)]
            public string name { get; set; }

            [ToolParameter("Directory path relative to Assets/ (for Save As)", Required = false)]
            public string path { get; set; }
        }

        public static object HandleCommand(JObject @params)
        {
            var p = @params ?? new JObject();
            string name = SceneHelpers.ParseString(p, "name");
            string path = SceneHelpers.ParseString(p, "path");

            SceneHelpers.SanitizeScenePath(name, path,
                out _, out string fullPathDir,
                out string fullPath, out string relativePath);

            try
            {
                var currentScene = EditorSceneManager.GetActiveScene();
                if (!currentScene.IsValid())
                    return new ErrorResponse("No valid scene is currently active to save.");

                bool saved;
                string finalPath = currentScene.path;

                if (!string.IsNullOrEmpty(relativePath) && currentScene.path != relativePath)
                {
                    // Save As...
                    string dir = Path.GetDirectoryName(fullPath);
                    if (!Directory.Exists(dir))
                        Directory.CreateDirectory(dir);

                    saved = EditorSceneManager.SaveScene(currentScene, relativePath);
                    finalPath = relativePath;
                }
                else
                {
                    if (string.IsNullOrEmpty(currentScene.path))
                        return new ErrorResponse(
                            "Cannot save an untitled scene without providing a 'name' and 'path'. Use Save As functionality.");

                    saved = EditorSceneManager.SaveScene(currentScene);
                }

                if (saved)
                {
                    AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
                    return new SuccessResponse(
                        $"Scene '{currentScene.name}' saved successfully to '{finalPath}'.",
                        new { path = finalPath, name = currentScene.name }
                    );
                }
                else
                {
                    return new ErrorResponse($"Failed to save scene '{currentScene.name}'.");
                }
            }
            catch (Exception e)
            {
                return new ErrorResponse($"Error saving scene: {e.Message}");
            }
        }
    }
}
