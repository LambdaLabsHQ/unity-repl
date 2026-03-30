using NativeMcp.Editor.Helpers;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;

namespace NativeMcp.Editor.Tools.Prefabs
{
    [McpForUnityTool("prefab_close_stage", Description = "Close the currently open prefab stage.")]
    public static class PrefabCloseStage
    {
        public class Parameters
        {
            [ToolParameter("Whether to save the prefab before closing (default false)", Required = false, DefaultValue = "false")]
            public bool saveBeforeClose { get; set; }
        }

        public static object HandleCommand(JObject @params)
        {
            PrefabStage stage = PrefabStageUtility.GetCurrentPrefabStage();
            if (stage == null)
            {
                return new SuccessResponse("No prefab stage was open.");
            }

            bool saveBeforeClose = @params?["saveBeforeClose"]?.ToObject<bool>() ?? false;
            if (saveBeforeClose && stage.scene.isDirty)
            {
                PrefabHelpers.SaveStagePrefab(stage);
                AssetDatabase.SaveAssets();
            }

            StageUtility.GoToMainStage();
            return new SuccessResponse($"Closed prefab stage for '{stage.assetPath}'.");
        }
    }
}
