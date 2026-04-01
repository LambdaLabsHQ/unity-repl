using System;
using NativeMcp.Editor.Helpers;
using NativeMcp.Editor.Tools;
using Newtonsoft.Json.Linq;
using UnityEditor;

namespace NativeMcp.Editor.Tools.EditorControl
{
    [McpForUnityTool("editor_stop", Internal = true, Description = "Exit Unity play mode.")]
    public static class EditorStop
    {
        public static object HandleCommand(JObject @params)
        {
            try
            {
                if (EditorApplication.isPlaying)
                {
                    EditorApplication.isPlaying = false;
                    return new SuccessResponse("Exited play mode.");
                }
                return new SuccessResponse("Already stopped (not in play mode).");
            }
            catch (Exception e)
            {
                return new ErrorResponse($"Error stopping play mode: {e.Message}");
            }
        }
    }
}
