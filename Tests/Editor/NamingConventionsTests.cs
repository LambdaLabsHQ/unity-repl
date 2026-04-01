using NativeMcp.Editor.Helpers;
using NUnit.Framework;

namespace NativeMcp.Editor.Tests
{
    [TestFixture]
    public class NamingConventionsTests
    {
        // --- ToSnakeCase ---

        [Test]
        public void ToSnakeCase_PascalCase()
        {
            Assert.AreEqual("manage_scene", NamingConventions.ToSnakeCase("ManageScene"));
        }

        [Test]
        public void ToSnakeCase_MultiWord()
        {
            Assert.AreEqual("game_object_create", NamingConventions.ToSnakeCase("GameObjectCreate"));
        }

        [Test]
        public void ToSnakeCase_Acronym()
        {
            Assert.AreEqual("http_client", NamingConventions.ToSnakeCase("HTTPClient"));
        }

        [Test]
        public void ToSnakeCase_SingleWord()
        {
            Assert.AreEqual("hello", NamingConventions.ToSnakeCase("Hello"));
        }

        [Test]
        public void ToSnakeCase_AlreadyLowercase()
        {
            Assert.AreEqual("simple", NamingConventions.ToSnakeCase("simple"));
        }

        [Test]
        public void ToSnakeCase_Empty_ReturnsEmpty()
        {
            Assert.AreEqual("", NamingConventions.ToSnakeCase(""));
        }

        [Test]
        public void ToSnakeCase_Null_ReturnsNull()
        {
            Assert.IsNull(NamingConventions.ToSnakeCase(null));
        }

        // --- ToCamelCase ---

        [Test]
        public void ToCamelCase_SnakeCase()
        {
            Assert.AreEqual("searchMethod", NamingConventions.ToCamelCase("search_method"));
        }

        [Test]
        public void ToCamelCase_MultiUnderscore()
        {
            Assert.AreEqual("aBC", NamingConventions.ToCamelCase("a_b_c"));
        }

        [Test]
        public void ToCamelCase_NoUnderscore_ReturnsSame()
        {
            Assert.AreEqual("already", NamingConventions.ToCamelCase("already"));
        }

        [Test]
        public void ToCamelCase_Empty_ReturnsEmpty()
        {
            Assert.AreEqual("", NamingConventions.ToCamelCase(""));
        }

        [Test]
        public void ToCamelCase_Null_ReturnsNull()
        {
            Assert.IsNull(NamingConventions.ToCamelCase(null));
        }

        [Test]
        public void ToCamelCase_SingleTrailingUnderscore()
        {
            // "hello_" should still work (RemoveEmptyEntries handles trailing)
            Assert.AreEqual("hello", NamingConventions.ToCamelCase("hello_"));
        }
    }
}
