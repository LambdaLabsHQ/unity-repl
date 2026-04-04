using System.Collections.Generic;
using NativeMcp.Editor.Helpers;
using NativeMcp.Editor.Tools;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using UnityEngine;

namespace NativeMcp.Editor.Tests
{
    [TestFixture]
    public class InvokeDynamicTests
    {
        // ── D9: Wildcard member enumeration ──

        [Test]
        public void ResolveMethod_Wildcard_ReturnsPropertiesAndMethods()
        {
            var result = InvokeDynamic.HandleCommand(new JObject
            {
                ["action"] = "resolve_method",
                ["method"] = "Transform.*"
            });

            Assert.IsInstanceOf<SuccessResponse>(result);
            var success = (SuccessResponse)result;
            Assert.That(success.Message, Does.Contain("properties"));
            Assert.That(success.Message, Does.Contain("methods"));

            var data = JObject.FromObject(success.Data);
            Assert.That(data["properties"], Is.Not.Null);
            Assert.That(data["methods"], Is.Not.Null);
            Assert.That(data["properties"].HasValues, Is.True, "Should have properties");
            Assert.That(data["methods"].HasValues, Is.True, "Should have methods");
        }

        [Test]
        public void ResolveMethod_DotOnly_ReturnsAllMembers()
        {
            var result = InvokeDynamic.HandleCommand(new JObject
            {
                ["action"] = "resolve_method",
                ["method"] = "Transform."
            });

            Assert.IsInstanceOf<SuccessResponse>(result);
            var data = JObject.FromObject(((SuccessResponse)result).Data);
            Assert.That(data["properties"].HasValues, Is.True);
            Assert.That(data["methods"].HasValues, Is.True);
        }

        [Test]
        public void ResolveMethod_Wildcard_ExcludesSpecialNames()
        {
            var result = InvokeDynamic.HandleCommand(new JObject
            {
                ["action"] = "resolve_method",
                ["method"] = "Transform.*"
            });

            Assert.IsInstanceOf<SuccessResponse>(result);
            var data = JObject.FromObject(((SuccessResponse)result).Data);
            var methods = data["methods"] as JArray;
            Assert.That(methods, Is.Not.Null);

            foreach (var m in methods)
            {
                string name = m["name"]?.ToString();
                Assert.That(name, Does.Not.StartWith("get_"),
                    $"Special name '{name}' should be excluded");
                Assert.That(name, Does.Not.StartWith("set_"),
                    $"Special name '{name}' should be excluded");
                Assert.That(name, Does.Not.StartWith("op_"),
                    $"Special name '{name}' should be excluded");
            }
        }

        [Test]
        public void ResolveMethod_SpecificMember_StillWorks()
        {
            var result = InvokeDynamic.HandleCommand(new JObject
            {
                ["action"] = "resolve_method",
                ["method"] = "Transform.position"
            });

            Assert.IsInstanceOf<SuccessResponse>(result);
            var success = (SuccessResponse)result;
            Assert.That(success.Message, Does.Contain("candidate"));
        }

        // ── D2: Instance targeting ──

        [Test]
        public void CallMethod_InvalidInstanceId_ReturnsError()
        {
            var result = InvokeDynamic.HandleCommand(new JObject
            {
                ["action"] = "call_method",
                ["method"] = "Transform.position",
                ["instance_id"] = 999999
            });

            Assert.IsInstanceOf<ErrorResponse>(result);
            var error = (ErrorResponse)result;
            Assert.That(error.Error, Does.Contain("instanceID"));
        }

        [Test]
        public void CallMethod_InvalidGameObject_ReturnsError()
        {
            var result = InvokeDynamic.HandleCommand(new JObject
            {
                ["action"] = "call_method",
                ["method"] = "Transform.position",
                ["game_object"] = "__NonExistentObject_12345__"
            });

            Assert.IsInstanceOf<ErrorResponse>(result);
            var error = (ErrorResponse)result;
            Assert.That(error.Error, Does.Contain("__NonExistentObject_12345__"));
        }

        [Test]
        public void CallMethod_WithInstanceId_FindsCorrectObject()
        {
            var go = new GameObject("InvokeDynamic_Test_InstanceId");
            try
            {
                int id = go.GetInstanceID();
                var result = InvokeDynamic.HandleCommand(new JObject
                {
                    ["action"] = "call_method",
                    ["method"] = "Transform.position",
                    ["instance_id"] = id
                });

                Assert.IsInstanceOf<SuccessResponse>(result);
            }
            finally
            {
                Object.DestroyImmediate(go);
            }
        }

