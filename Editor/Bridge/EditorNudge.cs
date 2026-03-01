using System;
using System.Threading;
using UnityEditor;
using UnityEngine;

namespace NativeMcp.Editor.Bridge
{
    /// <summary>
    /// Nudges the Unity Editor to process its update loop even when backgrounded.
    /// Uses two complementary strategies:
    /// 1. EditorApplication.QueuePlayerLoopUpdate() — official API, sets an internal flag
    /// 2. Win32 PostMessage(WM_NULL) — wakes the message pump without stealing focus
    ///
    /// Call <see cref="BeginNudge"/> when a background→main-thread task is enqueued,
    /// and <see cref="EndNudge"/> when it completes.  The timer only runs while there
    /// are pending tasks, so idle cost is zero.
    /// </summary>
    [InitializeOnLoad]
    internal static class EditorNudge
    {
        private static int _pendingCount;
        private static Timer _nudgeTimer;
        private static readonly object Lock = new();

        // ~30 Hz nudge frequency — fast enough for responsive MCP, low enough to be negligible cost
        private const int NudgeIntervalMs = 32;

#if UNITY_EDITOR_WIN
        [System.Runtime.InteropServices.DllImport("user32.dll")]
        private static extern bool PostMessage(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

        private const uint WM_NULL = 0x0000;

        // Cached window handle (refreshed after domain reload via InitializeOnLoad)
        private static IntPtr _unityHwnd;
#endif

        static EditorNudge()
        {
#if UNITY_EDITOR_WIN
            CacheWindowHandle();
#endif
        }

#if UNITY_EDITOR_WIN
        private static void CacheWindowHandle()
        {
            try
            {
                var proc = System.Diagnostics.Process.GetCurrentProcess();
                _unityHwnd = proc.MainWindowHandle;
            }
            catch
            {
                _unityHwnd = IntPtr.Zero;
            }
        }
#endif

        /// <summary>
        /// Begin nudging. Call when a background→main-thread task is enqueued.
        /// Thread-safe; supports nested calls (reference-counted).
        /// </summary>
        public static void BeginNudge()
        {
            lock (Lock)
            {
                _pendingCount++;
                if (_pendingCount == 1)
                {
                    // First pending request — start the timer
                    _nudgeTimer = new Timer(OnNudgeTick, null, 0, NudgeIntervalMs);
                }
            }
        }

        /// <summary>
        /// End nudging. Call when the main-thread task completes or is cancelled.
        /// </summary>
        public static void EndNudge()
        {
            lock (Lock)
            {
                _pendingCount = Math.Max(0, _pendingCount - 1);
                if (_pendingCount == 0 && _nudgeTimer != null)
                {
                    _nudgeTimer.Dispose();
                    _nudgeTimer = null;
                }
            }
        }

        private static void OnNudgeTick(object state)
        {
            // Strategy 1: Official API — request Unity to pump the player loop
            try
            {
                EditorApplication.QueuePlayerLoopUpdate();
            }
            catch
            {
                // Swallow — may fail during domain reload
            }

#if UNITY_EDITOR_WIN
            // Strategy 2: Post a harmless WM_NULL message to wake Unity's Win32 message pump.
            // Unlike SetForegroundWindow, this does NOT change window Z-order or steal focus.
            // It simply forces the message loop to iterate, which triggers
            // EditorApplication.update processing.
            try
            {
                if (_unityHwnd != IntPtr.Zero)
                {
                    PostMessage(_unityHwnd, WM_NULL, IntPtr.Zero, IntPtr.Zero);
                }
            }
            catch
            {
                // Swallow — handle may become invalid after window recreation
            }
#endif
        }
    }
}
