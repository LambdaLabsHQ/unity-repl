using System;
using NativeMcp.Editor.Helpers;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine;

namespace NativeMcp.Editor.Tools.Prefabs
{
    [McpForUnityTool("prefab_create_from_gameobject", Description = "Create a new prefab asset from an existing scene GameObject.")]
    public static class PrefabCreateFromGameObject
    {
        public class Parameters
        {
            [ToolParameter("Name of the source GameObject in the scene", Required = true)]
            public string target { get; set; }

            [ToolParameter("Asset path for the new prefab (e.g. Assets/Prefabs/MyPrefab.prefab)", Required = true)]
            public string prefabPath { get; set; }

            [ToolParameter("Whether to search inactive GameObjects (default false)", Required = false, DefaultValue = "false")]
            public bool searchInactive { get; set; }

            [ToolParameter("Whether to overwrite an existing prefab at the same path (default false)", Required = false, DefaultValue = "false")]
            public bool allowOverwrite { get; set; }
        }

        public static object HandleCommand(JObject @params)
        {
            string targetName = @params["target"]?.ToString() ?? @params["name"]?.ToString();
            if (string.IsNullOrEmpty(targetName))
            {
                return new ErrorResponse("'target' parameter is required for prefab_create_from_gameobject.");
            }

            bool includeInactive = @params["searchInactive"]?.ToObject<bool>() ?? false;
            GameObject sourceObject = PrefabHelpers.FindSceneObjectByName(targetName, includeInactive);
            if (sourceObject == null)
            {
                return new ErrorResponse($"GameObject '{targetName}' not found in the active scene.");
            }

            if (PrefabUtility.IsPartOfPrefabAsset(sourceObject))
            {
                return new ErrorResponse(
                    $"GameObject '{sourceObject.name}' is part of a prefab asset. Open the prefab stage to save changes instead."
                );
            }

            PrefabInstanceStatus status = PrefabUtility.GetPrefabInstanceStatus(sourceObject);
            if (status != PrefabInstanceStatus.NotAPrefab)
            {
                return new ErrorResponse(
                    $"GameObject '{sourceObject.name}' is already linked to an existing prefab instance."
                );
            }

            string requestedPath = @params["prefabPath"]?.ToString();
            if (string.IsNullOrWhiteSpace(requestedPath))
            {
                return new ErrorResponse("'prefabPath' parameter is required for prefab_create_from_gameobject.");
            }

            string sanitizedPath = AssetPathUtility.SanitizeAssetPath(requestedPath);
            if (!sanitizedPath.EndsWith(".prefab", StringComparison.OrdinalIgnoreCase))
            {
                sanitizedPath += ".prefab";
            }

            bool allowOverwrite = @params["allowOverwrite"]?.ToObject<bool>() ?? false;
            string finalPath = sanitizedPath;

            if (!allowOverwrite && AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(finalPath) != null)
            {
                finalPath = AssetDatabase.GenerateUniqueAssetPath(finalPath);
            }

            PrefabHelpers.EnsureAssetDirectoryExists(finalPath);

            try
            {
                GameObject connectedInstance = PrefabUtility.SaveAsPrefabAssetAndConnect(
                    sourceObject,
                    finalPath,
                    InteractionMode.AutomatedAction
                );

                if (connectedInstance == null)
                {
                    return new ErrorResponse($"Failed to save prefab asset at '{finalPath}'.");
                }

                Selection.activeGameObject = connectedInstance;

                return new SuccessResponse(
                    $"Prefab created at '{finalPath}' and instance linked.",
                    new
                    {
                        prefabPath = finalPath,
                        instanceId = connectedInstance.GetInstanceID()
                    }
                );
            }
            catch (Exception e)
            {
                return new ErrorResponse($"Error saving prefab asset at '{finalPath}': {e.Message}");
            }
        }
    }
}
