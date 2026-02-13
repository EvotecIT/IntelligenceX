using System;
using System.Linq;
using IntelligenceX.Chat.App.Theming;
using Xunit;

namespace IntelligenceX.Chat.App.Tests;

/// <summary>
/// Tests for canonical theme contract behavior.
/// </summary>
public sealed class ThemeContractTests {
    /// <summary>
    /// Ensures canonical list includes default and all non-default presets.
    /// </summary>
    [Fact]
    public void CanonicalPresetNames_ContainsDefaultAndRegistryPresets() {
        Assert.Contains("default", ThemeContract.CanonicalPresetNames);
        foreach (var preset in ThemeRegistry.PresetNames) {
            Assert.Contains(preset, ThemeContract.CanonicalPresetNames);
        }
    }

    /// <summary>
    /// Ensures aliases normalize to canonical values.
    /// </summary>
    [Theory]
    [InlineData("blue", "cobalt")]
    [InlineData("gray", "graphite")]
    [InlineData("grey", "graphite")]
    [InlineData("Default", "default")]
    [InlineData("ROSE", "rose")]
    public void Normalize_MapsAliasesAndCanonicalValues(string input, string expected) {
        var normalized = ThemeContract.Normalize(input);
        Assert.Equal(expected, normalized);
    }

    /// <summary>
    /// Ensures unknown/blank values are rejected.
    /// </summary>
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("neon")]
    public void Normalize_ReturnsNull_ForUnknownValues(string? input) {
        Assert.Null(ThemeContract.Normalize(input));
    }

    /// <summary>
    /// Ensures schema used in model prompts is canonical-only.
    /// </summary>
    [Fact]
    public void ThemePresetSchema_UsesCanonicalPresetsOnly() {
        var schemaParts = ThemeContract.ThemePresetSchema.Split('|', StringSplitOptions.RemoveEmptyEntries);
        Assert.Equal(ThemeContract.CanonicalPresetNames.Count, schemaParts.Length);
        Assert.DoesNotContain("blue", schemaParts, StringComparer.OrdinalIgnoreCase);
        Assert.DoesNotContain("gray", schemaParts, StringComparer.OrdinalIgnoreCase);
        Assert.DoesNotContain("grey", schemaParts, StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Ensures generated option tags include each canonical preset value.
    /// </summary>
    [Fact]
    public void BuildThemeOptionTagsHtml_IncludesEveryCanonicalPreset() {
        var html = ThemeContract.BuildThemeOptionTagsHtml();
        foreach (var preset in ThemeContract.CanonicalPresetNames) {
            Assert.Contains($"value=\"{preset}\"", html, StringComparison.Ordinal);
        }
    }

    /// <summary>
    /// Ensures known-token detection recognizes aliases and canonical names.
    /// </summary>
    [Fact]
    public void ContainsKnownToken_DetectsKnownThemeTokens() {
        Assert.True(ThemeContract.ContainsKnownToken("use cobalt theme"));
        Assert.True(ThemeContract.ContainsKnownToken("switch to blue theme"));
        Assert.False(ThemeContract.ContainsKnownToken("use neon theme"));
    }
}
