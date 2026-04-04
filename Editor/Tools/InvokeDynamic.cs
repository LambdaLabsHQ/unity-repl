using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using NativeMcp.Editor.Helpers;
using NativeMcp.Runtime;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine;

namespace NativeMcp.Editor.Tools
{
    /// <summary>
    /// MCP tool: reflect-invoke any method/property, or call registered dynamic tools.
    ///
    /// Two-step reflection workflow:
    ///   - resolve_method: fuzzy search, returns candidate list of methods/properties.
    ///   - call_method:    exact match, executes directly.
    ///
    /// Legacy actions (registered dynamic tools):
    ///   - list / call / describe
    /// </summary>
    [McpForUnityTool("invoke_dynamic", Internal = true,
        Description = "Reflect-invoke any method or access any property. Two-step workflow: " +
                      "1) action='resolve_method' with method='Type.Member' to inspect candidates (use 'Type.*' to list all). " +
                      "2) action='call_method' with method='Type.ExactName' to execute. " +
                      "Supports static/instance methods, properties. " +
                      "Instance methods on MonoBehaviours are auto-located in the scene. " +
                      "Use instance_id or game_object to target a specific instance when multiple exist. " +
                      "Also supports action='list'/'call'/'describe' for pre-registered dynamic tools.")]
    public static class InvokeDynamic
    {
        private const BindingFlags ReflectionFlags =
            BindingFlags.Public | BindingFlags.NonPublic |
            BindingFlags.Static | BindingFlags.Instance |
            BindingFlags.FlattenHierarchy;

        private static readonly object CacheLock = new();
        private static readonly Dictionary<string, Type> ResolvedTypeCache = new(StringComparer.OrdinalIgnoreCase);
        private static readonly HashSet<string> MissingTypeCache = new(StringComparer.OrdinalIgnoreCase);
        private static readonly Dictionary<Type, MethodInfo[]> CachedMethodsByType = new();
        private static readonly Dictionary<Type, PropertyInfo[]> CachedPropertiesByType = new();

        public class Parameters
        {
            [ToolParameter("Action: 'resolve_method' (search candidates), 'call_method' (execute), " +
                           "'list', 'call', or 'describe'")]
            public string action { get; set; }

            [ToolParameter("Method/property path, e.g. 'MyClass.MyMethod' or 'Namespace.MyClass.myProp'.", Required = false)]
            public string method { get; set; }

            [ToolParameter("Name of a registered dynamic function (for 'call'/'describe' actions)", Required = false)]
            public string function_name { get; set; }

            [ToolParameter("JSON object of arguments. Keys mapped to parameter names.", Required = false)]
            public string args { get; set; }

            [ToolParameter("Instance ID of the target object (from get_scene_tree or get_hierarchy). " +
                           "Targets a specific instance when multiple exist.", Required = false)]
            public int? instance_id { get; set; }

            [ToolParameter("GameObject name or hierarchy path to find the target instance on. " +
                           "Alternative to instance_id.", Required = false)]
            public string game_object { get; set; }
        }

        public static object HandleCommand(JObject @params)
        {
            string action = @params["action"]?.ToString()?.ToLowerInvariant();

            if (string.IsNullOrWhiteSpace(action))
            {
                return new ErrorResponse(
                    "Required parameter 'action' is missing. " +
                    "Use 'resolve_method', 'call_method', 'list', 'call', or 'describe'.");
            }

            switch (action)
            {
                case "resolve_method":
                case "resolve":
                    return HandleResolveMethod(@params);

                case "call_method":
                    return HandleCallMethod(@params);

                case "list":
                    return HandleList();

                case "call":
                    return HandleCall(@params);

                case "describe":
                    return HandleDescribe(@params);

                default:
                    return new ErrorResponse(
                        $"Unknown action '{action}'. " +
                        "Supported: 'resolve_method', 'call_method', 'list', 'call', 'describe'.");
            }
        }

