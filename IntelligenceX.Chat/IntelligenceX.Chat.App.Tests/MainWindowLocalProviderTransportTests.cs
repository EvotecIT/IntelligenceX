using IntelligenceX.Chat.App;
using Xunit;

namespace IntelligenceX.Chat.App.Tests;

/// <summary>
/// Guards local-provider transport normalization so runtime apply does not silently fall back to native.
/// </summary>
public sealed class MainWindowLocalProviderTransportTests {
    /// <summary>
    /// Ensures known transport aliases normalize to supported transport keys.
    /// </summary>
    [Theory]
    [InlineData("native", "native")]
    [InlineData("compatible-http", "compatible-http")]
    [InlineData("ollama", "compatible-http")]
    [InlineData("lmstudio", "compatible-http")]
    [InlineData("copilot-cli", "copilot-cli")]
    [InlineData("github-copilot", "copilot-cli")]
    public void TryNormalizeLocalProviderTransport_AcceptsKnownAliases(string value, string expectedTransport) {
        var parsed = MainWindow.TryNormalizeLocalProviderTransport(value, out var transport);

        Assert.True(parsed);
        Assert.Equal(expectedTransport, transport);
    }

    /// <summary>
    /// Ensures unknown values are rejected and reported as invalid.
    /// </summary>
    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("unsupported")]
    [InlineData("copilot-subscription")]
    public void TryNormalizeLocalProviderTransport_RejectsUnknownValues(string value) {
        var parsed = MainWindow.TryNormalizeLocalProviderTransport(value, out var transport);

        Assert.False(parsed);
        Assert.Equal("native", transport);
    }
}
