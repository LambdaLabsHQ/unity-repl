using System;
using System.Linq;
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
    /// MCP tool that retrieves the list of available tests from the Unity Test Runner.
    /// <para>
    /// Returns a flat list of all runnable test methods (leaf nodes) for the specified
    /// test mode. Each entry includes the test name, fully qualified name, and categories.
    /// </para>
    /// <para>
    /// The test tree is retrieved asynchronously via <see cref="TestRunnerApi.RetrieveTestList"/>.
    /// The method uses a <see cref="TaskCompletionSource{T}"/> with
    /// <see cref="EditorApplication.update"/> polling and <see cref="EditorNudge"/> to ensure
    /// the editor keeps ticking while waiting for the callback, even when backgrounded.
    /// </para>
    /// </summary>
    [McpForUnityTool("list_tests", Internal = true, Description =
        "Retrieves the list of available Unity tests. " +
        "Returns all runnable test methods with their fully qualified names and categories. " +
        "Use testMode to choose EditMode or PlayMode tests.")]
    public static class ListTests
    {
        private const int DefaultTimeoutSeconds = 30;

        public class Parameters
        {
            [ToolParameter("Test mode: 'EditMode' or 'PlayMode' (default 'EditMode')",
                Required = false, DefaultValue = "EditMode")]
            public string testMode { get; set; }
        }

        /// <summary>
        /// Handles the list_tests MCP command.
        /// <para>
        /// Creates a <see cref="TestRunnerApi"/> instance, calls
        /// <see cref="TestRunnerApi.RetrieveTestList(TestMode, Action{ITestAdaptor})"/>,
        /// and waits for the callback with a timeout. The returned test tree is flattened
        /// to a list of leaf test methods via <see cref="TestTreeHelper.FlattenTestTree"/>.
        /// </para>
        /// </summary>
        /// <param name="params">
        /// JSON object with optional "testMode" (string: "EditMode" or "PlayMode", default "EditMode").
        /// </param>
        /// <returns>
        /// A <see cref="SuccessResponse"/> with data containing:
        /// testMode (string), testCount (int), tests (array of {name, fullName, categories}).
        /// On failure, returns an <see cref="ErrorResponse"/>.
        /// </returns>
        public static async Task<object> HandleCommand(JObject @params)
        {
            try
            {
                string testModeStr = ParamCoercion.CoerceString(
                    @params?["testMode"] ?? @params?["test_mode"], "EditMode");

                if (!TryParseTestMode(testModeStr, out var testMode))
                {
                    return new ErrorResponse(
                        $"Invalid testMode '{testModeStr}'. Use 'EditMode' or 'PlayMode'.");
                }

                var tcs = new TaskCompletionSource<object>(
                    TaskCreationOptions.RunContinuationsAsynchronously);
                var api = ScriptableObject.CreateInstance<TestRunnerApi>();
                var start = DateTime.UtcNow;
                var timeout = TimeSpan.FromSeconds(DefaultTimeoutSeconds);

                api.RetrieveTestList(testMode, testRoot =>
                {
                    try
                    {
                        var tests = TestTreeHelper.FlattenTestTree(testRoot);
                        tcs.TrySetResult(new SuccessResponse(
                            $"Found {tests.Count} {testModeStr} test(s).",
                            new
                            {
                                testMode = testModeStr,
                                testCount = tests.Count,
                                tests = tests.Select(t => new
                                {
                                    t.name,
                                    t.fullName,
                                    t.categories
                                }).ToArray()
                            }));
                    }
                    catch (Exception ex)
                    {
                        tcs.TrySetResult(new ErrorResponse($"Error processing test list: {ex.Message}"));
                    }
                    finally
                    {
                        UnityEngine.Object.DestroyImmediate(api);
                    }
                });

                // Poll for timeout while waiting for the callback
                void Tick()
                {
                    if (tcs.Task.IsCompleted)
                    {
                        EditorApplication.update -= Tick;
                        EditorNudge.EndNudge();
                        return;
                    }

                    if ((DateTime.UtcNow - start) > timeout)
                    {
                        EditorApplication.update -= Tick;
                        EditorNudge.EndNudge();
                        UnityEngine.Object.DestroyImmediate(api);
                        tcs.TrySetResult(new ErrorResponse("list_tests_timeout",
                            new { timeoutSeconds = DefaultTimeoutSeconds }));
                    }
                }

                EditorApplication.update += Tick;
                EditorNudge.BeginNudge();
                return await tcs.Task;
            }
            catch (Exception ex)
            {
                McpLog.Error($"[ListTests] {ex.Message}");
                return new ErrorResponse($"Error listing tests: {ex.Message}");
            }
        }

        /// <summary>
        /// Parses a string test mode into the <see cref="TestMode"/> enum.
        /// Accepts "EditMode", "edit_mode", "PlayMode", "play_mode" (case-insensitive).
        /// </summary>
        private static bool TryParseTestMode(string str, out TestMode mode)
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
    }
}
