using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;

namespace LambdaLabs.UnityRepl.Runtime
{
    /// <summary>
    /// Static registry for dynamically registered repl tools.
    /// Game code can register/unregister functions at runtime, making them
    /// callable by AI via the <c>invoke_dynamic</c> repl tool.
    ///
    /// This class lives in the Runtime assembly so both Editor and game
    /// assemblies can reference it without circular dependencies.
    /// </summary>
    /// <example>
    /// <code>
    /// // Register a simple test function
    /// DynamicToolRegistry.Register(
    ///     "spawn_enemy",
    ///     "Spawn an enemy at the specified position for testing",
    ///     args =>
    ///     {
    ///         float x = Convert.ToSingle(args.GetValueOrDefault("x", 0f));
    ///         float y = Convert.ToSingle(args.GetValueOrDefault("y", 0f));
    ///         float z = Convert.ToSingle(args.GetValueOrDefault("z", 0f));
    ///         var go = GameObject.Instantiate(enemyPrefab, new Vector3(x, y, z), Quaternion.identity);
    ///         return new { success = true, name = go.name, position = new { x, y, z } };
    ///     },
    ///     new[]
    ///     {
    ///         new DynamicToolParameter("x", "X position", "number"),
    ///         new DynamicToolParameter("y", "Y position", "number"),
    ///         new DynamicToolParameter("z", "Z position", "number"),
    ///     }
    /// );
    ///
    /// // Later, unregister
    /// DynamicToolRegistry.Unregister("spawn_enemy");
    /// </code>
    /// </example>
    public static class DynamicToolRegistry
    {
        private static readonly Dictionary<string, DynamicToolInfo> _tools = new();

        /// <summary>
        /// Fired when a tool is registered or unregistered.
        /// </summary>
        public static event Action OnRegistryChanged;

        // ────────────────────────────────────────────────────────────
        //  Registration
        // ────────────────────────────────────────────────────────────

        /// <summary>
        /// Register a function as a dynamic repl tool.
        /// </summary>
        /// <param name="name">Unique tool name (snake_case recommended).</param>
        /// <param name="description">Description shown to the LLM.</param>
        /// <param name="handler">
        ///     Handler function. Receives args dict, returns an object that will be
        ///     serialized as JSON. Return anonymous objects or dictionaries.
        /// </param>
        /// <param name="parameters">Optional parameter descriptors.</param>
        public static void Register(
            string name,
            string description,
            Func<Dictionary<string, object>, object> handler,
            DynamicToolParameter[] parameters = null)
        {
            if (string.IsNullOrWhiteSpace(name))
                throw new ArgumentException("Tool name cannot be null or empty", nameof(name));
            if (handler == null)
                throw new ArgumentNullException(nameof(handler));

            var info = new DynamicToolInfo
            {
                Name = name,
                Description = description ?? $"Dynamic tool: {name}",
                Parameters = parameters ?? Array.Empty<DynamicToolParameter>(),
                Handler = handler,
                RegisteredAt = DateTime.UtcNow
            };

            bool isOverride = _tools.ContainsKey(name);
            _tools[name] = info;

            Debug.Log($"[DynamicToolRegistry] {(isOverride ? "Updated" : "Registered")} dynamic tool: {name}");
            OnRegistryChanged?.Invoke();
        }

