using System;
using System.IO;
using System.Reflection;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace LambdaLabs.UnityRepl.Editor.Transport
{
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

            // Reference all loaded assemblies
            var refAsm = evaluatorType.GetMethod("ReferenceAssembly", new[] { typeof(Assembly) });
            foreach (var a in AppDomain.CurrentDomain.GetAssemblies())
            {
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
                "using LambdaLabs.UnityRepl.Editor.Helpers;",
            };
            foreach (var u in usings)
            {
                try { _run.Invoke(_evaluator, new object[] { u }); }
                catch { /* Tolerate optional namespaces (e.g., InputSystem may not be installed) */ }
            }

            // InputSystem.Key is behind an optional package; add separately.
            try { _run.Invoke(_evaluator, new object[] { "using UnityEngine.InputSystem;" }); }
            catch { }

            _ready = true;
            Debug.Log($"[UnityREPL] Evaluator ready ({asm.Location})");
        }

        public string Eval(string code)
        {
            if (!_ready)
                return $"ERROR: evaluator not initialized\n{_initError}";

            // Clear previous errors
            _errorWriter.GetStringBuilder().Clear();

            try
            {
                var args = new object[] { code, null, false };
                string partial = (string)_evaluate.Invoke(_evaluator, args);

                // Check for compiler errors
                string errors = _errorWriter.ToString().Trim();
                if (!string.IsNullOrEmpty(errors))
                    return $"COMPILE ERROR:\n{errors}";

                if (partial != null)
                    return $"INCOMPLETE: {partial}";

                bool resultSet = (bool)args[2];
                object result = args[1];

                if (resultSet)
                    return result?.ToString() ?? "(null)";

                return "(ok)";
            }
            catch (TargetInvocationException ex)
            {
                var inner = ex.InnerException;
                return $"RUNTIME ERROR: {inner?.Message ?? ex.Message}\n{inner?.StackTrace ?? ex.StackTrace}";
            }
            catch (Exception ex)
            {
                return $"ERROR: {ex.Message}\n{ex.StackTrace}";
            }
        }
    }
}
