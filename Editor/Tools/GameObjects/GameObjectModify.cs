#nullable disable
using System;
using System.Collections.Generic;
using System.Linq;
using NativeMcp.Editor.Helpers;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEditorInternal;
using UnityEngine;

namespace NativeMcp.Editor.Tools.GameObjects
{
    [McpForUnityTool("gameobject_modify", Internal = true, Description = "Modify properties of an existing GameObject (name, tag, layer, transform, parent, active state).")]
    public static class GameObjectModify
    {
        public class Parameters
        {
            [ToolParameter("Target GameObject to modify (name, path, or instanceID)", Required = true)]
            public object target { get; set; }

            [ToolParameter("Search method: by_name, by_path, by_id, by_tag, by_layer, by_component, by_id_or_name_or_path", Required = false)]
            public string searchMethod { get; set; }

            [ToolParameter("New name for the GameObject", Required = false)]
            public string name { get; set; }

            [ToolParameter("Tag to assign", Required = false)]
            public string tag { get; set; }

            [ToolParameter("Layer name to assign", Required = false)]
            public string layer { get; set; }

            [ToolParameter("New parent GameObject (name, path, or instanceID; null to unparent)", Required = false)]
            public object parent { get; set; }

            [ToolParameter("Local position as {x,y,z}", Required = false)]
            public object position { get; set; }

            [ToolParameter("Local rotation (Euler angles) as {x,y,z}", Required = false)]
            public object rotation { get; set; }

            [ToolParameter("Local scale as {x,y,z}", Required = false)]
            public object scale { get; set; }

            [ToolParameter("Set the GameObject active or inactive", Required = false)]
            public bool? setActive { get; set; }
        }

        public static object HandleCommand(JObject @params)
        {
            JToken targetToken = @params?["target"];
            string searchMethod = @params?["searchMethod"]?.ToString()?.ToLower();
            return Handle(@params, targetToken, searchMethod);
        }

        public static object Handle(JObject @params, JToken targetToken, string searchMethod)
        {
            // When setActive=true is specified, we need to search for inactive objects
            // otherwise we can't find an inactive object to activate it
            JObject findParams = null;
            if (@params["setActive"]?.ToObject<bool?>() == true)
            {
                findParams = new JObject { ["searchInactive"] = true };
            }
            
            GameObject targetGo = ManageGameObjectCommon.FindObjectInternal(targetToken, searchMethod, findParams);
            if (targetGo == null)
            {
                return new ErrorResponse($"Target GameObject ('{targetToken}') not found using method '{searchMethod ?? "default"}'.");
            }

            Undo.RecordObject(targetGo.transform, "Modify GameObject Transform");
            Undo.RecordObject(targetGo, "Modify GameObject Properties");

            bool modified = false;

            string name = @params["name"]?.ToString();
            if (!string.IsNullOrEmpty(name) && targetGo.name != name)
            {
                targetGo.name = name;
                modified = true;
            }

            JToken parentToken = @params["parent"];
            if (parentToken != null)
            {
                GameObject newParentGo = ManageGameObjectCommon.FindObjectInternal(parentToken, "by_id_or_name_or_path");
                if (
                    newParentGo == null
                    && !(parentToken.Type == JTokenType.Null
                         || (parentToken.Type == JTokenType.String && string.IsNullOrEmpty(parentToken.ToString())))
                )
                {
                    return new ErrorResponse($"New parent ('{parentToken}') not found.");
                }
                if (newParentGo != null && newParentGo.transform.IsChildOf(targetGo.transform))
                {
                    return new ErrorResponse($"Cannot parent '{targetGo.name}' to '{newParentGo.name}', as it would create a hierarchy loop.");
                }
                if (targetGo.transform.parent != (newParentGo?.transform))
                {
                    targetGo.transform.SetParent(newParentGo?.transform, true);
                    modified = true;
                }
            }

            bool? setActive = @params["setActive"]?.ToObject<bool?>();
            if (setActive.HasValue && targetGo.activeSelf != setActive.Value)
            {
                targetGo.SetActive(setActive.Value);
                modified = true;
            }

            string tag = @params["tag"]?.ToString();
            if (tag != null && targetGo.tag != tag)
            {
                string tagToSet = string.IsNullOrEmpty(tag) ? "Untagged" : tag;

                if (tagToSet != "Untagged" && !System.Linq.Enumerable.Contains(InternalEditorUtility.tags, tagToSet))
                {
                    McpLog.Info($"[ManageGameObject] Tag '{tagToSet}' not found. Creating it.");
                    try
                    {
                        InternalEditorUtility.AddTag(tagToSet);
                    }
                    catch (Exception ex)
                    {
                        return new ErrorResponse($"Failed to create tag '{tagToSet}': {ex.Message}.");
                    }
                }

                try
                {
                    targetGo.tag = tagToSet;
                    modified = true;
                }
                catch (Exception ex)
                {
                    return new ErrorResponse($"Failed to set tag to '{tagToSet}': {ex.Message}.");
                }
            }

