using System;
using NativeMcp.Editor.Helpers;
using NativeMcp.Editor.Tools;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEditorInternal;

namespace NativeMcp.Editor.Tools.EditorControl
{
    [McpForUnityTool("editor_add_tag", Internal = true, Description = "Add a new tag to the project.")]
    public static class EditorAddTag
    {
        public class Parameters
        {
            [ToolParameter("The tag name to add.", Required = true)]
            public string tagName { get; set; }
        }

        public static object HandleCommand(JObject @params)
        {
            string tagName = @params["tagName"]?.ToString();
            if (string.IsNullOrWhiteSpace(tagName))
                return new ErrorResponse("Tag name cannot be empty or whitespace.");

            if (System.Linq.Enumerable.Contains(InternalEditorUtility.tags, tagName))
            {
                return new ErrorResponse($"Tag '{tagName}' already exists.");
            }

            try
            {
                InternalEditorUtility.AddTag(tagName);
                AssetDatabase.SaveAssets();
                return new SuccessResponse($"Tag '{tagName}' added successfully.");
            }
            catch (Exception e)
            {
                return new ErrorResponse($"Failed to add tag '{tagName}': {e.Message}");
            }
        }
    }
}
