using System;
using System.Collections.Generic;
using NativeMcp.Editor.Helpers;
using Newtonsoft.Json.Linq;
using UnityEditor;

namespace NativeMcp.Editor.Tools.Scene
{
    /// <summary>
    /// Get all scenes configured in Build Settings.
    /// </summary>
    [McpForUnityTool("scene_get_build_settings", Description = "Get all scenes configured in Build Settings.")]
    public static class SceneGetBuildSettings
    {
        public static object HandleCommand(JObject @params)
        {
            try
            {
                var scenes = new List<object>();
                for (int i = 0; i < EditorBuildSettings.scenes.Length; i++)
                {
                    var scene = EditorBuildSettings.scenes[i];
                    scenes.Add(new
                    {
                        path = scene.path,
                        guid = scene.guid.ToString(),
                        enabled = scene.enabled,
                        buildIndex = i,
                    });
                }
                return new SuccessResponse("Retrieved scenes from Build Settings.", scenes);
            }
            catch (Exception e)
            {
                return new ErrorResponse($"Error getting scenes from Build Settings: {e.Message}");
            }
        }
    }
}