        // ────────────────────────────────────────────────────────────
        //  resolve_method — fuzzy search, return candidate list
        // ────────────────────────────────────────────────────────────

        private static object HandleResolveMethod(JObject @params)
        {
            string methodPath = @params["method"]?.ToString();
            if (string.IsNullOrWhiteSpace(methodPath))
                return new ErrorResponse("Required parameter 'method' is missing. Format: 'TypeName.MemberName'.");

            int lastDot = methodPath.LastIndexOf('.');
            if (lastDot <= 0)
                return new ErrorResponse($"Invalid format '{methodPath}'. Expected 'TypeName.MemberName'.");

            string typePart = methodPath.Substring(0, lastDot);
            string memberName = methodPath.Substring(lastDot + 1);

            Type targetType = ResolveType(typePart);
            if (targetType == null)
                return new ErrorResponse($"Type '{typePart}' not found. Try full name like 'Namespace.ClassName'.");

            // Wildcard: "Type.*" or "Type." lists all members
            if (memberName == "*" || memberName == "")
                return HandleListAllMembers(targetType);

            var candidates = new List<object>();

            // Properties (case-insensitive)
            foreach (var p in GetPropertiesCached(targetType))
            {
                if (!string.Equals(p.Name, memberName, StringComparison.OrdinalIgnoreCase))
                    continue;
                candidates.Add(new
                {
                    kind = "property",
                    name = p.Name,
                    type = p.PropertyType.Name,
                    is_static = p.GetGetMethod(true)?.IsStatic ?? p.GetSetMethod(true)?.IsStatic ?? false,
                    can_read = p.CanRead,
                    can_write = p.CanWrite,
                    call_as = $"{targetType.Name}.{p.Name}"
                });
            }

            // Methods (case-insensitive, skip property accessors)
            foreach (var m in GetMethodsCached(targetType)
                .Where(m => string.Equals(m.Name, memberName, StringComparison.OrdinalIgnoreCase) && !m.IsSpecialName))
            {
                candidates.Add(new
                {
                    kind = "method",
                    name = m.Name,
                    signature = $"{m.Name}({string.Join(", ", m.GetParameters().Select(p => $"{p.ParameterType.Name} {p.Name}"))})",
                    return_type = m.ReturnType.Name,
                    is_static = m.IsStatic,
                    parameters = m.GetParameters().Select(p => new
                    {
                        name = p.Name,
                        type = p.ParameterType.Name,
                        has_default = p.HasDefaultValue,
                        default_value = p.HasDefaultValue ? p.DefaultValue?.ToString() : null
                    }).ToArray(),
                    call_as = $"{targetType.Name}.{m.Name}"
                });
            }

            if (candidates.Count == 0)
                return new ErrorResponse($"No members matching '{memberName}' on '{targetType.FullName}'.");

            return new SuccessResponse(
                $"Found {candidates.Count} candidate(s) for '{memberName}' on {targetType.Name}. " +
                "Use action='call_method' with the exact name to execute.",
                new { type = targetType.FullName, query = memberName, candidates });
        }

        // ────────────────────────────────────────────────────────────
        //  list all members — wildcard resolve
        // ────────────────────────────────────────────────────────────

        private static object HandleListAllMembers(Type targetType)
        {
            const int maxProperties = 50;
            const int maxMethods = 50;

            var properties = new List<object>();
            bool propsTruncated = false;

            foreach (var p in GetPropertiesCached(targetType))
            {
                if (properties.Count >= maxProperties) { propsTruncated = true; break; }
                properties.Add(new
                {
                    kind = "property",
                    name = p.Name,
                    type = p.PropertyType.Name,
                    is_static = p.GetGetMethod(true)?.IsStatic ?? p.GetSetMethod(true)?.IsStatic ?? false,
                    can_read = p.CanRead,
                    can_write = p.CanWrite,
                    call_as = $"{targetType.Name}.{p.Name}"
                });
            }

