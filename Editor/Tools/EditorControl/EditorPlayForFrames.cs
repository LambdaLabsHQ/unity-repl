using System;
using System.Threading.Tasks;
using NativeMcp.Editor.Bridge;
using NativeMcp.Editor.Helpers;
using NativeMcp.Editor.Tools;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine;

namespace NativeMcp.Editor.Tools.EditorControl
{
    [McpForUnityTool("editor_play_for_frames", Internal = true,
        Description = "Play the game for a specified number of frames, then pause. Supports recovery across domain reloads.")]
    public static class EditorPlayForFrames
    {
        private const int DefaultTimeoutSeconds = 30;
        private const string PendingKey = NativeMcpKeys.PendingPlayForFrames;

        public class Parameters
        {
            [ToolParameter("Number of frames to advance (>= 1).")]
            public int frames { get; set; }

            [ToolParameter("Timeout in seconds. Default 30.", Required = false)]
            public int? timeout { get; set; }
        }

        // Active operation state (in-memory, lost on domain reload)
        private static TaskCompletionSource<object> _activeTcs;
        private static EditorApplication.CallbackFunction _activeTick;
        private static int _framesRemaining;
        private static int _framesRequested;
        private static int _startFrame;
        private static DateTime _deadlineUtc;
        private static int _timeoutSeconds;
        private static string _activeOperationId;

        // Domain reload recovery flag (set by [InitializeOnLoad] after reload)
        private static bool _hasPendingRecovery;

        [Serializable]
        private class PendingState
        {
            public string operationId;
            public int framesRemaining;
            public int framesRequested;
            public string deadlineUtc;
        }

        [InitializeOnLoad]
        private static class ReloadWatcher
        {
            static ReloadWatcher()
            {
                AssemblyReloadEvents.beforeAssemblyReload += OnBeforeReload;
                AssemblyReloadEvents.afterAssemblyReload += OnAfterReload;
            }
        }

        private static void OnBeforeReload()
        {
            if (_activeTcs == null || _activeTcs.Task.IsCompleted)
                return;

            // Snapshot current state to SessionState
            var state = new PendingState
            {
                operationId = _activeOperationId,
                framesRemaining = _framesRemaining,
                framesRequested = _framesRequested,
                deadlineUtc = _deadlineUtc.ToString("O")
            };
            SessionState.SetString(PendingKey, JsonUtility.ToJson(state));
            Debug.Log($"[NativeMcp] PlayForFrames: saved pending state before domain reload ({_framesRemaining} frames remaining)");

            // Clean up in-memory state (will be lost anyway)
            CancelActive(silent: true);
        }

        private static void OnAfterReload()
        {
            string json = SessionState.GetString(PendingKey, "");
            if (!string.IsNullOrEmpty(json))
            {
                _hasPendingRecovery = true;
                Debug.Log("[NativeMcp] PlayForFrames: pending operation detected after domain reload, awaiting bridge replay.");
            }
        }

