using System;
using NativeMcp.Editor.Helpers;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace NativeMcp.Editor.Tools.Prefabs
{
    [McpForUnityTool("prefab_open_stage", Description = "Open a prefab in isolation mode for editing.")]
    public static class PrefabOpenStage
    {
        public class Parameters
        {
            [ToolParameter("Path to the prefab asset (e.g. Assets/Prefabs/MyPrefab.prefab)", Required = true)]
            public string prefabPath { get; set; }

            [ToolParameter("Editing mode (only 'InIsolation' supported)", Required = false)]
            public string mode { get; set; }
        }

        public static object HandleCommand(JObject @params)
        {
            string prefabPath = @params["prefabPath"]?.ToString();
            if (string.IsNullOrEmpty(prefabPath))
            {
                return new ErrorResponse("'prefabPath' parameter is required for prefab_open_stage.");
            }

            string sanitizedPath = AssetPathUtility.SanitizeAssetPath(prefabPath);
            GameObject prefabAsset = AssetDatabase.LoadAssetAtPath<GameObject>(sanitizedPath);
            if (prefabAsset == null)
            {
                return new ErrorResponse($"No prefab asset found at path '{sanitizedPath}'.");
            }

            string modeValue = @params["mode"]?.ToString();
            if (!string.IsNullOrEmpty(modeValue) && !modeValue.Equals(PrefabStage.Mode.InIsolation.ToString(), StringComparison.OrdinalIgnoreCase))
            {
                return new ErrorResponse("Only PrefabStage mode 'InIsolation' is supported at this time.");
            }

            PrefabStage stage = PrefabStageUtility.OpenPrefab(sanitizedPath);
            if (stage == null)
            {
                return new ErrorResponse($"Failed to open prefab stage for '{sanitizedPath}'.");
            }

            return new SuccessResponse($"Opened prefab stage for '{sanitizedPath}'.", PrefabHelpers.SerializeStage(stage));
        }
    }
}
