using NativeMcp.Editor.Transport;
using UnityEditor;
using UnityEngine;

namespace NativeMcp.Editor
{
    /// <summary>
    /// Entry point for UnityREPL. Starts the file-based IPC transport
    /// automatically when Unity Editor loads.
    /// </summary>
    [InitializeOnLoad]
    internal static class NativeMcpServerHost
    {
        private static FileIpcTransport _ipcTransport;

        public static bool IsRunning => _ipcTransport?.IsRunning ?? false;

        static NativeMcpServerHost()
        {
            EditorApplication.quitting += OnEditorQuitting;
            EditorApplication.delayCall += StartServer;
        }

        public static void StartServer()
        {
            if (IsRunning) return;
            _ipcTransport = new FileIpcTransport();
            _ipcTransport.Start();
        }

        public static void StopServer(bool deletePortFile = true, string reason = null)
        {
            _ipcTransport?.Stop();
            _ipcTransport = null;
        }

        private static void OnEditorQuitting()
        {
            StopServer();
        }
    }
}
