using System;
using NativeMcp.Editor.Helpers;
using UnityEditor;

namespace NativeMcp.Editor.Tools.EditorControl
{
    /// <summary>
    /// Shared helpers and constants for tag/layer management tools.
    /// </summary>
    internal static class TagLayerHelpers
    {
        /// <summary>First user-assignable layer index (0-7 are built-in).</summary>
        internal const int FirstUserLayerIndex = 8;

        /// <summary>Total number of layer slots in Unity.</summary>
        internal const int TotalLayerCount = 32;

        /// <summary>
        /// Gets the SerializedObject for the TagManager asset.
        /// </summary>
        internal static SerializedObject GetTagManager()
        {
            try
            {
                UnityEngine.Object[] tagManagerAssets = AssetDatabase.LoadAllAssetsAtPath(
                    "ProjectSettings/TagManager.asset"
                );
                if (tagManagerAssets == null || tagManagerAssets.Length == 0)
                {
                    McpLog.Error("[TagLayerHelpers] TagManager.asset not found in ProjectSettings.");
                    return null;
                }
                return new SerializedObject(tagManagerAssets[0]);
            }
            catch (Exception e)
            {
                McpLog.Error($"[TagLayerHelpers] Error accessing TagManager.asset: {e.Message}");
                return null;
            }
        }
    }
}
