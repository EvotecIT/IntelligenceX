using IntelligenceX.Chat.App;
using Xunit;

namespace IntelligenceX.Chat.App.Tests;

/// <summary>
/// Unit tests for webview bridge script helpers.
/// </summary>
public sealed class UiBridgeScriptsTests {
    /// <summary>
    /// Ensures wheel forwarding script includes host source marker and delta value.
    /// </summary>
    /// <param name="delta">Mouse wheel delta.</param>
    [Theory]
    [InlineData(120)]
    [InlineData(-240)]
    [InlineData(0)]
    public void BuildWheelForwardScript_UsesHostSourceMarker(int delta) {
        var script = UiBridgeScripts.BuildWheelForwardScript(delta);

        Assert.Contains($"ixScrollTranscript({delta}, 'host')", script, StringComparison.Ordinal);
    }
}
