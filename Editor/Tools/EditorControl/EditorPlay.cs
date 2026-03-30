using System;
using NativeMcp.Editor.Helpers;
using NativeMcp.Editor.Tools;
using Newtonsoft.Json.Linq;
using UnityEditor;

namespace NativeMcp.Editor.Tools.EditorControl
{
    [McpForUnityTool("editor_play", Description = "Enter Unity play mode.")]
    public static class EditorPlay
    {
        public static object HandleCommand(JObject @params)
        {
            try
            {
                if (!EditorApplication.isPlaying)
                {
                    EditorApplication.isPlaying = true;
                    return new SuccessResponse("Entered play mode.");
                }
                return new SuccessResponse("Already in play mode.");
            }
            catch (Exception e)
            {
                return new ErrorResponse($"Error entering play mode: {e.Message}");
            }
        }
    }
}
