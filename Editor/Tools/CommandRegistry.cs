using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using NativeMcp.Editor.Helpers;
using NativeMcp.Editor.Resources;
using NativeMcp.Editor.Services;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace NativeMcp.Editor.Tools
{
    /// <summary>
    /// Holds information about a registered resource handler.
    /// </summary>
    class ResourceHandlerInfo
    {
        public string CommandName { get; }
        public Func<JObject, object> SyncHandler { get; }
        public Func<JObject, Task<object>> AsyncHandler { get; }

        public bool IsAsync => AsyncHandler != null;

        public ResourceHandlerInfo(string commandName, Func<JObject, object> syncHandler, Func<JObject, Task<object>> asyncHandler)
        {
            CommandName = commandName;
            SyncHandler = syncHandler;
            AsyncHandler = asyncHandler;
        }
    }

    /// <summary>
    /// Registry for all MCP command handlers.
    /// Tool handlers are looked up from <see cref="IToolDiscoveryService"/>.
    /// Resource handlers are discovered separately via reflection.
    /// </summary>
    public static class CommandRegistry
    {
        private static readonly Dictionary<string, ResourceHandlerInfo> _resourceHandlers = new();
        private static IToolDiscoveryService _discovery;
        private static bool _initialized = false;

        /// <summary>
        /// Initialize with a tool discovery service and auto-discover resources.
        /// </summary>
        public static void Initialize(IToolDiscoveryService discovery)
        {
            if (_initialized) return;

            _discovery = discovery ?? throw new ArgumentNullException(nameof(discovery));

            // Ensure tools are discovered (this also binds handler delegates)
            _discovery.DiscoverAllTools();

            // Discover resources separately
            DiscoverResources();

            _initialized = true;
        }

        /// <summary>
        /// Get a synchronous command handler by name.
        /// Throws if the command is asynchronous.
        /// </summary>
        public static Func<JObject, object> GetHandler(string commandName)
        {
            // Try tool first
            var tool = _discovery?.GetToolMetadata(commandName);
            if (tool != null)
            {
                if (tool.IsAsync)
                {
                    throw new InvalidOperationException(
                        $"Command '{commandName}' is asynchronous and must be executed via ExecuteCommand"
                    );
                }
                if (tool.SyncHandler == null)
                {
                    throw new InvalidOperationException($"Handler for '{commandName}' does not provide a synchronous implementation");
                }
                return tool.SyncHandler;
            }

            // Try resource
            var resource = GetResourceHandler(commandName);
            if (resource.IsAsync)
            {
                throw new InvalidOperationException(
                    $"Command '{commandName}' is asynchronous and must be executed via ExecuteCommand"
                );
            }
            return resource.SyncHandler;
        }

        /// <summary>
        /// Execute a command handler, supporting both synchronous and asynchronous handlers.
        /// </summary>
        public static object ExecuteCommand(string commandName, JObject @params, TaskCompletionSource<string> tcs)
        {
            // Try tool first
            var tool = _discovery?.GetToolMetadata(commandName);
            if (tool != null && (tool.SyncHandler != null || tool.AsyncHandler != null))
            {
                if (tool.IsAsync)
                {
                    ExecuteAsyncHandler(tool.AsyncHandler, @params, commandName, tcs);
                    return null;
                }
                if (tool.SyncHandler == null)
                {
                    throw new InvalidOperationException($"Handler for '{commandName}' does not provide a synchronous implementation");
                }
                return tool.SyncHandler(@params);
            }

            // Try resource
            var resource = GetResourceHandler(commandName);
            if (resource.IsAsync)
            {
                ExecuteAsyncHandler(resource.AsyncHandler, @params, commandName, tcs);
                return null;
            }
            if (resource.SyncHandler == null)
            {
                throw new InvalidOperationException($"Handler for '{commandName}' does not provide a synchronous implementation");
            }
            return resource.SyncHandler(@params);
        }

        /// <summary>
        /// Execute a command handler and return its raw result, regardless of sync or async implementation.
        /// </summary>
        public static Task<object> InvokeCommandAsync(string commandName, JObject @params)
        {
            var payload = @params ?? new JObject();

            // Try tool first
            var tool = _discovery?.GetToolMetadata(commandName);
            if (tool != null && (tool.SyncHandler != null || tool.AsyncHandler != null))
            {
                if (tool.IsAsync)
                {
                    if (tool.AsyncHandler == null)
                        throw new InvalidOperationException($"Async handler for '{commandName}' is not configured correctly");
                    return tool.AsyncHandler(payload);
                }
                if (tool.SyncHandler == null)
                    throw new InvalidOperationException($"Handler for '{commandName}' does not provide a synchronous implementation");
                return Task.FromResult(tool.SyncHandler(payload));
            }

            // Try resource
            var resource = GetResourceHandler(commandName);
            if (resource.IsAsync)
            {
                if (resource.AsyncHandler == null)
                    throw new InvalidOperationException($"Async handler for '{commandName}' is not configured correctly");
                return resource.AsyncHandler(payload);
            }
            if (resource.SyncHandler == null)
                throw new InvalidOperationException($"Handler for '{commandName}' does not provide a synchronous implementation");
            return Task.FromResult(resource.SyncHandler(payload));
        }

        private static ResourceHandlerInfo GetResourceHandler(string commandName)
        {
            if (!_resourceHandlers.TryGetValue(commandName, out var handler))
            {
                throw new InvalidOperationException(
                    $"Unknown or unsupported command type: {commandName}"
                );
            }
            return handler;
        }

        /// <summary>
        /// Discover resource handlers via reflection.
        /// </summary>
        private static void DiscoverResources()
        {
            try
            {
                var allTypes = AppDomain.CurrentDomain.GetAssemblies()
                    .Where(a => !a.IsDynamic)
                    .SelectMany(a =>
                    {
                        try { return a.GetTypes(); }
                        catch { return new Type[0]; }
                    })
                    .ToList();

                var resourceTypes = allTypes.Where(t => t.GetCustomAttribute<McpForUnityResourceAttribute>() != null);
                int resourceCount = 0;
                foreach (var type in resourceTypes)
                {
                    if (RegisterResource(type))
                        resourceCount++;
                }

                McpLog.Info($"Auto-discovered {resourceCount} resources");
            }
            catch (Exception ex)
            {
                McpLog.Error($"Failed to auto-discover MCP resources: {ex.Message}");
            }
        }

        private static bool RegisterResource(Type type)
        {
            var resourceAttr = type.GetCustomAttribute<McpForUnityResourceAttribute>();
            string commandName = resourceAttr.ResourceName;

            if (string.IsNullOrEmpty(commandName))
            {
                commandName = NamingConventions.ToSnakeCase(type.Name);
            }

            var method = type.GetMethod(
                "HandleCommand",
                BindingFlags.Public | BindingFlags.Static,
                null,
                new[] { typeof(JObject) },
                null
            );

            if (method == null)
            {
                McpLog.Warn(
                    $"MCP resource {type.Name} is marked with [McpForUnityResource] " +
                    $"but has no public static HandleCommand(JObject) method"
                );
                return false;
            }

            try
            {
                ResourceHandlerInfo handlerInfo;

                if (typeof(Task).IsAssignableFrom(method.ReturnType))
                {
                    var asyncHandler = CreateAsyncHandlerDelegate(method, commandName);
                    handlerInfo = new ResourceHandlerInfo(commandName, null, asyncHandler);
                }
                else
                {
                    var handler = (Func<JObject, object>)Delegate.CreateDelegate(
                        typeof(Func<JObject, object>),
                        method
                    );
                    handlerInfo = new ResourceHandlerInfo(commandName, handler, null);
                }

                _resourceHandlers[commandName] = handlerInfo;
                return true;
            }
            catch (Exception ex)
            {
                McpLog.Error($"Failed to register resource {type.Name}: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Create a delegate for an async handler method that returns Task or Task&lt;T&gt;.
        /// </summary>
        private static Func<JObject, Task<object>> CreateAsyncHandlerDelegate(MethodInfo method, string commandName)
        {
            return async (JObject parameters) =>
            {
                object rawResult;

                try
                {
                    rawResult = method.Invoke(null, new object[] { parameters });
                }
                catch (TargetInvocationException ex)
                {
                    throw ex.InnerException ?? ex;
                }

                if (rawResult == null)
                {
                    return null;
                }

                if (rawResult is not Task task)
                {
                    throw new InvalidOperationException(
                        $"Async handler '{commandName}' returned an object that is not a Task"
                    );
                }

                await task.ConfigureAwait(true);

                var taskType = task.GetType();
                if (taskType.IsGenericType)
                {
                    var resultProperty = taskType.GetProperty("Result");
                    if (resultProperty != null)
                    {
                        return resultProperty.GetValue(task);
                    }
                }

                return null;
            };
        }

        private static void ExecuteAsyncHandler(
            Func<JObject, Task<object>> asyncHandler,
            JObject parameters,
            string commandName,
            TaskCompletionSource<string> tcs)
        {
            if (asyncHandler == null)
            {
                throw new InvalidOperationException($"Async handler for '{commandName}' is not configured correctly");
            }

            Task<object> handlerTask;

            try
            {
                handlerTask = asyncHandler(parameters);
            }
            catch (Exception ex)
            {
                ReportAsyncFailure(commandName, tcs, ex);
                return;
            }

            if (handlerTask == null)
            {
                CompleteAsyncCommand(commandName, tcs, null);
                return;
            }

            async void AwaitHandler()
            {
                try
                {
                    var finalResult = await handlerTask.ConfigureAwait(true);
                    CompleteAsyncCommand(commandName, tcs, finalResult);
                }
                catch (Exception ex)
                {
                    ReportAsyncFailure(commandName, tcs, ex);
                }
            }

            AwaitHandler();
        }

        private static void CompleteAsyncCommand(string commandName, TaskCompletionSource<string> tcs, object result)
        {
            try
            {
                var response = new { status = "success", result };
                string json = JsonConvert.SerializeObject(response);

                if (!tcs.TrySetResult(json))
                {
                    McpLog.Warn($"TCS for async command '{commandName}' was already completed");
                }
            }
            catch (Exception ex)
            {
                McpLog.Error($"Error completing async command '{commandName}': {ex.Message}\n{ex.StackTrace}");
                ReportAsyncFailure(commandName, tcs, ex);
            }
        }

        private static void ReportAsyncFailure(string commandName, TaskCompletionSource<string> tcs, Exception ex)
        {
            McpLog.Error($"Error in async command '{commandName}': {ex.Message}\n{ex.StackTrace}");

            var errorResponse = new
            {
                status = "error",
                error = ex.Message,
                command = commandName,
                stackTrace = ex.StackTrace
            };

            string json;
            try
            {
                json = JsonConvert.SerializeObject(errorResponse);
            }
            catch (Exception serializationEx)
            {
                McpLog.Error($"Failed to serialize error response for '{commandName}': {serializationEx.Message}");
                json = "{\"status\":\"error\",\"error\":\"Failed to complete command\"}";
            }

            if (!tcs.TrySetResult(json))
            {
                McpLog.Warn($"TCS for async command '{commandName}' was already completed when trying to report error");
            }
        }
    }
}