        public static async Task<object> HandleCommand(JObject @params)
        {
            try
            {
                int frames = @params["frames"]?.Value<int>() ?? 0;
                int timeout = @params["timeout"]?.Value<int>() ?? DefaultTimeoutSeconds;

                if (frames < 1)
                    return new ErrorResponse("frames must be >= 1.");

                if (!EditorApplication.isPlaying)
                {
                    if (EditorApplication.isPlayingOrWillChangePlaymode)
                        return new ErrorResponse("Play mode is transitioning. Wait and try again.");
                    return new ErrorResponse("Not in play mode. Call 'play' first.");
                }

                // Check for domain reload recovery
                if (_hasPendingRecovery)
                {
                    _hasPendingRecovery = false;
                    string json = SessionState.GetString(PendingKey, "");
                    if (!string.IsNullOrEmpty(json))
                    {
                        var recovered = JsonUtility.FromJson<PendingState>(json);
                        SessionState.EraseString(PendingKey);

                        if (recovered.framesRemaining > 0)
                        {
                            Debug.Log($"[NativeMcp] PlayForFrames: recovering from domain reload, {recovered.framesRemaining} frames remaining.");
                            frames = recovered.framesRemaining;
                            var recoveredDeadline = DateTime.Parse(recovered.deadlineUtc).ToUniversalTime();
                            if (DateTime.UtcNow >= recoveredDeadline)
                            {
                                EditorApplication.isPaused = true;
                                return new ErrorResponse("Timed out during domain reload.", new
                                {
                                    frames_requested = recovered.framesRequested,
                                    frames_elapsed = recovered.framesRequested - recovered.framesRemaining
                                });
                            }
                            timeout = (int)Math.Max(1, (recoveredDeadline - DateTime.UtcNow).TotalSeconds);
                        }
                    }
                }

                // Cancel any previous active operation
                CancelActive(silent: false);

                // Set up new operation
                _activeOperationId = Guid.NewGuid().ToString("N");
                _framesRequested = frames;
                _framesRemaining = frames;
                _startFrame = Time.frameCount;
                _timeoutSeconds = timeout;
                _deadlineUtc = DateTime.UtcNow.AddSeconds(timeout);
                _activeTcs = new TaskCompletionSource<object>(
                    TaskCreationOptions.RunContinuationsAsynchronously);

                // Unpause if paused
                EditorApplication.isPaused = false;

                // Start tick loop
                EditorNudge.BeginNudge();

                _activeTick = Tick;
                EditorApplication.update += _activeTick;

                return await _activeTcs.Task;
            }
            catch (Exception e)
            {
                return new ErrorResponse($"Error in play_for_frames: {e.Message}");
            }
        }

        private static void Tick()
        {
            if (_activeTcs == null || _activeTcs.Task.IsCompleted)
            {
                Cleanup();
                return;
            }

            // Detect exit from play mode
            if (!EditorApplication.isPlaying)
            {
                int elapsed = _framesRequested - _framesRemaining;
                Cleanup();
                _activeTcs.TrySetResult(new ErrorResponse("Play mode exited during play_for_frames.", new
                {
                    frames_requested = _framesRequested,
                    frames_elapsed = elapsed
                }));
                return;
            }

            // Check timeout
            if (DateTime.UtcNow >= _deadlineUtc)
            {
                int elapsed = _framesRequested - _framesRemaining;
                EditorApplication.isPaused = true;
                Cleanup();
                _activeTcs.TrySetResult(new ErrorResponse($"Timed out after {_timeoutSeconds}s.", new
                {
                    frames_requested = _framesRequested,
                    frames_elapsed = elapsed
                }));
                return;
            }

            // Count frame
            _framesRemaining--;

            if (_framesRemaining <= 0)
            {
                int endFrame = Time.frameCount;
                int elapsed = _framesRequested;
                EditorApplication.isPaused = true;
                Cleanup();
                _activeTcs.TrySetResult(new SuccessResponse($"Advanced {elapsed} frames and paused.", new
                {
                    frames_requested = elapsed,
                    frames_elapsed = elapsed,
                    start_frame = _startFrame,
                    end_frame = endFrame
                }));
            }
        }

        private static void CancelActive(bool silent)
        {
            if (_activeTick != null)
            {
                EditorApplication.update -= _activeTick;
                _activeTick = null;
            }

            EditorNudge.EndNudge();
            SessionState.EraseString(PendingKey);

            if (_activeTcs != null && !_activeTcs.Task.IsCompleted)
            {
                if (!silent)
                {
                    _activeTcs.TrySetResult(
                        new ErrorResponse("Cancelled by new play_for_frames call."));
                }
                else
                {
                    _activeTcs.TrySetCanceled();
                }
            }

            _activeTcs = null;
            _activeOperationId = null;
        }

        private static void Cleanup()
        {
            if (_activeTick != null)
            {
                EditorApplication.update -= _activeTick;
                _activeTick = null;
            }
            EditorNudge.EndNudge();
            SessionState.EraseString(PendingKey);
        }
    }
}
