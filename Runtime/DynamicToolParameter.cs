namespace NativeMcp.Runtime
{
    /// <summary>
    /// Describes a parameter for a dynamically registered MCP tool.
    /// </summary>
    public class DynamicToolParameter
    {
        /// <summary>
        /// Parameter name (used as the JSON key).
        /// </summary>
        public string Name { get; set; }

        /// <summary>
        /// Human-readable description for the LLM.
        /// </summary>
        public string Description { get; set; }

        /// <summary>
        /// JSON Schema type: "string", "integer", "number", "boolean", "object", "array".
        /// Defaults to "string".
        /// </summary>
        public string Type { get; set; } = "string";

        /// <summary>
        /// Whether this parameter is required. Defaults to false.
        /// </summary>
        public bool Required { get; set; }

        /// <summary>
        /// Optional default value (as string).
        /// </summary>
        public string DefaultValue { get; set; }

        public DynamicToolParameter() { }

        public DynamicToolParameter(string name, string description, string type = "string", bool required = false)
        {
            Name = name;
            Description = description;
            Type = type;
            Required = required;
        }
    }
}
