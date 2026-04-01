using System.Net;
using System.Net.Sockets;
using NativeMcp.Editor.Bridge;
using NativeMcp.Editor.Helpers;
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
        private const string AutoStartEditorPrefKey = NativeMcpKeys.AutoStart;

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

            int savedPort = SessionState.GetInt(NativeMcpKeys.LastPort, 0);
            SessionState.EraseInt(NativeMcpKeys.LastPort);

            int port;
            if (savedPort > 0 && IsPortAvailable(savedPort))
                port = savedPort;
            else
                port = AllocateAvailablePort();

            _sessionManager = new McpSessionManager();
            _toolBridge = new UnityToolBridge();
            var ctx = new NativeMcp.Editor.Transport.McpCancellationContext();
            _protocolHandler = new McpProtocolHandler(_toolBridge, ctx);
            _transport = new HttpListenerTransport(_protocolHandler, _sessionManager, port, ctx: ctx);

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
        /// <param name="reason">
        /// Optional reason for stopping. Pass "domain_reload" when stopping due to an assembly
        /// reload so that in-flight requests can signal clients to wait and retry.
        /// </param>
        public static void StopServer(bool deletePortFile = true, string reason = null)
        {
            if (_transport == null)
            {
                return;
            }

            if (!deletePortFile)
            {
                // Domain reload: remember port for reuse so bridge doesn't lose connection
                SessionState.SetInt(NativeMcpKeys.LastPort, _transport.Port);
            }

            _transport.Stop(reason);
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

        /// <summary>
        /// Checks whether a specific port is available by attempting to bind to it.
        /// </summary>
        private static bool IsPortAvailable(int port)
        {
            try
            {
                var listener = new TcpListener(IPAddress.Loopback, port);
                listener.Start();
                listener.Stop();
                return true;
            }
            catch
            {
                return false;
            }
        }
    }
}