            var methods = new List<object>();
            bool methodsTruncated = false;
            var seen = new HashSet<string>(StringComparer.Ordinal);

            foreach (var m in GetMethodsCached(targetType).Where(m => !m.IsSpecialName))
            {
                if (!seen.Add(m.Name)) continue; // skip overloads (use exact resolve to see all)
                if (methods.Count >= maxMethods) { methodsTruncated = true; break; }
                methods.Add(new
                {
                    kind = "method",
                    name = m.Name,
                    signature = $"{m.Name}({string.Join(", ", m.GetParameters().Select(p => $"{p.ParameterType.Name} {p.Name}"))})",
                    return_type = m.ReturnType.Name,
                    is_static = m.IsStatic,
                    call_as = $"{targetType.Name}.{m.Name}"
                });
            }

            bool truncated = propsTruncated || methodsTruncated;
            return new SuccessResponse(
                $"{targetType.Name}: {properties.Count} properties, {methods.Count} methods" +
                (truncated ? " (truncated, use 'Type.Name' for specific member)" : "") +
                ". Use action='call_method' with the exact call_as value to execute.",
                new
                {
                    type = targetType.FullName,
                    properties,
                    methods,
                    truncated
                });
        }

        // ────────────────────────────────────────────────────────────
        //  call_method — exact match, execute directly
        // ────────────────────────────────────────────────────────────

        private static object HandleCallMethod(JObject @params)
        {
            string methodPath = @params["method"]?.ToString();
            if (string.IsNullOrWhiteSpace(methodPath))
                return new ErrorResponse("Required parameter 'method' is missing. Format: 'TypeName.MethodName'.");

            int lastDot = methodPath.LastIndexOf('.');
            if (lastDot <= 0)
                return new ErrorResponse($"Invalid format '{methodPath}'. Expected 'TypeName.MethodName'.");

            string typePart = methodPath.Substring(0, lastDot);
            string memberName = methodPath.Substring(lastDot + 1);

            Type targetType = ResolveType(typePart);
            if (targetType == null)
                return new ErrorResponse($"Type '{typePart}' not found.");

            var argDict = ParseArgsFromParams(@params);
            if (argDict == null)
                return new ErrorResponse("Failed to parse 'args'.");

            // Extract instance targeting params (separate from method args)
            int? instanceId = @params["instance_id"]?.Type == JTokenType.Integer
                ? @params["instance_id"].Value<int>() : null;
            string gameObjectName = @params["game_object"]?.ToString();

            // 1) Try exact property match
            var prop = GetPropertiesCached(targetType).FirstOrDefault(p =>
                string.Equals(p.Name, memberName, StringComparison.Ordinal));
            if (prop != null)
            {
                var accessor = argDict.Count > 0 && prop.CanWrite
                    ? prop.GetSetMethod(true)
                    : prop.GetGetMethod(true);
                if (accessor == null)
                    return new ErrorResponse($"Property '{memberName}' has no {(argDict.Count > 0 ? "setter" : "getter")}.");
                return InvokeMethod(accessor, targetType, argDict, instanceId, gameObjectName);
            }

            // 2) Try exact method match
            var candidates = GetMethodsCached(targetType).Where(m => m.Name == memberName).ToArray();
            if (candidates.Length == 0)
                return new ErrorResponse($"'{memberName}' not found on '{targetType.Name}'. Use action='resolve_method' to search.");

            MethodInfo method = PickOverload(candidates, argDict);
            if (method == null)
            {
                var sigs = candidates.Select(m =>
                    $"  {m.Name}({string.Join(", ", m.GetParameters().Select(p => $"{p.ParameterType.Name} {p.Name}"))})");
                return new ErrorResponse($"No matching overload. Candidates:\n{string.Join("\n", sigs)}");
            }

            return InvokeMethod(method, targetType, argDict, instanceId, gameObjectName);
        }

        // ────────────────────────────────────────────────────────────
        //  Legacy actions (registered dynamic tools)
        // ────────────────────────────────────────────────────────────

