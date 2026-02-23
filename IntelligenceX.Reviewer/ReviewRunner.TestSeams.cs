using IntelligenceX.OpenAI.Native;

namespace IntelligenceX.Reviewer;

internal sealed partial class ReviewRunner {
    internal Task PreflightNativeConnectivityForTestsAsync(OpenAINativeOptions options, TimeSpan timeout,
        CancellationToken cancellationToken = default) =>
        PreflightNativeConnectivityAsync(options, timeout, cancellationToken);

    internal static Exception? MapPreflightConnectivityExceptionForTests(HttpRequestException ex, string host,
        TimeSpan timeout, bool cancellationRequested) =>
        MapPreflightConnectivityException(ex, host, timeout, cancellationRequested);
}
