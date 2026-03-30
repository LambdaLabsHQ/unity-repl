using NativeMcp.Editor.Protocol;
using Newtonsoft.Json.Linq;
using NUnit.Framework;

namespace NativeMcp.Editor.Tests
{
    [TestFixture]
    public class McpProtocolHandlerTests
    {
        // --- SerializeSuccess ---

        [Test]
        public void SerializeSuccess_ContainsJsonRpc2()
        {
            string json = McpProtocolHandler.SerializeSuccess(new JValue(1), new JObject { ["key"] = "val" });
            var parsed = JObject.Parse(json);

            Assert.AreEqual("2.0", parsed["jsonrpc"]?.ToString());
        }

        [Test]
        public void SerializeSuccess_ContainsId()
        {
            string json = McpProtocolHandler.SerializeSuccess(new JValue(42), new JObject());
            var parsed = JObject.Parse(json);

            Assert.AreEqual(42, parsed["id"]?.Value<int>());
        }

        [Test]
        public void SerializeSuccess_ContainsResult()
        {
            var result = new JObject { ["tools"] = new JArray() };
            string json = McpProtocolHandler.SerializeSuccess(new JValue(1), result);
            var parsed = JObject.Parse(json);

            Assert.IsNotNull(parsed["result"]);
            Assert.IsNotNull(parsed["result"]["tools"]);
        }

        [Test]
        public void SerializeSuccess_NoErrorField()
        {
            string json = McpProtocolHandler.SerializeSuccess(new JValue(1), new JObject());
            var parsed = JObject.Parse(json);

            Assert.IsNull(parsed["error"]);
        }

        [Test]
        public void SerializeSuccess_StringId()
        {
            string json = McpProtocolHandler.SerializeSuccess(new JValue("req-1"), new JObject());
            var parsed = JObject.Parse(json);

            Assert.AreEqual("req-1", parsed["id"]?.ToString());
        }

        // --- SerializeError ---

        [Test]
        public void SerializeError_ContainsJsonRpc2()
        {
            string json = McpProtocolHandler.SerializeError(new JValue(1), -32600, "Invalid");
            var parsed = JObject.Parse(json);

            Assert.AreEqual("2.0", parsed["jsonrpc"]?.ToString());
        }

        [Test]
        public void SerializeError_ContainsErrorCode()
        {
            string json = McpProtocolHandler.SerializeError(new JValue(1), -32601, "Method not found");
            var parsed = JObject.Parse(json);

            Assert.AreEqual(-32601, parsed["error"]?["code"]?.Value<int>());
        }

        [Test]
        public void SerializeError_ContainsErrorMessage()
        {
            string json = McpProtocolHandler.SerializeError(new JValue(1), -32700, "Parse error");
            var parsed = JObject.Parse(json);

            Assert.AreEqual("Parse error", parsed["error"]?["message"]?.ToString());
        }

        [Test]
        public void SerializeError_NullId()
        {
            string json = McpProtocolHandler.SerializeError(null, -32600, "Bad request");
            var parsed = JObject.Parse(json);

            Assert.IsTrue(parsed["id"] == null || parsed["id"].Type == JTokenType.Null);
        }

        [Test]
        public void SerializeError_NoResultField()
        {
            string json = McpProtocolHandler.SerializeError(new JValue(1), -32603, "Internal error");
            var parsed = JObject.Parse(json);

            Assert.IsNull(parsed["result"]);
        }

        [Test]
        public void SerializeError_WithData()
        {
            var data = new JObject { ["detail"] = "extra info" };
            string json = McpProtocolHandler.SerializeError(new JValue(1), -32603, "err", data);
            var parsed = JObject.Parse(json);

            Assert.AreEqual("extra info", parsed["error"]?["data"]?["detail"]?.ToString());
        }
    }
}
