using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;

namespace NativeMcp.Editor.Services
{
    /// <summary>
    /// Metadata for a discovered tool
    /// </summary>
    public class ToolMetadata
    {
        public string Name { get; set; }
        public string Description { get; set; }
        public bool StructuredOutput { get; set; }
        public List<ParameterMetadata> Parameters { get; set; }
        public string ClassName { get; set; }
        public string Namespace { get; set; }
        public string AssemblyName { get; set; }
        public bool AutoRegister { get; set; } = true;
        public bool RequiresPolling { get; set; } = false;
        public string PollAction { get; set; } = "status";
        public bool IsBuiltIn { get; set; }

        /// <summary>Handler delegate (sync). Null if the handler is async.</summary>
        public Func<JObject, object> SyncHandler { get; set; }

        /// <summary>Handler delegate (async). Null if the handler is sync.</summary>
        public Func<JObject, Task<object>> AsyncHandler { get; set; }

        /// <summary>True when the handler is asynchronous.</summary>
        public bool IsAsync => AsyncHandler != null;
    }

    /// <summary>
    /// Metadata for a tool parameter
    /// </summary>
    public class ParameterMetadata
    {
        public string Name { get; set; }
        public string Description { get; set; }
        public string Type { get; set; }  // "string", "int", "bool", "float", etc.
        public bool Required { get; set; }
        public string DefaultValue { get; set; }
    }

    /// <summary>
    /// Service for discovering MCP tools via reflection
    /// </summary>
    public interface IToolDiscoveryService
    {
        /// <summary>
        /// Discovers all tools marked with [McpForUnityTool]
        /// </summary>
        List<ToolMetadata> DiscoverAllTools();

        /// <summary>
        /// Gets metadata for a specific tool
        /// </summary>
        ToolMetadata GetToolMetadata(string toolName);

        /// <summary>
        /// Returns only the tools currently enabled for registration
        /// </summary>
        List<ToolMetadata> GetEnabledTools();

        /// <summary>
        /// Checks whether a tool is currently enabled for registration
        /// </summary>
        bool IsToolEnabled(string toolName);

        /// <summary>
        /// Updates the enabled state for a tool
        /// </summary>
        void SetToolEnabled(string toolName, bool enabled);

        /// <summary>
        /// Invalidates the tool discovery cache
        /// </summary>
        void InvalidateCache();
    }
}
