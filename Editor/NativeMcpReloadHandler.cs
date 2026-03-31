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
        private static bool _pendingResume;
        private static int _resumeFrameDelay;

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
                    NativeMcpServerHost.StopServer(deletePortFile: false);
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

                    // Use EditorApplication.update with a frame delay — more reliable than delayCall
                    _pendingResume = true;
                    _resumeFrameDelay = 3; // wait 3 editor frames
                    EditorApplication.update += OnResumeUpdate;
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[NativeMcp] Error during post-reload resume: {ex.Message}");
            }
        }

        private static void OnResumeUpdate()
        {
            if (!_pendingResume)
            {
                EditorApplication.update -= OnResumeUpdate;
                return;
            }

            // Wait a few frames for Unity to fully settle
            if (_resumeFrameDelay > 0)
            {
                _resumeFrameDelay--;
                return;
            }

            _pendingResume = false;
            EditorApplication.update -= OnResumeUpdate;

            try
            {
                if (!NativeMcpServerHost.IsRunning)
                {
                    Debug.Log("[NativeMcp] Resuming server after assembly reload...");
                    NativeMcpServerHost.StartServer();
                }
            }
            catch (Exception ex)
            {
                Debug.LogError($"[NativeMcp] Failed to resume after reload: {ex.Message}");
            }
        }
    }
}
