using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using NativeMcp.Editor.Bridge;
using NativeMcp.Editor.Helpers;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEditor.TestTools.TestRunner.Api;
using UnityEngine;

namespace NativeMcp.Editor.Tools.Testing
{
    /// <summary>
    /// MCP tool that executes Unity Test Runner tests and returns structured results.
    /// <para>
    /// Supports running all tests for a given mode, or a filtered subset by test names,
    /// categories, or assembly names. When no filter parameters are provided, all tests
    /// in the specified mode are executed (equivalent to "Run All").
    /// </para>
    /// <para>
    /// <b>EditMode tests</b> are run synchronously (<see cref="ExecutionSettings.runSynchronously"/> = true),
    /// meaning <see cref="TestRunnerApi.Execute"/> blocks until all tests complete, and results are
    /// available immediately from the registered <see cref="TestResultCollector"/>.
    /// </para>
    /// <para>
    /// <b>PlayMode tests</b> are run asynchronously. The tool registers a <see cref="TestResultCollector"/>
    /// whose <see cref="TestResultCollector.OnFinished"/> callback completes a
    /// <see cref="TaskCompletionSource{T}"/>. An <see cref="EditorApplication.update"/> tick
    /// monitors for timeout. <see cref="EditorNudge"/> keeps the editor ticking when backgrounded.
    /// </para>
    /// </summary>
    [McpForUnityTool("run_tests", Internal = true, Description =
        "Runs Unity Test Runner tests and returns results. " +
        "Supports EditMode (synchronous) and PlayMode (asynchronous) tests. " +
        "When no filter is provided, runs all tests for the given mode. " +
        "Filter by testNames, categoryNames, or assemblyNames to run specific tests.")]
    public static class RunTests
    {
        private const int DefaultTimeoutSeconds = 120;
        private const string PendingTestRunKey = NativeMcpKeys.PendingTestRun;

        [Serializable]
        private struct PendingTestRun
        {
            public string testMode;
            public string filterHash;
            public string startTimeUtc;
            public string resultFilePath;
        }

        public class Parameters
        {
            [ToolParameter("Test mode: 'EditMode' or 'PlayMode' (default 'EditMode')",
                Required = false, DefaultValue = "EditMode")]
            public string testMode { get; set; }

            [ToolParameter("Array of fully qualified test names to run (e.g. 'MyNamespace.MyClass.MyTest'). " +
                           "When omitted, all tests in the given mode are run.",
                Required = false)]
            public string[] testNames { get; set; }

            [ToolParameter("Array of NUnit category names to filter by. " +
                           "Only tests with at least one matching category will run.",
                Required = false)]
            public string[] categoryNames { get; set; }

            [ToolParameter("Array of test assembly names to filter by (without .dll extension). " +
                           "Only tests in matching assemblies will run.",
                Required = false)]
            public string[] assemblyNames { get; set; }

            [ToolParameter("Maximum time in seconds to wait for the test run to complete (default 120). " +
                           "If exceeded, returns a timeout error with any partial results collected so far.",
                Required = false, DefaultValue = "120")]
            public int timeoutSeconds { get; set; }
        }

