using NativeMcp.Editor.Helpers;
using Newtonsoft.Json.Linq;
using NUnit.Framework;

namespace NativeMcp.Editor.Tests
{
    [TestFixture]
    public class ParamCoercionTests
    {
        // --- CoerceInt ---

        [Test]
        public void CoerceInt_NullToken_ReturnsDefault()
        {
            Assert.AreEqual(42, ParamCoercion.CoerceInt(null, 42));
        }

        [Test]
        public void CoerceInt_JTokenNull_ReturnsDefault()
        {
            Assert.AreEqual(7, ParamCoercion.CoerceInt(JValue.CreateNull(), 7));
        }

        [Test]
        public void CoerceInt_IntegerToken_ReturnsValue()
        {
            Assert.AreEqual(10, ParamCoercion.CoerceInt(new JValue(10), 0));
        }

        [Test]
        public void CoerceInt_StringInteger_ReturnsValue()
        {
            Assert.AreEqual(42, ParamCoercion.CoerceInt(new JValue("42"), 0));
        }

        [Test]
        public void CoerceInt_StringFloat_ReturnsTruncated()
        {
            Assert.AreEqual(3, ParamCoercion.CoerceInt(new JValue("3.7"), 0));
        }

        [Test]
        public void CoerceInt_EmptyString_ReturnsDefault()
        {
            Assert.AreEqual(5, ParamCoercion.CoerceInt(new JValue(""), 5));
        }

        [Test]
        public void CoerceInt_NonNumericString_ReturnsDefault()
        {
            Assert.AreEqual(99, ParamCoercion.CoerceInt(new JValue("abc"), 99));
        }

        // --- CoerceBool ---

        [Test]
        public void CoerceBool_NullToken_ReturnsDefault()
        {
            Assert.AreEqual(true, ParamCoercion.CoerceBool(null, true));
        }

        [Test]
        public void CoerceBool_BooleanToken_ReturnsValue()
        {
            Assert.AreEqual(true, ParamCoercion.CoerceBool(new JValue(true), false));
            Assert.AreEqual(false, ParamCoercion.CoerceBool(new JValue(false), true));
        }

        [Test]
        public void CoerceBool_StringTrue_ReturnsTrue()
        {
            Assert.IsTrue(ParamCoercion.CoerceBool(new JValue("true"), false));
            Assert.IsTrue(ParamCoercion.CoerceBool(new JValue("True"), false));
        }

        [Test]
        public void CoerceBool_One_ReturnsTrue()
        {
            Assert.IsTrue(ParamCoercion.CoerceBool(new JValue("1"), false));
        }

        [Test]
        public void CoerceBool_Yes_ReturnsTrue()
        {
            Assert.IsTrue(ParamCoercion.CoerceBool(new JValue("yes"), false));
        }

        [Test]
        public void CoerceBool_On_ReturnsTrue()
        {
            Assert.IsTrue(ParamCoercion.CoerceBool(new JValue("on"), false));
        }

        [Test]
        public void CoerceBool_Zero_ReturnsFalse()
        {
            Assert.IsFalse(ParamCoercion.CoerceBool(new JValue("0"), true));
        }

        [Test]
        public void CoerceBool_No_ReturnsFalse()
        {
            Assert.IsFalse(ParamCoercion.CoerceBool(new JValue("no"), true));
        }

        [Test]
        public void CoerceBool_Off_ReturnsFalse()
        {
            Assert.IsFalse(ParamCoercion.CoerceBool(new JValue("off"), true));
        }

        [Test]
        public void CoerceBool_EmptyString_ReturnsDefault()
        {
            Assert.IsTrue(ParamCoercion.CoerceBool(new JValue(""), true));
        }

        // --- CoerceFloat ---

        [Test]
        public void CoerceFloat_NullToken_ReturnsDefault()
        {
            Assert.AreEqual(1.5f, ParamCoercion.CoerceFloat(null, 1.5f));
        }

        [Test]
        public void CoerceFloat_FloatToken_ReturnsValue()
        {
            Assert.AreEqual(3.14f, ParamCoercion.CoerceFloat(new JValue(3.14f), 0f), 0.001f);
        }

        [Test]
        public void CoerceFloat_IntegerToken_ReturnsValue()
        {
            Assert.AreEqual(5f, ParamCoercion.CoerceFloat(new JValue(5), 0f));
        }

        [Test]
        public void CoerceFloat_StringFloat_ReturnsValue()
        {
            Assert.AreEqual(2.5f, ParamCoercion.CoerceFloat(new JValue("2.5"), 0f), 0.001f);
        }

        // --- CoerceString ---

        [Test]
        public void CoerceString_NullToken_ReturnsDefault()
        {
            Assert.AreEqual("fallback", ParamCoercion.CoerceString(null, "fallback"));
        }