        private static object HandleList()
        {
            var tools = DynamicToolRegistry.GetAll();

            if (tools.Length == 0)
            {
                return new SuccessResponse(
                    "No dynamic tools registered. " +
                    "Game code can register functions via DynamicToolRegistry.Register().",
                    new { count = 0, tools = Array.Empty<object>() });
            }

            var toolList = tools.Select(t => new
            {
                name = t.Name,
                description = t.Description,
                parameter_count = t.Parameters?.Length ?? 0,
                parameters = t.Parameters?.Select(p => new
                {
                    name = p.Name,
                    type = p.Type,
                    description = p.Description,
                    required = p.Required
                }).ToArray(),
                registered_at = t.RegisteredAt.ToString("O")
            }).ToArray();

            return new SuccessResponse(
                $"Found {tools.Length} registered dynamic tool(s).",
                new { count = tools.Length, tools = toolList });
        }

        private static object HandleCall(JObject @params)
        {
            string functionName = @params["function_name"]?.ToString();
            if (string.IsNullOrWhiteSpace(functionName))
            {
                return new ErrorResponse(
                    "Required parameter 'function_name' is missing for 'call' action.");
            }

            var toolInfo = DynamicToolRegistry.Get(functionName);
            if (toolInfo == null)
            {
                var available = DynamicToolRegistry.GetAll()
                    .Select(t => t.Name)
                    .ToArray();

                return new ErrorResponse(
                    $"No dynamic tool registered with name '{functionName}'. " +
                    $"Available: [{string.Join(", ", available)}]");
            }

            // Parse args
            Dictionary<string, object> args = new Dictionary<string, object>();
            var argsToken = @params["args"];
            if (argsToken != null)
            {
                JObject argsObj;
                if (argsToken.Type == JTokenType.String)
                {
                    // Args passed as a JSON string
                    try
                    {
                        argsObj = JObject.Parse(argsToken.ToString());
                    }
                    catch (Exception ex)
                    {
                        return new ErrorResponse(
                            $"Failed to parse 'args' as JSON: {ex.Message}");
                    }
                }
                else if (argsToken.Type == JTokenType.Object)
                {
                    argsObj = (JObject)argsToken;
                }
                else
                {
                    return new ErrorResponse(
                        $"Parameter 'args' must be a JSON object or JSON string, got: {argsToken.Type}");
                }

                foreach (var prop in argsObj.Properties())
                {
                    args[prop.Name] = ConvertJToken(prop.Value);
                }
            }

            // Invoke
            try
            {
                object result = DynamicToolRegistry.Invoke(functionName, args);
                return new SuccessResponse(
                    $"Successfully invoked dynamic tool '{functionName}'.",
                    new { function_name = functionName, result });
            }
            catch (Exception ex)
            {
                McpLog.Error($"[InvokeDynamic] Error invoking '{functionName}': {ex}");
                return new ErrorResponse(
                    $"Error invoking '{functionName}': {ex.Message}",
                    new { exception_type = ex.GetType().Name, stack_trace = ex.StackTrace });
            }
        }

        private static object HandleDescribe(JObject @params)
        {
            string functionName = @params["function_name"]?.ToString();
            if (string.IsNullOrWhiteSpace(functionName))
            {
                return new ErrorResponse(
                    "Required parameter 'function_name' is missing for 'describe' action.");
            }

            var toolInfo = DynamicToolRegistry.Get(functionName);
            if (toolInfo == null)
            {
                return new ErrorResponse(
                    $"No dynamic tool registered with name '{functionName}'.");
            }

            var parameterDescriptions = toolInfo.Parameters?.Select(p => new
            {
                name = p.Name,
                type = p.Type ?? "string",
                description = p.Description,
                required = p.Required,
                default_value = p.DefaultValue
            }).ToArray();

