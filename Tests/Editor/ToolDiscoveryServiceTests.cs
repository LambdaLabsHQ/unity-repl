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
    }
}
