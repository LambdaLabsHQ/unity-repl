using System;
using UnityEditor;
using UnityEngine;

namespace NativeMcp.Editor
{
    /// <summary>
    /// Handles domain reload to gracefully restart the UnityREPL IPC transport.
    /// </summary>
    [InitializeOnLoad]
    internal static class NativeMcpReloadHandler
    {
        private const string ResumeAfterReloadKey = "NativeMcp.ResumeAfterReload";
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
                    NativeMcpServerHost.StopServer();
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[UnityREPL] Pre-reload cleanup error: {ex.Message}");
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
                    _pendingResume = true;
                    _resumeFrameDelay = 3;
                    EditorApplication.update += OnResumeUpdate;
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[UnityREPL] Post-reload resume error: {ex.Message}");
            }
        }

        private static void OnResumeUpdate()
        {
            if (!_pendingResume)
            {
                EditorApplication.update -= OnResumeUpdate;
                return;
            }
            if (_resumeFrameDelay > 0) { _resumeFrameDelay--; return; }

            _pendingResume = false;
            EditorApplication.update -= OnResumeUpdate;

            try
            {
                if (!NativeMcpServerHost.IsRunning)
                    NativeMcpServerHost.StartServer();
            }
            catch (Exception ex)
            {
                Debug.LogError($"[UnityREPL] Failed to resume: {ex.Message}");
            }
        }
    }
}
