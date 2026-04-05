using System;
using System.IO;
using System.Reflection;
using UnityEditor;
using UnityEngine;

namespace LambdaLabs.UnityRepl.Editor.Helpers
{
    /// <summary>
    /// Capture Game View screenshots including Screen Space - Overlay UI (HUD, health bars, etc.).
    ///
    /// Why this utility exists: the Runtime <c>ScreenshotUtility</c> uses <c>Camera.Render()</c>
    /// which produces a pure 3D render with no overlay UI. For agents observing gameplay, the
    /// HUD is often more important than the scene (health bars, building cards, timers).
    ///
    /// Tricks not discoverable from Unity docs:
    /// 1. GameView has an internal <c>m_RenderTexture</c> field holding the composited output
    ///    (including overlay UI). Access requires reflection into <c>UnityEditor.GameView</c>.
    /// 2. <c>Texture2D.ReadPixels()</c> from a RenderTexture reads with bottom-left origin,
    ///    so the resulting image is Y-flipped relative to standard screen/PNG orientation.
    ///    We flip it back manually.
    /// 3. <c>gameView.Repaint()</c> must be called before reading to ensure the RT is current.
    ///
    /// Example usage from REPL:
    /// <code>
    /// using LambdaLabs.UnityRepl.Editor.Helpers;
    /// var path = GameViewCaptureUtility.CaptureGameViewWithUIToFile("my_shot");
    /// Debug.Log(path);
    /// </code>
    /// </summary>
    public static class GameViewCaptureUtility
    {
        private static Type s_gameViewType;
        private static FieldInfo s_renderTextureField;

        /// <summary>
        /// Capture the Game View (including overlay UI) as a <see cref="Texture2D"/>.
        /// Returns null if the Game View window or its RenderTexture is unavailable.
        /// Caller is responsible for destroying the returned texture.
        /// </summary>
        public static Texture2D CaptureGameViewWithUI()
        {
            var gameView = FocusGameView();
            if (gameView == null)
            {
                Debug.LogWarning("[GameViewCaptureUtility] Could not find Game View window.");
                return null;
            }

            gameView.Repaint();

            var rt = GetRenderTexture(gameView);
            if (rt == null || rt.width <= 0 || rt.height <= 0)
            {
                Debug.LogWarning("[GameViewCaptureUtility] Game View RenderTexture not available or empty.");
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

            // RenderTexture origin is bottom-left; screen/PNG origin is top-left.
            FlipTextureVertically(tex);
            return tex;
        }

        /// <summary>
        /// Capture the Game View and save as PNG under <c>Assets/Screenshots/</c>.
        /// Returns the Assets-relative path of the saved file, or null on failure.
        /// </summary>
        /// <param name="fileName">File name without extension (default: timestamped).</param>
        public static string CaptureGameViewWithUIToFile(string fileName = null)
        {
            var tex = CaptureGameViewWithUI();
            if (tex == null) return null;

            try
            {
                byte[] pngBytes = tex.EncodeToPNG();

                string resolvedName = string.IsNullOrWhiteSpace(fileName)
                    ? $"screenshot-{DateTime.Now:yyyyMMdd-HHmmss}.png"
                    : (fileName.EndsWith(".png", StringComparison.OrdinalIgnoreCase) ? fileName : fileName + ".png");

                string folder = Path.Combine(Application.dataPath, "Screenshots");
                Directory.CreateDirectory(folder);
                string fullPath = Path.Combine(folder, resolvedName).Replace('\\', '/');

                // Avoid collision
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
                return fullPath.StartsWith(projectRoot, StringComparison.OrdinalIgnoreCase)
                    ? fullPath.Substring(projectRoot.Length)
                    : fullPath;
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(tex);
            }
        }

        // ── Helpers ──────────────────────────────────────────────────

        private static Type GameViewType => s_gameViewType ??=
            typeof(EditorWindow).Assembly.GetType("UnityEditor.GameView");

        private static EditorWindow FocusGameView()
        {
            try
            {
                if (GameViewType == null) return null;
                var gameView = EditorWindow.GetWindow(GameViewType, false, null, true);
                gameView?.Focus();
                return gameView;
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[GameViewCaptureUtility] FocusGameView failed: {ex.Message}");
                return null;
            }
        }

        private static RenderTexture GetRenderTexture(EditorWindow gameView)
        {
            if (s_renderTextureField == null)
            {
                s_renderTextureField = gameView.GetType().GetField("m_RenderTexture",
                    BindingFlags.NonPublic | BindingFlags.Instance);
            }
            return s_renderTextureField?.GetValue(gameView) as RenderTexture;
        }

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
    }
}
