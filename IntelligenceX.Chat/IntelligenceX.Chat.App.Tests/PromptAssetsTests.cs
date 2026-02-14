using IntelligenceX.Chat.App;
using Xunit;

namespace IntelligenceX.Chat.App.Tests;

/// <summary>
/// Tests for embedded prompt asset loading and token rendering.
/// </summary>
public sealed class PromptAssetsTests {
    /// <summary>
    /// Ensures onboarding prompt template tokens are rendered and missing-fields bullet is injected.
    /// </summary>
    [Fact]
    public void GetOnboardingGuidancePrompt_RendersTokensAndMissingFields() {
        var markdown = PromptAssets.GetOnboardingGuidancePrompt(
            new[] { "userName", "themePreset" },
            "default|emerald|rose|cobalt|amber|graphite");

        Assert.Contains("Missing profile fields: userName, themePreset", markdown);
        Assert.Contains("\"themePreset\":\"default|emerald|rose|cobalt|amber|graphite\"", markdown);
        Assert.DoesNotContain("{{MISSING_FIELDS_BULLET}}", markdown);
        Assert.DoesNotContain("{{THEME_PRESET_SCHEMA}}", markdown);
    }

    /// <summary>
    /// Ensures missing-fields bullet is omitted when there are no missing fields.
    /// </summary>
    [Fact]
    public void GetOnboardingGuidancePrompt_OmitsMissingFieldsBullet_WhenNoneMissing() {
        var markdown = PromptAssets.GetOnboardingGuidancePrompt(
            System.Array.Empty<string>(),
            "default|emerald");

        Assert.DoesNotContain("Missing profile fields:", markdown);
        Assert.Contains("\"themePreset\":\"default|emerald\"", markdown);
    }

    /// <summary>
    /// Ensures kickoff prelude prompt text is available from resources/fallback.
    /// </summary>
    [Fact]
    public void GetKickoffPreludePrompt_ReturnsExpectedPrelude() {
        var markdown = PromptAssets.GetKickoffPreludePrompt();

        Assert.Contains("Start the conversation naturally in 1-2 short sentences.", markdown);
        Assert.Contains("Do not use rigid onboarding scripts.", markdown);
    }

    /// <summary>
    /// Ensures persistent-memory protocol prompt is available from resources/fallback.
    /// </summary>
    [Fact]
    public void GetPersistentMemoryPrompt_ReturnsProtocolTemplate() {
        var markdown = PromptAssets.GetPersistentMemoryPrompt();

        Assert.Contains("[Persistent memory protocol]", markdown);
        Assert.Contains("```ix_memory", markdown);
        Assert.Contains("\"upserts\"", markdown);
    }
}
