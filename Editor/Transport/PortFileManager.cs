using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using UnityEngine;

namespace NativeMcp.Editor.Transport
{
    /// <summary>
    /// Manages a temporary port file that allows external processes (e.g. the stdio bridge)
    /// to discover which port this Unity instance's MCP server is listening on.
    /// The file path is derived from an MD5 hash of the project path, so each Unity project
    /// gets its own port file.
    /// </summary>
    internal static class PortFileManager
    {
        /// <summary>
        /// Returns the normalized absolute project path (forward slashes, no trailing slash).
        /// This must match the normalization used by the Node.js bridge.
        /// </summary>
        public static string GetNormalizedProjectPath()
        {
            // Application.dataPath points to the Assets folder
            string assetsPath = Application.dataPath;
            string projectRoot = Directory.GetParent(assetsPath)?.FullName ?? assetsPath;
            return Path.GetFullPath(projectRoot).Replace('\\', '/').TrimEnd('/');
        }

        /// <summary>
        /// Computes the port file path: {tmpdir}/unity-mcp-{hash}.port
        /// where hash is the first 8 hex chars of MD5(normalized_project_path).
        /// </summary>
        public static string GetPortFilePath()
        {
            string projectPath = GetNormalizedProjectPath();
            using (var md5 = MD5.Create())
            {
                byte[] hashBytes = md5.ComputeHash(Encoding.UTF8.GetBytes(projectPath));
                string hashHex = BitConverter.ToString(hashBytes)
                    .Replace("-", "")
                    .Substring(0, 8)
                    .ToLowerInvariant();
                // Use /tmp on macOS/Linux for a stable, well-known location.
                // Path.GetTempPath() returns $TMPDIR which varies per user session
                // and may differ between Unity and the bridge process.
                string tmpDir = Application.platform == RuntimePlatform.WindowsEditor
                    ? Path.GetTempPath()
                    : "/tmp";
                return Path.Combine(tmpDir, $"unity-mcp-{hashHex}.port");
            }
        }

        /// <summary>
        /// Writes the given port number to the port file.
        /// </summary>
        public static void WritePortFile(int port)
        {
            string filePath = GetPortFilePath();
            File.WriteAllText(filePath, port.ToString());
            Debug.Log($"[NativeMcp] Port file written: {filePath} (port {port})");
        }

        /// <summary>
        /// Deletes the port file if it exists. Silently ignores errors.
        /// </summary>
        public static void DeletePortFile()
        {
            try
            {
                string filePath = GetPortFilePath();
                if (File.Exists(filePath))
                {
                    File.Delete(filePath);
                    Debug.Log($"[NativeMcp] Port file deleted: {filePath}");
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[NativeMcp] Failed to delete port file: {ex.Message}");
            }
        }
    }
}