        /// <summary>
        /// Handles the run_tests MCP command.
        /// <para>
        /// Parses filter parameters from the JSON input, constructs a <see cref="Filter"/> and
        /// <see cref="ExecutionSettings"/>, registers a <see cref="TestResultCollector"/>, and
        /// executes the tests via <see cref="TestRunnerApi.Execute"/>.
        /// </para>
        /// </summary>
        /// <param name="params">
        /// JSON object with optional fields: testMode (string), testNames (string[]),
        /// categoryNames (string[]), assemblyNames (string[]), timeoutSeconds (int).
        /// All filter arrays support both camelCase and snake_case keys.
        /// </param>
        /// <returns>
        /// A <see cref="SuccessResponse"/> with data containing: testMode, passCount, failCount,
        /// skipCount, duration, and results array (each with name, fullName, status, duration,
        /// message, stackTrace). On failure or timeout, returns an <see cref="ErrorResponse"/>.
        /// </returns>
        public static async Task<object> HandleCommand(JObject @params)
        {
            TestRunnerApi api = null;
            TestResultCollector collector = null;
            EditorApplication.CallbackFunction tick = null;
            bool nudgeStarted = false;

            try
            {
                // ── Parse parameters ──────────────────────────────────────────
                string testModeStr = ParamCoercion.CoerceString(
                    @params?["testMode"] ?? @params?["test_mode"], "EditMode");

                if (!TryParseTestMode(testModeStr, out var testMode))
                {
                    return new ErrorResponse(
                        $"Invalid testMode '{testModeStr}'. Use 'EditMode' or 'PlayMode'.");
                }

                bool isEditMode = (testMode & TestMode.EditMode) != 0
                                  && (testMode & TestMode.PlayMode) == 0;
                string canonicalMode = isEditMode ? "EditMode" : "PlayMode";

                string[] testNames = ParseStringArray(
                    @params?["testNames"] ?? @params?["test_names"]);
                string[] categoryNames = ParseStringArray(
                    @params?["categoryNames"] ?? @params?["category_names"]);
                string[] assemblyNames = ParseStringArray(
                    @params?["assemblyNames"] ?? @params?["assembly_names"]);
                int timeoutSeconds = ParamCoercion.CoerceInt(
                    @params?["timeoutSeconds"] ?? @params?["timeout_seconds"],
                    DefaultTimeoutSeconds);

                // ── PlayMode recovery: check for pending test run after domain reload ──
                if (!isEditMode)
                {
                    string currentHash = ComputeFilterHash(canonicalMode, testNames, categoryNames, assemblyNames);
                    string pending = SessionState.GetString(PendingTestRunKey, "");
                    if (!string.IsNullOrEmpty(pending))
                    {
                        var info = JsonUtility.FromJson<PendingTestRun>(pending);
                        if (info.testMode == canonicalMode && info.filterHash == currentHash)
                        {
                            Debug.Log("[NativeMcp] Pending PlayMode test run detected after domain reload, polling for TestResults.xml...");
                            var recovered = await PollForTestResults(info, timeoutSeconds);
                            SessionState.EraseString(PendingTestRunKey);
                            if (recovered != null)
                            {
                                Debug.Log($"[NativeMcp] PlayMode test results recovered from {info.resultFilePath}");
                                return recovered;
                            }
                            Debug.LogWarning("[NativeMcp] TestResults.xml not found within timeout, re-running tests");
                        }
                        else
                        {
                            Debug.Log("[NativeMcp] Clearing stale pending test run marker (different request)");
                            SessionState.EraseString(PendingTestRunKey);
                        }
                    }
                }

                // ── Build filter and execution settings ───────────────────────
                var filter = new Filter
                {
                    testMode = testMode,
                    testNames = testNames,
                    categoryNames = categoryNames,
                    assemblyNames = assemblyNames
                };

                var settings = new ExecutionSettings(filter)
                {
                    runSynchronously = isEditMode
                };

                // ── Mark pending run for PlayMode (before Execute, survives domain reload) ──
                if (!isEditMode)
                {
                    string filterHash = ComputeFilterHash(canonicalMode, testNames, categoryNames, assemblyNames);
                    SessionState.SetString(PendingTestRunKey, JsonUtility.ToJson(new PendingTestRun
                    {
                        testMode = canonicalMode,
                        filterHash = filterHash,
                        startTimeUtc = DateTime.UtcNow.ToString("o"),
                        resultFilePath = Path.Combine(Application.persistentDataPath, "TestResults.xml")
                    }));
                }

                // ── Set up API and callbacks ──────────────────────────────────
                api = ScriptableObject.CreateInstance<TestRunnerApi>();
                var tcs = new TaskCompletionSource<object>(
                    TaskCreationOptions.RunContinuationsAsynchronously);

                collector = new TestResultCollector(onFinished: () =>
                {
                    tcs.TrySetResult(BuildSuccessResponse(testModeStr, collector));
                });

                api.RegisterCallbacks(collector);

                // ── Execute ───────────────────────────────────────────────────
                api.Execute(settings);

                if (isEditMode && collector.IsFinished)
                {
                    api.UnregisterCallbacks(collector);
                    UnityEngine.Object.DestroyImmediate(api);
                    return BuildSuccessResponse(testModeStr, collector);
                }

                // ── Async wait (PlayMode, or EditMode if not yet finished) ────
                var start = DateTime.UtcNow;
                var timeout = TimeSpan.FromSeconds(timeoutSeconds);

                tick = () =>
                {
                    if (tcs.Task.IsCompleted)
                    {
                        CleanupTestRun(tick, api, collector, nudgeStarted);
                        return;
                    }

                    if ((DateTime.UtcNow - start) > timeout)
                    {
                        CleanupTestRun(tick, api, collector, nudgeStarted);
                        tcs.TrySetResult(new ErrorResponse("test_run_timeout", new
                        {
                            timeoutSeconds,
                            partialResults = new
                            {
                                testMode = testModeStr,
                                passCount = collector.PassCount,
                                failCount = collector.FailCount,
                                skipCount = collector.SkipCount,
                                duration = collector.TotalDuration,
                                resultsCollected = collector.Results.Count
                            }
                        }));
                    }
                };

                EditorApplication.update += tick;
                EditorNudge.BeginNudge();
                nudgeStarted = true;
                return await tcs.Task;
            }
            catch (Exception ex)
            {
                CleanupTestRun(tick, api, collector, nudgeStarted);
                McpLog.Error($"[RunTests] {ex.Message}");
                return new ErrorResponse($"Error running tests: {ex.Message}");
            }
        }

