using IntelligenceX.OpenAI.Native;

namespace IntelligenceX.Reviewer;

internal sealed partial class ReviewRunner {
    // Test-only forwarders: keep connectivity behavior tests compile-time bound without private reflection.

    /// <summary>Test-only forwarder for native connectivity preflight.</summary>
    internal Task PreflightNativeConnectivityForTestsAsync(OpenAINativeOptions options, TimeSpan timeout,
        CancellationToken cancellationToken = default) =>
        PreflightNativeConnectivityAsync(options, timeout, cancellationToken);

    /// <summary>Test-only forwarder for preflight exception mapping.</summary>
    internal static Exception? MapPreflightConnectivityExceptionForTests(HttpRequestException ex, string host,
        TimeSpan timeout, bool cancellationRequested) =>
        MapPreflightConnectivityException(ex, host, timeout, cancellationRequested);
}
