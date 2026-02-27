using System;
using UnityEditor;
using UnityEngine;

namespace NativeMcp.Editor
{
    /// <summary>
    /// Handles domain reload (assembly reload) events to gracefully stop and restart
    /// the MCP server. Without this, the HttpListener port can get stuck after recompilation.
    /// </summary>
    [InitializeOnLoad]
    internal static class NativeMcpReloadHandler
    {
        private const string ResumeAfterReloadKey = "NativeMcp_ResumeAfterReload";

        static NativeMcpReloadHandler()
        {
            AssemblyReloadEvents.beforeAssemblyReload += OnBeforeAssemblyReload;
            AssemblyReloadEvents.afterAssemblyReload += OnAfterAssemblyReload;
        }

        private static void OnBeforeAssemblyReload()
        {
            try
            {
                bool wasRunning = NativeMcpServerHost.IsRunning;
                EditorPrefs.SetBool(ResumeAfterReloadKey, wasRunning);

                if (wasRunning)
                {
                    Debug.Log("[NativeMcp] Stopping server before assembly reload...");
                    NativeMcpServerHost.StopServer();
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[NativeMcp] Error during pre-reload cleanup: {ex.Message}");
            }
        }

        private static void OnAfterAssemblyReload()
        {
            try
            {
                bool shouldResume = EditorPrefs.GetBool(ResumeAfterReloadKey, false);
                if (shouldResume)
                {
                    EditorPrefs.DeleteKey(ResumeAfterReloadKey);

                    // Delay slightly to avoid conflicts during reload
                    EditorApplication.delayCall += () =>
                    {
                        try
                        {
                            Debug.Log("[NativeMcp] Resuming server after assembly reload...");
                            NativeMcpServerHost.StartServer();
                        }
                        catch (Exception ex)
                        {
                            Debug.LogError($"[NativeMcp] Failed to resume after reload: {ex.Message}");
                        }
                    };
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[NativeMcp] Error during post-reload resume: {ex.Message}");
            }
        }
    }
}