            return new SuccessResponse(
                $"Description of dynamic tool '{functionName}'.",
                new
                {
                    name = toolInfo.Name,
                    description = toolInfo.Description,
                    parameters = parameterDescriptions,
                    registered_at = toolInfo.RegisteredAt.ToString("O")
                });
        }

        // ────────────────────────────────────────────────────────────
        //  Helpers
        // ────────────────────────────────────────────────────────────

        /// <summary>
        /// Convert a JToken to a plain .NET object so handlers receive
        /// standard types (string, long, double, bool, etc.) instead of JTokens.
        /// </summary>
        private static object ConvertJToken(JToken token)
        {
            switch (token.Type)
            {
                case JTokenType.Integer:
                    return token.Value<long>();
                case JTokenType.Float:
                    return token.Value<double>();
                case JTokenType.String:
                    return token.Value<string>();
                case JTokenType.Boolean:
                    return token.Value<bool>();
                case JTokenType.Null:
                case JTokenType.Undefined:
                    return null;
                case JTokenType.Object:
                    var dict = new Dictionary<string, object>();
                    foreach (var prop in ((JObject)token).Properties())
                    {
                        dict[prop.Name] = ConvertJToken(prop.Value);
                    }
                    return dict;
                case JTokenType.Array:
                    return ((JArray)token).Select(ConvertJToken).ToArray();
                default:
                    return token.ToString();
            }
        }

        // ────────────────────────────────────────────────────────────
        //  Reflection helpers
        // ────────────────────────────────────────────────────────────

        /// <summary>
        /// Resolve a type by name. Priority: exact match → game assemblies → Unity → System.
        /// </summary>
        private static Type ResolveType(string typeName)
        {
            if (string.IsNullOrWhiteSpace(typeName))
            {
                return null;
            }

            lock (CacheLock)
            {
                if (ResolvedTypeCache.TryGetValue(typeName, out var cached))
                {
                    return cached;
                }

                if (MissingTypeCache.Contains(typeName))
                {
                    return null;
                }
            }

            // Fast path: reuse shared type resolver first.
            var resolved = UnityTypeResolver.ResolveAny(typeName);

            // Keep backward-compat behavior for legacy callers by falling back to
            // broader fuzzy matching if the shared resolver cannot find a match.
            if (resolved == null)
            {
                resolved = ResolveTypeLegacy(typeName);
            }

            lock (CacheLock)
            {
                if (resolved != null)
                {
                    ResolvedTypeCache[typeName] = resolved;
                    MissingTypeCache.Remove(typeName);
                }
                else
                {
                    MissingTypeCache.Add(typeName);
                }
            }

            return resolved;
        }

        private static Type ResolveTypeLegacy(string typeName)
        {
            // 1) Exact match
            foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
            {
                try
                {
                    var t = asm.GetType(typeName, false, true);
                    if (t != null) return t;
                }
                catch { /* skip problematic assemblies */ }
            }

            // 2) Fuzzy: priority scoring — game(3) > Unity(2) > System(1)
            Type bestMatch = null;
            int bestScore = -1;

            foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
            {
                Type[] types;
                try { types = asm.GetTypes(); }
                catch (ReflectionTypeLoadException ex) { types = ex.Types.Where(t => t != null).ToArray(); }
                catch { continue; }

                foreach (var t in types)
                {
                    bool nameMatch = t.Name == typeName;
                    bool suffixMatch = !nameMatch && t.FullName != null &&
                                       t.FullName.EndsWith("." + typeName, StringComparison.OrdinalIgnoreCase);
                    if (!nameMatch && !suffixMatch) continue;

                    var asmName = t.Assembly.FullName;
                    int asmPriority = (asmName.StartsWith("System") || asmName.StartsWith("mscorlib") ||
                                       asmName.StartsWith("netstandard")) ? 1
                                     : asmName.StartsWith("Unity") ? 2
                                     : 3;
                    int score = (nameMatch ? 100 : 0) + asmPriority;

                    if (score > bestScore)
                    {
                        bestScore = score;
                        bestMatch = t;
                    }
                }
            }

            return bestMatch;
        }

