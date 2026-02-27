using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using MCPForUnity.Editor.Services;
using MCPForUnity.Editor.Tools;
using NativeMcp.Editor.Protocol;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine;

namespace NativeMcp.Editor.Bridge
{
    /// <summary>
    /// Bridges existing <see cref="McpForUnityToolAttribute"/> tool classes
    /// to the MCP protocol format. Dispatches tool calls through
    /// <see cref="CommandRegistry"/> with main-thread marshaling.
    /// </summary>
    internal class UnityToolBridge
    {
        private readonly ToolDiscoveryService _discovery;
        private static SynchronizationContext _mainThreadContext;
        private static int _mainThreadId;

        static UnityToolBridge()
        {
            // Capture the Unity main thread context (static ctor runs on main thread
            // because this class is instantiated from [InitializeOnLoad] code path).
            _mainThreadContext = SynchronizationContext.Current;
            _mainThreadId = Thread.CurrentThread.ManagedThreadId;
        }

        public UnityToolBridge()
        {
            _discovery = new ToolDiscoveryService();
            // Ensure CommandRegistry is initialized
            CommandRegistry.Initialize();
        }

        /// <summary>
        /// Discover all enabled tools and convert them to MCP tool definitions.
        /// </summary>
        public List<McpToolDefinition> GetMcpToolDefinitions()
        {
            var enabledTools = _discovery.GetEnabledTools();
            var definitions = new List<McpToolDefinition>(enabledTools.Count);

            foreach (var tool in enabledTools)
            {
                definitions.Add(ConvertToMcpTool(tool));
            }

            return definitions;
        }

        /// <summary>
        /// Execute a tool by name with the given arguments.
        /// Marshals the call to the Unity main thread via SynchronizationContext.
        /// </summary>
        public async Task<McpToolCallResult> ExecuteToolAsync(
            string toolName, JObject arguments, CancellationToken ct)
        {
            try
            {
                // CommandRegistry.InvokeCommandAsync must run on the Unity main thread
                // because tool handlers access Unity APIs.
                object rawResult = await RunOnMainThreadAsync(() =>
                    CommandRegistry.InvokeCommandAsync(toolName, arguments ?? new JObject()), ct);

                return WrapResult(rawResult);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                Debug.LogError($"[NativeMcp] Tool execution error for '{toolName}': {ex}");
                return new McpToolCallResult
                {
                    IsError = true,
                    Content = new List<McpContentBlock>
                    {
                        new McpContentBlock { Type = "text", Text = $"Error: {ex.Message}" }
                    }
                };
            }
        }

        /// <summary>
        /// Marshal a function that returns Task&lt;object&gt; to the Unity main thread.
        /// </summary>
        private static async Task<object> RunOnMainThreadAsync(
            Func<Task<object>> func, CancellationToken ct)
        {
            // If already on the main thread, just execute directly
            if (Thread.CurrentThread.ManagedThreadId == _mainThreadId)
            {
                return await func();
            }

            var tcs = new TaskCompletionSource<object>(TaskCreationOptions.RunContinuationsAsynchronously);

            using (ct.Register(() => tcs.TrySetCanceled(ct)))
            {
                // Post to the Unity main thread via SynchronizationContext
                if (_mainThreadContext != null)
                {
                    _mainThreadContext.Post(async _ =>
                    {
                        try
                        {
                            var result = await func();
                            tcs.TrySetResult(result);
                        }
                        catch (Exception ex)
                        {
                            tcs.TrySetException(ex);
                        }
                    }, null);
                }
                else
                {
                    // Fallback: schedule on EditorApplication.update
                    void OnUpdate()
                    {
                        EditorApplication.update -= OnUpdate;
                        Task.Run(async () =>
                        {
                            try
                            {
                                // This isn't perfect but serves as a fallback
                                var result = await func();
                                tcs.TrySetResult(result);
                            }
                            catch (Exception ex)
                            {
                                tcs.TrySetException(ex);
                            }
                        });
                    }
                    EditorApplication.update += OnUpdate;
                }

                // Nudge Unity to process the queue
                try { EditorApplication.QueuePlayerLoopUpdate(); } catch { }

                return await tcs.Task;
            }
        }

        private static McpToolDefinition ConvertToMcpTool(ToolMetadata tool)
        {
            // Build JSON Schema for inputSchema
            var properties = new JObject();
            var required = new JArray();

            if (tool.Parameters != null)
            {
                foreach (var param in tool.Parameters)
                {
                    var propSchema = new JObject
                    {
                        ["type"] = param.Type ?? "string",
                        ["description"] = param.Description ?? ""
                    };

                    if (!string.IsNullOrEmpty(param.DefaultValue))
                    {
                        propSchema["default"] = param.DefaultValue;
                    }

                    properties[param.Name] = propSchema;

                    if (param.Required)
                    {
                        required.Add(param.Name);
                    }
                }
            }

            var inputSchema = new JObject
            {
                ["type"] = "object",
                ["properties"] = properties
            };

            if (required.Count > 0)
            {
                inputSchema["required"] = required;
            }

            return new McpToolDefinition
            {
                Name = tool.Name,
                Description = tool.Description ?? $"Unity tool: {tool.Name}",
                InputSchema = inputSchema
            };
        }

        private static McpToolCallResult WrapResult(object rawResult)
        {
            if (rawResult == null)
            {
                return new McpToolCallResult
                {
                    Content = new List<McpContentBlock>
                    {
                        new McpContentBlock { Type = "text", Text = "(no result)" }
                    }
                };
            }

            // If it's already a string, try to parse for error info
            string text;
            bool isError = false;

            if (rawResult is string str)
            {
                text = str;
            }
            else
            {
                // Serialize the result object to JSON
                try
                {
                    text = JsonConvert.SerializeObject(rawResult, Formatting.Indented);

                    // Check if the result contains an error status
                    if (rawResult is JObject jObj)
                    {
                        isError = jObj["status"]?.ToString()?.Equals("error",
                            StringComparison.OrdinalIgnoreCase) == true;
                    }
                }
                catch
                {
                    text = rawResult.ToString();
                }
            }

            return new McpToolCallResult
            {
                IsError = isError,
                Content = new List<McpContentBlock>
                {
                    new McpContentBlock { Type = "text", Text = text }
                }
            };
        }
    }
}
