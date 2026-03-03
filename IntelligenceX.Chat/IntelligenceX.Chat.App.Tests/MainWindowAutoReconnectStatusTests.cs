using System;
using IntelligenceX.Chat.App;
using Xunit;

namespace IntelligenceX.Chat.App.Tests;

/// <summary>
/// Tests reconnect-status text formatting for startup/runtime connection churn visibility.
/// </summary>
public sealed class MainWindowAutoReconnectStatusTests {
    /// <summary>
    /// Ensures immediate reconnect retries are rendered with normalized attempt numbering.
    /// </summary>
    [Fact]
    public void BuildAutoReconnectStatusText_FormatsImmediateRetry() {
        var status = MainWindow.BuildAutoReconnectStatusText(attempt: 0, delay: TimeSpan.Zero);
        Assert.Equal("Runtime connection dropped. Reconnecting now (attempt 1).", status);
    }

    /// <summary>
    /// Ensures delayed reconnect retries render stable time labels in milliseconds/seconds.
    /// </summary>
    [Theory]
    [InlineData(2, 250, "Runtime connection dropped. Reconnecting in 250ms (attempt 2).")]
    [InlineData(3, 1500, "Runtime connection dropped. Reconnecting in 1.5s (attempt 3).")]
    public void BuildAutoReconnectStatusText_FormatsDelayedRetry(
        int attempt,
        int delayMs,
        string expected) {
        var status = MainWindow.BuildAutoReconnectStatusText(attempt, TimeSpan.FromMilliseconds(delayMs));
        Assert.Equal(expected, status);
    }
}