        private static MethodInfo[] GetMethodsCached(Type type)
        {
            lock (CacheLock)
            {
                if (CachedMethodsByType.TryGetValue(type, out var methods))
                {
                    return methods;
                }

                methods = type.GetMethods(ReflectionFlags);
                CachedMethodsByType[type] = methods;
                return methods;
            }
        }

        private static PropertyInfo[] GetPropertiesCached(Type type)
        {
            lock (CacheLock)
            {
                if (CachedPropertiesByType.TryGetValue(type, out var properties))
                {
                    return properties;
                }

                properties = type.GetProperties(ReflectionFlags);
                CachedPropertiesByType[type] = properties;
                return properties;
            }
        }

        /// <summary>
        /// Find a live instance with explicit targeting (instance_id or game_object name).
        /// Falls back to auto-discovery when neither is specified.
        /// </summary>
        private static object ResolveInstance(Type type, int? instanceId, string gameObjectName)
        {
            // Path 1: Exact lookup by instance ID
            if (instanceId.HasValue)
            {
                var obj = EditorUtility.InstanceIDToObject(instanceId.Value);
                if (obj == null) return null;
                if (type.IsInstanceOfType(obj)) return obj;
                if (obj is GameObject go && typeof(Component).IsAssignableFrom(type))
                    return go.GetComponent(type);
                if (obj is Component comp && typeof(Component).IsAssignableFrom(type))
                    return comp.gameObject.GetComponent(type);
                return null;
            }

            // Path 2: Find by GameObject name or hierarchy path
            if (!string.IsNullOrEmpty(gameObjectName))
            {
                var go = GameObject.Find(gameObjectName);
                if (go == null)
                {
                    var ids = GameObjectLookup.SearchGameObjects("by_name", gameObjectName, true, 1);
                    int firstId = ids.FirstOrDefault();
                    if (firstId != 0)
                        go = EditorUtility.InstanceIDToObject(firstId) as GameObject;
                }
                if (go == null) return null;
                if (type == typeof(GameObject) || type.IsAssignableFrom(typeof(GameObject)))
                    return go;
                if (typeof(Component).IsAssignableFrom(type))
                    return go.GetComponent(type);
                return null;
            }

            // Path 3: Auto-discover (original behavior)
            return ResolveInstance(type);
        }

        /// <summary>
        /// Find a live instance of the given type in the scene (auto-discovery fallback).
        /// Searches DontDestroyOnLoad objects when FindObjectOfType returns null in play mode.
        /// </summary>
        private static object ResolveInstance(Type type)
        {
            if (!typeof(UnityEngine.Object).IsAssignableFrom(type))
                return null;

#pragma warning disable CS0618
            var found = UnityEngine.Object.FindObjectOfType(type);
#pragma warning restore CS0618
            if (found != null) return found;

            // FindObjectOfType does not search the DontDestroyOnLoad scene
            if (!Application.isPlaying) return null;

            foreach (var go in GameObjectLookup.GetDontDestroyOnLoadObjects(true))
            {
                if (type.IsAssignableFrom(typeof(GameObject)))
                    return go;
                if (typeof(Component).IsAssignableFrom(type))
                {
                    var comp = go.GetComponent(type);
                    if (comp != null) return comp;
                }
            }
            return null;
        }

        /// <summary>
        /// Pick the best method overload given the provided argument names.
        /// </summary>
        private static MethodInfo PickOverload(MethodInfo[] candidates, Dictionary<string, object> args)
        {
            if (candidates.Length == 1) return candidates[0];

            // Prefer: most matching params, fewest total params
            return candidates
                .Select(m => new
                {
                    method = m,
                    parms = m.GetParameters(),
                    matched = m.GetParameters().Count(p => args.ContainsKey(p.Name))
                })
                .Where(x => x.parms.All(p => args.ContainsKey(p.Name) || p.HasDefaultValue))
                .OrderByDescending(x => x.matched)
                .ThenBy(x => x.parms.Length)
                .Select(x => x.method)
                .FirstOrDefault();
        }

