using System;
using NativeMcp.Editor.Helpers;
using NativeMcp.Editor.Tools;
using Newtonsoft.Json.Linq;
using UnityEditor;

namespace NativeMcp.Editor.Tools.EditorControl
{
    [McpForUnityTool("editor_remove_layer", Description = "Remove a user layer from the project.")]
    public static class EditorRemoveLayer
    {
        public class Parameters
        {
            [ToolParameter("The layer name to remove.", Required = true)]
            public string layerName { get; set; }
        }

        public static object HandleCommand(JObject @params)
        {
            string layerName = @params["layerName"]?.ToString();
            if (string.IsNullOrWhiteSpace(layerName))
                return new ErrorResponse("Layer name cannot be empty or whitespace.");

            SerializedObject tagManager = TagLayerHelpers.GetTagManager();
            if (tagManager == null)
                return new ErrorResponse("Could not access TagManager asset.");

            SerializedProperty layersProp = tagManager.FindProperty("layers");
            if (layersProp == null || !layersProp.isArray)
                return new ErrorResponse("Could not find 'layers' property in TagManager.");

            // Find the layer by name (must be user layer)
            int layerIndexToRemove = -1;
            for (int i = TagLayerHelpers.FirstUserLayerIndex; i < TagLayerHelpers.TotalLayerCount; i++)
            {
                SerializedProperty layerSP = layersProp.GetArrayElementAtIndex(i);
                if (
                    layerSP != null
                    && layerName.Equals(layerSP.stringValue, StringComparison.OrdinalIgnoreCase)
                )
                {
                    layerIndexToRemove = i;
                    break;
                }
            }

            if (layerIndexToRemove == -1)
            {
                return new ErrorResponse($"User layer '{layerName}' not found.");
            }

            try
            {
                SerializedProperty targetLayerSP = layersProp.GetArrayElementAtIndex(
                    layerIndexToRemove
                );
                targetLayerSP.stringValue = string.Empty;
                tagManager.ApplyModifiedProperties();
                AssetDatabase.SaveAssets();
                return new SuccessResponse(
                    $"Layer '{layerName}' (slot {layerIndexToRemove}) removed successfully."
                );
            }
            catch (Exception e)
            {
                return new ErrorResponse($"Failed to remove layer '{layerName}': {e.Message}");
            }
        }
    }
}
