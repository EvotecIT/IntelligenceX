using System;
using System.Threading;
using IntelligenceX.Chat.App;
using Xunit;

namespace IntelligenceX.Chat.App.Tests;

/// <summary>
/// Guards active-turn cancellation classification in reconnect flow.
/// </summary>
public sealed class MainWindowTurnCancellationClassificationTests {
    /// <summary>
    /// Ensures per-turn cancellation token maps OperationCanceledException to canceled outcome.
    /// </summary>
    [Fact]
    public void IsActiveTurnCancellation_ReturnsTrueWhenOperationCanceledAndTokenCanceled() {
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var result = MainWindow.IsActiveTurnCancellation(new OperationCanceledException(), cts.Token);

        Assert.True(result);
    }

    /// <summary>
    /// Ensures OperationCanceledException is not treated as user cancel when the active token is not canceled.
    /// </summary>
    [Fact]
    public void IsActiveTurnCancellation_ReturnsFalseWhenOperationCanceledButTokenNotCanceled() {
        using var cts = new CancellationTokenSource();

        var result = MainWindow.IsActiveTurnCancellation(new OperationCanceledException(), cts.Token);

        Assert.False(result);
    }

    /// <summary>
    /// Ensures non-cancel exceptions do not map to canceled even when the active token is canceled.
    /// </summary>
    [Fact]
    public void IsActiveTurnCancellation_ReturnsFalseWhenExceptionIsNotOperationCanceled() {
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var result = MainWindow.IsActiveTurnCancellation(new InvalidOperationException("boom"), cts.Token);

        Assert.False(result);
    }
}
