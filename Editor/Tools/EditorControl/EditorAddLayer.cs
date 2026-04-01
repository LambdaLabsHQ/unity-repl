using System;
using NativeMcp.Editor.Helpers;
using NativeMcp.Editor.Tools;
using Newtonsoft.Json.Linq;
using UnityEditor;

namespace NativeMcp.Editor.Tools.EditorControl
{
    [McpForUnityTool("editor_add_layer", Internal = true, Description = "Add a new layer to the project.")]
    public static class EditorAddLayer
    {
        public class Parameters
        {
            [ToolParameter("The layer name to add.", Required = true)]
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

            // Check if layer name already exists
            for (int i = 0; i < TagLayerHelpers.TotalLayerCount; i++)
            {
                SerializedProperty layerSP = layersProp.GetArrayElementAtIndex(i);
                if (
                    layerSP != null
                    && layerName.Equals(layerSP.stringValue, StringComparison.OrdinalIgnoreCase)
                )
                {
                    return new ErrorResponse($"Layer '{layerName}' already exists at index {i}.");
                }
            }

            // Find the first empty user layer slot (indices 8 to 31)
            int firstEmptyUserLayer = -1;
            for (int i = TagLayerHelpers.FirstUserLayerIndex; i < TagLayerHelpers.TotalLayerCount; i++)
            {
                SerializedProperty layerSP = layersProp.GetArrayElementAtIndex(i);
                if (layerSP != null && string.IsNullOrEmpty(layerSP.stringValue))
                {
                    firstEmptyUserLayer = i;
                    break;
                }
            }

            if (firstEmptyUserLayer == -1)
            {
                return new ErrorResponse("No empty User Layer slots available (8-31 are full).");
            }

            try
            {
                SerializedProperty targetLayerSP = layersProp.GetArrayElementAtIndex(
                    firstEmptyUserLayer
                );
                targetLayerSP.stringValue = layerName;
                tagManager.ApplyModifiedProperties();
                AssetDatabase.SaveAssets();
                return new SuccessResponse(
                    $"Layer '{layerName}' added successfully to slot {firstEmptyUserLayer}."
                );
            }
            catch (Exception e)
            {
                return new ErrorResponse($"Failed to add layer '{layerName}': {e.Message}");
            }
        }
    }
}
