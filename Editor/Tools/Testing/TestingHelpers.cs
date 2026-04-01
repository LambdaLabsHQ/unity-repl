using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor.TestTools.TestRunner.Api;

namespace NativeMcp.Editor.Tools.Testing
{
    /// <summary>
    /// Data class representing a single test result entry.
    /// Used to serialize individual test outcomes in the MCP response.
    /// </summary>
    internal class TestResultEntry
    {
        /// <summary>Short name of the test method (e.g. "MyTest").</summary>
        public string name;

        /// <summary>Fully qualified name including namespace and class (e.g. "MyNamespace.MyClass.MyTest").</summary>
        public string fullName;

        /// <summary>
        /// Test outcome status string: "Passed", "Failed", "Skipped", or "Inconclusive".
        /// </summary>
        public string status;

        /// <summary>Execution duration in seconds.</summary>
        public double duration;

        /// <summary>
        /// Failure or skip message. Null for passing tests.
        /// </summary>
        public string message;

        /// <summary>
        /// Stack trace associated with a failure. Null for passing tests.
        /// </summary>
        public string stackTrace;
    }

    /// <summary>
    /// Implements <see cref="ICallbacks"/> to collect test results during a test run.
    /// <para>
    /// <b>Lifecycle:</b> Create an instance, register it with <see cref="TestRunnerApi.RegisterCallbacks{T}"/>
    /// before calling <see cref="TestRunnerApi.Execute"/>. After the run completes (indicated by
    /// <see cref="IsFinished"/> becoming true), read the collected results and summary counts.
    /// </para>
    /// <para>
    /// <b>Callback timing:</b>
    /// <list type="bullet">
    ///   <item><see cref="ICallbacks.RunStarted"/> — fired once when the test run begins.</item>
    ///   <item><see cref="ICallbacks.TestStarted"/> — fired before each test node executes.</item>
    ///   <item><see cref="ICallbacks.TestFinished"/> — fired after each test node executes. Only leaf
    ///     nodes (non-suite) are collected to avoid double-counting fixtures and assemblies.</item>
    ///   <item><see cref="ICallbacks.RunFinished"/> — fired once when the entire run completes.
    ///     Summary counts are captured here and <see cref="OnFinished"/> is invoked if provided.</item>
    /// </list>
    /// </para>
    /// </summary>
    internal class TestResultCollector : ICallbacks
    {
        /// <summary>Whether the test run has finished (RunFinished callback received).</summary>
        public bool IsFinished { get; private set; }

        /// <summary>Individual test results (leaf nodes only).</summary>
        public List<TestResultEntry> Results { get; } = new List<TestResultEntry>();

        /// <summary>Total number of passed tests, as reported by the run summary.</summary>
        public int PassCount { get; private set; }

        /// <summary>Total number of failed tests, as reported by the run summary.</summary>
        public int FailCount { get; private set; }

        /// <summary>Total number of skipped tests, as reported by the run summary.</summary>
        public int SkipCount { get; private set; }

        /// <summary>Total run duration in seconds, as reported by the run summary.</summary>
        public double TotalDuration { get; private set; }

        /// <summary>
        /// Optional callback invoked when the run finishes.
        /// Typically used to complete a <see cref="System.Threading.Tasks.TaskCompletionSource{T}"/>.
        /// </summary>
        public Action OnFinished { get; set; }

        /// <summary>
        /// Creates a new collector.
        /// </summary>
        /// <param name="onFinished">
        /// Optional callback invoked when <see cref="ICallbacks.RunFinished"/> fires.
        /// Pass null if you intend to poll <see cref="IsFinished"/> instead.
        /// </param>
        public TestResultCollector(Action onFinished = null)
        {
            OnFinished = onFinished;
        }

        /// <summary>Called when the test run starts. No action needed.</summary>
        void ICallbacks.RunStarted(ITestAdaptor testsToRun) { }

        /// <summary>Called before each test node executes. No action needed.</summary>
        void ICallbacks.TestStarted(ITestAdaptor test) { }

        /// <summary>
        /// Called after each test node finishes executing.
        /// Only leaf nodes (where <see cref="ITestResultAdaptor.HasChildren"/> is false)
        /// are recorded, to avoid double-counting suite/fixture-level results.
        /// </summary>
        void ICallbacks.TestFinished(ITestResultAdaptor result)
        {
            if (!result.HasChildren)
            {
                Results.Add(new TestResultEntry
                {
                    name = result.Name,
                    fullName = result.FullName,
                    status = result.TestStatus.ToString(),
                    duration = result.Duration,
                    message = string.IsNullOrEmpty(result.Message) ? null : result.Message,
                    stackTrace = string.IsNullOrEmpty(result.StackTrace) ? null : result.StackTrace
                });
            }
        }

        /// <summary>
        /// Called when the entire test run completes.
        /// Captures summary counts from the root result and invokes <see cref="OnFinished"/>.
        /// </summary>
        void ICallbacks.RunFinished(ITestResultAdaptor result)
        {
            PassCount = result.PassCount;
            FailCount = result.FailCount;
            SkipCount = result.SkipCount;
            TotalDuration = result.Duration;
            IsFinished = true;
            OnFinished?.Invoke();
        }
    }

    /// <summary>
    /// Utility methods for working with the Unity Test Runner's test tree structure.
    /// </summary>
    internal static class TestTreeHelper
    {
        /// <summary>
        /// Data class representing a single test entry in the test tree (for listing).
        /// </summary>
        internal class TestListEntry
        {
            /// <summary>Short name of the test method.</summary>
            public string name;

            /// <summary>Fully qualified name including namespace and class.</summary>
            public string fullName;

            /// <summary>Categories applied to this test (may be empty).</summary>
            public string[] categories;
        }

        /// <summary>
        /// Recursively walks an <see cref="ITestAdaptor"/> tree and collects all leaf nodes
        /// (actual test methods, not suites or fixtures) into a flat list.
        /// </summary>
        /// <param name="root">The root of the test tree, as returned by
        /// <see cref="TestRunnerApi.RetrieveTestList"/>.</param>
        /// <returns>A flat list of all runnable test entries.</returns>
        public static List<TestListEntry> FlattenTestTree(ITestAdaptor root)
        {
            var results = new List<TestListEntry>();
            CollectLeafNodes(root, results);
            return results;
        }

        /// <summary>
        /// Recursively traverses the test tree. If a node has no children, it is a leaf
        /// (an actual test method) and is added to the list. Suite/fixture nodes are
        /// traversed but not added.
        /// </summary>
        private static void CollectLeafNodes(ITestAdaptor node, List<TestListEntry> list)
        {
            if (node == null) return;

            if (!node.HasChildren)
            {
                list.Add(new TestListEntry
                {
                    name = node.Name,
                    fullName = node.FullName,
                    categories = node.Categories ?? Array.Empty<string>()
                });
                return;
            }

            foreach (var child in node.Children)
            {
                CollectLeafNodes(child, list);
            }
        }
    }
}
