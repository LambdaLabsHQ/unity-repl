using NativeMcp.Editor.Protocol;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using NUnit.Framework;

namespace NativeMcp.Editor.Tests
{
    [TestFixture]
    public class JsonRpcTypesTests
    {
        // --- JsonRpcRequest ---

        [Test]
        public void JsonRpcRequest_IsNotification_NullId_ReturnsTrue()
        {
            var request = new JsonRpcRequest { Method = "test", Id = null };
            Assert.IsTrue(request.IsNotification);
        }

        [Test]
        public void JsonRpcRequest_IsNotification_WithId_ReturnsFalse()
        {
            var request = new JsonRpcRequest { Method = "test", Id = new JValue(1) };
            Assert.IsFalse(request.IsNotification);
        }

        [Test]
        public void JsonRpcRequest_IsNotification_JTokenNull_ReturnsTrue()
        {
            var request = new JsonRpcRequest { Method = "test", Id = JValue.CreateNull() };
            Assert.IsTrue(request.IsNotification);
        }

        [Test]
        public void JsonRpcRequest_Deserialize_RoundTrip()
        {
            string json = @"{""jsonrpc"":""2.0"",""method"":""tools/list"",""id"":1}";
            var request = JsonConvert.DeserializeObject<JsonRpcRequest>(json);

            Assert.AreEqual("2.0", request.JsonRpc);
            Assert.AreEqual("tools/list", request.Method);
            Assert.AreEqual(1, request.Id.Value<int>());
            Assert.IsFalse(request.IsNotification);
        }

        [Test]
        public void JsonRpcRequest_Deserialize_Notification()
        {
            string json = @"{""jsonrpc"":""2.0"",""method"":""notifications/initialized""}";
            var request = JsonConvert.DeserializeObject<JsonRpcRequest>(json);

            Assert.AreEqual("notifications/initialized", request.Method);
            Assert.IsTrue(request.IsNotification);
        }

        // --- JsonRpcResponse ---

        [Test]
        public void JsonRpcResponse_Serialize_ContainsAllFields()
        {
            var response = new JsonRpcResponse
            {
                Id = new JValue(1),
                Result = new JObject { ["key"] = "value" }
            };
            string json = JsonConvert.SerializeObject(response);
            var parsed = JObject.Parse(json);

            Assert.AreEqual("2.0", parsed["jsonrpc"]?.ToString());
            Assert.AreEqual(1, parsed["id"]?.Value<int>());
            Assert.AreEqual("value", parsed["result"]?["key"]?.ToString());
        }

        // --- JsonRpcErrorResponse ---

        [Test]
        public void JsonRpcErrorResponse_Serialize_ContainsErrorFields()
        {
            var response = new JsonRpcErrorResponse
            {
                Id = new JValue(1),
                Error = new JsonRpcErrorDetail
                {
                    Code = JsonRpcErrorCodes.MethodNotFound,
                    Message = "Method not found"
                }
            };
            string json = JsonConvert.SerializeObject(response);
            var parsed = JObject.Parse(json);

            Assert.AreEqual("2.0", parsed["jsonrpc"]?.ToString());
            Assert.AreEqual(-32601, parsed["error"]?["code"]?.Value<int>());
            Assert.AreEqual("Method not found", parsed["error"]?["message"]?.ToString());
        }

        // --- JsonRpcErrorCodes ---

        [Test]
        public void ErrorCodes_StandardValues()
        {
            Assert.AreEqual(-32700, JsonRpcErrorCodes.ParseError);
            Assert.AreEqual(-32600, JsonRpcErrorCodes.InvalidRequest);
            Assert.AreEqual(-32601, JsonRpcErrorCodes.MethodNotFound);
            Assert.AreEqual(-32602, JsonRpcErrorCodes.InvalidParams);
            Assert.AreEqual(-32603, JsonRpcErrorCodes.InternalError);
            Assert.AreEqual(-32001, JsonRpcErrorCodes.DomainReloadCancelled);
        }
    }
}