        /// <summary>
        /// Register a method on a target object as a dynamic repl tool.
        /// Uses reflection to invoke the method. Supports both parameterless
        /// methods and methods that accept a <c>Dictionary&lt;string, object&gt;</c>.
        /// </summary>
        /// <param name="name">Unique tool name.</param>
        /// <param name="description">Description shown to the LLM.</param>
        /// <param name="target">The object instance to invoke the method on.</param>
        /// <param name="methodName">Name of the method to invoke.</param>
        /// <param name="parameters">Optional parameter descriptors.</param>
        public static void RegisterMethod(
            string name,
            string description,
            object target,
            string methodName,
            DynamicToolParameter[] parameters = null)
        {
            if (target == null) throw new ArgumentNullException(nameof(target));
            if (string.IsNullOrWhiteSpace(methodName))
                throw new ArgumentException("Method name cannot be null or empty", nameof(methodName));

            var type = target.GetType();
            var method = type.GetMethod(methodName,
                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);

            if (method == null)
            {
                throw new InvalidOperationException(
                    $"Method '{methodName}' not found on type {type.FullName}");
            }

            var methodParams = method.GetParameters();
            Func<Dictionary<string, object>, object> handler;

            if (methodParams.Length == 0)
            {
                // Parameterless method
                handler = _ => method.Invoke(target, null);
            }
            else if (methodParams.Length == 1
                     && methodParams[0].ParameterType == typeof(Dictionary<string, object>))
            {
                // Method that takes a Dictionary<string, object>
                handler = args => method.Invoke(target, new object[] { args });
            }
            else
            {
                throw new InvalidOperationException(
                    $"Method '{methodName}' must be parameterless or accept " +
                    $"Dictionary<string, object>. Found: ({string.Join(", ", Array.ConvertAll(methodParams, p => p.ParameterType.Name))})");
            }

            Register(name, description, handler, parameters);
        }

        // ────────────────────────────────────────────────────────────
        //  Unregistration
        // ────────────────────────────────────────────────────────────

        /// <summary>
        /// Unregister a dynamic tool by name.
        /// Returns true if the tool existed and was removed.
        /// </summary>
        public static bool Unregister(string name)
        {
            if (string.IsNullOrWhiteSpace(name))
                return false;

            bool removed = _tools.Remove(name);
            if (removed)
            {
                Debug.Log($"[DynamicToolRegistry] Unregistered dynamic tool: {name}");
                OnRegistryChanged?.Invoke();
            }

            return removed;
        }

        /// <summary>
        /// Remove all dynamic tools.
        /// </summary>
        public static void UnregisterAll()
        {
            int count = _tools.Count;
            _tools.Clear();

            if (count > 0)
            {
                Debug.Log($"[DynamicToolRegistry] Cleared all {count} dynamic tools");
                OnRegistryChanged?.Invoke();
            }
        }

        // ────────────────────────────────────────────────────────────
        //  Query
        // ────────────────────────────────────────────────────────────

        /// <summary>
        /// Get all registered dynamic tools.
        /// </summary>
        public static DynamicToolInfo[] GetAll()
        {
            var result = new DynamicToolInfo[_tools.Count];
            _tools.Values.CopyTo(result, 0);
            return result;
        }

        /// <summary>
        /// Get a specific dynamic tool by name. Returns null if not found.
        /// </summary>
        public static DynamicToolInfo Get(string name)
        {
            if (string.IsNullOrWhiteSpace(name))
                return null;

            return _tools.TryGetValue(name, out var info) ? info : null;
        }

        /// <summary>
        /// Check if a dynamic tool with the given name exists.
        /// </summary>
        public static bool Contains(string name)
        {
            return !string.IsNullOrWhiteSpace(name) && _tools.ContainsKey(name);
        }

        /// <summary>
        /// Get the number of registered dynamic tools.
        /// </summary>
        public static int Count => _tools.Count;

        // ────────────────────────────────────────────────────────────
        //  Invocation
        // ────────────────────────────────────────────────────────────

        /// <summary>
        /// Invoke a registered dynamic tool by name.
        /// </summary>
        /// <param name="name">Tool name.</param>
        /// <param name="args">Arguments dictionary. May be null for parameterless tools.</param>
        /// <returns>The return value from the handler.</returns>
        /// <exception cref="KeyNotFoundException">If no tool with that name is registered.</exception>
        public static object Invoke(string name, Dictionary<string, object> args)
        {
            if (!_tools.TryGetValue(name, out var info))
            {
                throw new KeyNotFoundException(
                    $"No dynamic tool registered with name '{name}'. " +
                    $"Available: [{string.Join(", ", _tools.Keys)}]");
            }

            return info.Handler(args ?? new Dictionary<string, object>());
        }
    }
}