            string layerName = @params["layer"]?.ToString();
            if (!string.IsNullOrEmpty(layerName))
            {
                int layerId = LayerMask.NameToLayer(layerName);
                if (layerId == -1)
                {
                    return new ErrorResponse($"Invalid layer specified: '{layerName}'. Use a valid layer name.");
                }
                if (layerId != -1 && targetGo.layer != layerId)
                {
                    targetGo.layer = layerId;
                    modified = true;
                }
            }

            Vector3? position = VectorParsing.ParseVector3(@params["position"]);
            Vector3? rotation = VectorParsing.ParseVector3(@params["rotation"]);
            Vector3? scale = VectorParsing.ParseVector3(@params["scale"]);

            if (position.HasValue && targetGo.transform.localPosition != position.Value)
            {
                targetGo.transform.localPosition = position.Value;
                modified = true;
            }
            if (rotation.HasValue && targetGo.transform.localEulerAngles != rotation.Value)
            {
                targetGo.transform.localEulerAngles = rotation.Value;
                modified = true;
            }
            if (scale.HasValue && targetGo.transform.localScale != scale.Value)
            {
                targetGo.transform.localScale = scale.Value;
                modified = true;
            }

            if (@params["componentsToRemove"] is JArray componentsToRemoveArray)
            {
                foreach (var compToken in componentsToRemoveArray)
                {
                    string typeName = compToken.ToString();
                    if (!string.IsNullOrEmpty(typeName))
                    {
                        var removeResult = GameObjectComponentHelpers.RemoveComponentInternal(targetGo, typeName);
                        if (removeResult != null)
                            return removeResult;
                        modified = true;
                    }
                }
            }

            if (@params["componentsToAdd"] is JArray componentsToAddArrayModify)
            {
                foreach (var compToken in componentsToAddArrayModify)
                {
                    string typeName = null;
                    JObject properties = null;
                    if (compToken.Type == JTokenType.String)
                        typeName = compToken.ToString();
                    else if (compToken is JObject compObj)
                    {
                        typeName = compObj["typeName"]?.ToString();
                        properties = compObj["properties"] as JObject;
                    }

                    if (!string.IsNullOrEmpty(typeName))
                    {
                        var addResult = GameObjectComponentHelpers.AddComponentInternal(targetGo, typeName, properties);
                        if (addResult != null)
                            return addResult;
                        modified = true;
                    }
                }
            }

            var componentErrors = new List<object>();
            if (@params["componentProperties"] is JObject componentPropertiesObj)
            {
                foreach (var prop in componentPropertiesObj.Properties())
                {
                    string compName = prop.Name;
                    JObject propertiesToSet = prop.Value as JObject;
                    if (propertiesToSet != null)
                    {
                        var setResult = GameObjectComponentHelpers.SetComponentPropertiesInternal(targetGo, compName, propertiesToSet);
                        if (setResult != null)
                        {
                            componentErrors.Add(setResult);
                        }
                        else
                        {
                            modified = true;
                        }
                    }
                }
            }

            if (componentErrors.Count > 0)
            {
                var aggregatedErrors = new List<string>();
                foreach (var errorObj in componentErrors)
                {
                    try
                    {
                        var dataProp = errorObj?.GetType().GetProperty("data");
                        var dataVal = dataProp?.GetValue(errorObj);
                        if (dataVal != null)
                        {
                            var errorsProp = dataVal.GetType().GetProperty("errors");
                            var errorsEnum = errorsProp?.GetValue(dataVal) as System.Collections.IEnumerable;
                            if (errorsEnum != null)
                            {
                                foreach (var item in errorsEnum)
                                {
                                    var s = item?.ToString();
                                    if (!string.IsNullOrEmpty(s)) aggregatedErrors.Add(s);
                                }
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        McpLog.Warn($"[GameObjectModify] Error aggregating component errors: {ex.Message}");
                    }
                }

                return new ErrorResponse(
                    $"One or more component property operations failed on '{targetGo.name}'.",
                    new { componentErrors = componentErrors, errors = aggregatedErrors }
                );
            }

            if (!modified)
            {
                return new SuccessResponse(
                    $"No modifications applied to GameObject '{targetGo.name}'.",
                    Helpers.GameObjectSerializer.GetGameObjectData(targetGo)
                );
            }

            EditorUtility.SetDirty(targetGo);
            return new SuccessResponse(
                $"GameObject '{targetGo.name}' modified successfully.",
                Helpers.GameObjectSerializer.GetGameObjectData(targetGo)
            );
        }
    }
}
