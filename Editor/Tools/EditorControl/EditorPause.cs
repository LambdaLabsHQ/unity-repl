using System;
using NativeMcp.Editor.Helpers;
using NativeMcp.Editor.Tools;
using Newtonsoft.Json.Linq;
using UnityEditor;

namespace NativeMcp.Editor.Tools.EditorControl
{
    [McpForUnityTool("editor_pause", Description = "Toggle pause/resume in play mode.")]
    public static class EditorPause
    {
        public static object HandleCommand(JObject @params)
        {
            try
            {
                if (EditorApplication.isPlaying)
                {
                    EditorApplication.isPaused = !EditorApplication.isPaused;
                    return new SuccessResponse(
                        EditorApplication.isPaused ? "Game paused." : "Game resumed."
                    );
                }
                return new ErrorResponse("Cannot pause/resume: Not in play mode.");
            }
            catch (Exception e)
            {
                return new ErrorResponse($"Error pausing/resuming game: {e.Message}");
            }
        }
    }
}
