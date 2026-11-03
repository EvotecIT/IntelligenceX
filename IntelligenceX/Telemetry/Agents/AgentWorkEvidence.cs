using System;

namespace IntelligenceX.Telemetry.Agents;

/// <summary>The structured request that needs a response, independently of execution state.</summary>
public enum AgentInputKind {
    /// <summary>A question explicitly emitted through an input tool or event.</summary>
    Question,
    /// <summary>An explicit approval request.</summary>
    Approval
}

/// <summary>Unresolved input metadata. Question text, options, answers and tool arguments are excluded.</summary>
public sealed record AgentPendingInput {
    /// <summary>Provider call/request identifier, when available. Interpret within the session identity.</summary>
    public string? Id { get; init; }
    /// <summary>Question or approval evidence.</summary>
    public AgentInputKind Kind { get; init; }
    /// <summary>Whether this request suspends execution. Asynchronous questions do not.</summary>
    public bool IsBlocking { get; init; }
    /// <summary>True when asynchronous delivery or a missing request identifier prevents correlated answer evidence in the observed log.</summary>
    public bool AnswerIsUnconfirmed { get; init; }
    /// <summary>Provider time of the request, if available.</summary>
    public DateTimeOffset? RequestedAtUtc { get; init; }
}

/// <summary>A controlled failure category. Unknown is preserved when structured evidence is absent.</summary>
public enum AgentFailureCause {
    /// <summary>No supported structured cause was recorded.</summary>
    Unknown,
    /// <summary>Explicit provider overload or unavailable capacity.</summary>
    Capacity,
    /// <summary>Explicit usage or session budget exhaustion.</summary>
    UsageLimit,
    /// <summary>Explicit rate limiting; quota exhaustion is not inferred.</summary>
    RateLimit,
    /// <summary>Explicit authentication failure.</summary>
    Authentication,
    /// <summary>Explicit connection or stream failure, not a client delivery receipt.</summary>
    Transport,
    /// <summary>Explicit context-window exhaustion.</summary>
    ContextLimit
}

/// <summary>Evidence supporting a turn failure category; never carries free-form provider text.</summary>
public enum AgentFailureEvidence {
    /// <summary>No failure evidence was observed.</summary>
    None,
    /// <summary>A failure/interruption event exists, but its cause is not established.</summary>
    TerminalEvent,
    /// <summary>A recognized structured provider code establishes the category.</summary>
    ProviderCode
}

/// <summary>Inputs contributing to an observation. These are observations, not process or delivery probes.</summary>
[Flags]
public enum AgentObservationSources {
    /// <summary>No observation source is established.</summary>
    Unknown = 0,
    /// <summary>A provider session log.</summary>
    SessionLog = 1,
    /// <summary>Desktop thread database metadata.</summary>
    DesktopMetadata = 2,
    /// <summary>An explicit title index.</summary>
    TitleIndex = 4
}
