using System;
using System.Collections;
using System.IO;
using System.Reflection;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace LambdaLabs.UnityRepl.Editor.Transport
{
    internal enum EvalOutcomeKind { Value, Coroutine }

    internal readonly struct EvalOutcome
    {
        public readonly EvalOutcomeKind Kind;
        public readonly string Text;
        public readonly IEnumerator Coroutine;

        private EvalOutcome(EvalOutcomeKind kind, string text, IEnumerator coroutine)
        {
            Kind = kind;
            Text = text;
            Coroutine = coroutine;
        }

        public static EvalOutcome Value(string text) => new EvalOutcome(EvalOutcomeKind.Value, text, null);
        public static EvalOutcome FromCoroutine(IEnumerator co) => new EvalOutcome(EvalOutcomeKind.Coroutine, null, co);
    }

    /// <summary>
    /// C# REPL evaluator using Mono.CSharp.Evaluator (loaded via reflection).
    /// Captures compiler errors via a StringWriter-based ReportPrinter.
    /// </summary>
    internal class CSharpEvaluator
    {
        private object _evaluator;
        private MethodInfo _compile;       // two-arg: string Compile(string, out CompiledMethod)
        private MethodInfo _compileSingle; // one-arg fallback: CompiledMethod Compile(string)
        private MethodInfo _run;
        private Type _compiledMethodType;
        private StringWriter _errorWriter;
        private bool _ready;
        private string _initError;

        // Validate() rollback plumbing — reflects the Evaluator's mutable declaration
        // state so we can snapshot before Compile() and restore after. All fields may
        // be null if the Mono.CSharp layout shifts; in that case _rollbackAvailable is
        // false and Validate() falls back to not-side-effect-free behavior.
        private FieldInfo _fieldsField;          // Evaluator.fields  (IDictionary)
        private FieldInfo _sourceFileField;      // Evaluator.source_file
        private object _sourceFile;              // cached reference
        private PropertyInfo _usingsProperty;    // source_file.Usings (IList)
        private PropertyInfo _containersProperty;// source_file.Containers (IList)
        private MethodInfo _removeContainer;     // source_file.RemoveContainer(TypeDefinition)
        private bool _rollbackAvailable;

        public bool IsReady => _ready;
        public string InitError => _initError;
        public bool ValidateRollbackAvailable => _rollbackAvailable;

        public CSharpEvaluator()
        {
            try { Init(); }
            catch (Exception ex)
            {
                _initError = ex.ToString();
                Debug.LogError($"[UnityREPL] Init failed: {ex}");
            }
        }

        private void Init()
        {
            Assembly asm = null;
            try { asm = Assembly.Load("Mono.CSharp"); }
            catch
            {
                var contentsDir = EditorApplication.applicationContentsPath;
                string[] paths = {
                    Path.Combine(contentsDir, "Resources", "Scripting", "MonoBleedingEdge", "lib", "mono", "4.7.1-api", "Mono.CSharp.dll"),
                    Path.Combine(contentsDir, "Resources", "Scripting", "MonoBleedingEdge", "lib", "mono", "4.5", "Mono.CSharp.dll"),
                    Path.Combine(contentsDir, "MonoBleedingEdge", "lib", "mono", "4.7.1-api", "Mono.CSharp.dll"),
                    Path.Combine(contentsDir, "MonoBleedingEdge", "lib", "mono", "4.5", "Mono.CSharp.dll"),
                };
                foreach (var p in paths)
                    if (File.Exists(p)) { asm = Assembly.LoadFrom(p); break; }
            }

            if (asm == null)
                throw new FileNotFoundException("Cannot load Mono.CSharp assembly");

            var evaluatorType = asm.GetType("Mono.CSharp.Evaluator")
                ?? throw new TypeLoadException("Cannot find Mono.CSharp.Evaluator");

            var settingsType = asm.GetType("Mono.CSharp.CompilerSettings");
            var settings = Activator.CreateInstance(settingsType);

            // Use StreamReportPrinter with a StringWriter to capture errors
            _errorWriter = new StringWriter();
            var reporterType = asm.GetType("Mono.CSharp.StreamReportPrinter")
                ?? asm.GetType("Mono.CSharp.ConsoleReportPrinter");
            var reporter = Activator.CreateInstance(reporterType, (TextWriter)_errorWriter);

            var contextType = asm.GetType("Mono.CSharp.CompilerContext");
            var reportPrinterBaseType = asm.GetType("Mono.CSharp.ReportPrinter");
            var contextCtor = contextType.GetConstructor(new[] { settingsType, reportPrinterBaseType });
            var context = contextCtor.Invoke(new[] { settings, reporter });

            var evalCtor = evaluatorType.GetConstructor(new[] { contextType });
            _evaluator = evalCtor.Invoke(new[] { context });

            _run = evaluatorType.GetMethod("Run", new[] { typeof(string) });

            _compiledMethodType = asm.GetType("Mono.CSharp.CompiledMethod")
                ?? throw new TypeLoadException("Cannot find Mono.CSharp.CompiledMethod");
            // Two-arg form preserves the "unparsed tail" signal used for INCOMPLETE: classification.
            _compile = evaluatorType.GetMethod("Compile", new[] {
                typeof(string), _compiledMethodType.MakeByRefType()
            });
            // One-arg form is a structural fallback for exotic Mono.CSharp builds; it
            // collapses "incomplete input" into plain compile errors.
            _compileSingle = evaluatorType.GetMethod("Compile", new[] { typeof(string) });
            if (_compile == null && _compileSingle == null)
                throw new MissingMethodException("Mono.CSharp.Evaluator.Compile(...)");

            TryWireValidateRollback(evaluatorType);

            // Reference loaded assemblies, skipping those Mono.CSharp.Evaluator already
            // pre-references internally. Adding mscorlib/System/System.Core/etc. a second
            // time triggers CS1685 ("predefined type ... defined multiple times") warnings
            // for System.Collections.IEnumerator, NotSupportedException, LINQ operators, etc.
            // Dedupe by simple name to also tolerate multi-version loading edge cases.
            var refAsm = evaluatorType.GetMethod("ReferenceAssembly", new[] { typeof(Assembly) });
            var skipAsmNames = new System.Collections.Generic.HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                "mscorlib", "System", "System.Core", "System.Xml", "System.Xml.Linq",
                "System.Runtime", "System.Numerics", "System.Data", "System.Drawing",
                "netstandard", "Mono.CSharp",
            };
            var addedAsmNames = new System.Collections.Generic.HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var a in AppDomain.CurrentDomain.GetAssemblies())
            {
                if (a.IsDynamic) continue;
                var name = a.GetName().Name;
                if (skipAsmNames.Contains(name)) continue;
                if (!addedAsmNames.Add(name)) continue;
                try { refAsm.Invoke(_evaluator, new object[] { a }); } catch { }
            }

            // Default usings
            string[] usings = {
                "using UnityEngine;",
                "using UnityEditor;",
                "using System;",
                "using System.IO;",
                "using System.Linq;",
                "using System.Collections.Generic;",
                "using UnityEditor.SceneManagement;",
                "using UnityEngine.SceneManagement;",
            };
            foreach (var u in usings)
                _run.Invoke(_evaluator, new object[] { u });

            _ready = true;
            Debug.Log($"[UnityREPL] Evaluator ready ({asm.Location}){(_rollbackAvailable ? ", Validate rollback enabled" : ", Validate rollback DISABLED")}");
        }

        /// <summary>
        /// Best-effort reflection wiring for Validate() rollback. Looks up private
        /// instance fields on Mono.CSharp.Evaluator (`fields`, `source_file`) and
        /// members on the CompilationSourceFile instance (`Usings`, `Containers`,
        /// `RemoveContainer`). If any lookup fails, logs a warning and leaves
        /// _rollbackAvailable == false so Validate() degrades to the old behavior.
        /// </summary>
        private void TryWireValidateRollback(Type evaluatorType)
        {
            try
            {
                const BindingFlags instFlags = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;

                _fieldsField = evaluatorType.GetField("fields", instFlags);
                _sourceFileField = evaluatorType.GetField("source_file", instFlags);
                if (_fieldsField == null || _sourceFileField == null)
                {
                    Debug.LogWarning("[UnityREPL] Validate rollback: could not reflect Evaluator.fields / source_file.");
                    return;
                }

                _sourceFile = _sourceFileField.GetValue(_evaluator);
                if (_sourceFile == null)
                {
                    Debug.LogWarning("[UnityREPL] Validate rollback: source_file is null.");
                    return;
                }

                // Walk inheritance chain — Containers is on TypeContainer, a base class
                // of CompilationSourceFile. Usings is on CompilationSourceFile itself.
                _usingsProperty = FindPropertyOnHierarchy(_sourceFile.GetType(), "Usings");
                _containersProperty = FindPropertyOnHierarchy(_sourceFile.GetType(), "Containers");
                _removeContainer = FindMethodOnHierarchy(_sourceFile.GetType(), "RemoveContainer");

                if (_usingsProperty == null || _containersProperty == null || _removeContainer == null)
                {
                    Debug.LogWarning($"[UnityREPL] Validate rollback: could not reflect source_file members (Usings={_usingsProperty != null}, Containers={_containersProperty != null}, RemoveContainer={_removeContainer != null}).");
                    return;
                }

                // Sanity-check that fields is actually IDictionary-compatible so
                // snapshot/restore won't throw at runtime.
                var fieldsValue = _fieldsField.GetValue(_evaluator);
                if (!(fieldsValue is System.Collections.IDictionary))
                {
                    Debug.LogWarning($"[UnityREPL] Validate rollback: Evaluator.fields is not IDictionary ({fieldsValue?.GetType().FullName}).");
                    return;
                }

                _rollbackAvailable = true;
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[UnityREPL] Validate rollback wiring failed: {ex.Message}");
                _rollbackAvailable = false;
            }
        }

        private static PropertyInfo FindPropertyOnHierarchy(Type t, string name)
        {
            const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;
            for (var cur = t; cur != null; cur = cur.BaseType)
            {
                var p = cur.GetProperty(name, flags);
                if (p != null) return p;
            }
            return null;
        }

        private static MethodInfo FindMethodOnHierarchy(Type t, string name)
        {
            const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;
            for (var cur = t; cur != null; cur = cur.BaseType)
            {
                var m = cur.GetMethod(name, flags);
                if (m != null) return m;
            }
            return null;
        }

        /// <summary>
        /// Snapshot of mutable evaluator declaration state taken immediately before
        /// a Validate() Compile() call. Restore() reverts anything Compile() added.
        /// </summary>
        private readonly struct ValidateSnapshot
        {
            public readonly System.Collections.DictionaryEntry[] Fields;
            public readonly int UsingsCount;
            public readonly object[] Containers; // identity set

            public ValidateSnapshot(System.Collections.DictionaryEntry[] fields, int usingsCount, object[] containers)
            {
                Fields = fields;
                UsingsCount = usingsCount;
                Containers = containers;
            }
        }

        private ValidateSnapshot CaptureSnapshot()
        {
            // fields — copy KV pairs by value.
            var dict = (System.Collections.IDictionary)_fieldsField.GetValue(_evaluator);
            var entries = new System.Collections.DictionaryEntry[dict.Count];
            int i = 0;
            foreach (System.Collections.DictionaryEntry de in dict)
                entries[i++] = de;

            // usings — record count; new entries are appended, so post-compile we
            // shrink the list back to the captured count.
            var usingsList = (System.Collections.IList)_usingsProperty.GetValue(_sourceFile);
            int usingsCount = usingsList?.Count ?? 0;

            // containers — snapshot identity set. On restore, anything not in the
            // set is treated as newly added and routed through RemoveContainer().
            var containersEnum = _containersProperty.GetValue(_sourceFile) as System.Collections.IEnumerable;
            var containersList = new System.Collections.Generic.List<object>();
            if (containersEnum != null)
                foreach (var c in containersEnum) containersList.Add(c);

            return new ValidateSnapshot(entries, usingsCount, containersList.ToArray());
        }

        private void RestoreSnapshot(ValidateSnapshot snap)
        {
            try
            {
                // fields — clear and re-populate. New entries added during Compile are
                // dropped. CAVEAT: if a Validate input re-declares an existing variable,
                // Mono.CSharp nulls the old field value BEFORE writing the new one
                // (eval.cs:832), so although the name binding is restored here, the
                // original runtime value is lost. Documented in Validate() XML doc.
                var dict = (System.Collections.IDictionary)_fieldsField.GetValue(_evaluator);
                dict.Clear();
                foreach (var de in snap.Fields)
                    dict[de.Key] = de.Value;

                // usings — shrink back to captured count via RemoveAt(end) in a loop.
                var usingsList = (System.Collections.IList)_usingsProperty.GetValue(_sourceFile);
                if (usingsList != null)
                {
                    while (usingsList.Count > snap.UsingsCount)
                        usingsList.RemoveAt(usingsList.Count - 1);
                }

                // containers — for each container currently present that wasn't in the
                // snapshot, call RemoveContainer(c). Use identity (ReferenceEquals) so
                // we don't mistakenly match user types to pre-existing containers.
                var containersEnum = _containersProperty.GetValue(_sourceFile) as System.Collections.IEnumerable;
                if (containersEnum != null)
                {
                    var toRemove = new System.Collections.Generic.List<object>();
                    foreach (var c in containersEnum)
                    {
                        bool existed = false;
                        foreach (var old in snap.Containers)
                        {
                            if (ReferenceEquals(old, c)) { existed = true; break; }
                        }
                        if (!existed) toRemove.Add(c);
                    }
                    foreach (var c in toRemove)
                    {
                        try { _removeContainer.Invoke(_sourceFile, new[] { c }); }
                        catch (Exception ex)
                        {
                            Debug.LogWarning($"[UnityREPL] Validate rollback: RemoveContainer failed: {ex.Message}");
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.LogError($"[UnityREPL] Validate rollback: RestoreSnapshot failed — evaluator state may be inconsistent: {ex}");
            }
        }

        public EvalOutcome Eval(string code)
        {
            if (!_ready)
                return EvalOutcome.Value($"ERROR: evaluator not initialized\n{_initError}");

            // Clear previous errors
            _errorWriter.GetStringBuilder().Clear();

            // Phase 1 — compile only. Never invokes user code. If compilation fails
            // the returned delegate is null and the error writer has the diagnostics.
            object compiledObj;
            string partial;
            try
            {
                if (!TryCompile(code, out compiledObj, out partial))
                    return EvalOutcome.Value($"COMPILE ERROR:\n{_errorWriter.ToString().Trim()}");
            }
            catch (TargetInvocationException ex)
            {
                var inner = ex.InnerException;
                return EvalOutcome.Value($"COMPILE ERROR: {inner?.Message ?? ex.Message}");
            }
            catch (Exception ex)
            {
                return EvalOutcome.Value($"ERROR: {ex.Message}\n{ex.StackTrace}");
            }

            string diagnostics = _errorWriter.ToString().Trim();
            if (!string.IsNullOrEmpty(diagnostics))
            {
                if (HasCompileErrors(diagnostics))
                    return EvalOutcome.Value($"COMPILE ERROR:\n{diagnostics}");
                Debug.LogWarning($"[UnityREPL] Compile warnings:\n{diagnostics}");
            }

            // Incomplete input — parser needed more tokens. Preserve the old INCOMPLETE:
            // classification so repl.sh / repl.bat exit-code routing (exit 2) is stable.
            if (!string.IsNullOrEmpty(partial))
                return EvalOutcome.Value($"INCOMPLETE: {partial}");

            // Declaration-only input (class/method/field): Compile returns a null
            // delegate but has already registered the definition in evaluator state.
            if (compiledObj == null)
                return EvalOutcome.Value("(ok)");

            // Phase 2 — invoke the CompiledMethod delegate:
            //   delegate void CompiledMethod(ref object result);
            // DynamicInvoke marshals the ref parameter back via the args array.
            try
            {
                var invokeArgs = new object[] { null };
                ((Delegate)compiledObj).DynamicInvoke(invokeArgs);
                object result = invokeArgs[0];
                if (result is IEnumerator ienum)
                    return EvalOutcome.FromCoroutine(ienum);
                return EvalOutcome.Value(result?.ToString() ?? "(ok)");
            }
            catch (TargetInvocationException ex)
            {
                var inner = ex.InnerException;
                return EvalOutcome.Value($"RUNTIME ERROR: {inner?.Message ?? ex.Message}\n{inner?.StackTrace ?? ex.StackTrace}");
            }
            catch (Exception ex)
            {
                return EvalOutcome.Value($"ERROR: {ex.Message}\n{ex.StackTrace}");
            }
        }

        /// <summary>
        /// Compile-only dry-run. Returns one of:
        ///   "COMPILE OK"            — expression/statement compiled cleanly
        ///   "COMPILE OK (no-op)"    — pure declaration compiled (and rolled back
        ///                             when rollback is available — see below)
        ///   "INCOMPLETE: ..."       — parser needs more tokens
        ///   "COMPILE ERROR: ..."    — syntax/semantic error
        /// Never invokes the compiled delegate, so expression/statement inputs have
        /// no runtime side effects.
        ///
        /// When <see cref="ValidateRollbackAvailable"/> is true (the common case on
        /// Unity's shipped Mono.CSharp), Validate also snapshots and restores the
        /// evaluator's mutable declaration state (fields dict, using directives,
        /// source-file type containers) so declarations like
        /// <c>class Foo {}</c> or <c>var x = 5;</c> are also side-effect-free.
        ///
        /// Remaining caveat: re-declaring an existing variable via Validate nulls
        /// the original field value before rollback (Mono.CSharp
        /// <c>eval.cs:832</c>). The name binding is restored but its runtime value
        /// is lost. Avoid using <c>--validate</c> to probe redefinitions of
        /// already-defined vars in a live session.
        /// </summary>
        public string Validate(string code)
        {
            if (!_ready)
                return $"ERROR: evaluator not initialized\n{_initError}";

            _errorWriter.GetStringBuilder().Clear();

            bool snapshotTaken = false;
            ValidateSnapshot snapshot = default;
            if (_rollbackAvailable)
            {
                try { snapshot = CaptureSnapshot(); snapshotTaken = true; }
                catch (Exception ex) { Debug.LogWarning($"[UnityREPL] Validate snapshot failed: {ex.Message}"); }
            }

            try
            {
                object compiled;
                string partial;
                try
                {
                    if (!TryCompile(code, out compiled, out partial))
                        return $"COMPILE ERROR:\n{_errorWriter.ToString().Trim()}";
                }
                catch (TargetInvocationException ex)
                {
                    return $"COMPILE ERROR: {ex.InnerException?.Message ?? ex.Message}";
                }
                catch (Exception ex)
                {
                    return $"ERROR: {ex.Message}";
                }

                string diagnostics = _errorWriter.ToString().Trim();
                if (!string.IsNullOrEmpty(diagnostics) && HasCompileErrors(diagnostics))
                    return $"COMPILE ERROR:\n{diagnostics}";
                if (!string.IsNullOrEmpty(partial))
                    return $"INCOMPLETE: {partial}";
                return compiled != null ? "COMPILE OK" : "COMPILE OK (no-op)";
            }
            finally
            {
                if (snapshotTaken)
                    RestoreSnapshot(snapshot);
            }
        }

        /// <summary>
        /// Calls Mono.CSharp.Evaluator.Compile via reflection. Prefers the two-arg
        /// overload so the unparsed-tail signal (used for INCOMPLETE: classification)
        /// is preserved; falls back to the one-arg overload otherwise.
        /// Returns false if neither overload is available (should not happen —
        /// Init() already validated this).
        /// </summary>
        private bool TryCompile(string code, out object compiled, out string partial)
        {
            if (_compile != null)
            {
                var args = new object[] { code, null };
                partial = (string)_compile.Invoke(_evaluator, args);
                compiled = args[1];
                return true;
            }
            if (_compileSingle != null)
            {
                compiled = _compileSingle.Invoke(_evaluator, new object[] { code });
                partial = null;
                return true;
            }
            compiled = null;
            partial = null;
            return false;
        }

        /// <summary>
        /// True if any line of Mono.CSharp diagnostic output looks like a real error
        /// (as opposed to a warning). Mono's format is either
        ///   <c>(line,col): error CS####: ...</c> or
        ///   <c>(line,col): warning CS####: ...</c> (location may be omitted).
        /// We scan for ": error " / leading "error " — warning-only output returns false.
        /// </summary>
        private static bool HasCompileErrors(string diagnostics)
        {
            if (string.IsNullOrEmpty(diagnostics)) return false;
            foreach (var rawLine in diagnostics.Split('\n'))
            {
                var line = rawLine.TrimStart();
                if (line.Length == 0) continue;
                if (line.StartsWith("error ", StringComparison.Ordinal)) return true;
                if (line.IndexOf(": error ", StringComparison.Ordinal) >= 0) return true;
            }
            return false;
        }
    }
}
