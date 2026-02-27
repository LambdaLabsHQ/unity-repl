using NativeMcp.Editor.Bridge;
using NativeMcp.Editor.Protocol;
using NativeMcp.Editor.Transport;
using UnityEditor;
using UnityEngine;

namespace NativeMcp.Editor
{
    /// <summary>
    /// Entry point for the native MCP server. Starts automatically when Unity Editor loads
    /// via <see cref="InitializeOnLoadAttribute"/>.
    /// </summary>
    [InitializeOnLoad]
    internal static class NativeMcpServerHost
    {
        private const int DefaultPort = 8090;
        private const string PortEditorPrefKey = "NativeMcp_Port";
        private const string AutoStartEditorPrefKey = "NativeMcp_AutoStart";

        private static HttpListenerTransport _transport;
        private static McpProtocolHandler _protocolHandler;
        private static McpSessionManager _sessionManager;
        private static UnityToolBridge _toolBridge;

        public static bool IsRunning => _transport?.IsRunning ?? false;
        public static string ServerUrl => _transport?.Url ?? $"http://localhost:{GetPort()}/mcp";

        static NativeMcpServerHost()
        {
            EditorApplication.quitting += OnEditorQuitting;

            if (GetAutoStart())
            {
                // Delay start slightly to let Unity finish initialization
                EditorApplication.delayCall += StartServer;
            }
        }

        /// <summary>
        /// Start the MCP server.
        /// </summary>
        public static void StartServer()
        {
            if (IsRunning)
            {
                Debug.Log("[NativeMcp] Server is already running");
                return;
            }

            int port = GetPort();

            _sessionManager = new McpSessionManager();
            _toolBridge = new UnityToolBridge();
            _protocolHandler = new McpProtocolHandler(_toolBridge);
            _transport = new HttpListenerTransport(_protocolHandler, _sessionManager, port);

            try
            {
                _transport.Start();
            }
            catch
            {
                _transport = null;
                throw;
            }
        }

        /// <summary>
        /// Stop the MCP server.
        /// </summary>
        public static void StopServer()
        {
            if (_transport == null)
            {
                return;
            }

            _transport.Stop();
            _transport = null;
            _protocolHandler = null;
            _sessionManager = null;
            _toolBridge = null;
        }

        public static int GetPort()
        {
            return EditorPrefs.GetInt(PortEditorPrefKey, DefaultPort);
        }

        public static void SetPort(int port)
        {
            EditorPrefs.SetInt(PortEditorPrefKey, port);
        }

        public static bool GetAutoStart()
        {
            return EditorPrefs.GetBool(AutoStartEditorPrefKey, true);
        }

        public static void SetAutoStart(bool value)
        {
            EditorPrefs.SetBool(AutoStartEditorPrefKey, value);
        }

        private static void OnEditorQuitting()
        {
            StopServer();
        }
    }
}
