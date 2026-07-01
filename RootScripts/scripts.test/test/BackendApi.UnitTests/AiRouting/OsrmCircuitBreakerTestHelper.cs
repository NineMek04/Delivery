using System.Reflection;
using BackendApi.Services.Ai;

namespace BackendApi.UnitTests.AiRouting;

internal static class OsrmCircuitBreakerTestHelper
{
    public static void Reset()
    {
        var field = typeof(OsrmRoutingService).GetField(
            "_circuitBreakerPolicy",
            BindingFlags.NonPublic | BindingFlags.Static);
        var policy = field?.GetValue(null);
        policy?.GetType()
            .GetMethod("Reset", BindingFlags.Public | BindingFlags.Instance)
            ?.Invoke(policy, null);
    }
}