        /// <summary>
        /// Coerce a value to the target parameter type.
        /// </summary>
        private static object CoerceArg(object value, Type target)
        {
            if (value == null) return null;
            if (target.IsInstanceOfType(value)) return value;

            // Enum
            if (target.IsEnum)
                return Enum.Parse(target, value.ToString(), true);

            // Vector3
            if (target == typeof(Vector3) && value is Dictionary<string, object> v3)
            {
                return new Vector3(
                    Convert.ToSingle(v3.GetValueOrDefault("x", 0f)),
                    Convert.ToSingle(v3.GetValueOrDefault("y", 0f)),
                    Convert.ToSingle(v3.GetValueOrDefault("z", 0f)));
            }

            // Vector2
            if (target == typeof(Vector2) && value is Dictionary<string, object> v2)
            {
                return new Vector2(
                    Convert.ToSingle(v2.GetValueOrDefault("x", 0f)),
                    Convert.ToSingle(v2.GetValueOrDefault("y", 0f)));
            }

            // Vector4
            if (target == typeof(Vector4) && value is Dictionary<string, object> v4)
            {
                return new Vector4(
                    Convert.ToSingle(v4.GetValueOrDefault("x", 0f)),
                    Convert.ToSingle(v4.GetValueOrDefault("y", 0f)),
                    Convert.ToSingle(v4.GetValueOrDefault("z", 0f)),
                    Convert.ToSingle(v4.GetValueOrDefault("w", 0f)));
            }

            // Quaternion
            if (target == typeof(Quaternion) && value is Dictionary<string, object> qd)
            {
                return new Quaternion(
                    Convert.ToSingle(qd.GetValueOrDefault("x", 0f)),
                    Convert.ToSingle(qd.GetValueOrDefault("y", 0f)),
                    Convert.ToSingle(qd.GetValueOrDefault("z", 0f)),
                    Convert.ToSingle(qd.GetValueOrDefault("w", 0f)));
            }

            // Color
            if (target == typeof(Color) && value is Dictionary<string, object> cd)
            {
                return new Color(
                    Convert.ToSingle(cd.GetValueOrDefault("r", 0f)),
                    Convert.ToSingle(cd.GetValueOrDefault("g", 0f)),
                    Convert.ToSingle(cd.GetValueOrDefault("b", 0f)),
                    Convert.ToSingle(cd.GetValueOrDefault("a", 1f)));
            }

            // Rect
            if (target == typeof(Rect) && value is Dictionary<string, object> rd)
            {
                return new Rect(
                    Convert.ToSingle(rd.GetValueOrDefault("x", 0f)),
                    Convert.ToSingle(rd.GetValueOrDefault("y", 0f)),
                    Convert.ToSingle(rd.GetValueOrDefault("width", 0f)),
                    Convert.ToSingle(rd.GetValueOrDefault("height", 0f)));
            }

            // Bounds
            if (target == typeof(Bounds) && value is Dictionary<string, object> bd)
            {
                Vector3 center = default, size = default;
                if (bd.TryGetValue("center", out var cv) && cv is Dictionary<string, object> cd2)
                    center = new Vector3(
                        Convert.ToSingle(cd2.GetValueOrDefault("x", 0f)),
                        Convert.ToSingle(cd2.GetValueOrDefault("y", 0f)),
                        Convert.ToSingle(cd2.GetValueOrDefault("z", 0f)));
                if (bd.TryGetValue("size", out var sv) && sv is Dictionary<string, object> sd)
                    size = new Vector3(
                        Convert.ToSingle(sd.GetValueOrDefault("x", 0f)),
                        Convert.ToSingle(sd.GetValueOrDefault("y", 0f)),
                        Convert.ToSingle(sd.GetValueOrDefault("z", 0f)));
                return new Bounds(center, size);
            }

