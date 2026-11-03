using System;

namespace IntelligenceX.Telemetry.Agents;

// Opt-in, content-free phase hooks for the diagnostic executable. No clocks, native calls,
// allocation or public configuration are added to the normal consumer path.
internal static class AgentWorkMeasurement {
    internal static Func<string, IDisposable>? Measure;
    internal static Action<string, long>? Count;
    internal static IDisposable? Begin(string phase) => Measure?.Invoke(phase);
    internal static void Add(string counter, long value) => Count?.Invoke(counter, value);
}
