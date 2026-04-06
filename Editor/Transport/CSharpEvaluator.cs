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
        private MethodInfo _evaluate;
        private MethodInfo _run;
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
            _evaluate = evaluatorType.GetMethod("Evaluate", new[] {
                typeof(string), typeof(object).MakeByRefType(), typeof(bool).MakeByRefType()
            });

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

            try
            {
                var args = new object[] { code, null, false };
                string partial = (string)_evaluate.Invoke(_evaluator, args);

                // Check for compiler errors. Mono.CSharp emits both errors AND warnings
                // to the same printer, so distinguish them: only "error CS####" lines
                // should fail the eval. Warning-only output is logged to Unity console
                // and the eval continues normally.
                string diagnostics = _errorWriter.ToString().Trim();
                if (!string.IsNullOrEmpty(diagnostics))
                {
                    if (HasCompileErrors(diagnostics))
                        return EvalOutcome.Value($"COMPILE ERROR:\n{diagnostics}");
                    Debug.LogWarning($"[UnityREPL] Compile warnings:\n{diagnostics}");
                }

                if (partial != null)
                {
                    // Declarations (methods, classes) and some multi-statement inputs are
                    // reported as partial by Evaluate(); retry via Run() which accepts them.
                    _errorWriter.GetStringBuilder().Clear();
                    bool ranOk;
                    try
                    {
                        ranOk = (bool)_run.Invoke(_evaluator, new object[] { code });
                    }
                    catch (TargetInvocationException rex)
                    {
                        var rinner = rex.InnerException;
                        return EvalOutcome.Value($"RUNTIME ERROR: {rinner?.Message ?? rex.Message}\n{rinner?.StackTrace ?? rex.StackTrace}");
                    }
                    string runDiagnostics = _errorWriter.ToString().Trim();
                    if (!string.IsNullOrEmpty(runDiagnostics))
                    {
                        if (HasCompileErrors(runDiagnostics))
                            return EvalOutcome.Value($"COMPILE ERROR:\n{runDiagnostics}");
                        Debug.LogWarning($"[UnityREPL] Compile warnings:\n{runDiagnostics}");
                    }
                    if (!ranOk)
                        return EvalOutcome.Value($"INCOMPLETE: {partial}");
                    return EvalOutcome.Value("(ok)");
                }

                bool resultSet = (bool)args[2];
                object result = args[1];

                if (resultSet)
                {
                    if (result is IEnumerator ienum)
                        return EvalOutcome.FromCoroutine(ienum);
                    return EvalOutcome.Value(result?.ToString() ?? "(null)");
                }

                return EvalOutcome.Value("(ok)");
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
