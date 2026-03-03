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
}
