using System;
using NativeMcp.Editor.Helpers;
using NativeMcp.Editor.Tools;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEditorInternal;

namespace NativeMcp.Editor.Tools.EditorControl
{
    [McpForUnityTool("editor_remove_tag", Internal = true, Description = "Remove an existing tag from the project.")]
    public static class EditorRemoveTag
    {
        public class Parameters
        {
            [ToolParameter("The tag name to remove.", Required = true)]
            public string tagName { get; set; }
        }

        public static object HandleCommand(JObject @params)
        {
            string tagName = @params["tagName"]?.ToString();
            if (string.IsNullOrWhiteSpace(tagName))
                return new ErrorResponse("Tag name cannot be empty or whitespace.");

            if (tagName.Equals("Untagged", StringComparison.OrdinalIgnoreCase))
                return new ErrorResponse("Cannot remove the built-in 'Untagged' tag.");

            if (!System.Linq.Enumerable.Contains(InternalEditorUtility.tags, tagName))
            {
                return new ErrorResponse($"Tag '{tagName}' does not exist.");
            }

            try
            {
                InternalEditorUtility.RemoveTag(tagName);
                AssetDatabase.SaveAssets();
                return new SuccessResponse($"Tag '{tagName}' removed successfully.");
            }
            catch (Exception e)
            {
                return new ErrorResponse($"Failed to remove tag '{tagName}': {e.Message}");
            }
        }
    }
}
