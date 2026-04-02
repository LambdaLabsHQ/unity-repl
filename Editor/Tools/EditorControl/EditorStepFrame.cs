using System;
using NativeMcp.Editor.Helpers;
using NativeMcp.Editor.Tools;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine;

namespace NativeMcp.Editor.Tools.EditorControl
{
    [McpForUnityTool("editor_step_frame", Internal = true,
        Description = "Advance exactly one frame while paused in play mode.")]
    public static class EditorStepFrame
    {
        public static object HandleCommand(JObject @params)
        {
            try
            {
                if (!EditorApplication.isPlaying)
                {
                    if (EditorApplication.isPlayingOrWillChangePlaymode)
                        return new ErrorResponse("Play mode is transitioning. Wait and try again.");
                    return new ErrorResponse("Not in play mode. Call 'play' first.");
                }

                if (!EditorApplication.isPaused)
                    EditorApplication.isPaused = true;

                EditorApplication.Step();

                return new SuccessResponse("Stepped 1 frame.", new
                {
                    frame = Time.frameCount
                });
            }
            catch (Exception e)
            {
                return new ErrorResponse($"Error stepping frame: {e.Message}");
            }
        }
    }
}
