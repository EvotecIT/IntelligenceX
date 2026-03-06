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
            runtimeCapabilityLines: new[] { "Parallel tool execution: enabled", "Max tool rounds: 24" },
            proactiveExecutionEnabled: true,
            conversationStyleLines: new[] { "Recent user style is terse and direct." },
            persistentMemoryLines: new[] { "Prefers concise answers." },
            persistentMemoryPrompt: "[Persistent memory protocol]");

        Assert.Contains("[Conversation style]", markdown);
        Assert.Contains("Recent user style is terse and direct.", markdown);
        Assert.Contains("[Session profile context]", markdown);
        Assert.Contains("- User name: Przemek", markdown);
        Assert.Contains("- Assistant persona: security analyst with concise outputs", markdown);
        Assert.Contains("[Onboarding context]", markdown);
        Assert.Contains("[Live profile updates]", markdown);
        Assert.Contains("[Runtime capability handshake]", markdown);
        Assert.Contains("Parallel tool execution: enabled", markdown);
        Assert.Contains("[Execution behavior]", markdown);
        Assert.Contains("[Proactive execution mode]", markdown);
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
            userText: "Can you summarize replication health?",
            effectiveName: null,
            effectivePersona: null,
            onboardingInProgress: false,
            missingOnboardingFields: new string[0],
            includeLiveProfileUpdates: false,
            executionBehaviorPrompt: string.Empty);

        Assert.Equal("Can you summarize replication health?", markdown);
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

    /// <summary>
    /// Ensures proactive execution mode can be explicitly disabled.
    /// </summary>
    [Fact]
    public void BuildServiceRequest_IncludesProactiveModeDisabledGuidance() {
        var markdown = PromptMarkdownBuilder.BuildServiceRequest(
            userText: "Just answer exactly what I asked.",
            effectiveName: null,
            effectivePersona: null,
            onboardingInProgress: false,
            missingOnboardingFields: Array.Empty<string>(),
            includeLiveProfileUpdates: false,
            executionBehaviorPrompt: string.Empty,
            proactiveExecutionEnabled: false);

        Assert.Contains("[Proactive execution mode]", markdown);
        Assert.Contains("Stay strictly scoped", markdown);
    }

    /// <summary>
    /// Ensures low-context short openers receive a conversation-mode hint instead of a bare task envelope.
    /// </summary>
    [Fact]
    public void BuildServiceRequest_IncludesConversationModeForLowContextShortTurn() {
        var markdown = PromptMarkdownBuilder.BuildServiceRequest(
            userText: "Hello mr",
            effectiveName: null,
            effectivePersona: null,
            onboardingInProgress: false,
            missingOnboardingFields: Array.Empty<string>(),
            includeLiveProfileUpdates: false,
            executionBehaviorPrompt: string.Empty);

        Assert.Contains("[Conversation mode]", markdown);
        Assert.Contains("Mode: low_context_short_turn", markdown);
        Assert.Contains("greet back naturally", markdown, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("brief natural close is enough", markdown, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Avoid generic closing filler", markdown, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("User request:", markdown);
    }

    /// <summary>
    /// Ensures compact follow-ups with recent transcript context are treated as continuations.
    /// </summary>
    [Fact]
    public void BuildServiceRequest_IncludesConversationModeForContextualFollowUp() {
        var markdown = PromptMarkdownBuilder.BuildServiceRequest(
            userText: "And the rest?",
            effectiveName: null,
            effectivePersona: null,
            onboardingInProgress: false,
            missingOnboardingFields: Array.Empty<string>(),
            includeLiveProfileUpdates: false,
            executionBehaviorPrompt: string.Empty,
            localContextLines: new[] {
                "Assistant: I checked AD0 replication and found two failing partners.",
                "User: Continue across the remaining discovered DCs after AD0."
            });

        Assert.Contains("[Conversation mode]", markdown);
        Assert.Contains("Mode: contextual_follow_up", markdown);
        Assert.Contains("Treat this as a continuation", markdown, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Do not ask the user to restate", markdown, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Ensures short replies after a recent assistant answer can be treated as acknowledgements instead of reopened tasks.
    /// </summary>
    [Fact]
    public void BuildServiceRequest_IncludesConversationModeForLightPostAnswerReply() {
        var markdown = PromptMarkdownBuilder.BuildServiceRequest(
            userText: "ok",
            effectiveName: null,
            effectivePersona: null,
            onboardingInProgress: false,
            missingOnboardingFields: Array.Empty<string>(),
            includeLiveProfileUpdates: false,
            executionBehaviorPrompt: string.Empty,
            recentAssistantAnswerWasSubstantive: true,
            recentAssistantAskedQuestion: false,
            localContextLines: new[] {
                "User: Check AD0 replication health and summarize the blockers.",
                "Assistant: AD0 has two failing replication partners and one transport error."
            });

        Assert.Contains("[Conversation mode]", markdown);
        Assert.Contains("Mode: light_post_answer_reply", markdown);
        Assert.Contains("only acknowledging or lightly closing", markdown, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Do not reopen the conversation", markdown, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Ensures short replies after a non-substantive assistant turn stay in generic low-context mode.
    /// </summary>
    [Fact]
    public void BuildServiceRequest_UsesLowContextModeWhenRecentAssistantWasNotSubstantive() {
        var markdown = PromptMarkdownBuilder.BuildServiceRequest(
            userText: "ok",
            effectiveName: null,
            effectivePersona: null,
            onboardingInProgress: false,
            missingOnboardingFields: Array.Empty<string>(),
            includeLiveProfileUpdates: false,
            executionBehaviorPrompt: string.Empty,
            recentAssistantAnswerWasSubstantive: false,
            recentAssistantAskedQuestion: false,
            localContextLines: new[] {
                "User: test",
                "Assistant: Sure."
            });

        Assert.Contains("[Conversation mode]", markdown);
        Assert.Contains("Mode: low_context_short_turn", markdown);
        Assert.DoesNotContain("Mode: light_post_answer_reply", markdown);
    }

    /// <summary>
    /// Ensures short replies to recent assistant questions are treated as answers, not closures.
    /// </summary>
    [Fact]
    public void BuildServiceRequest_UsesConversationModeForCompactAnswerToRecentQuestion() {
        var markdown = PromptMarkdownBuilder.BuildServiceRequest(
            userText: "public",
            effectiveName: null,
            effectivePersona: null,
            onboardingInProgress: false,
            missingOnboardingFields: Array.Empty<string>(),
            includeLiveProfileUpdates: false,
            executionBehaviorPrompt: string.Empty,
            recentAssistantAnswerWasSubstantive: true,
            recentAssistantAskedQuestion: true,
            localContextLines: new[] {
                "User: Check evotec.xyz.",
                "Assistant: Do you mean internal AD health or the public DNS/mail side?"
            });

        Assert.Contains("[Conversation mode]", markdown);
        Assert.Contains("Mode: compact_answer_to_recent_question", markdown);
        Assert.Contains("likely answer or confirmation", markdown, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Continue from the pending question or action", markdown, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Mode: light_post_answer_reply", markdown);
    }

    /// <summary>
    /// Ensures recent style guidance can be embedded so persona and live tone can be blended.
    /// </summary>
    [Fact]
    public void BuildServiceRequest_IncludesConversationStyleGuidance() {
        var markdown = PromptMarkdownBuilder.BuildServiceRequest(
            userText: "Check the remaining DCs too.",
            effectiveName: null,
            effectivePersona: "sharp operator with concise outputs",
            onboardingInProgress: false,
            missingOnboardingFields: Array.Empty<string>(),
            includeLiveProfileUpdates: false,
            executionBehaviorPrompt: string.Empty,
            conversationStyleLines: new[] {
                "Recent user style is terse and direct. Match that with short, confident phrasing instead of padded preambles.",
                "Match the user's energy and directness without becoming robotic, defensive, or confrontational."
            });

        Assert.Contains("[Conversation style]", markdown);
        Assert.Contains("Blend the selected persona with the user's recent style", markdown, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("response shape", markdown, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("short, confident phrasing", markdown, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("without becoming robotic, defensive, or confrontational", markdown, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Ensures continuation state can be embedded so the model can continue from recent assistant questions or actions.
    /// </summary>
    [Fact]
    public void BuildServiceRequest_IncludesContinuationStateGuidance() {
        var markdown = PromptMarkdownBuilder.BuildServiceRequest(
            userText: "public",
            effectiveName: null,
            effectivePersona: null,
            onboardingInProgress: false,
            missingOnboardingFields: Array.Empty<string>(),
            includeLiveProfileUpdates: false,
            executionBehaviorPrompt: string.Empty,
            continuationStateLines: new[] {
                "Latest assistant turn left a pending question or clarification. Treat the current user message as likely answering that question if it fits naturally.",
                "Pending assistant question: Do you mean internal AD health or the public DNS/mail side?"
            });

        Assert.Contains("[Continuation state]", markdown);
        Assert.Contains("continue the live thread naturally", markdown, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("pending question or clarification", markdown, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Pending assistant question:", markdown, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Ensures a single domain target can request natural clarification without leaking internal routing protocol.
    /// </summary>
    [Fact]
    public void BuildServiceRequest_IncludesConversationModeForAmbiguousSingleDomainTarget() {
        var markdown = PromptMarkdownBuilder.BuildServiceRequest(
            userText: "Can you check evotec.xyz?",
            effectiveName: null,
            effectivePersona: null,
            onboardingInProgress: false,
            missingOnboardingFields: Array.Empty<string>(),
            includeLiveProfileUpdates: false,
            executionBehaviorPrompt: string.Empty);

        Assert.Contains("[Conversation mode]", markdown);
        Assert.Contains("Mode: ambiguous_scope_target", markdown);
        Assert.Contains("`evotec.xyz`", markdown);
        Assert.Contains("Do not expose internal routing tokens", markdown, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Ensures email addresses do not accidentally trigger ambiguous domain-scope mode.
    /// </summary>
    [Fact]
    public void BuildServiceRequest_DoesNotTreatEmailAddressAsAmbiguousScopeTarget() {
        var markdown = PromptMarkdownBuilder.BuildServiceRequest(
            userText: "Please check user@evotec.xyz",
            effectiveName: null,
            effectivePersona: null,
            onboardingInProgress: false,
            missingOnboardingFields: Array.Empty<string>(),
            includeLiveProfileUpdates: false,
            executionBehaviorPrompt: string.Empty);

        Assert.DoesNotContain("Mode: ambiguous_scope_target", markdown);
        Assert.DoesNotContain("`evotec.xyz`", markdown);
    }

    /// <summary>
    /// Ensures punctuation-wrapped email inputs still avoid ambiguous domain-scope mode.
    /// </summary>
    [Fact]
    public void BuildServiceRequest_DoesNotTreatPunctuationWrappedEmailAsAmbiguousScopeTarget() {
        var markdown = PromptMarkdownBuilder.BuildServiceRequest(
            userText: "Please check (user@evotec.xyz), thanks",
            effectiveName: null,
            effectivePersona: null,
            onboardingInProgress: false,
            missingOnboardingFields: Array.Empty<string>(),
            includeLiveProfileUpdates: false,
            executionBehaviorPrompt: string.Empty);

        Assert.DoesNotContain("Mode: ambiguous_scope_target", markdown);
        Assert.DoesNotContain("`evotec.xyz`", markdown);
    }

    /// <summary>
    /// Ensures quoted emails with trailing separators still avoid ambiguous domain-scope mode.
    /// </summary>
    [Fact]
    public void BuildServiceRequest_DoesNotTreatQuotedEmailWithSeparatorsAsAmbiguousScopeTarget() {
        var markdown = PromptMarkdownBuilder.BuildServiceRequest(
            userText: "Please check \"user@evotec.xyz\"; thanks",
            effectiveName: null,
            effectivePersona: null,
            onboardingInProgress: false,
            missingOnboardingFields: Array.Empty<string>(),
            includeLiveProfileUpdates: false,
            executionBehaviorPrompt: string.Empty);

        Assert.DoesNotContain("Mode: ambiguous_scope_target", markdown);
        Assert.DoesNotContain("`evotec.xyz`", markdown);
    }
}
