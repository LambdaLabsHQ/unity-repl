#nullable disable
using System.Collections.Generic;
using NativeMcp.Editor.Helpers;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine;

namespace NativeMcp.Editor.Tools.GameObjects
{
    [McpForUnityTool("gameobject_delete", Description = "Delete one or more GameObjects from the scene.")]
    public static class GameObjectDelete
    {
        public class Parameters
        {
            [ToolParameter("Target GameObject(s) to delete (name, path, instanceID, or array)", Required = true)]
            public object target { get; set; }

            [ToolParameter("Search method: by_name, by_path, by_id, by_tag, by_layer, by_component, by_id_or_name_or_path", Required = false)]
            public string searchMethod { get; set; }
        }

        public static object HandleCommand(JObject @params)
        {
            JToken targetToken = @params?["target"];
            string searchMethod = @params?["searchMethod"]?.ToString()?.ToLower();
            return Handle(targetToken, searchMethod);
        }

        public static object Handle(JToken targetToken, string searchMethod)
        {
            List<GameObject> targets = ManageGameObjectCommon.FindObjectsInternal(targetToken, searchMethod, true);

            if (targets.Count == 0)
            {
                return new ErrorResponse($"Target GameObject(s) ('{targetToken}') not found using method '{searchMethod ?? "default"}'.");
            }

            List<object> deletedObjects = new List<object>();
            foreach (var targetGo in targets)
            {
                if (targetGo != null)
                {
                    string goName = targetGo.name;
                    int goId = targetGo.GetInstanceID();
                    Undo.DestroyObjectImmediate(targetGo);
                    deletedObjects.Add(new { name = goName, instanceID = goId });
                }
            }

            if (deletedObjects.Count > 0)
            {
                string message =
                    targets.Count == 1
                        ? $"GameObject '{((dynamic)deletedObjects[0]).name}' deleted successfully."
                        : $"{deletedObjects.Count} GameObjects deleted successfully.";
                return new SuccessResponse(message, deletedObjects);
            }

            return new ErrorResponse("Failed to delete target GameObject(s).");
        }
    }
}
