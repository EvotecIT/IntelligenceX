using System;
using IntelligenceX.Chat.Abstractions;
using IntelligenceX.Chat.App.Markdown;
using Xunit;

namespace IntelligenceX.Chat.App.Tests;

/// <summary>
/// Tests for prompt markdown envelope composition.
/// </summary>
public sealed class PromptMarkdownBuilderTests {
    /// <summary>
    /// Ensures thin service requests stay raw when the app has no metadata to add.
    /// </summary>
    [Fact]
    public void BuildThinServiceRequest_ReturnsRawUserTextWhenNoMetadataExists() {
        var markdown = PromptMarkdownBuilder.BuildThinServiceRequest("Check replication health.");

        Assert.Equal("Check replication health.", markdown);
    }

    /// <summary>
    /// Ensures thin service requests lead with the user task before any session metadata.
    /// </summary>
    [Fact]
    public void BuildThinServiceRequest_PutsUserRequestBeforeMetadata() {
        var markdown = PromptMarkdownBuilder.BuildThinServiceRequest(
            userText: "Check replication health and show a table.",
            effectiveName: "Przemek",
            effectivePersona: "sharp operator",
            persistentMemoryLines: new[] { "Prefers compact operational summaries." },
            persistentMemoryPrompt: "[Persistent memory protocol]");

        var userRequestIndex = markdown.IndexOf("User request:", StringComparison.Ordinal);
        var profileIndex = markdown.IndexOf("[Session profile context]", StringComparison.Ordinal);
        var memoryIndex = markdown.IndexOf("[Persistent memory]", StringComparison.Ordinal);

        Assert.True(userRequestIndex >= 0);
        Assert.True(profileIndex > userRequestIndex);
        Assert.True(memoryIndex > profileIndex);
        Assert.Contains("Check replication health and show a table.", markdown);
        Assert.Contains("Use this only as stable session metadata", markdown, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Ensures the thin request path can still carry a structured runtime self-report directive.
    /// </summary>
    [Fact]
    public void BuildThinServiceRequest_IncludesRuntimeSelfReportDirectiveWhenProvided() {
        var markdown = PromptMarkdownBuilder.BuildThinServiceRequest(
            userText: "What model/tools for DNS/AD?",
            runtimeSelfReportDirectiveLines: RuntimeSelfReportDirective.BuildLines(
                "What model/tools for DNS/AD?",
                compactReply: true,
                toolingRequested: true));

        Assert.Contains("User request:", markdown, StringComparison.Ordinal);
        Assert.Contains("What model/tools for DNS/AD?", markdown, StringComparison.Ordinal);
        Assert.Contains("ix:runtime-self-report:v1", markdown, StringComparison.Ordinal);
        Assert.Contains("reply_shape: compact", markdown, StringComparison.Ordinal);
        Assert.Contains("tooling_requested: true", markdown, StringComparison.Ordinal);
    }

    /// <summary>
    /// Ensures weak lexical-fallback runtime self-report turns do not carry persistent-memory context on the thin path.
    /// </summary>
    [Fact]
    public void BuildThinServiceRequest_OmitsPersistentMemoryForLexicalFallbackRuntimeSelfReport() {
        var analysis = CreateLexicalFallbackAnalysis(
            "What model are you using?",
            compactReply: true,
            modelRequested: true,
            toolingRequested: false);

        var markdown = PromptMarkdownBuilder.BuildThinServiceRequest(
            userText: "What model are you using?",
            persistentMemoryLines: new[] { "Prefers compact operational summaries." },
            persistentMemoryPrompt: "[Persistent memory protocol]",
            runtimeSelfReportDirectiveLines: PromptMarkdownBuilder.BuildRuntimeSelfReportDirectiveLines(analysis),
            runtimeSelfReportAnalysis: analysis);

        Assert.DoesNotContain("[Persistent memory protocol]", markdown, StringComparison.Ordinal);
        Assert.DoesNotContain("[Persistent memory]", markdown, StringComparison.Ordinal);
        Assert.DoesNotContain("Prefers compact operational summaries.", markdown, StringComparison.Ordinal);
        Assert.Contains("detection_source: lexical_fallback", markdown, StringComparison.Ordinal);
    }

    /// <summary>
    /// Ensures weak lexical-fallback runtime self-report turns do not carry session profile metadata on the thin path.
    /// </summary>
    [Fact]
    public void BuildThinServiceRequest_OmitsSessionProfileContextForLexicalFallbackRuntimeSelfReport() {
        var analysis = CreateLexicalFallbackAnalysis(
            "What model are you using?",
            compactReply: true,
            modelRequested: true,
            toolingRequested: false);

        var markdown = PromptMarkdownBuilder.BuildThinServiceRequest(
            userText: "What model are you using?",
            effectiveName: "Przemek",
            effectivePersona: "sharp operator",
            runtimeSelfReportDirectiveLines: PromptMarkdownBuilder.BuildRuntimeSelfReportDirectiveLines(analysis),
            runtimeSelfReportAnalysis: analysis);

        Assert.DoesNotContain("[Session profile context]", markdown, StringComparison.Ordinal);
        Assert.DoesNotContain("User name: Przemek", markdown, StringComparison.Ordinal);
        Assert.DoesNotContain("Assistant persona: sharp operator", markdown, StringComparison.Ordinal);
        Assert.Contains("detection_source: lexical_fallback", markdown, StringComparison.Ordinal);
    }

    /// <summary>
    /// Ensures structured runtime self-report turns keep trusted persistent-memory context on the thin path.
    /// </summary>
    [Fact]
    public void BuildThinServiceRequest_PreservesPersistentMemoryForStructuredRuntimeSelfReport() {
        var userText = string.Join(
            Environment.NewLine,
            RuntimeSelfReportDirective.BuildLines(
                "Czego teraz uzywasz?",
                compactReply: true,
                detectionSource: RuntimeSelfReportDetectionSource.StructuredDirective,
                toolingRequested: false));
        var analysis = RuntimeSelfReportTurnClassifier.Analyze(userText);

        var markdown = PromptMarkdownBuilder.BuildThinServiceRequest(
            userText: userText,
            persistentMemoryLines: new[] { "Prefers compact operational summaries." },
            persistentMemoryPrompt: "[Persistent memory protocol]",
            runtimeSelfReportDirectiveLines: PromptMarkdownBuilder.BuildRuntimeSelfReportDirectiveLines(analysis),
            runtimeSelfReportAnalysis: analysis);

        Assert.Contains("[Persistent memory protocol]", markdown, StringComparison.Ordinal);
        Assert.Contains("[Persistent memory]", markdown, StringComparison.Ordinal);
        Assert.Contains("Prefers compact operational summaries.", markdown, StringComparison.Ordinal);
        Assert.Contains("detection_source: structured_directive", markdown, StringComparison.Ordinal);
    }

    /// <summary>
    /// Ensures structured runtime self-report turns keep session profile metadata on the thin path.
    /// </summary>
    [Fact]
    public void BuildThinServiceRequest_PreservesSessionProfileContextForStructuredRuntimeSelfReport() {
        var userText = string.Join(
            Environment.NewLine,
            RuntimeSelfReportDirective.BuildLines(
                "Czego teraz uzywasz?",
                compactReply: true,
                detectionSource: RuntimeSelfReportDetectionSource.StructuredDirective,
                toolingRequested: false));
        var analysis = RuntimeSelfReportTurnClassifier.Analyze(userText);

        var markdown = PromptMarkdownBuilder.BuildThinServiceRequest(
            userText: userText,
            effectiveName: "Przemek",
            effectivePersona: "sharp operator",
            runtimeSelfReportDirectiveLines: PromptMarkdownBuilder.BuildRuntimeSelfReportDirectiveLines(analysis),
            runtimeSelfReportAnalysis: analysis);

        Assert.Contains("[Session profile context]", markdown, StringComparison.Ordinal);
        Assert.Contains("User name: Przemek", markdown, StringComparison.Ordinal);
        Assert.Contains("Assistant persona: sharp operator", markdown, StringComparison.Ordinal);
        Assert.Contains("detection_source: structured_directive", markdown, StringComparison.Ordinal);
    }

    /// <summary>
    /// Ensures runtime self-report directive generation can be shared by thin and full prompt paths.
    /// </summary>
    [Fact]
    public void BuildRuntimeSelfReportDirectiveLines_ReturnsCompactDirectiveForRuntimeQuestion() {
        var lines = PromptMarkdownBuilder.BuildRuntimeSelfReportDirectiveLines(CreateLexicalFallbackAnalysis(
            "What model/tools for DNS/AD?",
            compactReply: true,
            modelRequested: true,
            toolingRequested: true));

        Assert.NotNull(lines);
        Assert.Contains(RuntimeSelfReportDirective.Marker, lines!);
        Assert.Contains("reply_shape: compact", lines!);
        Assert.Contains("detection_source: lexical_fallback", lines!);
        Assert.Contains("model_requested: true", lines!);
        Assert.Contains("tooling_requested: true", lines!);
    }

    /// <summary>
    /// Ensures tooling-only runtime introspection asks can narrow the structured directive away from model focus.
    /// </summary>
    [Fact]
    public void BuildRuntimeSelfReportDirectiveLines_CanMarkToolingOnlyRuntimeQuestion() {
        var lines = PromptMarkdownBuilder.BuildRuntimeSelfReportDirectiveLines(CreateLexicalFallbackAnalysis(
            "What tools are available right now?",
            compactReply: true,
            modelRequested: false,
            toolingRequested: true));

        Assert.NotNull(lines);
        Assert.Contains("detection_source: lexical_fallback", lines!);
        Assert.Contains("model_requested: false", lines!);
        Assert.Contains("tooling_requested: true", lines!);
    }

    /// <summary>
    /// Ensures directive generation stays disabled for ordinary non-meta turns.
    /// </summary>
    [Fact]
    public void BuildRuntimeSelfReportDirectiveLines_ReturnsNullForNonRuntimeTurn() {
        var lines = PromptMarkdownBuilder.BuildRuntimeSelfReportDirectiveLines(
            RuntimeSelfReportTurnClassifier.Analyze("Check replication health across all DCs."));

        Assert.Null(lines);
    }

    /// <summary>
    /// Ensures plain cue-word runtime asks do not auto-upgrade into structured runtime directives without trusted metadata.
    /// </summary>
    [Fact]
    public void BuildRuntimeSelfReportDirectiveLines_ReturnsNullForNaturalCueWordAskWithoutTrustedDirective() {
        var lines = PromptMarkdownBuilder.BuildRuntimeSelfReportDirectiveLines(
            RuntimeSelfReportTurnClassifier.Analyze("What model are you using?"));

        Assert.Null(lines);
    }

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
            personaGuidanceLines: new[] { "Prefer compact phrasing and shorter answers by default unless the user clearly wants depth." },
            runtimeCapabilityLines: new[] { "Parallel tool execution: enabled", "Max tool rounds: 24" },
            proactiveExecutionEnabled: true,
            conversationStyleLines: new[] { "Recent user style is terse and direct." },
            persistentMemoryLines: new[] { "Prefers concise answers." },
            persistentMemoryPrompt: "[Persistent memory protocol]");

        Assert.Contains("[Conversation style]", markdown);
        Assert.Contains("Recent user style is terse and direct.", markdown);
        Assert.Contains("[Persona guidance]", markdown);
        Assert.Contains("[Session profile context]", markdown);
        Assert.Contains("- User name: Przemek", markdown);
        Assert.Contains("- Assistant persona: security analyst with concise outputs", markdown);
        Assert.Contains("preferred voice and working style", markdown, StringComparison.OrdinalIgnoreCase);
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
    /// Ensures capability questions stay conversational instead of turning into runtime/tool demos.
    /// </summary>
    [Fact]
    public void BuildServiceRequest_IncludesConversationModeForAssistantCapabilityQuestion() {
        var markdown = PromptMarkdownBuilder.BuildServiceRequest(
            userText: "What can you do for me today?",
            effectiveName: null,
            effectivePersona: null,
            onboardingInProgress: false,
            missingOnboardingFields: Array.Empty<string>(),
            includeLiveProfileUpdates: false,
            executionBehaviorPrompt: string.Empty);

        Assert.Contains("[Conversation mode]", markdown);
        Assert.Contains("Mode: assistant_capability_question", markdown);
        Assert.Contains("Answer naturally in human terms", markdown, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("few concrete examples", markdown, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Do not run live checks", markdown, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Do not dump low-level runtime details", markdown, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Ensures non-English broad capability asks still enter the conversational capability mode without borrowed English nouns.
    /// </summary>
    [Fact]
    public void BuildServiceRequest_IncludesConversationModeForNonEnglishAssistantCapabilityQuestion() {
        var markdown = PromptMarkdownBuilder.BuildServiceRequest(
            userText: "Co mozesz zrobic dla mnie?",
            effectiveName: null,
            effectivePersona: null,
            onboardingInProgress: false,
            missingOnboardingFields: Array.Empty<string>(),
            includeLiveProfileUpdates: false,
            executionBehaviorPrompt: string.Empty);

        Assert.Contains("[Conversation mode]", markdown);
        Assert.Contains("Mode: assistant_capability_question", markdown);
        Assert.Contains("Answer naturally in human terms", markdown, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Do not dump low-level runtime details", markdown, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Ensures capability-answer style guidance can be embedded separately from general conversation style.
    /// </summary>
    [Fact]
    public void BuildServiceRequest_IncludesCapabilityAnswerStyleSection() {
        var markdown = PromptMarkdownBuilder.BuildServiceRequest(
            userText: "What can you do for me today?",
            effectiveName: null,
            effectivePersona: "helpful assistant with a bit of dark humour",
            onboardingInProgress: false,
            missingOnboardingFields: Array.Empty<string>(),
            includeLiveProfileUpdates: false,
            executionBehaviorPrompt: string.Empty,
            capabilityAnswerStyleLines: new[] {
                "For capability questions, answer with 2-3 concrete examples and one short invitation.",
                "Keep it to one short paragraph or a tight bullet list.",
                "Do not turn capability answers into environment inventories, exhaustive tool lists, or self-validation demos."
            });

        Assert.Contains("[Capability answer style]", markdown);
        Assert.Contains("fit the user's pacing", markdown, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("2-3 concrete examples", markdown, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("one short paragraph", markdown, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("self-validation demos", markdown, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Ensures human-facing capability self-knowledge can be embedded separately from raw runtime telemetry.
    /// </summary>
    [Fact]
    public void BuildServiceRequest_IncludesCapabilitySelfKnowledgeSection() {
        var markdown = PromptMarkdownBuilder.BuildServiceRequest(
            userText: "What can you do for me today?",
            effectiveName: null,
            effectivePersona: "helpful assistant with a bit of dark humour",
            onboardingInProgress: false,
            missingOnboardingFields: Array.Empty<string>(),
            includeLiveProfileUpdates: false,
            executionBehaviorPrompt: string.Empty,
            capabilitySelfKnowledgeLines: new[] {
                "Areas you can help with here include Active Directory, Event Viewer.",
                "You can help with Active Directory checks such as users, groups, LDAP lookups, and replication-related investigation.",
                "Concrete examples you can mention: check AD replication health, find users/groups/computers, or review group membership and LDAP data.",
                "For explicit capability questions, lead with a few practical examples that are genuinely live in this session, then invite the user's task.",
                "When asked what you can do, answer with useful examples and invite the task instead of listing internal identifiers or protocol details."
            });

        Assert.Contains("[Capability self-knowledge]", markdown);
        Assert.Contains("human terms", markdown, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Active Directory, Event Viewer", markdown, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Concrete examples you can mention", markdown, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("few practical examples", markdown, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("invite the task", markdown, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Ensures runtime self-report questions receive a dedicated conversation mode so the answer stays short and human.
    /// </summary>
    [Fact]
    public void BuildServiceRequest_IncludesConversationModeForRuntimeIntrospectionQuestion() {
        var analysis = CreateLexicalFallbackAnalysis(
            "What model/tools for DNS/AD?",
            compactReply: true,
            modelRequested: true,
            toolingRequested: true);
        var markdown = PromptMarkdownBuilder.BuildServiceRequest(
            userText: "What model/tools for DNS/AD?",
            effectiveName: null,
            effectivePersona: null,
            onboardingInProgress: false,
            missingOnboardingFields: Array.Empty<string>(),
            includeLiveProfileUpdates: false,
            executionBehaviorPrompt: string.Empty,
            runtimeSelfReportAnalysis: analysis);

        Assert.Contains("[Conversation mode]", markdown);
        Assert.Contains("Mode: assistant_runtime_introspection_compact", markdown);
        Assert.Contains("ix:runtime-self-report:v1", markdown, StringComparison.Ordinal);
        Assert.Contains("reply_shape: compact", markdown, StringComparison.Ordinal);
        Assert.Contains("detection_source: lexical_fallback", markdown, StringComparison.Ordinal);
        Assert.Contains("model_requested: true", markdown, StringComparison.Ordinal);
        Assert.Contains("tooling_requested: true", markdown, StringComparison.Ordinal);
        Assert.Contains("user_request_literal: \"What model/tools for DNS/AD?\"", markdown, StringComparison.Ordinal);
        Assert.Contains("one or two short human sentences", markdown, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Do not use headings, bullet lists, inventories", markdown, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("mention the relevant tooling in plain language", markdown, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("lightweight lexical fallback", markdown, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Do not broaden a lexical-fallback self-report turn", markdown, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Do not run live checks", markdown, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Ensures runtime introspection can keep capability self-knowledge concise while deferring exact limits to runtime handshake lines.
    /// </summary>
    [Fact]
    public void BuildServiceRequest_RuntimeHandshakeTakesPriorityOverGenericCapabilityTail() {
        var analysis = CreateLexicalFallbackAnalysis(
            "What model and tools are you using right now?",
            compactReply: false,
            modelRequested: true,
            toolingRequested: true);
        var markdown = PromptMarkdownBuilder.BuildServiceRequest(
            userText: "What model and tools are you using right now?",
            effectiveName: null,
            effectivePersona: null,
            onboardingInProgress: false,
            missingOnboardingFields: Array.Empty<string>(),
            includeLiveProfileUpdates: false,
            executionBehaviorPrompt: string.Empty,
            capabilitySelfKnowledgeLines: new[] {
                "Areas you can help with here include Active Directory, Event Viewer.",
                "Keep this section practical and concise; exact runtime/model/tool limits belong in the runtime capability handshake."
            },
            runtimeCapabilityLines: new[] {
                "Runtime transport: native, active model for this turn: gpt-5.4",
                "Tool availability for this turn: available (enabled tools: 20, disabled: 0)."
            },
            runtimeSelfReportAnalysis: analysis);

        Assert.Contains("[Capability self-knowledge]", markdown);
        Assert.Contains("Mode: assistant_runtime_introspection_question", markdown);
        Assert.Contains("ix:runtime-self-report:v1", markdown, StringComparison.Ordinal);
        Assert.Contains("reply_shape: default", markdown, StringComparison.Ordinal);
        Assert.Contains("detection_source: lexical_fallback", markdown, StringComparison.Ordinal);
        Assert.Contains("model_requested: true", markdown, StringComparison.Ordinal);
        Assert.Contains("tooling_requested: true", markdown, StringComparison.Ordinal);
        Assert.Contains("[Runtime capability handshake]", markdown);
        Assert.Contains("exact runtime/model/tool limits belong in the runtime capability handshake", markdown, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("lightweight lexical fallback", markdown, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("invite the task", markdown, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Ensures lexical-fallback runtime turns get a tighter runtime/capability budget than the structured runtime path.
    /// </summary>
    [Fact]
    public void BuildServiceRequest_TrimsRuntimeHandshakeMoreAggressivelyForLexicalFallbackRuntimeTurn() {
        var analysis = CreateLexicalFallbackAnalysis(
            "What model and tools are you using right now?",
            compactReply: false,
            modelRequested: true,
            toolingRequested: true);
        var markdown = PromptMarkdownBuilder.BuildServiceRequest(
            userText: "What model and tools are you using right now?",
            effectiveName: null,
            effectivePersona: null,
            onboardingInProgress: false,
            missingOnboardingFields: Array.Empty<string>(),
            includeLiveProfileUpdates: false,
            executionBehaviorPrompt: string.Empty,
            capabilitySelfKnowledgeLines: new[] {
                "cap-1",
                "cap-2",
                "cap-3"
            },
            runtimeCapabilityLines: new[] {
                "runtime-1",
                "runtime-2",
                "runtime-3",
                "runtime-4",
                "runtime-5"
            },
            runtimeSelfReportAnalysis: analysis);

        Assert.Contains("detection_source: lexical_fallback", markdown, StringComparison.Ordinal);
        Assert.Contains("cap-1", markdown, StringComparison.Ordinal);
        Assert.Contains("cap-2", markdown, StringComparison.Ordinal);
        Assert.DoesNotContain("cap-3", markdown, StringComparison.Ordinal);
        Assert.Contains("runtime-1", markdown, StringComparison.Ordinal);
        Assert.Contains("runtime-4", markdown, StringComparison.Ordinal);
        Assert.DoesNotContain("runtime-5", markdown, StringComparison.Ordinal);
    }

    /// <summary>
    /// Ensures lexical-fallback runtime turns drop unrelated persistent-memory context on the full prompt path.
    /// </summary>
    [Fact]
    public void BuildServiceRequest_OmitsPersistentMemoryForLexicalFallbackRuntimeTurn() {
        var analysis = CreateLexicalFallbackAnalysis(
            "What model and tools are you using right now?",
            compactReply: false,
            modelRequested: true,
            toolingRequested: true);
        var markdown = PromptMarkdownBuilder.BuildServiceRequest(
            userText: "What model and tools are you using right now?",
            effectiveName: null,
            effectivePersona: null,
            onboardingInProgress: false,
            missingOnboardingFields: Array.Empty<string>(),
            includeLiveProfileUpdates: false,
            executionBehaviorPrompt: string.Empty,
            persistentMemoryLines: new[] { "Prefers compact operational summaries." },
            persistentMemoryPrompt: "[Persistent memory protocol]",
            runtimeSelfReportAnalysis: analysis);

        Assert.Contains("detection_source: lexical_fallback", markdown, StringComparison.Ordinal);
        Assert.Contains("Ignore unrelated persistent memory", markdown, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("[Persistent memory protocol]", markdown, StringComparison.Ordinal);
        Assert.DoesNotContain("[Persistent memory]", markdown, StringComparison.Ordinal);
        Assert.DoesNotContain("Prefers compact operational summaries.", markdown, StringComparison.Ordinal);
    }

    /// <summary>
    /// Ensures lexical-fallback runtime turns drop low-priority transcript and style context on the full prompt path.
    /// </summary>
    [Fact]
    public void BuildServiceRequest_OmitsLowPriorityContextForLexicalFallbackRuntimeTurn() {
        var analysis = CreateLexicalFallbackAnalysis(
            "What model and tools are you using right now?",
            compactReply: false,
            modelRequested: true,
            toolingRequested: true);
        var markdown = PromptMarkdownBuilder.BuildServiceRequest(
            userText: "What model and tools are you using right now?",
            effectiveName: null,
            effectivePersona: "sharp operator",
            onboardingInProgress: false,
            missingOnboardingFields: Array.Empty<string>(),
            includeLiveProfileUpdates: false,
            executionBehaviorPrompt: string.Empty,
            localContextLines: new[] { "Assistant: Previous answer about replication health." },
            conversationStyleLines: new[] { "Recent user style is terse and direct." },
            personaGuidanceLines: new[] { "Prefer crisp and highly compressed delivery." },
            continuationStateLines: new[] { "There is a pending follow-up about replication." },
            runtimeSelfReportAnalysis: analysis);

        Assert.Contains("detection_source: lexical_fallback", markdown, StringComparison.Ordinal);
        Assert.DoesNotContain("[Conversation style]", markdown, StringComparison.Ordinal);
        Assert.DoesNotContain("[Continuation state]", markdown, StringComparison.Ordinal);
        Assert.DoesNotContain("[Persona guidance]", markdown, StringComparison.Ordinal);
        Assert.DoesNotContain("[Local transcript context fallback]", markdown, StringComparison.Ordinal);
        Assert.DoesNotContain("Recent user style is terse and direct.", markdown, StringComparison.Ordinal);
        Assert.DoesNotContain("pending follow-up about replication", markdown, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("crisp and highly compressed delivery", markdown, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Ensures lexical-fallback runtime turns drop session profile metadata on the full prompt path.
    /// </summary>
    [Fact]
    public void BuildServiceRequest_OmitsSessionProfileContextForLexicalFallbackRuntimeTurn() {
        var analysis = CreateLexicalFallbackAnalysis(
            "What model and tools are you using right now?",
            compactReply: false,
            modelRequested: true,
            toolingRequested: true);
        var markdown = PromptMarkdownBuilder.BuildServiceRequest(
            userText: "What model and tools are you using right now?",
            effectiveName: "Przemek",
            effectivePersona: "sharp operator",
            onboardingInProgress: false,
            missingOnboardingFields: Array.Empty<string>(),
            includeLiveProfileUpdates: false,
            executionBehaviorPrompt: string.Empty,
            runtimeSelfReportAnalysis: analysis);

        Assert.Contains("detection_source: lexical_fallback", markdown, StringComparison.Ordinal);
        Assert.DoesNotContain("[Session profile context]", markdown, StringComparison.Ordinal);
        Assert.DoesNotContain("User name: Przemek", markdown, StringComparison.Ordinal);
        Assert.DoesNotContain("Assistant persona: sharp operator", markdown, StringComparison.Ordinal);
    }

    /// <summary>
    /// Ensures lexical-fallback runtime turns drop generic execution-behavior scaffolding on the full prompt path.
    /// </summary>
    [Fact]
    public void BuildServiceRequest_OmitsExecutionBehaviorForLexicalFallbackRuntimeTurn() {
        var analysis = CreateLexicalFallbackAnalysis(
            "What model and tools are you using right now?",
            compactReply: false,
            modelRequested: true,
            toolingRequested: true);
        var markdown = PromptMarkdownBuilder.BuildServiceRequest(
            userText: "What model and tools are you using right now?",
            effectiveName: null,
            effectivePersona: null,
            onboardingInProgress: false,
            missingOnboardingFields: Array.Empty<string>(),
            includeLiveProfileUpdates: false,
            executionBehaviorPrompt: "[Execution behavior]\n- Retry tools before asking user.",
            runtimeSelfReportAnalysis: analysis);

        Assert.Contains("detection_source: lexical_fallback", markdown, StringComparison.Ordinal);
        Assert.DoesNotContain("[Execution behavior]", markdown, StringComparison.Ordinal);
        Assert.DoesNotContain("Retry tools before asking user.", markdown, StringComparison.Ordinal);
    }

    /// <summary>
    /// Ensures explicitly structured runtime self-report turns keep the trusted structured-scope guidance in the full prompt path.
    /// </summary>
    [Fact]
    public void BuildServiceRequest_UsesStructuredDirectiveGuidanceForStructuredRuntimeIntrospectionQuestion() {
        var userText = string.Join(
            Environment.NewLine,
            RuntimeSelfReportDirective.BuildLines(
                "Czego teraz uzywasz?",
                compactReply: false,
                detectionSource: RuntimeSelfReportDetectionSource.StructuredDirective,
                toolingRequested: false));

        var markdown = PromptMarkdownBuilder.BuildServiceRequest(
            userText: userText,
            effectiveName: null,
            effectivePersona: null,
            onboardingInProgress: false,
            missingOnboardingFields: Array.Empty<string>(),
            includeLiveProfileUpdates: false,
            executionBehaviorPrompt: string.Empty);

        Assert.Contains("[Conversation mode]", markdown);
        Assert.Contains("Mode: assistant_runtime_introspection_question", markdown);
        Assert.Contains("detection_source: structured_directive", markdown, StringComparison.Ordinal);
        Assert.Contains("structured scope", markdown, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("lightweight lexical fallback", markdown, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Ensures plain cue-word runtime asks no longer auto-enter runtime self-report mode without trusted directive metadata.
    /// </summary>
    [Fact]
    public void BuildServiceRequest_DoesNotAutoEnterRuntimeIntrospectionModeForNaturalCueWordAskWithoutTrustedDirective() {
        var markdown = PromptMarkdownBuilder.BuildServiceRequest(
            userText: "What model are you using?",
            effectiveName: null,
            effectivePersona: null,
            onboardingInProgress: false,
            missingOnboardingFields: Array.Empty<string>(),
            includeLiveProfileUpdates: false,
            executionBehaviorPrompt: string.Empty);

        Assert.DoesNotContain("assistant_runtime_introspection", markdown, StringComparison.Ordinal);
        Assert.DoesNotContain("assistant_capability_question", markdown, StringComparison.Ordinal);
        Assert.DoesNotContain("ix:runtime-self-report:v1", markdown, StringComparison.Ordinal);
        Assert.DoesNotContain("detection_source: lexical_fallback", markdown, StringComparison.Ordinal);
    }

    /// <summary>
    /// Ensures broader runtime inventory asks do not fall back into generic capability mode when no trusted runtime directive exists.
    /// </summary>
    [Theory]
    [InlineData("What tools are available right now?")]
    [InlineData("What model and tools are you using right now?")]
    public void BuildServiceRequest_DoesNotAutoEnterCapabilityModeForRuntimeInventoryAskWithoutTrustedDirective(string userText) {
        var markdown = PromptMarkdownBuilder.BuildServiceRequest(
            userText: userText,
            effectiveName: null,
            effectivePersona: null,
            onboardingInProgress: false,
            missingOnboardingFields: Array.Empty<string>(),
            includeLiveProfileUpdates: false,
            executionBehaviorPrompt: string.Empty);

        Assert.DoesNotContain("assistant_runtime_introspection", markdown, StringComparison.Ordinal);
        Assert.DoesNotContain("assistant_capability_question", markdown, StringComparison.Ordinal);
        Assert.DoesNotContain("ix:runtime-self-report:v1", markdown, StringComparison.Ordinal);
    }

    /// <summary>
    /// Ensures structured runtime self-report turns keep the broader runtime/capability budget.
    /// </summary>
    [Fact]
    public void BuildServiceRequest_KeepsBroaderRuntimeHandshakeBudgetForStructuredRuntimeTurn() {
        var userText = string.Join(
            Environment.NewLine,
            RuntimeSelfReportDirective.BuildLines(
                "Czego teraz uzywasz?",
                compactReply: false,
                detectionSource: RuntimeSelfReportDetectionSource.StructuredDirective,
                toolingRequested: false));

        var markdown = PromptMarkdownBuilder.BuildServiceRequest(
            userText: userText,
            effectiveName: null,
            effectivePersona: null,
            onboardingInProgress: false,
            missingOnboardingFields: Array.Empty<string>(),
            includeLiveProfileUpdates: false,
            executionBehaviorPrompt: string.Empty,
            capabilitySelfKnowledgeLines: new[] {
                "cap-1",
                "cap-2",
                "cap-3"
            },
            runtimeCapabilityLines: new[] {
                "runtime-1",
                "runtime-2",
                "runtime-3",
                "runtime-4",
                "runtime-5"
            });

        Assert.Contains("detection_source: structured_directive", markdown, StringComparison.Ordinal);
        Assert.Contains("cap-3", markdown, StringComparison.Ordinal);
        Assert.Contains("runtime-5", markdown, StringComparison.Ordinal);
    }

    /// <summary>
    /// Ensures structured runtime self-report turns keep persistent-memory context on the trusted full prompt path.
    /// </summary>
    [Fact]
    public void BuildServiceRequest_PreservesPersistentMemoryForStructuredRuntimeTurn() {
        var userText = string.Join(
            Environment.NewLine,
            RuntimeSelfReportDirective.BuildLines(
                "Czego teraz uzywasz?",
                compactReply: false,
                detectionSource: RuntimeSelfReportDetectionSource.StructuredDirective,
                toolingRequested: false));

        var markdown = PromptMarkdownBuilder.BuildServiceRequest(
            userText: userText,
            effectiveName: null,
            effectivePersona: null,
            onboardingInProgress: false,
            missingOnboardingFields: Array.Empty<string>(),
            includeLiveProfileUpdates: false,
            executionBehaviorPrompt: string.Empty,
            persistentMemoryLines: new[] { "Prefers compact operational summaries." },
            persistentMemoryPrompt: "[Persistent memory protocol]");

        Assert.Contains("detection_source: structured_directive", markdown, StringComparison.Ordinal);
        Assert.Contains("[Persistent memory protocol]", markdown, StringComparison.Ordinal);
        Assert.Contains("[Persistent memory]", markdown, StringComparison.Ordinal);
        Assert.Contains("Prefers compact operational summaries.", markdown, StringComparison.Ordinal);
    }

    /// <summary>
    /// Ensures trusted structured runtime turns keep low-priority context available when explicitly provided.
    /// </summary>
    [Fact]
    public void BuildServiceRequest_PreservesLowPriorityContextForStructuredRuntimeTurn() {
        var userText = string.Join(
            Environment.NewLine,
            RuntimeSelfReportDirective.BuildLines(
                "Czego teraz uzywasz?",
                compactReply: false,
                detectionSource: RuntimeSelfReportDetectionSource.StructuredDirective,
                toolingRequested: false));

        var markdown = PromptMarkdownBuilder.BuildServiceRequest(
            userText: userText,
            effectiveName: null,
            effectivePersona: "sharp operator",
            onboardingInProgress: false,
            missingOnboardingFields: Array.Empty<string>(),
            includeLiveProfileUpdates: false,
            executionBehaviorPrompt: string.Empty,
            localContextLines: new[] { "Assistant: Previous answer about replication health." },
            conversationStyleLines: new[] { "Recent user style is terse and direct." },
            personaGuidanceLines: new[] { "Prefer crisp and highly compressed delivery." },
            continuationStateLines: new[] { "There is a pending follow-up about replication." });

        Assert.Contains("detection_source: structured_directive", markdown, StringComparison.Ordinal);
        Assert.Contains("[Conversation style]", markdown, StringComparison.Ordinal);
        Assert.Contains("[Continuation state]", markdown, StringComparison.Ordinal);
        Assert.Contains("[Persona guidance]", markdown, StringComparison.Ordinal);
        Assert.Contains("[Local transcript context fallback]", markdown, StringComparison.Ordinal);
        Assert.Contains("Recent user style is terse and direct.", markdown, StringComparison.Ordinal);
        Assert.Contains("pending follow-up about replication", markdown, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("crisp and highly compressed delivery", markdown, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Ensures structured runtime self-report turns keep session profile metadata on the full prompt path.
    /// </summary>
    [Fact]
    public void BuildServiceRequest_PreservesSessionProfileContextForStructuredRuntimeTurn() {
        var userText = string.Join(
            Environment.NewLine,
            RuntimeSelfReportDirective.BuildLines(
                "Czego teraz uzywasz?",
                compactReply: false,
                detectionSource: RuntimeSelfReportDetectionSource.StructuredDirective,
                toolingRequested: false));

        var markdown = PromptMarkdownBuilder.BuildServiceRequest(
            userText: userText,
            effectiveName: "Przemek",
            effectivePersona: "sharp operator",
            onboardingInProgress: false,
            missingOnboardingFields: Array.Empty<string>(),
            includeLiveProfileUpdates: false,
            executionBehaviorPrompt: string.Empty);

        Assert.Contains("detection_source: structured_directive", markdown, StringComparison.Ordinal);
        Assert.Contains("[Session profile context]", markdown, StringComparison.Ordinal);
        Assert.Contains("User name: Przemek", markdown, StringComparison.Ordinal);
        Assert.Contains("Assistant persona: sharp operator", markdown, StringComparison.Ordinal);
    }

    /// <summary>
    /// Ensures structured runtime self-report turns keep execution-behavior guidance on the trusted full prompt path.
    /// </summary>
    [Fact]
    public void BuildServiceRequest_PreservesExecutionBehaviorForStructuredRuntimeTurn() {
        var userText = string.Join(
            Environment.NewLine,
            RuntimeSelfReportDirective.BuildLines(
                "Czego teraz uzywasz?",
                compactReply: false,
                detectionSource: RuntimeSelfReportDetectionSource.StructuredDirective,
                toolingRequested: false));

        var markdown = PromptMarkdownBuilder.BuildServiceRequest(
            userText: userText,
            effectiveName: null,
            effectivePersona: null,
            onboardingInProgress: false,
            missingOnboardingFields: Array.Empty<string>(),
            includeLiveProfileUpdates: false,
            executionBehaviorPrompt: "[Execution behavior]\n- Retry tools before asking user.");

        Assert.Contains("detection_source: structured_directive", markdown, StringComparison.Ordinal);
        Assert.Contains("[Execution behavior]", markdown, StringComparison.Ordinal);
        Assert.Contains("Retry tools before asking user.", markdown, StringComparison.Ordinal);
    }

    /// <summary>
    /// Ensures compact runtime self-report turns keep only the tight runtime lines instead of the broader handshake payload.
    /// </summary>
    [Fact]
    public void BuildServiceRequest_UsesCompactRuntimeHandshakeBudgetForCompactRuntimeAsk() {
        var analysis = CreateLexicalFallbackAnalysis(
            "What model/tools for DNS/AD?",
            compactReply: true,
            modelRequested: true,
            toolingRequested: true);
        var markdown = PromptMarkdownBuilder.BuildServiceRequest(
            userText: "What model/tools for DNS/AD?",
            effectiveName: null,
            effectivePersona: null,
            onboardingInProgress: false,
            missingOnboardingFields: Array.Empty<string>(),
            includeLiveProfileUpdates: false,
            executionBehaviorPrompt: string.Empty,
            runtimeCapabilityLines: new[] {
                "runtime-1",
                "runtime-2",
                "runtime-3"
            },
            runtimeSelfReportAnalysis: analysis);

        Assert.Contains("Mode: assistant_runtime_introspection_compact", markdown);
        Assert.Contains("runtime-1", markdown, StringComparison.Ordinal);
        Assert.Contains("runtime-2", markdown, StringComparison.Ordinal);
        Assert.DoesNotContain("runtime-3", markdown, StringComparison.Ordinal);
    }

    /// <summary>
    /// Ensures persona-derived guidance can sharpen how the selected persona affects delivery.
    /// </summary>
    [Fact]
    public void BuildServiceRequest_IncludesPersonaGuidanceSection() {
        var markdown = PromptMarkdownBuilder.BuildServiceRequest(
            userText: "What can you do for me today?",
            effectiveName: null,
            effectivePersona: "helpful assistant with a bit of dark humour",
            onboardingInProgress: false,
            missingOnboardingFields: Array.Empty<string>(),
            includeLiveProfileUpdates: false,
            executionBehaviorPrompt: string.Empty,
            personaGuidanceLines: new[] {
                "Be proactively useful: reduce user effort, infer sensible next steps, and avoid making the user micromanage the conversation.",
                "Light humor is allowed when it fits naturally. Keep it subtle, optional, and secondary to usefulness."
            });

        Assert.Contains("[Persona guidance]", markdown);
        Assert.Contains("meaningfully affect phrasing", markdown, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("reduce user effort", markdown, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Light humor is allowed", markdown, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Ensures lexical-fallback runtime turns keep only higher-priority runtime context when prompt context grows too large.
    /// </summary>
    [Fact]
    public void BuildServiceRequest_TrimsLowPrioritySupplementalSectionsUnderBudgetPressure() {
        var analysis = CreateLexicalFallbackAnalysis(
            "What model and tools are you using right now?",
            compactReply: false,
            modelRequested: true,
            toolingRequested: true);
        var markdown = PromptMarkdownBuilder.BuildServiceRequest(
            userText: "What model and tools are you using right now?",
            effectiveName: "Przemek",
            effectivePersona: "helpful assistant with concise outputs",
            onboardingInProgress: false,
            missingOnboardingFields: Array.Empty<string>(),
            includeLiveProfileUpdates: false,
            executionBehaviorPrompt: string.Empty,
            localContextLines: new[] {
                "Assistant: local-1", "Assistant: local-2", "Assistant: local-3", "Assistant: local-4", "Assistant: local-5"
            },
            conversationStyleLines: new[] {
                "style-1", "style-2", "style-3", "style-4", "style-5"
            },
            capabilityAnswerStyleLines: new[] {
                "cap-style-1", "cap-style-2", "cap-style-3", "cap-style-4"
            },
            personaGuidanceLines: new[] {
                "persona-1", "persona-2", "persona-3", "persona-4", "persona-5"
            },
            continuationStateLines: new[] {
                "continuation-1", "continuation-2", "continuation-3", "continuation-4", "continuation-5"
            },
            persistentMemoryLines: new[] {
                "memory-1", "memory-2", "memory-3", "memory-4", "memory-5"
            },
            capabilitySelfKnowledgeLines: new[] {
                "self-1", "self-2", "self-3", "self-4", "self-5"
            },
            runtimeCapabilityLines: new[] {
                "runtime-1", "runtime-2", "runtime-3", "runtime-4", "runtime-5", "runtime-6", "runtime-7", "runtime-8", "runtime-9"
            },
            runtimeSelfReportAnalysis: analysis);

        Assert.DoesNotContain("continuation-1", markdown, StringComparison.Ordinal);
        Assert.DoesNotContain(Environment.NewLine + "- style-1" + Environment.NewLine, markdown, StringComparison.Ordinal);
        Assert.DoesNotContain(Environment.NewLine + "- persona-1" + Environment.NewLine, markdown, StringComparison.Ordinal);
        Assert.Contains("runtime-1", markdown, StringComparison.Ordinal);
        Assert.DoesNotContain("local-1", markdown, StringComparison.Ordinal);
        Assert.DoesNotContain("memory-1", markdown, StringComparison.Ordinal);
    }

    /// <summary>
    /// Ensures capability self-knowledge trimming preserves negative tooling and reachability status under budget pressure.
    /// </summary>
    [Fact]
    public void BuildServiceRequest_PreservesCapabilityStatusLinesUnderBudgetPressure() {
        var markdown = PromptMarkdownBuilder.BuildServiceRequest(
            userText: "What can you do for me today?",
            effectiveName: null,
            effectivePersona: null,
            onboardingInProgress: false,
            missingOnboardingFields: Array.Empty<string>(),
            includeLiveProfileUpdates: false,
            executionBehaviorPrompt: string.Empty,
            capabilitySelfKnowledgeLines: new[] {
                "Areas you can help with here include Active Directory, Event Viewer, DnsClientX, System.",
                "Tooling is not currently available in this session, so answers should stay conversational and reasoning-based.",
                "Remote reachability right now is local-only.",
                "You can help with Active Directory checks such as users, groups, LDAP lookups, and domain-controller or replication-related investigation when those tools are enabled.",
                "You can inspect Windows event logs and correlate system evidence when the session has Event Log tooling available.",
                "You can investigate public-domain signals such as DNS and mail configuration when the relevant tooling is enabled.",
                "Concrete examples you can mention: inspect Windows event logs, summarize recurring errors, or correlate recent failures on this machine or a reachable target.",
                "For explicit capability questions, lead with a few practical examples that are genuinely live in this session, then invite the user's task."
            });

        Assert.Contains("Tooling is not currently available", markdown, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Remote reachability right now is local-only.", markdown, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("For explicit capability questions, lead with a few practical examples", markdown, StringComparison.OrdinalIgnoreCase);
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

    /// <summary>
    /// Ensures supplemental-section budgeting skips blank lines and preserves section priority order under a tight shared budget.
    /// </summary>
    [Fact]
    public void BuildServiceRequest_PrioritizesSupplementalSectionsWhenBudgetSkipsEmptyLines() {
        var markdown = PromptMarkdownBuilder.BuildServiceRequest(
            userText: "What can you do for me today?",
            effectiveName: null,
            effectivePersona: null,
            onboardingInProgress: false,
            missingOnboardingFields: Array.Empty<string>(),
            includeLiveProfileUpdates: false,
            executionBehaviorPrompt: string.Empty,
            continuationStateLines: new[] { "", "cont-1", " ", "cont-2", "cont-3", "cont-4" },
            conversationStyleLines: new[] { "", "style-1", "style-2", "", "style-3", "style-4" },
            capabilityAnswerStyleLines: new[] { "", "cap-style-1", "cap-style-2", "cap-style-3" },
            personaGuidanceLines: new[] { "", "persona-1", "persona-2", "", "persona-3", "persona-4" },
            capabilitySelfKnowledgeLines: new[] { "", "cap-1", "cap-2", "cap-3", "cap-4", "cap-5", "cap-6" },
            persistentMemoryLines: new[] { "", "memory-1", "memory-2", "memory-3", "memory-4" },
            localContextLines: new[] { "local-1", "local-2", "local-3", "local-4" });

        Assert.Contains("cont-1", markdown);
        Assert.Contains("cont-4", markdown);
        Assert.Contains("style-4", markdown);
        Assert.Contains("cap-style-3", markdown);
        Assert.Contains("persona-4", markdown);
        Assert.Contains("cap-6", markdown);
        Assert.Contains("memory-1", markdown);
        Assert.Contains("memory-3", markdown);
        Assert.DoesNotContain("memory-4", markdown);
        Assert.DoesNotContain("local-1", markdown);
    }

    private static RuntimeSelfReportTurnClassifier.RuntimeSelfReportTurnAnalysis CreateLexicalFallbackAnalysis(
        string literal,
        bool compactReply,
        bool modelRequested,
        bool toolingRequested) {
        return new RuntimeSelfReportTurnClassifier.RuntimeSelfReportTurnAnalysis(
            IsRuntimeIntrospectionQuestion: true,
            CompactReply: compactReply,
            ModelRequested: modelRequested,
            ToolingRequested: toolingRequested,
            UserRequestLiteral: literal,
            FromStructuredDirective: false);
    }
}
