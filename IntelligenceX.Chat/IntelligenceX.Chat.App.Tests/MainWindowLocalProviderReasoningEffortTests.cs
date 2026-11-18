using IntelligenceX.Chat.App;
using Xunit;

namespace IntelligenceX.Chat.App.Tests;

/// <summary>
/// Covers reasoning effort normalization for local provider profiles.
/// </summary>
public sealed class MainWindowLocalProviderReasoningEffortTests {
    /// <summary>
    /// Ensures current Codex effort levels survive normalization and unknown values clear the override.
    /// </summary>
    [Theory]
    [InlineData("xhigh", "xhigh")]
    [InlineData("X-High", "xhigh")]
    [InlineData("max", "max")]
    [InlineData(" Ultra ", "ultra")]
    [InlineData("extreme", "")]
    public void NormalizeLocalProviderReasoningEffort_KeepsSupportedLevels(string input, string expected) {
        Assert.Equal(expected, MainWindow.NormalizeLocalProviderReasoningEffort(input));
    }
}
