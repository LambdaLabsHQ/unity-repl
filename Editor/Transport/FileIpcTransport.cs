using System;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace LambdaLabs.UnityRepl.Editor.Transport
{
    /// <summary>
    /// UnityREPL file-based IPC transport.
    /// Reads <c>.req</c> files containing raw C# code, evaluates via
    /// <see cref="CSharpEvaluator"/>, writes plain text result to <c>.res</c>.
    /// Coroutine (IEnumerator) results are handed off to <see cref="CoroutinePump"/>
    /// which writes the final response when the coroutine completes.
    /// <c>.cancel</c> files trigger cancellation of the matching active/queued coroutine.
    /// </summary>
    internal class FileIpcTransport
    {
        private const int DefaultTimeoutMs = 60000;

        private readonly string _reqDir;
        private readonly string _resDir;
        private CSharpEvaluator _evaluator;
        private CoroutinePump _pump;
        private bool _isRunning;

        public bool IsRunning => _isRunning;

        public FileIpcTransport()
        {
            var projectRoot = Directory.GetParent(Application.dataPath).FullName;
            var ipcDir = Path.Combine(projectRoot, "Temp", "UnityReplIpc");
            _reqDir = Path.Combine(ipcDir, "Requests");
            _resDir = Path.Combine(ipcDir, "Responses");
        }

        public void Start()
        {
            if (_isRunning) return;
            Directory.CreateDirectory(_reqDir);
            Directory.CreateDirectory(_resDir);
            _evaluator = new CSharpEvaluator();
            _pump = new CoroutinePump();
            EditorApplication.update += OnUpdate;
            _isRunning = true;
        }

        public void Stop()
        {
            if (!_isRunning) return;
            EditorApplication.update -= OnUpdate;
            if (_pump != null)
            {
                _pump.Drain("RELOAD");
                _pump.Dispose();
                _pump = null;
            }
            _isRunning = false;
        }

        private void OnUpdate()
        {
            if (!Directory.Exists(_reqDir)) return;

            // 1. Process incoming requests.
            string[] reqFiles;
            try { reqFiles = Directory.GetFiles(_reqDir, "*.req"); }
            catch { reqFiles = Array.Empty<string>(); }

            foreach (var file in reqFiles)
            {
                string code;
                try
                {
                    code = File.ReadAllText(file);
                    File.Delete(file);
                }
                catch { continue; }

                var uuid = Path.GetFileNameWithoutExtension(file);
                var resPath = Path.Combine(_resDir, $"{uuid}.res");

                int timeoutMs;
                bool validateOnly;
                string userCode;
                ParseDirectives(code, out userCode, out timeoutMs, out validateOnly);
                userCode = userCode.Trim();

                if (validateOnly)
                {
                    string text;
                    try { text = _evaluator.Validate(userCode); }
                    catch (Exception ex) { text = $"ERROR: {ex.Message}"; }
                    WriteResponse(resPath, text);
                    continue;
                }

                EvalOutcome outcome;
                try
                {
                    outcome = _evaluator.Eval(userCode);
                }
                catch (Exception ex)
                {
                    outcome = EvalOutcome.Value($"ERROR: {ex.Message}");
                }

                if (outcome.Kind == EvalOutcomeKind.Coroutine)
                {
                    bool queued = _pump.Enqueue(outcome.Coroutine, uuid, resPath, timeoutMs);
                    if (!queued)
                    {
                        WriteResponse(resPath, "BUSY: queue full");
                    }
                    // else: pump owns the .res write
                }
                else
                {
                    WriteResponse(resPath, outcome.Text);
                }
            }

            // 2. Process cancellation requests (after .req so same-tick races resolve correctly).
            string[] cancelFiles;
            try { cancelFiles = Directory.GetFiles(_reqDir, "*.cancel"); }
            catch { cancelFiles = Array.Empty<string>(); }

            foreach (var file in cancelFiles)
            {
                var uuid = Path.GetFileNameWithoutExtension(file);
                try { File.Delete(file); } catch { }
                _pump.Cancel(uuid);
            }

            // 3. Drive any active coroutine.
            _pump.Tick();
        }

        /// <summary>
        /// Parses leading <c>//!</c>-prefixed directive lines. Supported:
        ///   <c>//!timeout=30s</c> — per-request timeout (formats: <c>30s</c>, <c>2m</c>, <c>5000</c>).
        ///   <c>//!validate</c>   — compile-only dry run (no execution).
        /// Directives may stack in any order on the leading lines of the request;
        /// consumed lines are stripped from <paramref name="userCode"/>.
        /// </summary>
        private static void ParseDirectives(string code, out string userCode, out int timeoutMs, out bool validateOnly)
        {
            timeoutMs = DefaultTimeoutMs;
            validateOnly = false;
            userCode = code ?? string.Empty;

            while (!string.IsNullOrEmpty(userCode))
            {
                int lineEnd = userCode.IndexOf('\n');
                string firstLine = (lineEnd >= 0 ? userCode.Substring(0, lineEnd) : userCode).TrimEnd('\r', ' ', '\t');
                if (!firstLine.StartsWith("//!", StringComparison.Ordinal))
                    return;

                const string timeoutPrefix = "//!timeout=";
                if (firstLine.StartsWith(timeoutPrefix, StringComparison.Ordinal))
                {
                    string value = firstLine.Substring(timeoutPrefix.Length).Trim();
                    if (TryParseTimeout(value, out int ms))
                        timeoutMs = ms;
                }
                else if (firstLine == "//!validate")
                {
                    validateOnly = true;
                }
                else
                {
                    // Unknown //! directive — leave the line in userCode so the
                    // evaluator surfaces a real compile error rather than silently
                    // swallowing typos.
                    return;
                }

                userCode = lineEnd >= 0 ? userCode.Substring(lineEnd + 1) : string.Empty;
            }
        }

        private static bool TryParseTimeout(string s, out int ms)
        {
            ms = 0;
            if (string.IsNullOrEmpty(s)) return false;
            double mult = 1.0;
            char last = s[s.Length - 1];
            string numPart = s;
            if (last == 's') { mult = 1000; numPart = s.Substring(0, s.Length - 1); }
            else if (last == 'm') { mult = 60000; numPart = s.Substring(0, s.Length - 1); }
            else if (last == 'h') { mult = 3600000; numPart = s.Substring(0, s.Length - 1); }
            if (!double.TryParse(numPart, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out double n))
                return false;
            double result = n * mult;
            if (result < 0) return false;
            if (result > int.MaxValue) result = int.MaxValue;
            ms = (int)result;
            return true;
        }

        private static void WriteResponse(string resPath, string content)
        {
            try
            {
                var tmpPath = resPath + ".tmp";
                File.WriteAllText(tmpPath, content);
                if (File.Exists(resPath)) File.Delete(resPath);
                File.Move(tmpPath, resPath);
            }
            catch (Exception ex)
            {
                Debug.LogError($"[UnityREPL] write failed: {ex.Message}");
            }
        }
    }
}
