using System.Collections.Generic;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace NativeMcp.Editor.Protocol
{
    /// <summary>
    /// MCP server information returned during initialization.
    /// </summary>
    internal class McpServerInfo
    {
        [JsonProperty("name")]
        public string Name { get; set; }

        [JsonProperty("version")]
        public string Version { get; set; }
    }

    /// <summary>
    /// MCP server capabilities advertised during initialization.
    /// </summary>
    internal class McpServerCapabilities
    {
        [JsonProperty("tools")]
        public McpToolsCapability Tools { get; set; }
    }

    internal class McpToolsCapability
    {
        [JsonProperty("listChanged")]
        public bool ListChanged { get; set; }
    }

    /// <summary>
    /// Result of the MCP initialize request.
    /// </summary>
    internal class McpInitializeResult
    {
        [JsonProperty("protocolVersion")]
        public string ProtocolVersion { get; set; }

        [JsonProperty("capabilities")]
        public McpServerCapabilities Capabilities { get; set; }

        [JsonProperty("serverInfo")]
        public McpServerInfo ServerInfo { get; set; }
    }

    /// <summary>
    /// An MCP tool definition as returned by tools/list.
    /// </summary>
    internal class McpToolDefinition
    {
        [JsonProperty("name")]
        public string Name { get; set; }

        [JsonProperty("description")]
        public string Description { get; set; }

        [JsonProperty("inputSchema")]
        public JObject InputSchema { get; set; }
    }

    /// <summary>
    /// Result of tools/list.
    /// </summary>
    internal class McpToolsListResult
    {
        [JsonProperty("tools")]
        public List<McpToolDefinition> Tools { get; set; }
    }

    /// <summary>
    /// A content block inside a tool call result.
    /// </summary>
    internal class McpContentBlock
    {
        [JsonProperty("type")]
        public string Type { get; set; } = "text";

        [JsonProperty("text", NullValueHandling = NullValueHandling.Ignore)]
        public string Text { get; set; }

        /// <summary>Base64-encoded binary data (used when Type == "image").</summary>
        [JsonProperty("data", NullValueHandling = NullValueHandling.Ignore)]
        public string Data { get; set; }

        /// <summary>MIME type of the binary data, e.g. "image/png" (used when Type == "image").</summary>
        [JsonProperty("mimeType", NullValueHandling = NullValueHandling.Ignore)]
        public string MimeType { get; set; }
    }

    /// <summary>
    /// Result of tools/call.
    /// </summary>
    internal class McpToolCallResult
    {
        [JsonProperty("content")]
        public List<McpContentBlock> Content { get; set; }

        [JsonProperty("isError", DefaultValueHandling = DefaultValueHandling.Ignore)]
        public bool IsError { get; set; }
    }
}
