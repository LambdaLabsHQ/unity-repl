using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Threading.Tasks;
using NativeMcp.Editor.Bridge;
using NativeMcp.Editor.Helpers;
using NativeMcp.Editor.Protocol;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine;

namespace NativeMcp.Editor.Tools.Scene
{
    /// <summary>
    /// Capture a screenshot from the current camera view.
    /// In Play Mode, focuses the Game View and uses a coroutine-based ScreenCapture for reliable capture
    /// (including Screen Space - Overlay UI). Falls back to window pixel reading if needed.
    /// In Edit Mode, renders via Camera.Render() into a RenderTexture.
    /// </summary>
    [McpForUnityTool("scene_screenshot", Internal = true,
        Description = "Capture a screenshot from the current camera view. " +
                      "In Play Mode captures the Game View including UI overlays.")]
    public static class SceneScreenshot
    {
        private const int CoroutineTimeoutMs = 5000;

        public class Parameters
        {
            [ToolParameter("Output file name (default: screenshot-<timestamp>.png)", Required = false)]
            public string fileName { get; set; }

            [ToolParameter("Super-sampling multiplier for resolution (default 1)", Required = false)]
            public int? superSize { get; set; }

            [ToolParameter(
                "Capture mode: 'auto' (default, tries coroutine then RT fallback), " +
                "'game_view' (coroutine-based ScreenCapture only), " +
                "'window' (Game View internal RenderTexture), " +
                "'camera' (Camera.Render, no Overlay UI)",
                Required = false)]
            public string mode { get; set; }
        }

        public static async Task<object> HandleCommand(JObject @params)
        {
            var p = @params ?? new JObject();
            string fileName = SceneHelpers.ParseString(p, "fileName", "filename");
            int? superSize = SceneHelpers.ParseInt(p, "superSize", "super_size", "supersize");
            string mode = SceneHelpers.ParseString(p, "mode") ?? "auto";
            return await CaptureScreenshotAsync(fileName, superSize, mode);
        }

        internal static async Task<object> CaptureScreenshotAsync(string fileName, int? superSize, string mode = "auto")
        {
            try
            {
                int size = (superSize.HasValue && superSize.Value > 0) ? superSize.Value : 1;
                Texture2D tex = null;
                string captureMethod = null;

                if (Application.isPlaying)
                {
                    tex = await CapturePlayModeAsync(size, mode);
                    if (tex != null)
                        captureMethod = _lastCaptureMethod;
                }
                else
                {
                    if (mode == "game_view" || mode == "window")
                        return new ErrorResponse($"Mode '{mode}' is only available in Play Mode.");

                    tex = CaptureViaCamera(size);
                    captureMethod = "camera";
                }

                if (tex == null)
                    return new ErrorResponse("All capture methods failed. Ensure a valid Camera exists or the Game View is visible.");

                return BuildResult(tex, fileName, captureMethod);
            }
            catch (Exception e)
            {
                return new ErrorResponse($"Error capturing screenshot: {e.Message}");
            }
        }

        // Tracks which method succeeded for the response message
        private static string _lastCaptureMethod;

        private static async Task<Texture2D> CapturePlayModeAsync(int size, string mode)
        {
            _lastCaptureMethod = null;

            if (mode == "game_view" || mode == "auto")
            {
                try
                {
                    var tex = await CaptureViaCoroutineAsync(size);
                    if (tex != null)
                    {
                        _lastCaptureMethod = "coroutine";
                        return tex;
                    }
                }
                catch (Exception e)
                {
                    Debug.LogWarning($"[NativeMcp] Coroutine screenshot failed: {e.Message}");
                }

                if (mode == "game_view")
                    return null;
            }

            if (mode == "window" || mode == "auto")
            {
                try
                {
                    var tex = CaptureGameViewPixels();
                    if (tex != null)
                    {
                        _lastCaptureMethod = "window";
                        return tex;
                    }
                }
                catch (Exception e)
                {
                    Debug.LogWarning($"[NativeMcp] Window pixel screenshot failed: {e.Message}");
                }

                if (mode == "window")
                    return null;
            }

            if (mode == "camera" || mode == "auto")
            {
                try
                {
                    var tex = CaptureViaCamera(size);
                    if (tex != null)
                    {
                        _lastCaptureMethod = "camera";
                        return tex;
                    }
                }
                catch (Exception e)
                {
                    Debug.LogWarning($"[NativeMcp] Camera screenshot failed: {e.Message}");
                }
            }

            return null;
        }

        #region Coroutine-based capture (ScreenCapture)

        private class ScreenshotCoroutineRunner : MonoBehaviour { }

