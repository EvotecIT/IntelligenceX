using IntelligenceX.Chat.App;
using Xunit;

namespace IntelligenceX.Chat.App.Tests;

/// <summary>
/// Tests for persona-derived guidance used by prompt assembly.
/// </summary>
public sealed class MainWindowPersonaGuidanceTests {
    /// <summary>
    /// Ensures common persona traits become concrete guidance lines instead of staying inert profile text.
    /// </summary>
    [Fact]
    public void BuildPersonaGuidanceLines_ExpandsHelpfulConciseHumorousPersona() {
        var lines = MainWindow.BuildPersonaGuidanceLines("helpful assistant with a bit of dark humour and concise outputs");

        Assert.Contains(lines, line => line.Contains("Preferred role framing", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(lines, line => line.Contains("reduce user effort", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(lines, line => line.Contains("Light humor is allowed", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(lines, line => line.Contains("shorter answers", StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Ensures empty persona text does not create synthetic guidance.
    /// </summary>
    [Fact]
    public void BuildPersonaGuidanceLines_ReturnsEmptyForBlankPersona() {
        var lines = MainWindow.BuildPersonaGuidanceLines("   ");

        Assert.Empty(lines);
    }
}
