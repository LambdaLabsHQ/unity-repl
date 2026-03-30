using System;
using NativeMcp.Editor.Helpers;
using NativeMcp.Editor.Tools;
using Newtonsoft.Json.Linq;
using UnityEditor;

namespace NativeMcp.Editor.Tools.EditorControl
{
    [McpForUnityTool("editor_set_active_tool", Description = "Set the active editor tool (View, Move, Rotate, Scale, Rect, Transform).")]
    public static class EditorSetActiveTool
    {
        public class Parameters
        {
            [ToolParameter("The tool to activate (View, Move, Rotate, Scale, Rect, Transform).", Required = true)]
            public string toolName { get; set; }
        }

        public static object HandleCommand(JObject @params)
        {
            string toolName = @params["toolName"]?.ToString();
            if (string.IsNullOrEmpty(toolName))
                return new ErrorResponse("'toolName' parameter is required.");

            try
            {
                Tool targetTool;
                if (Enum.TryParse<Tool>(toolName, true, out targetTool))
                {
                    if (targetTool != Tool.None && targetTool <= Tool.Custom)
                    {
                        UnityEditor.Tools.current = targetTool;
                        return new SuccessResponse($"Set active tool to '{targetTool}'.");
                    }
                    else
                    {
                        return new ErrorResponse(
                            $"Cannot directly set tool to '{toolName}'. It might be None, Custom, or invalid."
                        );
                    }
                }
                else
                {
                    return new ErrorResponse(
                        $"Could not parse '{toolName}' as a standard Unity Tool (View, Move, Rotate, Scale, Rect, Transform, Custom)."
                    );
                }
            }
            catch (Exception e)
            {
                return new ErrorResponse($"Error setting active tool: {e.Message}");
            }
        }
    }
}
