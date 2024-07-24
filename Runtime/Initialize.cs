using Unity.PolySpatial.Internals;
using UnityEngine;

namespace Unity.PolySpatial.Extensions
{
#if POLYSPATIAL_INTERNAL
    /// Dummy runtime class to make sure runtime assembly works
    public static class Initialize
    {
        [RuntimeInitializeOnLoadMethod]
        static void RuntimeInitialize()
        {
            // Test that this class can access com.unity.polyspatial code
            Logging.Log(LogCategory.Debug, "Unity.PolySpatial.Extensions.Initialize.RuntimeInitialize() successfully called");
        }
    }
#endif
}
