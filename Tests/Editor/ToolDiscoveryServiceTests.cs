using System.Collections.Generic;
using System.Linq;
using NativeMcp.Editor.Services;
using NUnit.Framework;

namespace NativeMcp.Editor.Tests
{
    [TestFixture]
    public class ToolDiscoveryServiceTests
    {
        private ToolDiscoveryService _service;

        [SetUp]
        public void SetUp()
        {
            _service = new ToolDiscoveryService();
        }

        // --- GetParameterType ---

        [Test]
        public void GetParameterType_String()
        {
            Assert.AreEqual("string", _service.GetParameterType(typeof(string)));
        }

        [Test]
        public void GetParameterType_Int()
        {
            Assert.AreEqual("integer", _service.GetParameterType(typeof(int)));
        }

        [Test]
        public void GetParameterType_Long()
        {
            Assert.AreEqual("integer", _service.GetParameterType(typeof(long)));
        }

        [Test]
        public void GetParameterType_Float()
        {
            Assert.AreEqual("number", _service.GetParameterType(typeof(float)));
        }

        [Test]
        public void GetParameterType_Double()
        {
            Assert.AreEqual("number", _service.GetParameterType(typeof(double)));
        }

        [Test]
        public void GetParameterType_Bool()
        {
            Assert.AreEqual("boolean", _service.GetParameterType(typeof(bool)));
        }

        [Test]
        public void GetParameterType_IntArray()
        {
            Assert.AreEqual("array", _service.GetParameterType(typeof(int[])));
        }

        [Test]
        public void GetParameterType_NullableInt_ReturnsInteger()
        {
            Assert.AreEqual("integer", _service.GetParameterType(typeof(int?)));
        }

        [Test]
        public void GetParameterType_NullableBool_ReturnsBoolean()
        {
            Assert.AreEqual("boolean", _service.GetParameterType(typeof(bool?)));
        }

        [Test]
        public void GetParameterType_CustomClass_ReturnsObject()
        {
            Assert.AreEqual("object", _service.GetParameterType(typeof(ToolMetadata)));
        }

        [Test]
        public void UnityTest_Metadata_ExposesRunAndListParameters()
        {
            var tool = _service.GetToolMetadata("unity_test");
            Assert.IsNotNull(tool);
            Assert.IsNotNull(tool.Parameters);

            var names = new HashSet<string>(tool.Parameters.Select(p => p.Name));
            var expected = new HashSet<string>
            {
                "action",
                "testMode",
                "testNames",
                "categoryNames",
                "assemblyNames",
                "timeoutSeconds",
            };

            CollectionAssert.IsSubsetOf(expected, names);

            Assert.AreEqual("array", tool.Parameters.First(p => p.Name == "testNames").Type);
            Assert.AreEqual("array", tool.Parameters.First(p => p.Name == "categoryNames").Type);
            Assert.AreEqual("array", tool.Parameters.First(p => p.Name == "assemblyNames").Type);
            Assert.AreEqual("integer", tool.Parameters.First(p => p.Name == "timeoutSeconds").Type);
        }

        [Test]
        public void ExposedMetaTools_Metadata_ContainsActionSpecificFields()
        {
            var scene = _service.GetToolMetadata("unity_scene");
            Assert.IsNotNull(scene);
            CollectionAssert.Contains(scene.Parameters.Select(p => p.Name), "searchTerm");
            CollectionAssert.Contains(scene.Parameters.Select(p => p.Name), "superSize");

            var edit = _service.GetToolMetadata("unity_edit");
            Assert.IsNotNull(edit);
            CollectionAssert.Contains(edit.Parameters.Select(p => p.Name), "componentType");
            CollectionAssert.Contains(edit.Parameters.Select(p => p.Name), "buildIndex");
            CollectionAssert.Contains(edit.Parameters.Select(p => p.Name), "saveBeforeClose");

            var editor = _service.GetToolMetadata("unity_editor");
            Assert.IsNotNull(editor);
            CollectionAssert.Contains(editor.Parameters.Select(p => p.Name), "wait_for_ready");

            var console = _service.GetToolMetadata("unity_console");
            Assert.IsNotNull(console);
            CollectionAssert.Contains(console.Parameters.Select(p => p.Name), "types");
            CollectionAssert.Contains(console.Parameters.Select(p => p.Name), "includeStacktrace");
        }

        [Test]
        public void UnityBatchAndInvoke_Metadata_HaveCorrectParameterTypes()
        {
            var batch = _service.GetToolMetadata("unity_batch");
            Assert.IsNotNull(batch);
            Assert.AreEqual("array", batch.Parameters.First(p => p.Name == "commands").Type);

            var invoke = _service.GetToolMetadata("unity_invoke");
            Assert.IsNotNull(invoke);
            Assert.AreEqual("object", invoke.Parameters.First(p => p.Name == "args").Type);
        }
    }
}