        private static async Task<Texture2D> CaptureViaCoroutineAsync(int size)
        {
            // Focus the Game View first
            FocusGameView();

            // Wait one editor frame for focus to settle
            var focusTcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            EditorApplication.delayCall += () => focusTcs.TrySetResult(true);
            EditorNudge.BeginNudge();
            try
            {
                await focusTcs.Task;
            }
            finally
            {
                EditorNudge.EndNudge();
            }

            if (!Application.isPlaying)
                return null;

            // Create temp GO to run coroutine
            var tcs = new TaskCompletionSource<Texture2D>(TaskCreationOptions.RunContinuationsAsynchronously);
            var go = new GameObject("[NativeMcp] ScreenshotHelper")
            {
                hideFlags = HideFlags.HideAndDontSave
            };

            EditorNudge.BeginNudge();
            try
            {
                var runner = go.AddComponent<ScreenshotCoroutineRunner>();
                runner.StartCoroutine(CaptureCoroutine(size, tcs));

                // Timeout
                var timeoutTask = Task.Delay(CoroutineTimeoutMs);
                var completed = await Task.WhenAny(tcs.Task, timeoutTask);

                if (completed == timeoutTask)
                {
                    tcs.TrySetResult(null);
                    Debug.LogWarning("[NativeMcp] Coroutine screenshot timed out.");
                    return null;
                }

                return tcs.Task.Result;
            }
            finally
            {
                EditorNudge.EndNudge();
                if (go != null)
                    UnityEngine.Object.DestroyImmediate(go);
            }
        }

        private static IEnumerator CaptureCoroutine(int size, TaskCompletionSource<Texture2D> tcs)
        {
            yield return new WaitForEndOfFrame();
            try
            {
                var tex = ScreenCapture.CaptureScreenshotAsTexture(size);
                tcs.TrySetResult(tex);
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[NativeMcp] ScreenCapture.CaptureScreenshotAsTexture failed: {e.Message}");
                tcs.TrySetResult(null);
            }
        }

        #endregion

        #region Game View RenderTexture capture (fallback)

        private static Texture2D CaptureGameViewPixels()
        {
            // Read the Game View's internal m_RenderTexture via reflection.
            // This is the composited output that includes UI overlays.
            // Safer than ReadScreenPixel which causes native crashes on macOS.
            var gameView = FocusGameView();
            if (gameView == null)
            {
                Debug.LogWarning("[NativeMcp] Could not find Game View window.");
                return null;
            }

            gameView.Repaint();

            var gameViewType = gameView.GetType();

            // Try m_RenderTexture first (available in most Unity versions)
            var rtField = gameViewType.GetField("m_RenderTexture",
                BindingFlags.NonPublic | BindingFlags.Instance);
            var rt = rtField?.GetValue(gameView) as RenderTexture;

            if (rt == null || rt.width <= 0 || rt.height <= 0)
            {
                Debug.LogWarning("[NativeMcp] Game View RenderTexture not available or empty.");
                return null;
            }

            var tex = new Texture2D(rt.width, rt.height, TextureFormat.RGBA32, false);
            var prevActive = RenderTexture.active;
            try
            {
                RenderTexture.active = rt;
                tex.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0);
                tex.Apply();
            }
            finally
            {
                RenderTexture.active = prevActive;
            }

            // Flip vertically — ReadPixels from RenderTexture yields Y-inverted data
            // (RenderTexture origin is bottom-left, screen/PNG origin is top-left)
            FlipTextureVertically(tex);

            return tex;
        }

        #endregion

        #region Camera.Render capture (Edit Mode / fallback)

        private static Texture2D CaptureViaCamera(int size)
        {
            Camera cam = Camera.main;

            var urpCamDataType = Type.GetType(
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
                return null;

            int width = Mathf.Max(1, cam.pixelWidth > 0 ? cam.pixelWidth : Screen.width) * size;
            int height = Mathf.Max(1, cam.pixelHeight > 0 ? cam.pixelHeight : Screen.height) * size;

            var rt = RenderTexture.GetTemporary(width, height, 24, RenderTextureFormat.ARGB32);
            var tex = new Texture2D(width, height, TextureFormat.RGBA32, false);

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

            return tex;
        }

        #endregion

        #region Helpers

        private static void FlipTextureVertically(Texture2D tex)
        {
            var pixels = tex.GetPixels();
            int w = tex.width, h = tex.height;
            for (int y = 0; y < h / 2; y++)
            {
                int topRow = y * w;
                int bottomRow = (h - 1 - y) * w;
                for (int x = 0; x < w; x++)
                {
                    (pixels[topRow + x], pixels[bottomRow + x]) = (pixels[bottomRow + x], pixels[topRow + x]);
                }
            }
            tex.SetPixels(pixels);
            tex.Apply();
        }

        private static EditorWindow FocusGameView()
        {
            try
            {
                var gameViewType = typeof(EditorWindow).Assembly.GetType("UnityEditor.GameView");
                if (gameViewType == null) return null;
                var gameView = EditorWindow.GetWindow(gameViewType, false, null, true);
                gameView?.Focus();
                return gameView;
            }
            catch
            {
                return null;
            }
        }

        private static object BuildResult(Texture2D tex, string fileName, string captureMethod)
        {
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

            string base64 = Convert.ToBase64String(pngBytes);
            string methodNote = captureMethod != null ? $" [method: {captureMethod}]" : "";
            string message = $"Screenshot captured to '{assetsRelPath}' ({texWidth}x{texHeight}).{methodNote}";

            return new McpToolCallResult
            {
                IsError = false,
                Content = new List<McpContentBlock>
                {
                    new McpContentBlock { Type = "text", Text = message },
                    new McpContentBlock { Type = "image", Data = base64, MimeType = "image/png" }
                }
            };
        }

        #endregion
    }
}
