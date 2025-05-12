using NUnit.Framework;
using Unity.PolySpatial.Internals;
using UnityEngine;

namespace Unity.PolySpatial.Extensions.RuntimeTests
{
    class UtilsTests
    {
        [TestCase("a", "b", -1)]
        [TestCase("b", "a", 1)]
        [TestCase("a", "a", 0)]
        [TestCase("a", "A", 32)]
        [TestCase("A", "a", -32)]
        [TestCase("a", "aa", -1)]
        [TestCase("aa", "a", 1)]
        [TestCase("a", "ab", -1)]
        [TestCase("ab", "a", 1)]
        [TestCase("a", "a1", -1)]
        [TestCase("a1", "a", 1)]
        [TestCase("a1", "a2", -1)]
        [TestCase("a2", "a1", 1)]
        [TestCase("a1", "a10", -1)]
        [TestCase("a10", "a1", 1)]
        [Test]
        public void Utils_LexicalCompare(string a, string b, int expected)
        {
            Assert.AreEqual(expected, Utils.LexicalCompare(a, b));
        }

        /// <summary>
        /// Will ignore if the test is for generic tracker, and the generic tracker is not enabled, and vice versa.
        /// </summary>
        public static void AssertIgnore_CheckGenericTrackerEnabled(bool IsGenericTrackerTest)
        {
            if (IsGenericTrackerTest && !PolySpatialUnityTracker.GenericTrackerEnabled)
                Assert.Ignore("Generic tracker test ignored because generic tracking is not enabled.");

            if (!IsGenericTrackerTest && PolySpatialUnityTracker.GenericTrackerEnabled)
                Assert.Ignore("Non-generic tracker test ignored because generic tracking is enabled.");
        }
    }
}
