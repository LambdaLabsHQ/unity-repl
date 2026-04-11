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

        public bool IsReady => _ready;
        public string InitError => _initError;

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
            Debug.Log($"[UnityREPL] Evaluator ready ({asm.Location})");
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
        ///   "COMPILE OK (no-op)"    — pure declaration compiled (note: already
        ///                             registered in evaluator state — Validate is
        ///                             NOT side-effect-free for class/method/field
        ///                             declarations)
        ///   "INCOMPLETE: ..."       — parser needs more tokens
        ///   "COMPILE ERROR: ..."    — syntax/semantic error
        /// Never invokes the compiled delegate, so expression/statement inputs have
        /// no runtime side effects.
        /// </summary>
        public string Validate(string code)
        {
            if (!_ready)
                return $"ERROR: evaluator not initialized\n{_initError}";

            _errorWriter.GetStringBuilder().Clear();

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
