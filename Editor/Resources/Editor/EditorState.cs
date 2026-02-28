using System;
using NativeMcp.Editor.Helpers;
using NativeMcp.Editor.Services;
using Newtonsoft.Json.Linq;

namespace NativeMcp.Editor.Resources.Editor
{
    /// <summary>
    /// Provides dynamic editor state information that changes frequently.
    /// </summary>
    [McpForUnityResource("get_editor_state")]
    public static class EditorState
    {
        public static object HandleCommand(JObject @params)
        {
            try
            {
                var snapshot = EditorStateCache.GetSnapshot();
                return new SuccessResponse("Retrieved editor state.", snapshot);
            }
            catch (Exception e)
            {
                return new ErrorResponse($"Error getting editor state: {e.Message}");
            }
        }
    }
}
