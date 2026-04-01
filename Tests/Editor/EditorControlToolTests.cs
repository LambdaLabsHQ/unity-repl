using NativeMcp.Editor.Helpers;
using NativeMcp.Editor.Tools.EditorControl;
using Newtonsoft.Json.Linq;
using NUnit.Framework;

namespace NativeMcp.Editor.Tests
{
    [TestFixture]
    public class EditorControlToolTests
    {
        // --- EditorStepFrame ---

        [Test]
        public void StepFrame_NotInPlayMode_ReturnsError()
        {
            var result = EditorStepFrame.HandleCommand(new JObject());
            Assert.IsInstanceOf<ErrorResponse>(result);
            Assert.That(((ErrorResponse)result).Error, Does.Contain("Not in play mode"));
        }

        // --- EditorSetUpdateFrequency ---

        [Test]
        public void SetUpdateFrequency_NoArgs_ReturnsCurrentValues()
        {
            var result = EditorSetUpdateFrequency.HandleCommand(new JObject());
            Assert.IsInstanceOf<SuccessResponse>(result);

            var success = (SuccessResponse)result;
            Assert.That(success.Message, Does.Contain("configured"));
            Assert.IsNotNull(success.Data);
        }

        [Test]
        public void SetUpdateFrequency_TimeScaleNegative_ReturnsError()
        {
            var @params = new JObject { ["time_scale"] = -1f };
            var result = EditorSetUpdateFrequency.HandleCommand(@params);
            Assert.IsInstanceOf<ErrorResponse>(result);
            Assert.That(((ErrorResponse)result).Error, Does.Contain("time_scale"));
        }

        [Test]
        public void SetUpdateFrequency_TimeScaleAboveMax_ReturnsError()
        {
            var @params = new JObject { ["time_scale"] = 101f };
            var result = EditorSetUpdateFrequency.HandleCommand(@params);
            Assert.IsInstanceOf<ErrorResponse>(result);
            Assert.That(((ErrorResponse)result).Error, Does.Contain("time_scale"));
        }

        [Test]
        public void SetUpdateFrequency_CaptureFramerateNegative_ReturnsError()
        {
            var @params = new JObject { ["capture_framerate"] = -1 };
            var result = EditorSetUpdateFrequency.HandleCommand(@params);
            Assert.IsInstanceOf<ErrorResponse>(result);
            Assert.That(((ErrorResponse)result).Error, Does.Contain("capture_framerate"));
        }

        [Test]
        public void SetUpdateFrequency_ValidTimeScale_AppliesAndReturns()
        {
            float original = UnityEngine.Time.timeScale;
            try
            {
                var @params = new JObject { ["time_scale"] = 0.5f };
                var result = EditorSetUpdateFrequency.HandleCommand(@params);
                Assert.IsInstanceOf<SuccessResponse>(result);
                Assert.AreEqual(0.5f, UnityEngine.Time.timeScale, 0.001f);
            }
            finally
            {
                UnityEngine.Time.timeScale = original;
            }
        }

        [Test]
        public void SetUpdateFrequency_ValidCaptureFramerate_AppliesAndReturns()
        {
            int original = UnityEngine.Time.captureFramerate;
            try
            {
                var @params = new JObject { ["capture_framerate"] = 10 };
                var result = EditorSetUpdateFrequency.HandleCommand(@params);
                Assert.IsInstanceOf<SuccessResponse>(result);
                Assert.AreEqual(10, UnityEngine.Time.captureFramerate);
            }
            finally
            {
                UnityEngine.Time.captureFramerate = original;
            }
        }

        // --- EditorPlayForFrames ---

        [Test]
        public void PlayForFrames_FramesZero_ReturnsError()
        {
            var @params = new JObject { ["frames"] = 0 };
            var result = EditorPlayForFrames.HandleCommand(@params).Result;
            Assert.IsInstanceOf<ErrorResponse>(result);
            Assert.That(((ErrorResponse)result).Error, Does.Contain("frames"));
        }

        [Test]
        public void PlayForFrames_FramesNegative_ReturnsError()
        {
            var @params = new JObject { ["frames"] = -5 };
            var result = EditorPlayForFrames.HandleCommand(@params).Result;
            Assert.IsInstanceOf<ErrorResponse>(result);
            Assert.That(((ErrorResponse)result).Error, Does.Contain("frames"));
        }

        [Test]
        public void PlayForFrames_NotInPlayMode_ReturnsError()
        {
            var @params = new JObject { ["frames"] = 10 };
            var result = EditorPlayForFrames.HandleCommand(@params).Result;
            Assert.IsInstanceOf<ErrorResponse>(result);
            Assert.That(((ErrorResponse)result).Error, Does.Contain("Not in play mode"));
        }
    }
}
