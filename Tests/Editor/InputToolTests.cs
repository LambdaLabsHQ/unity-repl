using NativeMcp.Editor.Helpers;
using NativeMcp.Editor.Tools.EditorControl;
using Newtonsoft.Json.Linq;
using NUnit.Framework;

#if NATIVE_MCP_HAS_INPUT_SYSTEM
using NativeMcp.Editor.Tools.Input;
#endif

namespace NativeMcp.Editor.Tests
{
    [TestFixture]
    public class InputToolTests
    {
#if NATIVE_MCP_HAS_INPUT_SYSTEM

        // ── SimulateInput: Play Mode guard ──

        [Test]
        public void KeyDown_NotInPlayMode_ReturnsError()
        {
            var result = SimulateInput.HandleCommand(new JObject
            {
                ["action"] = "key_down",
                ["key"] = "w"
            });
            Assert.IsInstanceOf<ErrorResponse>(result);
            Assert.That(((ErrorResponse)result).Error, Does.Contain("Play Mode"));
        }

        [Test]
        public void ReleaseAll_NotInPlayMode_ReturnsError()
        {
            var result = SimulateInput.HandleCommand(new JObject
            {
                ["action"] = "release_all"
            });
            Assert.IsInstanceOf<ErrorResponse>(result);
            Assert.That(((ErrorResponse)result).Error, Does.Contain("Play Mode"));
        }

        // ── SimulateInput: Parameter validation ──

        [Test]
        public void KeyDown_MissingKey_ReturnsError()
        {
            // Without Play Mode this will hit the guard first, so we test
            // that the action is recognized and the guard fires (not "unknown action")
            var result = SimulateInput.HandleCommand(new JObject
            {
                ["action"] = "key_down"
            });
            Assert.IsInstanceOf<ErrorResponse>(result);
            // Should be Play Mode error, not "unknown action"
            Assert.That(((ErrorResponse)result).Error, Does.Contain("Play Mode"));
        }

        [Test]
        public void KeyDown_InvalidKey_ErrorNotUnknownAction()
        {
            // Since we're not in play mode, this hits the guard.
            // We verify the action is dispatched correctly (not "unknown action")
            var result = SimulateInput.HandleCommand(new JObject
            {
                ["action"] = "key_down",
                ["key"] = "___invalid___"
            });
            Assert.IsInstanceOf<ErrorResponse>(result);
            Assert.That(((ErrorResponse)result).Error, Does.Not.Contain("Unknown action"));
        }

        [Test]
        public void MouseMove_NotInPlayMode_ReturnsError()
        {
            var result = SimulateInput.HandleCommand(new JObject
            {
                ["action"] = "mouse_move"
            });
            Assert.IsInstanceOf<ErrorResponse>(result);
            Assert.That(((ErrorResponse)result).Error, Does.Contain("Play Mode"));
        }

        [Test]
        public void MouseScroll_NotInPlayMode_ReturnsError()
        {
            var result = SimulateInput.HandleCommand(new JObject
            {
                ["action"] = "mouse_scroll",
                ["scroll_y"] = 120
            });
            Assert.IsInstanceOf<ErrorResponse>(result);
            Assert.That(((ErrorResponse)result).Error, Does.Contain("Play Mode"));
        }

        [Test]
        public void Click_NotInPlayMode_ReturnsError()
        {
            var result = SimulateInput.HandleCommand(new JObject
            {
                ["action"] = "click",
                ["button"] = "left"
            });
            Assert.IsInstanceOf<ErrorResponse>(result);
            Assert.That(((ErrorResponse)result).Error, Does.Contain("Play Mode"));
        }

        [Test]
        public void MissingAction_ReturnsError()
        {
            var result = SimulateInput.HandleCommand(new JObject());
            Assert.IsInstanceOf<ErrorResponse>(result);
            Assert.That(((ErrorResponse)result).Error, Does.Contain("Play Mode"));
        }

        [Test]
        public void UnknownAction_NotInPlayMode_ReturnsPlayModeError()
        {
            // Play Mode guard fires before action dispatch
            var result = SimulateInput.HandleCommand(new JObject
            {
                ["action"] = "bogus_action"
            });
            Assert.IsInstanceOf<ErrorResponse>(result);
            Assert.That(((ErrorResponse)result).Error, Does.Contain("Play Mode"));
        }

#endif

        // ── ExecuteMenuItem ──

        [Test]
        public void ExecuteMenuItem_MissingPath_ReturnsError()
        {
            var result = ExecuteMenuItem.HandleCommand(new JObject());
            Assert.IsInstanceOf<ErrorResponse>(result);
            Assert.That(((ErrorResponse)result).Error, Does.Contain("menu_path"));
        }

        [Test]
        public void ExecuteMenuItem_BlacklistedPath_ReturnsError()
        {
            var result = ExecuteMenuItem.HandleCommand(new JObject
            {
                ["menu_path"] = "File/Quit"
            });
            Assert.IsInstanceOf<ErrorResponse>(result);
            Assert.That(((ErrorResponse)result).Error, Does.Contain("blocked"));
        }

        [Test]
        public void ExecuteMenuItem_BlacklistedPath_CaseInsensitive()
        {
            var result = ExecuteMenuItem.HandleCommand(new JObject
            {
                ["menu_path"] = "file/quit"
            });
            Assert.IsInstanceOf<ErrorResponse>(result);
            Assert.That(((ErrorResponse)result).Error, Does.Contain("blocked"));
        }

        [Test]
        public void ExecuteMenuItem_ValidPath_Succeeds()
        {
            // Window/General/Console is always available in the editor
            var result = ExecuteMenuItem.HandleCommand(new JObject
            {
                ["menu_path"] = "Window/General/Console"
            });
            Assert.IsInstanceOf<SuccessResponse>(result);
            Assert.That(((SuccessResponse)result).Message, Does.Contain("Executed"));
        }

        [Test]
        public void ExecuteMenuItem_InvalidPath_ReturnsError()
        {
            var result = ExecuteMenuItem.HandleCommand(new JObject
            {
                ["menu_path"] = "This/Menu/Does/Not/Exist/At/All"
            });
            Assert.IsInstanceOf<ErrorResponse>(result);
            Assert.That(((ErrorResponse)result).Error, Does.Contain("Failed"));
        }
    }
}
