using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace Unity.PolySpatial.Extensions.RuntimeTests
{
    [TestFixture]
    class ValidationTests
    {
        /// <summary>
        /// Override and return true if tests rely on GenericTracker to be enabled
        /// </summary>
        protected virtual bool IsGenericTrackerTest => false;

        [TestCase("Shader Graphs/FlatStereoscropicProjected")]
        [TestCase("Shader Graphs/DepthReprojection")]
        [TestCase("Shader Graphs/FlatStereoscropicStatic")]
        [Test]
        public void PolySpatialStereoFramebufferCamera_Shaders(string shaderName)
        {
            var shader = Shader.Find(shaderName);
            Assert.IsNotNull(shader);
        }

        // These tests aren't able to run in isolation because the stereo camera render requires additional SRP setup which is configured in the test proj.
    }
}
