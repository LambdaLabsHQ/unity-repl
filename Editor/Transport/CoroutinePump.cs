using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using LambdaLabs.UnityRepl.Runtime.Transport;
using UnityEditor;
using UnityEngine;

namespace LambdaLabs.UnityRepl.Editor.Transport
{
    /// <summary>
    /// Drives IEnumerator results across frames. Single active coroutine plus a FIFO queue.
    /// In Edit Mode, steps via EditorApplication.update with a small yield-instruction polyfill.
    /// In Play Mode, routes to a hidden ReplCoroutineHost MonoBehaviour so Unity's native
    /// scheduler handles WaitForEndOfFrame/WaitForFixedUpdate/etc. correctly.
    /// </summary>
    internal class CoroutinePump : IDisposable
    {
        private const int MaxQueueDepth = 8;

        private enum WaitKind { None, Deadline, Predicate }

        private class Active
        {
            public IEnumerator Root;
            public Stack<IEnumerator> Stack;
            public string Uuid;
            public string ResPath;
            public object LastValue;
            public double TimeoutAt;
            public WaitKind WaitKind;
            public double WaitDeadline;
            public Func<bool> WaitPredicate;
            public bool PlayModeHosted;
            public ReplCoroutineHost.Tracker PlayModeTracker;
        }

        private class Pending
        {
            public IEnumerator Co;
            public string Uuid;
            public string ResPath;
            public int TimeoutMs;
        }

        private Active _active;
        private readonly Queue<Pending> _queue = new Queue<Pending>();
        private ReplCoroutineHost _host;

        private static FieldInfo _wfsField;
        private static bool _wfsFieldWarned;

        public CoroutinePump()
        {
            EditorApplication.playModeStateChanged += OnPlayModeStateChanged;
        }

        public void Dispose()
        {
            EditorApplication.playModeStateChanged -= OnPlayModeStateChanged;
        }

        /// <summary>
        /// Returns false if the queue is full (caller should write "BUSY: queue full" to the .res).
        /// </summary>
        public bool Enqueue(IEnumerator co, string uuid, string resPath, int timeoutMs)
        {
            if (co == null) return false;
            if (_queue.Count >= MaxQueueDepth) return false;
            _queue.Enqueue(new Pending { Co = co, Uuid = uuid, ResPath = resPath, TimeoutMs = timeoutMs });
            return true;
        }

        public void Cancel(string uuid)
        {
            if (string.IsNullOrEmpty(uuid)) return;

            if (uuid == "__all__")
            {
                if (_active != null) Finish(_active, "CANCELLED");
                DrainQueue("CANCELLED");
                return;
            }

            if (_active != null && _active.Uuid == uuid)
            {
                Finish(_active, "CANCELLED");
                return;
            }

            // Remove from queue by filtering
            if (_queue.Count == 0) return;
            var remaining = new Queue<Pending>();
            while (_queue.Count > 0)
            {
                var p = _queue.Dequeue();
                if (p.Uuid == uuid)
                {
                    try { (p.Co as IDisposable)?.Dispose(); } catch { }
                    WriteResponse(p.ResPath, "CANCELLED");
                }
                else
                {
                    remaining.Enqueue(p);
                }
            }
            while (remaining.Count > 0) _queue.Enqueue(remaining.Dequeue());
        }

        public void Drain(string reason)
        {
            if (_active != null) Finish(_active, reason);
            DrainQueue(reason);
        }

        public void Tick()
        {
            // Promote next queued coroutine if slot is free
            if (_active == null && _queue.Count > 0)
            {
                var p = _queue.Dequeue();
                _active = Start(p);
            }

            if (_active == null) return;

            var now = EditorApplication.timeSinceStartup;

            // Global timeout check
            if (now > _active.TimeoutAt)
            {
                Finish(_active, "TIMEOUT");
                return;
            }

            if (_active.PlayModeHosted)
            {
                var t = _active.PlayModeTracker;
                if (t == null)
                {
                    Finish(_active, "CANCELLED: host unavailable");
                    return;
                }
                if (t.Error != null)
                {
                    Finish(_active, $"RUNTIME ERROR: {t.Error.Message}\n{t.Error.StackTrace}");
                    return;
                }
                if (t.Done)
                {
                    Finish(_active, FormatResult(t.LastValue));
                }
                return;
            }

            StepEditMode(_active, now);
        }

        private Active Start(Pending p)
        {
            var now = EditorApplication.timeSinceStartup;
            var a = new Active
            {
                Root = p.Co,
                Uuid = p.Uuid,
                ResPath = p.ResPath,
                TimeoutAt = now + p.TimeoutMs / 1000.0,
                WaitKind = WaitKind.None,
            };

            if (Application.isPlaying)
            {
                var host = GetOrCreateHost();
                if (host != null)
                {
                    a.PlayModeHosted = true;
                    a.PlayModeTracker = host.Run(p.Co);
                    return a;
                }
            }

            a.Stack = new Stack<IEnumerator>();
            a.Stack.Push(p.Co);
            return a;
        }

