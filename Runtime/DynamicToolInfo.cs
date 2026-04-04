using System;
using System.Collections.Generic;

namespace LambdaLabs.UnityRepl.Runtime
{
    /// <summary>
    /// Holds metadata and the handler delegate for a dynamically registered repl tool.
    /// </summary>
    public class DynamicToolInfo
    {
        /// <summary>
        /// Unique tool name (snake_case recommended, e.g. "spawn_enemy").
        /// </summary>
        public string Name { get; set; }

        /// <summary>
        /// Human-readable description shown to the LLM.
        /// </summary>
        public string Description { get; set; }

        /// <summary>
        /// Parameter definitions for this tool.
        /// </summary>
        public DynamicToolParameter[] Parameters { get; set; }

        /// <summary>
        /// The handler function.  Receives a dictionary of argument name → value.
        /// Return value will be serialized as JSON and sent back to the repl client.
        /// </summary>
        public Func<Dictionary<string, object>, object> Handler { get; set; }

        /// <summary>
        /// UTC timestamp when this tool was registered.
        /// </summary>
        public DateTime RegisteredAt { get; set; }
    }
}
