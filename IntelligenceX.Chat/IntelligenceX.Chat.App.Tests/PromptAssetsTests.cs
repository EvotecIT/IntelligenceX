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
        Assert.Contains("greet them back naturally instead of front-loading scope questions", markdown, System.StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Match the user's tone and energy level", markdown, System.StringComparison.OrdinalIgnoreCase);
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

    /// <summary>
    /// Ensures execution-behavior guidance reinforces natural conversation over protocol leakage.
    /// </summary>
    [Fact]
    public void GetExecutionBehaviorPrompt_ReturnsNaturalConversationGuidance() {
        var markdown = PromptAssets.GetExecutionBehaviorPrompt();

        Assert.Contains("respond like a real person first", markdown, System.StringComparison.OrdinalIgnoreCase);
        Assert.Contains("continue naturally", markdown, System.StringComparison.OrdinalIgnoreCase);
        Assert.Contains("recent pacing, directness, and energy level", markdown, System.StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Match response shape to the user", markdown, System.StringComparison.OrdinalIgnoreCase);
        Assert.Contains("meta questions", markdown, System.StringComparison.OrdinalIgnoreCase);
        Assert.Contains("1-2 short sentences", markdown, System.StringComparison.OrdinalIgnoreCase);
        Assert.Contains("compress them into one plain sentence", markdown, System.StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Preferred compact shape example", markdown, System.StringComparison.OrdinalIgnoreCase);
        Assert.Contains("best tool by task", markdown, System.StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Do not append generic follow-up suggestions by default", markdown, System.StringComparison.OrdinalIgnoreCase);
        Assert.Contains("end cleanly instead of forcing a follow-up", markdown, System.StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Avoid generic closing filler", markdown, System.StringComparison.OrdinalIgnoreCase);
        Assert.Contains("possible acknowledgement or light close", markdown, System.StringComparison.OrdinalIgnoreCase);
        Assert.Contains("likely answer or confirmation", markdown, System.StringComparison.OrdinalIgnoreCase);
        Assert.Contains("pending question, clarification, or structured follow-up action", markdown, System.StringComparison.OrdinalIgnoreCase);
        Assert.Contains("ask one short human clarification", markdown, System.StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Never expose internal routing tokens", markdown, System.StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Translate findings into plain human terms", markdown, System.StringComparison.OrdinalIgnoreCase);
        Assert.Contains("actively correlate them into one coherent story", markdown, System.StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Separate confirmed findings from hypotheses", markdown, System.StringComparison.OrdinalIgnoreCase);
        Assert.Contains("propose practical next steps grounded in the evidence", markdown, System.StringComparison.OrdinalIgnoreCase);
    }
}
