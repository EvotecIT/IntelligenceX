namespace IntelligenceX.Chat.Abstractions;

/// <summary>
/// Shared bracketed section labels used in app-generated prompt envelopes.
/// Keep producers and parsers aligned by consuming these constants instead of duplicating literals.
/// </summary>
public static class PromptEnvelopeSections {
    /// <summary>Bracketed header for conversation-mode metadata.</summary>
    public const string ConversationMode = "[Conversation mode]";
    /// <summary>Bracketed header for continuation-state metadata.</summary>
    public const string ContinuationState = "[Continuation state]";
    /// <summary>Bracketed header for recent conversation-style hints.</summary>
    public const string ConversationStyle = "[Conversation style]";
    /// <summary>Bracketed header for capability-answer guidance.</summary>
    public const string CapabilityAnswerStyle = "[Capability answer style]";
    /// <summary>Bracketed header for persona guidance.</summary>
    public const string PersonaGuidance = "[Persona guidance]";
    /// <summary>Bracketed header for stable session profile metadata.</summary>
    public const string SessionProfileContext = "[Session profile context]";
    /// <summary>Bracketed header for onboarding context.</summary>
    public const string OnboardingContext = "[Onboarding context]";
    /// <summary>Bracketed header for live profile update guidance.</summary>
    public const string LiveProfileUpdates = "[Live profile updates]";
    /// <summary>Bracketed header for capability self-knowledge.</summary>
    public const string CapabilitySelfKnowledge = "[Capability self-knowledge]";
    /// <summary>Bracketed header for runtime capability handshake details.</summary>
    public const string RuntimeCapabilityHandshake = "[Runtime capability handshake]";
    /// <summary>Bracketed header for proactive execution mode metadata.</summary>
    public const string ProactiveExecutionMode = "[Proactive execution mode]";
    /// <summary>Bracketed header for persistent-memory hints.</summary>
    public const string PersistentMemory = "[Persistent memory]";
    /// <summary>Bracketed header for local transcript context fallback.</summary>
    public const string LocalTranscriptContextFallback = "[Local transcript context fallback]";

    /// <summary>
    /// Known app-generated bracketed section headers that may follow the primary user request in markdown envelopes.
    /// </summary>
    public static IReadOnlyList<string> KnownStructuredRequestSectionHeaders { get; } = new[] {
        ConversationMode,
        ContinuationState,
        ConversationStyle,
        CapabilityAnswerStyle,
        PersonaGuidance,
        SessionProfileContext,
        OnboardingContext,
        LiveProfileUpdates,
        CapabilitySelfKnowledge,
        RuntimeCapabilityHandshake,
        ProactiveExecutionMode,
        PersistentMemory,
        LocalTranscriptContextFallback
    };
}
