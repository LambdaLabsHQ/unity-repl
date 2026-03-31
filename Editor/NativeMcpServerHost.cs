using System.Net;
using System.Net.Sockets;
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
        private const string AutoStartEditorPrefKey = "NativeMcp_AutoStart";

        private static HttpListenerTransport _transport;
        private static McpProtocolHandler _protocolHandler;
        private static McpSessionManager _sessionManager;
        private static UnityToolBridge _toolBridge;

        public static bool IsRunning => _transport?.IsRunning ?? false;
        public static string ServerUrl => _transport?.Url;

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
        /// Start the MCP server with a dynamically allocated port.
        /// </summary>
        public static void StartServer()
        {
            if (IsRunning)
            {
                Debug.Log("[NativeMcp] Server is already running");
                return;
            }

            int port = AllocateAvailablePort();

            _sessionManager = new McpSessionManager();
            _toolBridge = new UnityToolBridge();
            _protocolHandler = new McpProtocolHandler(_toolBridge);
            _transport = new HttpListenerTransport(_protocolHandler, _sessionManager, port);

            try
            {
                _transport.Start();
                PortFileManager.WritePortFile(port);
            }
            catch
            {
                _transport?.Stop();
                _transport = null;
                PortFileManager.DeletePortFile();
                throw;
            }
        }

        /// <summary>
        /// Stop the MCP server.
        /// </summary>
        /// <param name="deletePortFile">
        /// When true (default), deletes the port file. Pass false during assembly reload
        /// so the bridge can still discover the port while the server restarts.
        /// </param>
        public static void StopServer(bool deletePortFile = true)
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

            if (deletePortFile)
            {
                PortFileManager.DeletePortFile();
            }
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

        /// <summary>
        /// Allocates an available TCP port by briefly binding to port 0.
        /// </summary>
        private static int AllocateAvailablePort()
        {
            var listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            int port = ((IPEndPoint)listener.LocalEndpoint).Port;
            listener.Stop();
            return port;
        }
    }
}
