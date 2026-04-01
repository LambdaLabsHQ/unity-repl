using System.Globalization;
using System.Linq;
using System.Xml.Linq;
using NativeMcp.Editor.Helpers;

namespace NativeMcp.Editor.Tools.Testing
{
    /// <summary>
    /// Parses NUnit3 XML test result files (TestResults.xml) produced by Unity Test Runner.
    /// Used for recovering PlayMode test results after a domain reload.
    /// </summary>
    internal static class TestResultXmlParser
    {
        /// <summary>
        /// Parses a NUnit3 XML result file and returns a <see cref="SuccessResponse"/>
        /// with the same shape as <see cref="RunTests.BuildSuccessResponse"/>.
        /// </summary>
        public static object Parse(string path, string testMode)
        {
            var doc = XDocument.Load(path);
            var root = doc.Root; // <test-run>

            int passed = int.Parse(root.Attribute("passed")?.Value ?? "0");
            int failed = int.Parse(root.Attribute("failed")?.Value ?? "0");
            int skipped = int.Parse(root.Attribute("skipped")?.Value ?? "0");
            double duration = double.Parse(
                root.Attribute("duration")?.Value ?? "0",
                CultureInfo.InvariantCulture);

            var results = root.Descendants("test-case").Select(tc =>
            {
                string rawStatus = tc.Attribute("result")?.Value ?? "Unknown";
                return new TestResultEntry
                {
                    name = tc.Attribute("name")?.Value,
                    fullName = tc.Attribute("fullname")?.Value,
                    status = NormalizeStatus(rawStatus),
                    duration = double.Parse(
                        tc.Attribute("duration")?.Value ?? "0",
                        CultureInfo.InvariantCulture),
                    message = tc.Element("failure")?.Element("message")?.Value
                           ?? tc.Element("reason")?.Element("message")?.Value,
                    stackTrace = tc.Element("failure")?.Element("stack-trace")?.Value
                };
            }).ToList();

            return new SuccessResponse(
                $"Test run complete: {passed} passed, {failed} failed, " +
                $"{skipped} skipped ({duration:F2}s).",
                new
                {
                    testMode,
                    passCount = passed,
                    failCount = failed,
                    skipCount = skipped,
                    duration,
                    results = results.Select(r => new
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
        /// Maps NUnit result strings to the values used by <see cref="TestResultCollector"/>.
        /// "Inconclusive" is mapped to "Skipped" for consistency.
        /// </summary>
        private static string NormalizeStatus(string nunitResult)
        {
            if (nunitResult == "Inconclusive") return "Skipped";
            return nunitResult;
        }
    }
}