        private void StepEditMode(Active a, double now)
        {
            // Honor pending wait
            if (a.WaitKind == WaitKind.Deadline)
            {
                if (now < a.WaitDeadline) return;
                a.WaitKind = WaitKind.None;
            }
            else if (a.WaitKind == WaitKind.Predicate)
            {
                bool satisfied;
                try { satisfied = a.WaitPredicate == null || a.WaitPredicate(); }
                catch (Exception ex) { Finish(a, $"RUNTIME ERROR: {ex.Message}\n{ex.StackTrace}"); return; }
                if (!satisfied) return;
                a.WaitKind = WaitKind.None;
                a.WaitPredicate = null;
            }

            var top = a.Stack.Peek();
            bool moved;
            try { moved = top.MoveNext(); }
            catch (Exception ex) { Finish(a, $"RUNTIME ERROR: {ex.Message}\n{ex.StackTrace}"); return; }

            if (!moved)
            {
                try { (top as IDisposable)?.Dispose(); } catch { }
                a.Stack.Pop();
                if (a.Stack.Count == 0)
                {
                    Finish(a, FormatResult(a.LastValue));
                }
                return;
            }

            InspectYield(a, top.Current);
        }

        private void InspectYield(Active a, object current)
        {
            // Check IEnumerator first so we don't store it as LastValue.
            if (current is IEnumerator nested)
            {
                a.Stack.Push(nested);
                return;
            }
            if (current == null)
            {
                return; // advance one tick
            }
            if (current is WaitForSeconds wfs)
            {
                float seconds = GetWaitForSecondsSeconds(wfs);
                a.WaitKind = WaitKind.Deadline;
                a.WaitDeadline = EditorApplication.timeSinceStartup + seconds;
                return;
            }
            if (current is WaitForSecondsRealtime wfsr)
            {
                a.WaitKind = WaitKind.Deadline;
                a.WaitDeadline = EditorApplication.timeSinceStartup + wfsr.waitTime;
                return;
            }
            if (current is CustomYieldInstruction cyi)
            {
                a.WaitKind = WaitKind.Predicate;
                a.WaitPredicate = () => !cyi.keepWaiting;
                return;
            }
            if (current is AsyncOperation aop)
            {
                a.WaitKind = WaitKind.Predicate;
                a.WaitPredicate = () => aop.isDone;
                a.LastValue = aop;
                return;
            }
            // Scalar / string / arbitrary object — record as LastValue, advance one tick.
            a.LastValue = current;
        }

        private void Finish(Active a, string response)
        {
            try { (a.Root as IDisposable)?.Dispose(); } catch { }
            if (a.PlayModeHosted && _host != null && a.PlayModeTracker != null)
            {
                _host.Stop(a.PlayModeTracker);
            }
            WriteResponse(a.ResPath, response);
            if (_active == a) _active = null;
        }

        private void DrainQueue(string reason)
        {
            while (_queue.Count > 0)
            {
                var p = _queue.Dequeue();
                try { (p.Co as IDisposable)?.Dispose(); } catch { }
                WriteResponse(p.ResPath, reason);
            }
        }

        private static string FormatResult(object v)
        {
            if (v == null) return "(ok)";
            return v.ToString();
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
                Debug.LogError($"[UnityREPL] response write failed: {ex.Message}");
            }
        }

        private ReplCoroutineHost GetOrCreateHost()
        {
            if (_host != null) return _host;
            if (!Application.isPlaying) return null;
            try
            {
                var go = new GameObject("__ReplCoroutineHost__");
                go.hideFlags = HideFlags.HideAndDontSave;
                UnityEngine.Object.DontDestroyOnLoad(go);
                _host = go.AddComponent<ReplCoroutineHost>();
                return _host;
            }
            catch (Exception ex)
            {
                Debug.LogError($"[UnityREPL] Failed to create coroutine host: {ex.Message}");
                return null;
            }
        }

        private void OnPlayModeStateChanged(PlayModeStateChange change)
        {
            if (change == PlayModeStateChange.ExitingPlayMode)
            {
                if (_active != null && _active.PlayModeHosted)
                {
                    Finish(_active, "CANCELLED: play mode exited");
                }
                _host = null; // GameObject destroyed with Play Mode scene
            }
        }

        private static float GetWaitForSecondsSeconds(WaitForSeconds wfs)
        {
            try
            {
                if (_wfsField == null)
                    _wfsField = typeof(WaitForSeconds).GetField("m_Seconds", BindingFlags.Instance | BindingFlags.NonPublic);
                if (_wfsField != null)
                    return (float)_wfsField.GetValue(wfs);
            }
            catch { }
            if (!_wfsFieldWarned)
            {
                _wfsFieldWarned = true;
                Debug.LogWarning("[UnityREPL] Cannot read WaitForSeconds.m_Seconds via reflection; advancing single tick.");
            }
            return 0f;
        }
    }
}
