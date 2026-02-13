using IntelligenceX.Chat.App.Markdown;
using Xunit;

namespace IntelligenceX.Chat.App.Tests;

/// <summary>
/// Tests for prompt markdown envelope composition.
/// </summary>
public sealed class PromptMarkdownBuilderTests {
    /// <summary>
    /// Ensures service request contains typed context sections when profile context is present.
    /// </summary>
    [Fact]
    public void BuildServiceRequest_IncludesContextSections() {
        var markdown = PromptMarkdownBuilder.BuildServiceRequest(
            userText: "Can you list top 3 domain admins?",
            effectiveName: "Przemek",
            effectivePersona: "security analyst with concise outputs",
            onboardingInProgress: true,
            missingOnboardingFields: new[] { "themePreset" },
            includeLiveProfileUpdates: true,
            executionBehaviorPrompt: "[Execution behavior]\n- Retry tools before asking user.");

        Assert.Contains("[Session profile context]", markdown);
        Assert.Contains("- User name: Przemek", markdown);
        Assert.Contains("- Assistant persona: security analyst with concise outputs", markdown);
        Assert.Contains("[Onboarding context]", markdown);
        Assert.Contains("[Live profile updates]", markdown);
        Assert.Contains("[Execution behavior]", markdown);
        Assert.Contains("User request:", markdown);
        Assert.Contains("Can you list top 3 domain admins?", markdown);
    }

    /// <summary>
    /// Ensures plain user text is returned when no context envelope is needed.
    /// </summary>
    [Fact]
    public void BuildServiceRequest_ReturnsUserTextWithoutContext() {
        var markdown = PromptMarkdownBuilder.BuildServiceRequest(
            userText: "hello",
            effectiveName: null,
            effectivePersona: null,
            onboardingInProgress: false,
            missingOnboardingFields: new string[0],
            includeLiveProfileUpdates: false,
            executionBehaviorPrompt: string.Empty);

        Assert.Equal("hello", markdown);
    }
}
