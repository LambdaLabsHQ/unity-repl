using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace NativeMcp.Editor.Protocol
{
    /// <summary>
    /// JSON-RPC 2.0 request object.
    /// </summary>
    internal class JsonRpcRequest
    {
        [JsonProperty("jsonrpc")]
        public string JsonRpc { get; set; } = "2.0";

        [JsonProperty("method")]
        public string Method { get; set; }

        [JsonProperty("params")]
        public JToken Params { get; set; }

        [JsonProperty("id")]
        public JToken Id { get; set; }

        /// <summary>
        /// True if this is a notification (no id field).
        /// </summary>
        [JsonIgnore]
        public bool IsNotification => Id == null || Id.Type == JTokenType.Null;
    }

    /// <summary>
    /// JSON-RPC 2.0 success response.
    /// </summary>
    internal class JsonRpcResponse
    {
        [JsonProperty("jsonrpc")]
        public string JsonRpc { get; set; } = "2.0";

        [JsonProperty("result")]
        public JToken Result { get; set; }

        [JsonProperty("id")]
        public JToken Id { get; set; }
    }

    /// <summary>
    /// JSON-RPC 2.0 error response.
    /// </summary>
    internal class JsonRpcErrorResponse
    {
        [JsonProperty("jsonrpc")]
        public string JsonRpc { get; set; } = "2.0";

        [JsonProperty("error")]
        public JsonRpcErrorDetail Error { get; set; }

        [JsonProperty("id")]
        public JToken Id { get; set; }
    }

    /// <summary>
    /// JSON-RPC 2.0 error detail.
    /// </summary>
    internal class JsonRpcErrorDetail
    {
        [JsonProperty("code")]
        public int Code { get; set; }

        [JsonProperty("message")]
        public string Message { get; set; }

        [JsonProperty("data")]
        public JToken Data { get; set; }
    }

    /// <summary>
    /// Standard JSON-RPC error codes.
    /// </summary>
    internal static class JsonRpcErrorCodes
    {
        public const int ParseError = -32700;
        public const int InvalidRequest = -32600;
        public const int MethodNotFound = -32601;
        public const int InvalidParams = -32602;
        public const int InternalError = -32603;

        /// <summary>
        /// Returned when an in-flight request is cancelled due to a domain reload.
        /// The error data includes <c>{ "reason": "domain_reload" }</c>.
        /// Bridge clients should wait for the server to restart and retry the request.
        /// </summary>
        public const int DomainReloadCancelled = -32001;
    }
}
