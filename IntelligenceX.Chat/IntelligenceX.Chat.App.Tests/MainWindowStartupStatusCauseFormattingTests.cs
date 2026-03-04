using System;
using IntelligenceX.Chat.App;
using Xunit;

namespace IntelligenceX.Chat.App.Tests;

/// <summary>
/// Tests startup status cause formatting helpers used by startup/reconnect status text.
/// </summary>
public sealed class MainWindowStartupStatusCauseFormattingTests {
    /// <summary>
    /// Ensures structured startup context suffix formatter emits phase/cause tokens consistently.
    /// </summary>
    [Theory]
    [InlineData(null, null, "")]
    [InlineData("startup_connect", null, " (phase startup_connect)")]
    [InlineData(null, "pipe_retry", " (cause pipe_retry)")]
    [InlineData("startup_connect", "pipe_retry", " (phase startup_connect, cause pipe_retry)")]
    public void BuildStartupStatusContextSuffix_ReturnsExpectedValue(
        string? phase,
        string? cause,
        string expected) {
        var suffix = MainWindow.BuildStartupStatusContextSuffix(phase, cause);
        Assert.Equal(expected, suffix);
    }

    /// <summary>
    /// Ensures structured startup context segment formatter emits phase/cause tokens consistently.
    /// </summary>
    [Theory]
    [InlineData(null, null, "")]
    [InlineData("startup_connect", null, ", phase startup_connect")]
    [InlineData(null, "pipe_retry", ", cause pipe_retry")]
    [InlineData("startup_connect", "pipe_retry", ", phase startup_connect, cause pipe_retry")]
    public void BuildStartupStatusContextSegment_ReturnsExpectedValue(
        string? phase,
        string? cause,
        string expected) {
        var segment = MainWindow.BuildStartupStatusContextSegment(phase, cause);
        Assert.Equal(expected, segment);
    }

    /// <summary>
    /// Ensures status context append helper preserves base text and normalized phase/cause suffix behavior.
    /// </summary>
    [Theory]
    [InlineData("Runtime connected.", null, null, "Runtime connected.")]
    [InlineData("Runtime connected.", " startup_metadata_sync ", " metadata_sync ", "Runtime connected. (phase startup_metadata_sync, cause metadata_sync)")]
    [InlineData("Runtime connected.", "startup_metadata_sync", null, "Runtime connected. (phase startup_metadata_sync)")]
    [InlineData("Runtime connected.", null, "metadata_sync", "Runtime connected. (cause metadata_sync)")]
    public void AppendStartupStatusContext_ReturnsExpectedValue(
        string statusText,
        string? phase,
        string? cause,
        string expected) {
        var text = MainWindow.AppendStartupStatusContext(statusText, phase, cause);
        Assert.Equal(expected, text);
    }

    /// <summary>
    /// Ensures cause suffix formatter trims input and suppresses null/blank causes.
    /// </summary>
    [Theory]
    [InlineData(null, "")]
    [InlineData("", "")]
    [InlineData("   ", "")]
    [InlineData("metadata_retry", " (cause metadata_retry)")]
    [InlineData("  metadata_retry  ", " (cause metadata_retry)")]
    public void BuildStartupStatusCauseSuffix_ReturnsExpectedValue(
        string? cause,
        string expected) {
        var suffix = MainWindow.BuildStartupStatusCauseSuffix(cause);
        Assert.Equal(expected, suffix);
    }

    /// <summary>
    /// Ensures cause segment formatter trims input and suppresses null/blank causes.
    /// </summary>
    [Theory]
    [InlineData(null, "")]
    [InlineData("", "")]
    [InlineData("   ", "")]
    [InlineData("pipe_retry", ", cause pipe_retry")]
    [InlineData("  pipe_retry  ", ", cause pipe_retry")]
    public void BuildStartupStatusCauseSegment_ReturnsExpectedValue(
        string? cause,
        string expected) {
        var segment = MainWindow.BuildStartupStatusCauseSegment(cause);
        Assert.Equal(expected, segment);
    }

    /// <summary>
    /// Ensures status cause append helper preserves base text and normalized cause suffix behavior.
    /// </summary>
    [Theory]
    [InlineData("Runtime connected.", null, "Runtime connected.")]
    [InlineData("Runtime connected.", " ", "Runtime connected.")]
    [InlineData("Runtime connected.", "metadata_sync", "Runtime connected. (cause metadata_sync)")]
    [InlineData("Runtime connected", "metadata_sync", "Runtime connected (cause metadata_sync)")]
    [InlineData("Runtime connected...", " metadata_sync ", "Runtime connected... (cause metadata_sync)")]
    public void AppendStartupStatusCause_ReturnsExpectedValue(
        string statusText,
        string? cause,
        string expected) {
        var text = MainWindow.AppendStartupStatusCause(statusText, cause);
        Assert.Equal(expected, text);
    }

    /// <summary>
    /// Ensures append helper enforces non-null base status text.
    /// </summary>
    [Fact]
    public void AppendStartupStatusCause_ThrowsOnNullStatusText() {
        var error = Assert.Throws<ArgumentNullException>(() =>
            MainWindow.AppendStartupStatusCause(null!, MainWindow.StartupStatusCauseMetadataSync));
        Assert.Equal("statusText", error.ParamName);
    }

    /// <summary>
    /// Ensures append helper enforces non-null base status text for phase+cause context mode.
    /// </summary>
    [Fact]
    public void AppendStartupStatusContext_ThrowsOnNullStatusText() {
        var error = Assert.Throws<ArgumentNullException>(() =>
            MainWindow.AppendStartupStatusContext(
                null!,
                MainWindow.StartupStatusPhaseStartupMetadataSync,
                MainWindow.StartupStatusCauseMetadataSync));
        Assert.Equal("statusText", error.ParamName);
    }
}