        [Test]
        public void CoerceString_EmptyString_ReturnsDefault()
        {
            Assert.AreEqual("fallback", ParamCoercion.CoerceString(new JValue(""), "fallback"));
        }

        [Test]
        public void CoerceString_ValidString_ReturnsValue()
        {
            Assert.AreEqual("hello", ParamCoercion.CoerceString(new JValue("hello"), "fallback"));
        }

        // --- CoerceEnum ---

        private enum TestEnum { Alpha, Beta, Gamma }

        [Test]
        public void CoerceEnum_NullToken_ReturnsDefault()
        {
            Assert.AreEqual(TestEnum.Beta, ParamCoercion.CoerceEnum(null, TestEnum.Beta));
        }

        [Test]
        public void CoerceEnum_ValidName_ReturnsEnum()
        {
            Assert.AreEqual(TestEnum.Gamma, ParamCoercion.CoerceEnum(new JValue("Gamma"), TestEnum.Alpha));
        }

        [Test]
        public void CoerceEnum_CaseInsensitive_ReturnsEnum()
        {
            Assert.AreEqual(TestEnum.Alpha, ParamCoercion.CoerceEnum(new JValue("alpha"), TestEnum.Beta));
        }

        [Test]
        public void CoerceEnum_InvalidName_ReturnsDefault()
        {
            Assert.AreEqual(TestEnum.Beta, ParamCoercion.CoerceEnum(new JValue("invalid"), TestEnum.Beta));
        }

        // --- IsNumericToken ---

        [Test]
        public void IsNumericToken_Integer_ReturnsTrue()
        {
            Assert.IsTrue(ParamCoercion.IsNumericToken(new JValue(42)));
        }

        [Test]
        public void IsNumericToken_Float_ReturnsTrue()
        {
            Assert.IsTrue(ParamCoercion.IsNumericToken(new JValue(3.14)));
        }

        [Test]
        public void IsNumericToken_String_ReturnsFalse()
        {
            Assert.IsFalse(ParamCoercion.IsNumericToken(new JValue("42")));
        }

        [Test]
        public void IsNumericToken_Null_ReturnsFalse()
        {
            Assert.IsFalse(ParamCoercion.IsNumericToken(null));
        }

        // --- ValidateNumericField ---

        [Test]
        public void ValidateNumericField_AbsentField_ReturnsTrue()
        {
            var obj = new JObject();
            Assert.IsTrue(ParamCoercion.ValidateNumericField(obj, "x", out string error));
            Assert.IsNull(error);
        }

        [Test]
        public void ValidateNumericField_NumericField_ReturnsTrue()
        {
            var obj = new JObject { ["x"] = 42 };
            Assert.IsTrue(ParamCoercion.ValidateNumericField(obj, "x", out string error));
            Assert.IsNull(error);
        }

        [Test]
        public void ValidateNumericField_NonNumericField_ReturnsFalse()
        {
            var obj = new JObject { ["x"] = "hello" };
            Assert.IsFalse(ParamCoercion.ValidateNumericField(obj, "x", out string error));
            Assert.IsNotNull(error);
        }

        // --- ValidateIntegerField ---

        [Test]
        public void ValidateIntegerField_IntegerField_ReturnsTrue()
        {
            var obj = new JObject { ["x"] = 10 };
            Assert.IsTrue(ParamCoercion.ValidateIntegerField(obj, "x", out string error));
            Assert.IsNull(error);
        }

        [Test]
        public void ValidateIntegerField_FloatField_ReturnsFalse()
        {
            var obj = new JObject { ["x"] = 3.14 };
            Assert.IsFalse(ParamCoercion.ValidateIntegerField(obj, "x", out string error));
            Assert.IsNotNull(error);
        }

        // --- NormalizePropertyName ---

        [Test]
        public void NormalizePropertyName_SpaceSeparated()
        {
            Assert.AreEqual("useGravity", ParamCoercion.NormalizePropertyName("Use Gravity"));
        }

        [Test]
        public void NormalizePropertyName_SnakeCase()
        {
            Assert.AreEqual("isKinematic", ParamCoercion.NormalizePropertyName("is_kinematic"));
        }

        [Test]
        public void NormalizePropertyName_DashSeparated()
        {
            Assert.AreEqual("maxAngularVelocity", ParamCoercion.NormalizePropertyName("max-angular-velocity"));
        }

        [Test]
        public void NormalizePropertyName_NullInput_ReturnsNull()
        {
            Assert.IsNull(ParamCoercion.NormalizePropertyName(null));
        }

        [Test]
        public void NormalizePropertyName_EmptyInput_ReturnsEmpty()
        {
            Assert.AreEqual("", ParamCoercion.NormalizePropertyName(""));
        }
    }
}