            // Unwrap Nullable<T> → T so Convert.ChangeType works (it cannot convert to Nullable directly)
            Type underlying = Nullable.GetUnderlyingType(target);
            if (underlying != null)
            {
                if (value == null) return null;
                return Convert.ChangeType(value, underlying);
            }

            return Convert.ChangeType(value, target);
        }

        /// <summary>
        /// Parse args from the params JObject.
        /// </summary>
        private static Dictionary<string, object> ParseArgsFromParams(JObject @params)
        {
            var result = new Dictionary<string, object>();
            var argsToken = @params["args"];
            if (argsToken == null) return result;

            JObject argsObj;
            if (argsToken.Type == JTokenType.String)
            {
                try { argsObj = JObject.Parse(argsToken.ToString()); }
                catch { return null; }
            }
            else if (argsToken.Type == JTokenType.Object)
            {
                argsObj = (JObject)argsToken;
            }
            else
            {
                return null;
            }

            foreach (var prop in argsObj.Properties())
                result[prop.Name] = ConvertJToken(prop.Value);

            return result;
        }

        /// <summary>
        /// Shared invoke: build args, resolve instance, invoke, return result.
        /// </summary>
        private static object InvokeMethod(MethodInfo method, Type targetType,
            Dictionary<string, object> argDict, int? instanceId = null, string gameObjectName = null)
        {
            var methodParams = method.GetParameters();
            object[] invokeArgs = new object[methodParams.Length];

            for (int i = 0; i < methodParams.Length; i++)
            {
                var p = methodParams[i];
                if (argDict.TryGetValue(p.Name, out var val))
                {
                    try { invokeArgs[i] = CoerceArg(val, p.ParameterType); }
                    catch (Exception ex)
                    {
                        return new ErrorResponse($"Cannot convert arg '{p.Name}' to {p.ParameterType.Name}: {ex.Message}");
                    }
                }
                else if (p.HasDefaultValue) { invokeArgs[i] = p.DefaultValue; }
                else { return new ErrorResponse($"Required parameter '{p.Name}' ({p.ParameterType.Name}) is missing."); }
            }

            object target = null;
            if (!method.IsStatic)
            {
                target = ResolveInstance(targetType, instanceId, gameObjectName);
                if (target == null)
                {
                    if (instanceId.HasValue)
                        return new ErrorResponse(
                            $"Object with instanceID {instanceId.Value} not found or does not have " +
                            $"component '{targetType.Name}'. IDs may be stale after domain reload.");
                    if (!string.IsNullOrEmpty(gameObjectName))
                        return new ErrorResponse(
                            $"GameObject '{gameObjectName}' not found or does not have " +
                            $"component '{targetType.Name}'.");
                    return new ErrorResponse($"No instance of '{targetType.FullName}' found in scene.");
                }
            }

            try
            {
                object result = method.Invoke(target, invokeArgs);

                // If the method returns a McpToolCallResult (e.g., screenshot with image block),
                // pass it through directly so the MCP transport can return the image content block.
                if (result is NativeMcp.Editor.Protocol.McpToolCallResult)
                    return result;

                string info = method.ReturnType == typeof(void) ? "void" : method.ReturnType.Name;
                return new SuccessResponse(
                    $"{targetType.Name}.{method.Name}() → {info}",
                    new
                    {
                        method = method.Name,
                        is_static = method.IsStatic,
                        target_instance = target?.ToString(),
                        result = method.ReturnType == typeof(void) ? null : result
                    });
            }
            catch (TargetInvocationException ex)
            {
                var inner = ex.InnerException ?? ex;
                McpLog.Error($"[InvokeDynamic] {targetType.Name}.{method.Name} error: {inner}");
                return new ErrorResponse($"{targetType.Name}.{method.Name}: {inner.Message}",
                    new { exception_type = inner.GetType().Name, stack_trace = inner.StackTrace });
            }
        }
    }
}
