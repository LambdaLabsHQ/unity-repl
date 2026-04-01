#nullable disable
using NativeMcp.Editor.Helpers;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace NativeMcp.Editor.Tools.GameObjects
{
    [McpForUnityTool("gameobject_duplicate", Internal = true, Description = "Duplicate a GameObject in the scene.")]
    public static class GameObjectDuplicate
    {
        public class Parameters
        {
            [ToolParameter("Target GameObject to duplicate (name, path, or instanceID)", Required = true)]
            public object target { get; set; }

            [ToolParameter("Search method: by_name, by_path, by_id, by_tag, by_layer, by_component, by_id_or_name_or_path", Required = false)]
            public string searchMethod { get; set; }

            [ToolParameter("Name for the duplicated GameObject", Required = false)]
            public string new_name { get; set; }

            [ToolParameter("World position for the duplicate as {x,y,z}", Required = false)]
            public object position { get; set; }

            [ToolParameter("Offset from the original position as {x,y,z}", Required = false)]
            public object offset { get; set; }

            [ToolParameter("Parent GameObject for the duplicate (name, path, or instanceID; null for root)", Required = false)]
            public object parent { get; set; }
        }

        public static object HandleCommand(JObject @params)
        {
            JToken targetToken = @params?["target"];
            string searchMethod = @params?["searchMethod"]?.ToString()?.ToLower();
            return Handle(@params, targetToken, searchMethod);
        }

        public static object Handle(JObject @params, JToken targetToken, string searchMethod)
        {
            GameObject sourceGo = ManageGameObjectCommon.FindObjectInternal(targetToken, searchMethod);
            if (sourceGo == null)
            {
                return new ErrorResponse($"Target GameObject ('{targetToken}') not found using method '{searchMethod ?? "default"}'.");
            }

            string newName = @params["new_name"]?.ToString();
            Vector3? position = VectorParsing.ParseVector3(@params["position"]);
            Vector3? offset = VectorParsing.ParseVector3(@params["offset"]);
            JToken parentToken = @params["parent"];

            GameObject duplicatedGo = UnityEngine.Object.Instantiate(sourceGo);
            Undo.RegisterCreatedObjectUndo(duplicatedGo, $"Duplicate {sourceGo.name}");

            if (!string.IsNullOrEmpty(newName))
            {
                duplicatedGo.name = newName;
            }
            else
            {
                duplicatedGo.name = sourceGo.name.Replace("(Clone)", "").Trim() + "_Copy";
            }

            if (position.HasValue)
            {
                duplicatedGo.transform.position = position.Value;
            }
            else if (offset.HasValue)
            {
                duplicatedGo.transform.position = sourceGo.transform.position + offset.Value;
            }

            if (parentToken != null)
            {
                if (parentToken.Type == JTokenType.Null || (parentToken.Type == JTokenType.String && string.IsNullOrEmpty(parentToken.ToString())))
                {
                    duplicatedGo.transform.SetParent(null);
                }
                else
                {
                    GameObject newParent = ManageGameObjectCommon.FindObjectInternal(parentToken, "by_id_or_name_or_path");
                    if (newParent != null)
                    {
                        duplicatedGo.transform.SetParent(newParent.transform, true);
                    }
                    else
                    {
                        McpLog.Warn($"[ManageGameObject.Duplicate] Parent '{parentToken}' not found. Object will remain at root level.");
                    }
                }
            }
            else
            {
                duplicatedGo.transform.SetParent(sourceGo.transform.parent, true);
            }

            EditorUtility.SetDirty(duplicatedGo);
            EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());

            Selection.activeGameObject = duplicatedGo;

            return new SuccessResponse(
                $"Duplicated '{sourceGo.name}' as '{duplicatedGo.name}'.",
                new
                {
                    originalName = sourceGo.name,
                    originalId = sourceGo.GetInstanceID(),
                    duplicatedObject = Helpers.GameObjectSerializer.GetGameObjectData(duplicatedGo)
                }
            );
        }
    }
}
