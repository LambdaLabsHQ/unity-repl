using System;
using System.Collections.Generic;
using NativeMcp.Editor.Helpers;
using Newtonsoft.Json.Linq;
using UnityEditor;

namespace NativeMcp.Editor.Tools.EditorControl
{
    [McpForUnityTool("execute_menu_item", Internal = true,
        Description = "Execute a Unity Editor menu item by path (e.g. 'GameObject/3D Object/Cube').")]
    public static class ExecuteMenuItem
    {
        private static readonly HashSet<string> Blacklist = new(StringComparer.OrdinalIgnoreCase)
        {
            "File/Quit",
            "File/Exit",
        };

        public class Parameters
        {
            [ToolParameter("Menu item path, e.g. 'GameObject/3D Object/Cube' or 'Window/General/Console'")]
            public string menu_path { get; set; }
        }

        public static object HandleCommand(JObject @params)
        {
            string menuPath = @params["menu_path"]?.ToString();
            if (string.IsNullOrWhiteSpace(menuPath))
                return new ErrorResponse("Required parameter 'menu_path' is missing.");

            if (Blacklist.Contains(menuPath))
                return new ErrorResponse($"Menu item '{menuPath}' is blocked for safety.");

            bool ok = EditorApplication.ExecuteMenuItem(menuPath);
            if (!ok)
                return new ErrorResponse(
                    $"Failed to execute '{menuPath}'. It may be invalid, disabled, or context-dependent.");

            return new SuccessResponse($"Executed menu item: '{menuPath}'.");
        }
    }
}
