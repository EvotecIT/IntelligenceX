using System;
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
            executionBehaviorPrompt: "[Execution behavior]\n- Retry tools before asking user.",
            persistentMemoryLines: new[] { "Prefers concise answers." },
            persistentMemoryPrompt: "[Persistent memory protocol]");

        Assert.Contains("[Session profile context]", markdown);
        Assert.Contains("- User name: Przemek", markdown);
        Assert.Contains("- Assistant persona: security analyst with concise outputs", markdown);
        Assert.Contains("[Onboarding context]", markdown);
        Assert.Contains("[Live profile updates]", markdown);
        Assert.Contains("[Execution behavior]", markdown);
        Assert.Contains("[Persistent memory protocol]", markdown);
        Assert.Contains("[Persistent memory]", markdown);
        Assert.Contains("Prefers concise answers.", markdown);
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

    /// <summary>
    /// Ensures persistent memory context can still shape the prompt even without profile context.
    /// </summary>
    [Fact]
    public void BuildServiceRequest_IncludesMemoryContextWithoutProfile() {
        var markdown = PromptMarkdownBuilder.BuildServiceRequest(
            userText: "What should we do next?",
            effectiveName: null,
            effectivePersona: null,
            onboardingInProgress: false,
            missingOnboardingFields: Array.Empty<string>(),
            includeLiveProfileUpdates: false,
            executionBehaviorPrompt: string.Empty,
            localContextLines: null,
            persistentMemoryLines: new[] { "Default domain controller is DC-01." },
            persistentMemoryPrompt: "[Persistent memory protocol]");

        Assert.Contains("[Persistent memory protocol]", markdown);
        Assert.Contains("[Persistent memory]", markdown);
        Assert.Contains("Default domain controller is DC-01.", markdown);
        Assert.Contains("User request:", markdown);
    }
}
