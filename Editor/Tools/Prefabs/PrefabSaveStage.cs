using NativeMcp.Editor.Helpers;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;

namespace NativeMcp.Editor.Tools.Prefabs
{
    [McpForUnityTool("prefab_save_stage", Internal = true, Description = "Save the currently open prefab stage.")]
    public static class PrefabSaveStage
    {
        public static object HandleCommand(JObject @params)
        {
            PrefabStage stage = PrefabStageUtility.GetCurrentPrefabStage();
            if (stage == null)
            {
                return new ErrorResponse("No prefab stage is currently open.");
            }

            PrefabHelpers.SaveStagePrefab(stage);
            AssetDatabase.SaveAssets();
            return new SuccessResponse($"Saved prefab stage for '{stage.assetPath}'.", PrefabHelpers.SerializeStage(stage));
        }
    }
}
