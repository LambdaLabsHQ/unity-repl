using System;
using System.Collections.Generic;
using System.IO;
using NativeMcp.Editor.Helpers;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace NativeMcp.Editor.Tools.Scene
{
    /// <summary>
    /// Capture a screenshot from the current camera view.
    /// </summary>
    [McpForUnityTool("scene_screenshot", Internal = true, Description = "Capture a screenshot from the current camera view.")]
    public static class SceneScreenshot
    {
        public class Parameters
        {
            [ToolParameter("Output file name (default: screenshot-<timestamp>.png)", Required = false)]
            public string fileName { get; set; }

            [ToolParameter("Super-sampling multiplier for resolution (default 1)", Required = false)]
            public int? superSize { get; set; }
        }

        public static object HandleCommand(JObject @params)
        {
            var p = @params ?? new JObject();
            string fileName = SceneHelpers.ParseString(p, "fileName", "filename");
            int? superSize = SceneHelpers.ParseInt(p, "superSize", "super_size", "supersize");
            return CaptureScreenshot(fileName, superSize);
        }

        /// <summary>
        /// Core screenshot capture logic. Can be called directly for backward compatibility.
        /// </summary>
        internal static object CaptureScreenshot(string fileName, int? superSize)
        {
            try
            {
                int size = (superSize.HasValue && superSize.Value > 0) ? superSize.Value : 1;

                Texture2D tex;

                if (Application.isPlaying)
                {
                    tex = ScreenCapture.CaptureScreenshotAsTexture(size);
                    if (tex == null)
                        return new ErrorResponse("ScreenCapture.CaptureScreenshotAsTexture returned null.");
                }
                else
                {
                    Camera cam = Camera.main;

                    var urpCamDataType = System.Type.GetType(
                        "UnityEngine.Rendering.Universal.UniversalAdditionalCameraData, Unity.RenderPipelines.Universal.Runtime");
                    var renderTypeProp = urpCamDataType?.GetProperty("renderType");

                    if (cam == null || !cam.isActiveAndEnabled)
                    {
                        foreach (var c in UnityEngine.Object.FindObjectsByType<Camera>(FindObjectsSortMode.None))
                        {
                            if (!c.isActiveAndEnabled || c.targetTexture != null) continue;

                            if (urpCamDataType != null && renderTypeProp != null)
                            {
                                var uacd = c.GetComponent(urpCamDataType);
                                if (uacd != null && renderTypeProp.GetValue(uacd)?.ToString() == "Overlay") continue;
                            }

                            if (cam == null || c.depth > cam.depth) cam = c;
                        }
                    }

                    if (cam == null)
                        return new ErrorResponse("No valid screen-rendering Camera found to capture screenshot.");

                    int width = Mathf.Max(1, cam.pixelWidth > 0 ? cam.pixelWidth : Screen.width) * size;
                    int height = Mathf.Max(1, cam.pixelHeight > 0 ? cam.pixelHeight : Screen.height) * size;

                    var rt = RenderTexture.GetTemporary(width, height, 24, RenderTextureFormat.ARGB32);
                    tex = new Texture2D(width, height, TextureFormat.RGBA32, false);

                    var prevRT = cam.targetTexture;
                    var prevActive = RenderTexture.active;
                    try
                    {
                        cam.targetTexture = rt;
                        cam.Render();
                        RenderTexture.active = rt;
                        tex.ReadPixels(new Rect(0, 0, width, height), 0, 0);
                        tex.Apply();
                    }
                    finally
                    {
                        cam.targetTexture = prevRT;
                        RenderTexture.active = prevActive;
                        RenderTexture.ReleaseTemporary(rt);
                    }
                }

                int texWidth = tex.width;
                int texHeight = tex.height;
                byte[] pngBytes = tex.EncodeToPNG();
                UnityEngine.Object.DestroyImmediate(tex);

                string resolvedName = string.IsNullOrWhiteSpace(fileName)
                    ? $"screenshot-{DateTime.Now:yyyyMMdd-HHmmss}.png"
                    : (fileName.EndsWith(".png", StringComparison.OrdinalIgnoreCase) ? fileName : fileName + ".png");
                string folder = Path.Combine(Application.dataPath, "Screenshots");
                Directory.CreateDirectory(folder);
                string fullPath = Path.Combine(folder, resolvedName).Replace('\\', '/');

                if (File.Exists(fullPath))
                {
                    string dir = Path.GetDirectoryName(fullPath);
                    string baseName = Path.GetFileNameWithoutExtension(fullPath);
                    string ext = Path.GetExtension(fullPath);
                    int counter = 1;
                    do
                    {
                        fullPath = Path.Combine(dir, $"{baseName}-{counter}{ext}").Replace('\\', '/');
                        counter++;
                    } while (File.Exists(fullPath));
                }
                File.WriteAllBytes(fullPath, pngBytes);

                string projectRoot = Path.GetFullPath(Path.Combine(Application.dataPath, "..")).Replace('\\', '/');
                if (!projectRoot.EndsWith("/")) projectRoot += "/";
                string assetsRelPath = fullPath.StartsWith(projectRoot, StringComparison.OrdinalIgnoreCase)
                    ? fullPath.Substring(projectRoot.Length)
                    : fullPath;

                string base64 = System.Convert.ToBase64String(pngBytes);
                string message = $"Screenshot captured to '{assetsRelPath}' ({texWidth}x{texHeight}).";

                return new NativeMcp.Editor.Protocol.McpToolCallResult
                {
                    IsError = false,
                    Content = new List<NativeMcp.Editor.Protocol.McpContentBlock>
                    {
                        new NativeMcp.Editor.Protocol.McpContentBlock { Type = "text", Text = message },
                        new NativeMcp.Editor.Protocol.McpContentBlock { Type = "image", Data = base64, MimeType = "image/png" }
                    }
                };
            }
            catch (Exception e)
            {
                return new ErrorResponse($"Error capturing screenshot: {e.Message}");
            }
        }
    }
}
