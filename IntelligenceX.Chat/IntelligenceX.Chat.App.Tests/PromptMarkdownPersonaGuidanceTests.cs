using IntelligenceX.Chat.App.Markdown;
using Xunit;

namespace IntelligenceX.Chat.App.Tests;

/// <summary>
/// Contract tests for language-neutral persona guidance in shared prompt envelopes.
/// </summary>
public sealed class PromptMarkdownPersonaGuidanceTests {
    /// <summary>
    /// Ensures the normal operational envelope preserves non-English persona text and its semantic boundary.
    /// </summary>
    [Fact]
    public void BuildThinServiceRequest_PreservesNonEnglishPersonaSemantics() {
        var markdown = PromptMarkdownBuilder.BuildThinServiceRequest(
            userText: "Sprawdź replikację.",
            effectivePersona: "pomocny, pragmatyczny administrator z lekkim humorem");

        Assert.Contains("Assistant persona: pomocny, pragmatyczny administrator z lekkim humorem", markdown);
        Assert.Contains("language it was written", markdown, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("actual runtime capabilities", markdown, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Ensures the richer envelope applies the same language-neutral semantic contract without lexical expansion.
    /// </summary>
    [Fact]
    public void BuildServiceRequest_PreservesNonEnglishPersonaSemantics() {
        var markdown = PromptMarkdownBuilder.BuildServiceRequest(
            userText: "Sprawdź replikację.",
            effectiveName: null,
            effectivePersona: "pomocny, pragmatyczny administrator z lekkim humorem",
            onboardingInProgress: false,
            missingOnboardingFields: Array.Empty<string>(),
            includeLiveProfileUpdates: false,
            executionBehaviorPrompt: string.Empty);

        Assert.Contains("Assistant persona: pomocny, pragmatyczny administrator z lekkim humorem", markdown);
        Assert.Contains("language it was written", markdown, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("actual runtime capabilities", markdown, StringComparison.OrdinalIgnoreCase);
    }
}
