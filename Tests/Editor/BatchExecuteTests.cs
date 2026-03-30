using NativeMcp.Editor.Helpers;
using NativeMcp.Editor.Tools;
using Newtonsoft.Json.Linq;
using NUnit.Framework;

namespace NativeMcp.Editor.Tests
{
    [TestFixture]
    public class BatchExecuteTests
    {
        // --- NormalizeParameterKeys ---

        [Test]
        public void NormalizeParameterKeys_SnakeCaseKeys_ConvertedToCamelCase()
        {
            var input = new JObject { ["search_method"] = "by_name", ["component_type"] = "Rigidbody" };
            var result = BatchExecute.NormalizeParameterKeys(input);

            Assert.AreEqual("by_name", result["searchMethod"]?.ToString());
            Assert.AreEqual("Rigidbody", result["componentType"]?.ToString());
        }

        [Test]
        public void NormalizeParameterKeys_CamelCaseKeys_Unchanged()
        {
            var input = new JObject { ["alreadyCamel"] = "value" };
            var result = BatchExecute.NormalizeParameterKeys(input);

            Assert.AreEqual("value", result["alreadyCamel"]?.ToString());
        }

        [Test]
        public void NormalizeParameterKeys_NestedObjects_NormalizedRecursively()
        {
            var input = new JObject
            {
                ["outer_key"] = new JObject { ["inner_key"] = "deep" }
            };
            var result = BatchExecute.NormalizeParameterKeys(input);

            var inner = result["outerKey"] as JObject;
            Assert.IsNotNull(inner);
            Assert.AreEqual("deep", inner["innerKey"]?.ToString());
        }

        [Test]
        public void NormalizeParameterKeys_Null_ReturnsEmptyObject()
        {
            var result = BatchExecute.NormalizeParameterKeys(null);
            Assert.IsNotNull(result);
            Assert.AreEqual(0, result.Count);
        }

        [Test]
        public void NormalizeParameterKeys_ArrayValues_Preserved()
        {
            var input = new JObject
            {
                ["my_array"] = new JArray(1, 2, 3)
            };
            var result = BatchExecute.NormalizeParameterKeys(input);

            var arr = result["myArray"] as JArray;
            Assert.IsNotNull(arr);
            Assert.AreEqual(3, arr.Count);
        }

        // --- DetermineCallSucceeded ---

        [Test]
        public void DetermineCallSucceeded_Null_ReturnsTrue()
        {
            Assert.IsTrue(BatchExecute.DetermineCallSucceeded(null));
        }

        [Test]
        public void DetermineCallSucceeded_SuccessResponse_ReturnsTrue()
        {
            var response = new SuccessResponse("ok");
            Assert.IsTrue(BatchExecute.DetermineCallSucceeded(response));
        }

        [Test]
        public void DetermineCallSucceeded_ErrorResponse_ReturnsFalse()
        {
            var response = new ErrorResponse("fail");
            Assert.IsFalse(BatchExecute.DetermineCallSucceeded(response));
        }

        [Test]
        public void DetermineCallSucceeded_JObjectWithSuccessTrue_ReturnsTrue()
        {
            var obj = new JObject { ["success"] = true };
            Assert.IsTrue(BatchExecute.DetermineCallSucceeded(obj));
        }

        [Test]
        public void DetermineCallSucceeded_JObjectWithSuccessFalse_ReturnsFalse()
        {
            var obj = new JObject { ["success"] = false };
            Assert.IsFalse(BatchExecute.DetermineCallSucceeded(obj));
        }

        [Test]
        public void DetermineCallSucceeded_PlainObject_ReturnsTrue()
        {
            Assert.IsTrue(BatchExecute.DetermineCallSucceeded("just a string"));
        }
    }
}