        private static void CleanupTestRun(
            EditorApplication.CallbackFunction tick,
            TestRunnerApi api,
            TestResultCollector collector,
            bool nudgeStarted)
        {
            if (tick != null) EditorApplication.update -= tick;
            if (nudgeStarted) EditorNudge.EndNudge();
            SessionState.EraseString(PendingTestRunKey);
            if (api != null && collector != null) api.UnregisterCallbacks(collector);
            if (api != null) UnityEngine.Object.DestroyImmediate(api);
        }

        /// <summary>
        /// Builds a <see cref="SuccessResponse"/> from the collected test results.
        /// </summary>
        /// <param name="testModeStr">The test mode string for inclusion in the response.</param>
        /// <param name="collector">The <see cref="TestResultCollector"/> containing results.</param>
        /// <returns>A structured success response with summary counts and per-test details.</returns>
        private static object BuildSuccessResponse(string testModeStr, TestResultCollector collector)
        {
            return new SuccessResponse(
                $"Test run complete: {collector.PassCount} passed, {collector.FailCount} failed, " +
                $"{collector.SkipCount} skipped ({collector.TotalDuration:F2}s).",
                new
                {
                    testMode = testModeStr,
                    passCount = collector.PassCount,
                    failCount = collector.FailCount,
                    skipCount = collector.SkipCount,
                    duration = collector.TotalDuration,
                    results = collector.Results.Select(r => new
                    {
                        r.name,
                        r.fullName,
                        r.status,
                        r.duration,
                        r.message,
                        r.stackTrace
                    }).ToArray()
                });
        }

        /// <summary>
        /// Parses a string test mode into the <see cref="TestMode"/> enum.
        /// Accepts "EditMode", "edit_mode", "PlayMode", "play_mode" (case-insensitive).
        /// </summary>
        internal static bool TryParseTestMode(string str, out TestMode mode)
        {
            if (string.IsNullOrEmpty(str))
            {
                mode = TestMode.EditMode;
                return true;
            }

            string normalized = str.Replace("_", "").ToLowerInvariant();
            switch (normalized)
            {
                case "editmode":
                    mode = TestMode.EditMode;
                    return true;
                case "playmode":
                    mode = TestMode.PlayMode;
                    return true;
                default:
                    mode = default;
                    return false;
            }
        }

        /// <summary>
        /// Extracts a string array from a <see cref="JToken"/>.
        /// Returns null if the token is null or not an array, which means "no filter"
        /// (all tests match). An empty array is treated the same as null.
        /// </summary>
        internal static string[] ParseStringArray(JToken token)
        {
            if (token == null) return null;

            if (token is JArray arr)
            {
                var result = arr.Select(t => t.ToString()).Where(s => !string.IsNullOrEmpty(s)).ToArray();
                return result.Length > 0 ? result : null;
            }

            // Single string value — wrap in array for convenience
            string single = token.ToString();
            return string.IsNullOrEmpty(single) ? null : new[] { single };
        }

        /// <summary>
        /// Computes a deterministic hash string from filter parameters to match recovery requests.
        /// Uses SHA-256 (first 8 hex chars) for stability across .NET versions.
        /// </summary>
        internal static string ComputeFilterHash(string testMode, string[] testNames,
            string[] categoryNames, string[] assemblyNames)
        {
            string joined = string.Join("\x1F",
                testMode ?? "",
                testNames != null ? string.Join("\x1E", testNames) : "",
                categoryNames != null ? string.Join("\x1E", categoryNames) : "",
                assemblyNames != null ? string.Join("\x1E", assemblyNames) : "");
            using (var sha = SHA256.Create())
            {
                var bytes = sha.ComputeHash(Encoding.UTF8.GetBytes(joined));
                return BitConverter.ToString(bytes, 0, 4).Replace("-", "").ToLowerInvariant();
            }
        }

        /// <summary>
        /// Polls for TestResults.xml written by Unity Test Runner after a domain reload.
        /// Returns a parsed <see cref="SuccessResponse"/> if the file is found and updated
        /// after the pending run's start time, or null on timeout.
        /// </summary>
        private static async Task<object> PollForTestResults(PendingTestRun info, int timeoutSeconds)
        {
            var startTime = DateTime.Parse(info.startTimeUtc, null,
                DateTimeStyles.RoundtripKind);
            var deadline = DateTime.UtcNow.AddSeconds(Math.Max(timeoutSeconds, 30));

            while (DateTime.UtcNow < deadline)
            {
                if (File.Exists(info.resultFilePath))
                {
                    var mtime = File.GetLastWriteTimeUtc(info.resultFilePath);
                    if (mtime > startTime)
                    {
                        try
                        {
                            return TestResultXmlParser.Parse(info.resultFilePath, info.testMode);
                        }
                        catch (IOException ex)
                        {
                            Debug.LogWarning($"[NativeMcp] TestResults.xml read failed (will retry): {ex.Message}");
                        }
                        catch (System.Xml.XmlException ex)
                        {
                            Debug.LogWarning($"[NativeMcp] TestResults.xml parse failed (will retry): {ex.Message}");
                        }
                    }
                }

                await Task.Delay(1000);
            }

            return null;
        }
    }
}
