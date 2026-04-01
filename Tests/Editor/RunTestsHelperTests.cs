using NativeMcp.Editor.Tools.Testing;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using UnityEditor.TestTools.TestRunner.Api;

namespace NativeMcp.Editor.Tests
{
    [TestFixture]
    public class RunTestsHelperTests
    {
        // --- TryParseTestMode ---

        [Test]
        public void TryParseTestMode_EditMode_ReturnsTrue()
        {
            Assert.IsTrue(RunTests.TryParseTestMode("EditMode", out var mode));
            Assert.AreEqual(TestMode.EditMode, mode);
        }

        [Test]
        public void TryParseTestMode_PlayMode_ReturnsTrue()
        {
            Assert.IsTrue(RunTests.TryParseTestMode("PlayMode", out var mode));
            Assert.AreEqual(TestMode.PlayMode, mode);
        }

        [Test]
        public void TryParseTestMode_SnakeCase_EditMode_ReturnsTrue()
        {
            Assert.IsTrue(RunTests.TryParseTestMode("edit_mode", out var mode));
            Assert.AreEqual(TestMode.EditMode, mode);
        }

        [Test]
        public void TryParseTestMode_SnakeCase_PlayMode_ReturnsTrue()
        {
            Assert.IsTrue(RunTests.TryParseTestMode("play_mode", out var mode));
            Assert.AreEqual(TestMode.PlayMode, mode);
        }

        [Test]
        public void TryParseTestMode_UpperCase_ReturnsTrue()
        {
            Assert.IsTrue(RunTests.TryParseTestMode("PLAYMODE", out var mode));
            Assert.AreEqual(TestMode.PlayMode, mode);
        }

        [Test]
        public void TryParseTestMode_Null_DefaultsToEditMode()
        {
            Assert.IsTrue(RunTests.TryParseTestMode(null, out var mode));
            Assert.AreEqual(TestMode.EditMode, mode);
        }

        [Test]
        public void TryParseTestMode_Empty_DefaultsToEditMode()
        {
            Assert.IsTrue(RunTests.TryParseTestMode("", out var mode));
            Assert.AreEqual(TestMode.EditMode, mode);
        }

        [Test]
        public void TryParseTestMode_Invalid_ReturnsFalse()
        {
            Assert.IsFalse(RunTests.TryParseTestMode("BothModes", out _));
        }

        // --- ParseStringArray ---

        [Test]
        public void ParseStringArray_Null_ReturnsNull()
        {
            Assert.IsNull(RunTests.ParseStringArray(null));
        }

        [Test]
        public void ParseStringArray_EmptyArray_ReturnsNull()
        {
            Assert.IsNull(RunTests.ParseStringArray(new JArray()));
        }

        [Test]
        public void ParseStringArray_SingleString_WrapsInArray()
        {
            var result = RunTests.ParseStringArray(new JValue("MyTest"));
            Assert.IsNotNull(result);
            Assert.AreEqual(1, result.Length);
            Assert.AreEqual("MyTest", result[0]);
        }

        [Test]
        public void ParseStringArray_Array_ReturnsStrings()
        {
            var arr = new JArray("TestA", "TestB");
            var result = RunTests.ParseStringArray(arr);
            Assert.IsNotNull(result);
            Assert.AreEqual(2, result.Length);
            Assert.AreEqual("TestA", result[0]);
            Assert.AreEqual("TestB", result[1]);
        }

        [Test]
        public void ParseStringArray_ArrayWithEmptyStrings_FiltersEmpty()
        {
            var arr = new JArray("TestA", "", "TestB");
            var result = RunTests.ParseStringArray(arr);
            Assert.IsNotNull(result);
            Assert.AreEqual(2, result.Length);
            Assert.AreEqual("TestA", result[0]);
            Assert.AreEqual("TestB", result[1]);
        }

        [Test]
        public void ParseStringArray_EmptyString_ReturnsNull()
        {
            Assert.IsNull(RunTests.ParseStringArray(new JValue("")));
        }

        // --- ComputeFilterHash ---

        [Test]
        public void ComputeFilterHash_Deterministic_SameInputsSameOutput()
        {
            var h1 = RunTests.ComputeFilterHash("PlayMode", new[] { "A" }, null, null);
            var h2 = RunTests.ComputeFilterHash("PlayMode", new[] { "A" }, null, null);
            Assert.AreEqual(h1, h2);
        }

        [Test]
        public void ComputeFilterHash_DifferentInputs_DifferentOutput()
        {
            var h1 = RunTests.ComputeFilterHash("PlayMode", new[] { "A" }, null, null);
            var h2 = RunTests.ComputeFilterHash("EditMode", new[] { "A" }, null, null);
            Assert.AreNotEqual(h1, h2);
        }

        [Test]
        public void ComputeFilterHash_NullArrays_DoesNotThrow()
        {
            Assert.DoesNotThrow(() => RunTests.ComputeFilterHash("EditMode", null, null, null));
        }

        [Test]
        public void ComputeFilterHash_Returns8HexChars()
        {
            var hash = RunTests.ComputeFilterHash("PlayMode", null, null, null);
            Assert.AreEqual(8, hash.Length);
            Assert.IsTrue(System.Text.RegularExpressions.Regex.IsMatch(hash, "^[0-9a-f]{8}$"));
        }

        [Test]
        public void ComputeFilterHash_NoAmbiguity_DifferentFieldPositions()
        {
            // "A|B" in testNames vs "A" in testNames + "B" in categoryNames should differ
            var h1 = RunTests.ComputeFilterHash("PlayMode", new[] { "A", "B" }, null, null);
            var h2 = RunTests.ComputeFilterHash("PlayMode", new[] { "A" }, new[] { "B" }, null);
            Assert.AreNotEqual(h1, h2);
        }
    }
}
