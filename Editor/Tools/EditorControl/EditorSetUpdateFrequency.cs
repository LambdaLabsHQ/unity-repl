using System;
using NativeMcp.Editor.Helpers;
using NativeMcp.Editor.Tools;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace NativeMcp.Editor.Tools.EditorControl
{
    [McpForUnityTool("editor_set_update_frequency", Internal = true,
        Description = "Get or set game update frequency. Controls Time.timeScale and Time.captureFramerate. Call with no arguments to read current values.")]
    public static class EditorSetUpdateFrequency
    {
        public class Parameters
        {
            [ToolParameter("Time scale multiplier (0=frozen, 1=normal). Range [0, 100].", Required = false)]
            public float? time_scale { get; set; }

            [ToolParameter("Capture framerate for deterministic mode. When >0, deltaTime = timeScale/captureFramerate per frame regardless of real time. 0=off.", Required = false)]
            public int? capture_framerate { get; set; }
        }

        public static object HandleCommand(JObject @params)
        {
            try
            {
                float? timeScale = (float?)@params["time_scale"];
                int? captureFramerate = (int?)@params["capture_framerate"];

                if (timeScale.HasValue)
                {
                    if (timeScale.Value < 0f || timeScale.Value > 100f)
                        return new ErrorResponse("time_scale must be in range [0, 100].");
                    Time.timeScale = timeScale.Value;
                }

                if (captureFramerate.HasValue)
                {
                    if (captureFramerate.Value < 0)
                        return new ErrorResponse("capture_framerate must be >= 0.");
                    Time.captureFramerate = captureFramerate.Value;
                }

                return new SuccessResponse("Update frequency configured.", new
                {
                    time_scale = Time.timeScale,
                    capture_framerate = Time.captureFramerate,
                    frame_count = Time.frameCount
                });
            }
            catch (Exception e)
            {
                return new ErrorResponse($"Error setting update frequency: {e.Message}");
            }
        }
    }
}
