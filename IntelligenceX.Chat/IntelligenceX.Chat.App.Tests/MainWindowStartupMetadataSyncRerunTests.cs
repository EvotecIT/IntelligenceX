using IntelligenceX.Chat.App;
using Xunit;

namespace IntelligenceX.Chat.App.Tests;

/// <summary>
/// Tests deferred startup metadata rerun scheduling decisions.
/// </summary>
public sealed class MainWindowStartupMetadataSyncRerunTests {
    /// <summary>
    /// Ensures busy metadata sync requests ask for rerun only when explicitly requested.
    /// </summary>
    [Theory]
    [InlineData(true, true, true)]
    [InlineData(true, false, false)]
    [InlineData(false, true, false)]
    [InlineData(false, false, false)]
    public void ShouldRequestDeferredStartupMetadataSyncRerun_ReturnsExpectedValue(
        bool metadataSyncAlreadyQueued,
        bool requestRerunIfBusy,
        bool expected) {
        var shouldRerun = MainWindow.ShouldRequestDeferredStartupMetadataSyncRerun(
            metadataSyncAlreadyQueued,
            requestRerunIfBusy);
        Assert.Equal(expected, shouldRerun);
    }

    /// <summary>
    /// Ensures deferred metadata sync rerun dispatch runs only when requested and still safe.
    /// </summary>
    [Theory]
    [InlineData(true, false, true, true)]
    [InlineData(true, true, true, false)]
    [InlineData(true, false, false, false)]
    [InlineData(false, false, true, false)]
    public void ShouldDispatchDeferredStartupMetadataSyncRerun_ReturnsExpectedValue(
        bool rerunRequested,
        bool shutdownRequested,
        bool isConnected,
        bool expected) {
        var shouldDispatch = MainWindow.ShouldDispatchDeferredStartupMetadataSyncRerun(
            rerunRequested,
            shutdownRequested,
            isConnected);
        Assert.Equal(expected, shouldDispatch);
    }

    /// <summary>
    /// Ensures phase-failure recovery rerun is only requested when startup metadata sync is
    /// connected/safe and a critical phase (`hello` or `list_tools`) did not complete.
    /// </summary>
    [Theory]
    [InlineData(true, false, true, true, 0, 1, false)]
    [InlineData(true, false, false, true, 0, 1, true)]
    [InlineData(true, false, true, false, 0, 1, true)]
    [InlineData(true, false, false, false, 0, 1, true)]
    [InlineData(true, false, false, true, 1, 1, false)]
    [InlineData(true, true, false, true, 0, 1, false)]
    [InlineData(false, false, false, true, 0, 1, false)]
    [InlineData(true, false, false, true, 0, 0, false)]
    public void ShouldRequestDeferredStartupMetadataFailureRecoveryRerun_ReturnsExpectedValue(
        bool isConnected,
        bool shutdownRequested,
        bool helloPhaseSucceeded,
        bool toolCatalogPhaseSucceeded,
        int retriesConsumed,
        int retryLimit,
        bool expected) {
        var shouldRequest = MainWindow.ShouldRequestDeferredStartupMetadataFailureRecoveryRerun(
            isConnected: isConnected,
            shutdownRequested: shutdownRequested,
            helloPhaseSucceeded: helloPhaseSucceeded,
            toolCatalogPhaseSucceeded: toolCatalogPhaseSucceeded,
            retriesConsumed: retriesConsumed,
            retryLimit: retryLimit);

        Assert.Equal(expected, shouldRequest);
    }

    /// <summary>
    /// Ensures startup metadata failure kind labeling remains deterministic for diagnostics.
    /// </summary>
    [Theory]
    [InlineData(true, true, "none")]
    [InlineData(false, true, "hello")]
    [InlineData(true, false, "list_tools")]
    [InlineData(false, false, "hello_and_list_tools")]
    public void ResolveDeferredStartupMetadataFailureKind_ReturnsExpectedToken(
        bool helloPhaseSucceeded,
        bool toolCatalogPhaseSucceeded,
        string expectedToken) {
        var token = MainWindow.ResolveDeferredStartupMetadataFailureKind(
            helloPhaseSucceeded,
            toolCatalogPhaseSucceeded);

        Assert.Equal(expectedToken, token);
    }

    /// <summary>
    /// Ensures failure-recovery retry budget is consumed atomically and capped by configured limit.
    /// </summary>
    [Fact]
    public void TryConsumeDeferredStartupMetadataFailureRecoveryRetry_RespectsRetryLimit() {
        var retriesConsumed = 0;

        var first = MainWindow.TryConsumeDeferredStartupMetadataFailureRecoveryRetry(
            ref retriesConsumed,
            retryLimit: 1);
        var second = MainWindow.TryConsumeDeferredStartupMetadataFailureRecoveryRetry(
            ref retriesConsumed,
            retryLimit: 1);

        Assert.True(first);
        Assert.False(second);
        Assert.Equal(1, retriesConsumed);
    }

    /// <summary>
    /// Ensures deferred startup metadata phases retry on timeout/cancel/disconnect-class transient failures.
    /// </summary>
    [Theory]
    [InlineData("timeout", true)]
    [InlineData("cancel", true)]
    [InlineData("disconnected", true)]
    [InlineData("generic", false)]
    public void ShouldRetryDeferredStartupMetadataPhaseAttempt_ReturnsExpectedValue(
        string exceptionKind,
        bool expected) {
        Exception ex = exceptionKind switch {
            "timeout" => new TimeoutException("phase timeout"),
            "cancel" => new OperationCanceledException("phase canceled"),
            "disconnected" => new InvalidOperationException("Not connected to runtime."),
            _ => new InvalidOperationException("invalid request")
        };

        var shouldRetry = MainWindow.ShouldRetryDeferredStartupMetadataPhaseAttempt(ex);

        Assert.Equal(expected, shouldRetry);
    }
}