        [Test]
        public void CallMethod_WithGameObject_FindsByName()
        {
            var go = new GameObject("InvokeDynamic_Test_ByName");
            try
            {
                var result = InvokeDynamic.HandleCommand(new JObject
                {
                    ["action"] = "call_method",
                    ["method"] = "Transform.position",
                    ["game_object"] = "InvokeDynamic_Test_ByName"
                });

                Assert.IsInstanceOf<SuccessResponse>(result);
            }
            finally
            {
                Object.DestroyImmediate(go);
            }
        }

        [Test]
        public void CallMethod_StaticMethod_IgnoresInstanceParams()
        {
            // Static methods should work regardless of instance_id/game_object
            var result = InvokeDynamic.HandleCommand(new JObject
            {
                ["action"] = "call_method",
                ["method"] = "Time.frameCount",
                ["instance_id"] = 999999
            });

            Assert.IsInstanceOf<SuccessResponse>(result);
        }

        // ── D6: Serialization ──

        [Test]
        public void Settings_Vector3_SerializesClean()
        {
            var v = new Vector3(1f, 2f, 3f);
            string json = JsonConvert.SerializeObject(v, Formatting.None, UnityJsonSerializer.Settings);
            var obj = JObject.Parse(json);

            Assert.AreEqual(1f, obj["x"].Value<float>());
            Assert.AreEqual(2f, obj["y"].Value<float>());
            Assert.AreEqual(3f, obj["z"].Value<float>());
            // Should NOT contain computed properties like magnitude
            Assert.That(obj["magnitude"], Is.Null, "Should not serialize computed properties");
        }

        [Test]
        public void Settings_Quaternion_SerializesClean()
        {
            var q = new Quaternion(0f, 0.707f, 0f, 0.707f);
            string json = JsonConvert.SerializeObject(q, Formatting.None, UnityJsonSerializer.Settings);
            var obj = JObject.Parse(json);

            Assert.AreEqual(4, obj.Count, "Should have exactly x, y, z, w");
            Assert.That(obj["x"], Is.Not.Null);
            Assert.That(obj["w"], Is.Not.Null);
            Assert.That(obj["eulerAngles"], Is.Null, "Should not serialize computed properties");
        }

        [Test]
        public void Settings_Color_SerializesClean()
        {
            var c = new Color(0.5f, 0.6f, 0.7f, 1f);
            string json = JsonConvert.SerializeObject(c, Formatting.None, UnityJsonSerializer.Settings);
            var obj = JObject.Parse(json);

            Assert.AreEqual(0.5f, obj["r"].Value<float>(), 0.001f);
            Assert.AreEqual(0.6f, obj["g"].Value<float>(), 0.001f);
            Assert.AreEqual(0.7f, obj["b"].Value<float>(), 0.001f);
            Assert.AreEqual(1f, obj["a"].Value<float>(), 0.001f);
        }

        [Test]
        public void Settings_UnityObject_SerializesNameAndId()
        {
            var go = new GameObject("InvokeDynamic_Test_Serialize");
            try
            {
                string json = JsonConvert.SerializeObject(go, Formatting.None, UnityJsonSerializer.Settings);
                var obj = JObject.Parse(json);

                Assert.That(obj["name"]?.ToString(), Is.EqualTo("InvokeDynamic_Test_Serialize"));
                Assert.That(obj["instanceID"]?.Value<int>(), Is.EqualTo(go.GetInstanceID()));
                // Should NOT contain the full GameObject dump
                Assert.That(obj["transform"], Is.Null);
            }
            finally
            {
                Object.DestroyImmediate(go);
            }
        }

        [Test]
        public void Settings_Matrix4x4_SerializesRawElements()
        {
            var m = Matrix4x4.identity;
            string json = JsonConvert.SerializeObject(m, Formatting.None, UnityJsonSerializer.Settings);
            var obj = JObject.Parse(json);

            Assert.AreEqual(1f, obj["m00"].Value<float>());
            Assert.AreEqual(0f, obj["m01"].Value<float>());
            Assert.AreEqual(1f, obj["m11"].Value<float>());
            // Should not contain computed properties like inverse, determinant
            Assert.That(obj["inverse"], Is.Null);
        }
    }
}
