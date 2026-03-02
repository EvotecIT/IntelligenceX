using IntelligenceX.Chat.App;
using Xunit;

namespace IntelligenceX.Chat.App.Tests;

/// <summary>
/// Verifies status text extraction from service bootstrap diagnostics.
/// </summary>
public sealed class MainWindowServiceBootstrapStatusTests {
    /// <summary>
    /// Parses pack bootstrap begin-progress diagnostics into a user-facing startup status.
    /// </summary>
    [Fact]
    public void TryBuildServiceBootstrapStatus_ParsesPackProgressBegin() {
        var parsed = MainWindow.TryBuildServiceBootstrapStatus(
            "[pack warning] [startup] pack_load_progress pack='eventlog' phase='begin' index='2' total='11'",
            out var statusText);

        Assert.True(parsed);
        Assert.Equal("Starting runtime... initializing tool packs 2/11 (eventlog)", statusText);
    }

    /// <summary>
    /// Ignores pack bootstrap end-progress diagnostics to avoid status spam.
    /// </summary>
    [Fact]
    public void TryBuildServiceBootstrapStatus_IgnoresPackProgressEnd() {
        var parsed = MainWindow.TryBuildServiceBootstrapStatus(
            "[pack warning] [startup] pack_load_progress pack='eventlog' phase='end' index='2' total='11' elapsed_ms='42'",
            out var statusText);

        Assert.False(parsed);
        Assert.Equal(string.Empty, statusText);
    }

    /// <summary>
    /// Parses plugin folder begin-progress diagnostics into a user-facing startup status.
    /// </summary>
    [Fact]
    public void TryBuildServiceBootstrapStatus_ParsesPluginProgressBegin() {
        var parsed = MainWindow.TryBuildServiceBootstrapStatus(
            "[pack warning] [plugin] load_progress plugin='dnsclientx' phase='begin' index='3' total='12'",
            out var statusText);

        Assert.True(parsed);
        Assert.Equal("Starting runtime... loading tool packs 3/12 (dnsclientx)", statusText);
    }

    /// <summary>
    /// Ignores end-progress diagnostics to avoid status spam.
    /// </summary>
    [Fact]
    public void TryBuildServiceBootstrapStatus_IgnoresPluginProgressEnd() {
        var parsed = MainWindow.TryBuildServiceBootstrapStatus(
            "[pack warning] [plugin] load_progress plugin='dnsclientx' phase='end' index='3' total='12' elapsed_ms='99'",
            out var statusText);

        Assert.False(parsed);
        Assert.Equal(string.Empty, statusText);
    }

    /// <summary>
    /// Parses summarized plugin progress diagnostics into a user-facing startup status.
    /// </summary>
    [Fact]
    public void TryBuildServiceBootstrapStatus_ParsesPluginProgressSummary() {
        var parsed = MainWindow.TryBuildServiceBootstrapStatus(
            "[pack warning] [startup] plugin load progress: processed 8/15 plugin folders (begin=15, end=8).",
            out var statusText);

        Assert.True(parsed);
        Assert.Equal("Starting runtime... plugin folder scan 8/15", statusText);
    }

    /// <summary>
    /// Parses tooling bootstrap timing diagnostics into a user-facing startup status.
    /// </summary>
    [Fact]
    public void TryBuildServiceBootstrapStatus_ParsesBootstrapTiming() {
        var parsed = MainWindow.TryBuildServiceBootstrapStatus(
            "[pack warning] [startup] tooling bootstrap timings total=1.8s policy=50ms options=20ms packs=1.6s registry=120ms tools=200 packsLoaded=14 packsDisabled=2 pluginRoots=3.",
            out var statusText);

        Assert.True(parsed);
        Assert.Equal("Starting runtime... tool bootstrap finished (1.8s), finalizing runtime connection", statusText);
    }

    /// <summary>
    /// Rejects unrelated service output lines.
    /// </summary>
    [Fact]
    public void TryBuildServiceBootstrapStatus_ReturnsFalseForUnrelatedLines() {
        var parsed = MainWindow.TryBuildServiceBootstrapStatus(
            "Client connected.",
            out var statusText);

        Assert.False(parsed);
        Assert.Equal(string.Empty, statusText);
    }
}
