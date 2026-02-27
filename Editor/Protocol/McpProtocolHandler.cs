using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using NativeMcp.Editor.Bridge;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace NativeMcp.Editor.Protocol
{
    /// <summary>
    /// Routes incoming JSON-RPC method calls to appropriate MCP handlers.
    /// </summary>
    internal class McpProtocolHandler
    {
        private const string ProtocolVersion = "2025-03-26";
        private const string ServerName = "unity-mcp-native";
        private const string ServerVersion = "0.1.0";

        private readonly UnityToolBridge _toolBridge;
        private bool _initialized;

        public McpProtocolHandler(UnityToolBridge toolBridge)
        {
            _toolBridge = toolBridge ?? throw new ArgumentNullException(nameof(toolBridge));
        }

        /// <summary>
        /// Handle a single JSON-RPC request and return a JSON-RPC response string.
        /// Returns null for notifications (no response needed).
        /// </summary>
        public async Task<string> HandleRequestAsync(JsonRpcRequest request, CancellationToken ct)
        {
            if (request == null)
            {
                return SerializeError(null, JsonRpcErrorCodes.InvalidRequest, "Null request");
            }

            // Notifications don't get responses
            if (request.IsNotification)
            {
                HandleNotification(request);
                return null;
            }

            try
            {
                JToken result;
                switch (request.Method)
                {
                    case "initialize":
                        result = HandleInitialize(request.Params);
                        break;
                    case "ping":
                        result = new JObject();
                        break;
                    case "tools/list":
                        result = HandleToolsList();
                        break;
                    case "tools/call":
                        result = await HandleToolsCallAsync(request.Params, ct);
                        break;
                    default:
                        return SerializeError(request.Id, JsonRpcErrorCodes.MethodNotFound,
                            $"Method not found: {request.Method}");
                }

                return SerializeSuccess(request.Id, result);
            }
            catch (OperationCanceledException)
            {
                return SerializeError(request.Id, JsonRpcErrorCodes.InternalError, "Request cancelled");
            }
            catch (Exception ex)
            {
                Debug.LogError($"[NativeMcp] Error handling {request.Method}: {ex}");
                return SerializeError(request.Id, JsonRpcErrorCodes.InternalError, ex.Message);
            }
        }

        /// <summary>
        /// Handle a batch of JSON-RPC requests.
        /// </summary>
        public async Task<string> HandleBatchAsync(JArray batch, CancellationToken ct)
        {
            var responses = new List<string>();
            foreach (var item in batch)
            {
                var request = item.ToObject<JsonRpcRequest>();
                var response = await HandleRequestAsync(request, ct);
                if (response != null)
                {
                    responses.Add(response);
                }
            }

            if (responses.Count == 0)
            {
                return null; // All notifications
            }

            if (responses.Count == 1)
            {
                return responses[0];
            }

            return "[" + string.Join(",", responses) + "]";
        }

        private JToken HandleInitialize(JToken parameters)
        {
            _initialized = true;

            var result = new McpInitializeResult
            {
                ProtocolVersion = ProtocolVersion,
                Capabilities = new McpServerCapabilities
                {
                    Tools = new McpToolsCapability { ListChanged = false }
                },
                ServerInfo = new McpServerInfo
                {
                    Name = ServerName,
                    Version = ServerVersion
                }
            };

            return JToken.FromObject(result);
        }

        private void HandleNotification(JsonRpcRequest notification)
        {
            switch (notification.Method)
            {
                case "notifications/initialized":
                    Debug.Log("[NativeMcp] Client initialized notification received");
                    break;
                case "notifications/cancelled":
                    // TODO: support cancellation of in-flight requests
                    break;
                default:
                    Debug.LogWarning($"[NativeMcp] Unknown notification: {notification.Method}");
                    break;
            }
        }

        private JToken HandleToolsList()
        {
            var tools = _toolBridge.GetMcpToolDefinitions();
            var result = new McpToolsListResult { Tools = tools };
            return JToken.FromObject(result);
        }

        private async Task<JToken> HandleToolsCallAsync(JToken parameters, CancellationToken ct)
        {
            if (parameters == null)
            {
                throw new ArgumentException("tools/call requires params");
            }

            string toolName = parameters["name"]?.ToString();
            JObject arguments = parameters["arguments"] as JObject ?? new JObject();

            if (string.IsNullOrEmpty(toolName))
            {
                throw new ArgumentException("tools/call requires 'name' parameter");
            }

            var result = await _toolBridge.ExecuteToolAsync(toolName, arguments, ct);
            return JToken.FromObject(result);
        }

        private static string SerializeSuccess(JToken id, JToken result)
        {
            var response = new JsonRpcResponse
            {
                Id = id,
                Result = result
            };
            return JsonConvert.SerializeObject(response, Formatting.None);
        }

        private static string SerializeError(JToken id, int code, string message, JToken data = null)
        {
            var response = new JsonRpcErrorResponse
            {
                Id = id,
                Error = new JsonRpcErrorDetail
                {
                    Code = code,
                    Message = message,
                    Data = data
                }
            };
            return JsonConvert.SerializeObject(response, Formatting.None);
        }
    }
}
