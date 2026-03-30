using System;
using NativeMcp.Editor.Helpers;
using Newtonsoft.Json.Linq;
using UnityEditor.SceneManagement;

namespace NativeMcp.Editor.Tools.Scene
{
    /// <summary>
    /// Get information about the currently active scene.
    /// </summary>
    [McpForUnityTool("scene_get_active", Description = "Get information about the currently active scene.")]
    public static class SceneGetActive
    {
        public static object HandleCommand(JObject @params)
        {
            try
            {
                var activeScene = EditorSceneManager.GetActiveScene();
                if (!activeScene.IsValid())
                    return new ErrorResponse("No active scene found.");

                var sceneInfo = new
                {
                    name = activeScene.name,
                    path = activeScene.path,
                    buildIndex = activeScene.buildIndex,
                    isDirty = activeScene.isDirty,
                    isLoaded = activeScene.isLoaded,
                    rootCount = activeScene.rootCount,
                };

                return new SuccessResponse("Retrieved active scene information.", sceneInfo);
            }
            catch (Exception e)
            {
                return new ErrorResponse($"Error getting active scene info: {e.Message}");
            }
        }
    }
}
