using System;
using System.IO;
using System.Net;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using NativeMcp.Editor.Protocol;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace NativeMcp.Editor.Transport
{
    /// <summary>
    /// MCP Streamable HTTP transport using <see cref="HttpListener"/>.
    /// Handles POST (JSON-RPC), GET (SSE stream), and DELETE (session termination).
    /// </summary>
    internal class HttpListenerTransport : IDisposable
    {
        private HttpListener _listener;
        private CancellationTokenSource _cts;
        private readonly McpProtocolHandler _protocolHandler;
        private readonly McpSessionManager _sessionManager;
        private readonly int _port;
        private readonly string _endpoint;
        private bool _running;

        public bool IsRunning => _running;
        public int Port => _port;
        public string Url => $"http://localhost:{_port}{_endpoint}";

        public HttpListenerTransport(McpProtocolHandler protocolHandler,
            McpSessionManager sessionManager, int port = 8090, string endpoint = "/mcp")
        {
            _protocolHandler = protocolHandler ?? throw new ArgumentNullException(nameof(protocolHandler));
            _sessionManager = sessionManager ?? throw new ArgumentNullException(nameof(sessionManager));
            _port = port;
            _endpoint = endpoint;
        }

        /// <summary>
        /// Start listening for HTTP requests.
        /// </summary>
        public void Start()
        {
            if (_running)
            {
                return;
            }

            try
            {
                _cts = new CancellationTokenSource();
                _listener = new HttpListener();

                // HttpListener prefix must end with /
                string prefix = $"http://localhost:{_port}/";
                _listener.Prefixes.Add(prefix);
                _listener.Start();
                _running = true;

                Debug.Log($"[NativeMcp] Server started on {Url}");

                // Start the accept loop on a background thread
                Task.Run(() => AcceptLoopAsync(_cts.Token), _cts.Token);
            }
            catch (HttpListenerException ex)
            {
                Debug.LogError($"[NativeMcp] Failed to start HTTP listener on port {_port}: {ex.Message}");
                _running = false;
                throw;
            }
        }

        /// <summary>
        /// Stop listening and clean up resources.
        /// </summary>
        public void Stop()
        {
            if (!_running)
            {
                return;
            }

            _running = false;

            try
            {
                _cts?.Cancel();
            }
            catch (ObjectDisposedException) { }

            try
            {
                _listener?.Stop();
                _listener?.Close();
            }
            catch (ObjectDisposedException) { }

            _listener = null;
            _cts?.Dispose();
            _cts = null;

            _sessionManager.ClearAll();
            Debug.Log("[NativeMcp] Server stopped");
        }

        public void Dispose()
        {
            Stop();
        }

        private async Task AcceptLoopAsync(CancellationToken ct)
        {
            while (!ct.IsCancellationRequested && _running)
            {
                try
                {
                    var context = await _listener.GetContextAsync();

                    // Handle each request on its own task (don't block the accept loop)
                    _ = Task.Run(() => HandleRequestSafeAsync(context, ct), ct);
                }
                catch (HttpListenerException) when (ct.IsCancellationRequested || !_running)
                {
                    break; // Normal shutdown
                }
                catch (ObjectDisposedException)
                {
                    break; // Listener was disposed
                }
                catch (Exception ex)
                {
                    if (!ct.IsCancellationRequested)
                    {
                        Debug.LogError($"[NativeMcp] Accept loop error: {ex.Message}");
                    }
                }
            }
        }

        private async Task HandleRequestSafeAsync(HttpListenerContext context, CancellationToken ct)
        {
            try
            {
                await HandleRequestAsync(context, ct);
            }
            catch (Exception ex)
            {
                Debug.LogError($"[NativeMcp] Request handler error: {ex}");
                try
                {
                    context.Response.StatusCode = 500;
                    context.Response.Close();
                }
                catch { }
            }
        }

        private async Task HandleRequestAsync(HttpListenerContext context, CancellationToken ct)
        {
            var request = context.Request;
            var response = context.Response;

            // Check that the request path matches our endpoint
            if (!request.Url.AbsolutePath.TrimEnd('/').Equals(_endpoint.TrimEnd('/'),
                StringComparison.OrdinalIgnoreCase))
            {
                response.StatusCode = 404;
                response.Close();
                return;
            }

            switch (request.HttpMethod.ToUpperInvariant())
            {
                case "POST":
                    await HandlePostAsync(context, ct);
                    break;
                case "GET":
                    // We don't support server-initiated SSE streams for now
                    response.StatusCode = 405;
                    response.Close();
                    break;
                case "DELETE":
                    HandleDelete(context);
                    break;
                case "OPTIONS":
                    // CORS preflight
                    SetCorsHeaders(response);
                    response.StatusCode = 204;
                    response.Close();
                    break;
                default:
                    response.StatusCode = 405;
                    response.Close();
                    break;
            }
        }

        private async Task HandlePostAsync(HttpListenerContext context, CancellationToken ct)
        {
            var request = context.Request;
            var response = context.Response;

            SetCorsHeaders(response);

            // Read the request body
            string body;
            using (var reader = new StreamReader(request.InputStream, request.ContentEncoding))
            {
                body = await reader.ReadToEndAsync();
            }

            if (string.IsNullOrWhiteSpace(body))
            {
                await SendJsonResponseAsync(response, 400,
                    MakeErrorJson(null, JsonRpcErrorCodes.ParseError, "Empty request body"));
                return;
            }

            // Parse JSON
            JToken parsed;
            try
            {
                parsed = JToken.Parse(body);
            }
            catch (JsonException ex)
            {
                await SendJsonResponseAsync(response, 400,
                    MakeErrorJson(null, JsonRpcErrorCodes.ParseError, $"Invalid JSON: {ex.Message}"));
                return;
            }

            // Determine if this is a batch or single request
            bool isBatch = parsed is JArray;
            bool isInitialize = false;

            if (!isBatch)
            {
                var req = parsed.ToObject<JsonRpcRequest>();
                isInitialize = req?.Method == "initialize";
            }

            // Session validation disabled — this is a localhost-only dev tool,
            // and rejecting stale session IDs breaks clients (like Antigravity)
            // that cache the ID across server restarts.

            // Process the request(s)
            string responseJson;
            if (isBatch)
            {
                responseJson = await _protocolHandler.HandleBatchAsync((JArray)parsed, ct);
            }
            else
            {
                var rpcRequest = parsed.ToObject<JsonRpcRequest>();

                // Check if it's a notification or response (no id)
                if (rpcRequest.IsNotification)
                {
                    // Handle notification, return 202 Accepted
                    await _protocolHandler.HandleRequestAsync(rpcRequest, ct);
                    response.StatusCode = 202;
                    response.Close();
                    return;
                }

                responseJson = await _protocolHandler.HandleRequestAsync(rpcRequest, ct);
            }

            if (responseJson == null)
            {
                // All notifications → 202
                response.StatusCode = 202;
                response.Close();
                return;
            }

            // Check Accept header for SSE preference
            string accept = request.Headers["Accept"] ?? "";
            bool wantsSse = accept.Contains("text/event-stream");

            // For initialize, create a session and include in response header
            if (isInitialize)
            {
                string newSessionId = _sessionManager.CreateSession();
                response.Headers.Set("Mcp-Session-Id", newSessionId);
            }

            if (wantsSse)
            {
                await SendSseResponseAsync(response, responseJson);
            }
            else
            {
                await SendJsonResponseAsync(response, 200, responseJson);
            }
        }

        private void HandleDelete(HttpListenerContext context)
        {
            var response = context.Response;
            SetCorsHeaders(response);

            string sessionId = context.Request.Headers["Mcp-Session-Id"];
            if (!string.IsNullOrEmpty(sessionId))
            {
                _sessionManager.TerminateSession(sessionId);
            }

            response.StatusCode = 200;
            response.Close();
        }

        private static async Task SendJsonResponseAsync(HttpListenerResponse response, int statusCode, string json)
        {
            response.StatusCode = statusCode;
            response.ContentType = "application/json";

            byte[] buffer = Encoding.UTF8.GetBytes(json);
            response.ContentLength64 = buffer.Length;

            try
            {
                await response.OutputStream.WriteAsync(buffer, 0, buffer.Length);
            }
            finally
            {
                response.Close();
            }
        }

        private static async Task SendSseResponseAsync(HttpListenerResponse response, string json)
        {
            response.StatusCode = 200;
            response.ContentType = "text/event-stream";
            response.Headers.Set("Cache-Control", "no-cache");
            response.Headers.Set("Connection", "keep-alive");

            // Don't set ContentLength64 for streaming
            var writer = new SseWriter(response.OutputStream);

            try
            {
                await writer.WriteEventAsync(json);
            }
            finally
            {
                response.Close();
            }
        }

        private static void SetCorsHeaders(HttpListenerResponse response)
        {
            response.Headers.Set("Access-Control-Allow-Origin", "*");
            response.Headers.Set("Access-Control-Allow-Methods", "GET, POST, DELETE, OPTIONS");
            response.Headers.Set("Access-Control-Allow-Headers",
                "Content-Type, Accept, Mcp-Session-Id");
            response.Headers.Set("Access-Control-Expose-Headers", "Mcp-Session-Id");
        }

        private static string MakeErrorJson(JToken id, int code, string message)
        {
            var error = new JsonRpcErrorResponse
            {
                Id = id,
                Error = new JsonRpcErrorDetail
                {
                    Code = code,
                    Message = message
                }
            };
            return JsonConvert.SerializeObject(error, Formatting.None);
        }
    }
}
