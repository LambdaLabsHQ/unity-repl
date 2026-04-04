using System;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace NativeMcp.Editor.Transport
{
    /// <summary>
    /// UnityREPL file-based IPC transport.
    /// Reads <c>.req</c> files containing raw C# code, evaluates via
    /// <see cref="CSharpEvaluator"/>, writes plain text result to <c>.res</c>.
    /// </summary>
    internal class FileIpcTransport
    {
        private readonly string _reqDir;
        private readonly string _resDir;
        private CSharpEvaluator _evaluator;
        private bool _isRunning;

        public bool IsRunning => _isRunning;

        public FileIpcTransport()
        {
            var projectRoot = Directory.GetParent(Application.dataPath).FullName;
            var ipcDir = Path.Combine(projectRoot, "Temp", "UnityMcpIpc");
            _reqDir = Path.Combine(ipcDir, "Requests");
            _resDir = Path.Combine(ipcDir, "Responses");
        }

        public void Start()
        {
            if (_isRunning) return;
            Directory.CreateDirectory(_reqDir);
            Directory.CreateDirectory(_resDir);
            _evaluator = new CSharpEvaluator();
            EditorApplication.update += OnUpdate;
            _isRunning = true;
        }

        public void Stop()
        {
            if (!_isRunning) return;
            EditorApplication.update -= OnUpdate;
            _isRunning = false;
        }

        private void OnUpdate()
        {
            if (!Directory.Exists(_reqDir)) return;

            string[] files;
            try { files = Directory.GetFiles(_reqDir, "*.req"); }
            catch { return; }

            foreach (var file in files)
            {
                string code;
                try
                {
                    code = File.ReadAllText(file).Trim();
                    File.Delete(file);
                }
                catch { continue; }

                var uuid = Path.GetFileNameWithoutExtension(file);
                var tmpPath = Path.Combine(_resDir, $"{uuid}.tmp");
                var resPath = Path.Combine(_resDir, $"{uuid}.res");

                // Evaluate on main thread (we're already on it via EditorApplication.update)
                string result;
                try
                {
                    result = _evaluator.Eval(code);
                }
                catch (Exception ex)
                {
                    result = $"ERROR: {ex.Message}";
                }

                try
                {
                    File.WriteAllText(tmpPath, result);
                    File.Move(tmpPath, resPath);
                }
                catch (Exception ex)
                {
                    Debug.LogError($"[UnityREPL] write failed: {ex.Message}");
                }
            }
        }
    }
}
