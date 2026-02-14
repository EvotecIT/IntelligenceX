using System;
using IntelligenceX.Chat.App;
using Xunit;

namespace IntelligenceX.Chat.App.Tests;

/// <summary>
/// Regression tests for shell asset composition.
/// </summary>
public sealed class UiShellAssetsTests {
    /// <summary>
    /// Ensures the tool-source helpers are present in generated shell script.
    /// Missing helpers break tools rendering at runtime.
    /// </summary>
    [Fact]
    public void Load_IncludesPackSourceHelpers_ForToolsRendering() {
        var html = UiShellAssets.Load();

        Assert.Contains("function packSourceKind(", html, StringComparison.Ordinal);
        Assert.Contains("function packSourceLabel(", html, StringComparison.Ordinal);
    }
}
