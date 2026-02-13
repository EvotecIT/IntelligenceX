using System;
using System.Collections.Generic;
using IntelligenceX.Chat.App.Theming;
using Xunit;

namespace IntelligenceX.Chat.App.Tests;

/// <summary>
/// Tests for theme preset registry behavior and integrity.
/// </summary>
public sealed class ThemeRegistryTests {
    private static readonly string[] ExpectedPresets = { "amber", "cobalt", "emerald", "graphite", "rose" };

    private static readonly string[] RequiredCssVariables = {
        "--ix-accent",
        "--ix-bg-primary",
        "--ix-bg-secondary",
        "--ix-bg-elevated",
        "--ix-text-secondary",
        "--ix-bubble-assistant-bg",
        "--ix-bubble-user-bg",
        "--ix-sidebar-bg",
        "--ix-scrollbar-thumb"
    };

    /// <summary>
    /// Ensures known preset names remain stable for UI/profile contracts.
    /// </summary>
    [Fact]
    public void PresetNames_ReturnsExpectedSet() {
        var names = new HashSet<string>(ThemeRegistry.PresetNames, StringComparer.OrdinalIgnoreCase);
        Assert.Equal(ExpectedPresets.Length, names.Count);

        foreach (var expected in ExpectedPresets) {
            Assert.Contains(expected, names);
        }
    }

    /// <summary>
    /// Ensures preset lookup is case-insensitive and resolves css variables.
    /// </summary>
    [Fact]
    public void TryGetVariables_IsCaseInsensitive() {
        var found = ThemeRegistry.TryGetVariables("RoSe", out var variables);

        Assert.True(found);
        Assert.NotNull(variables);
        Assert.True(variables.Count > 0);
        Assert.Equal("#f472b6", variables["--ix-accent"]);
    }

    /// <summary>
    /// Ensures unknown or blank names are rejected cleanly.
    /// </summary>
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("default")]
    [InlineData("neon")]
    public void TryGetVariables_ReturnsFalse_ForUnknownPreset(string? preset) {
        var found = ThemeRegistry.TryGetVariables(preset, out var variables);

        Assert.False(found);
        Assert.Empty(variables);
    }

    /// <summary>
    /// Ensures each preset carries the minimum required css variables.
    /// </summary>
    [Fact]
    public void Presets_ContainRequiredCssVariables() {
        foreach (var preset in ThemeRegistry.PresetNames) {
            Assert.True(ThemeRegistry.TryGetVariables(preset, out var variables));
            foreach (var key in RequiredCssVariables) {
                Assert.True(variables.TryGetValue(key, out var value), $"Preset '{preset}' missing key '{key}'.");
                Assert.False(string.IsNullOrWhiteSpace(value), $"Preset '{preset}' has empty value for '{key}'.");
            }
        }
    }
}
