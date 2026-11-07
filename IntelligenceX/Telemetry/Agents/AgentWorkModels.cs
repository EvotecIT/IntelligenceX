using System;
using System.Collections.Generic;

namespace IntelligenceX.Telemetry.Agents;

/// <summary>Observed work state. Finishing a turn is distinct from finishing a session.</summary>
public enum AgentWorkState {
    /// <summary>A turn has recent activity; this is inferred from a local log, not a live process probe.</summary>
    Running,
    /// <summary>A structured blocking input/approval request awaits a response.</summary>
    WaitingOnPerson,
    /// <summary>No active turn is known.</summary>
    Idle,
    /// <summary>An inactive session still has an open pull request.</summary>
    Stale,
    /// <summary>The session was explicitly closed or archived.</summary>
    Finished
}

/// <summary>Outcome of the most recently observed turn.</summary>
public enum AgentTurnOutcome {
    /// <summary>No terminal event was observed.</summary>
    Unknown,
    /// <summary>A terminal success event was observed.</summary>
    Completed,
    /// <summary>A terminal failure event was observed.</summary>
    Failed,
    /// <summary>An explicit abort/interruption was observed.</summary>
    Interrupted,
    /// <summary>A started turn went quiet without a terminal event; interruption is possible.</summary>
    Incomplete
}

/// <summary>Current rollup for checks on a pull request's head commit.</summary>
public enum AgentCheckState {
    /// <summary>No check rollup is available.</summary>
    Unknown,
    /// <summary>All reported checks succeeded.</summary>
    Success,
    /// <summary>At least one check failed.</summary>
    Failure,
    /// <summary>Checks are still pending.</summary>
    Pending
}

/// <summary>Metadata-only local session. No transcript messages or tool arguments are retained.</summary>
public sealed record AgentSessionState {
    /// <summary>Originating telemetry root, used to keep local and recovered metadata provenance separate.</summary>
    public string? SourceRootId { get; init; }
    /// <summary>Provider identifier, such as codex or claude.</summary>
    public string Provider { get; init; } = string.Empty;
    /// <summary>Provider session identifier.</summary>
    public string Id { get; init; } = string.Empty;
    /// <summary>Explicit session title, when available; never synthesized from a message.</summary>
    public string? Title { get; init; }
    /// <summary>Working directory reported by the provider.</summary>
    public string? WorkingDirectory { get; init; }
    /// <summary>GitHub repository in owner/name form, when known.</summary>
    public string? Repository { get; init; }
    /// <summary>Git branch reported by the provider or local repository.</summary>
    public string? Branch { get; init; }
    /// <summary>Source machine label. Null for recovered sources without a known machine.</summary>
    public string? Machine { get; init; }
    /// <summary>Current observed state.</summary>
    public AgentWorkState State { get; init; } = AgentWorkState.Idle;
    /// <summary>Whether state relies on recent log activity rather than a terminal/request event.</summary>
    public bool StateIsInferred { get; init; }
    /// <summary>Whether the local provider archived the thread.</summary>
    public bool IsArchived { get; init; }
    /// <summary>First provider timestamp, when available.</summary>
    public DateTimeOffset? StartedAtUtc { get; init; }
    /// <summary>Latest valid provider event timestamp, rather than copied-file modification time.</summary>
    public DateTimeOffset? LastActivityAtUtc { get; init; }
    /// <summary>Outcome of the latest turn, distinct from the session lifecycle.</summary>
    public AgentTurnOutcome LastTurnOutcome { get; init; }
    /// <summary>Whether a provider turn is known to be open; null before any turn evidence.</summary>
    public bool? TurnOpen { get; init; }
    /// <summary>Provider turn identity or its first structured record identity. Null when the source does not expose either.</summary>
    public string? TurnId { get; init; }
    /// <summary>Start of the most recently observed turn, independent of session creation.</summary>
    public DateTimeOffset? TurnStartedAtUtc { get; init; }
    /// <summary>Explicit terminal timestamp of the latest turn; null while it is open.</summary>
    public DateTimeOffset? TurnEndedAtUtc { get; init; }
    /// <summary>Latest provider turn activity, excluding Desktop/title metadata refreshes.</summary>
    public DateTimeOffset? LastTurnActivityAtUtc { get; init; }
    /// <summary>Controlled cause for the latest failure/interruption; never derived from prose.</summary>
    public AgentFailureCause FailureCause { get; init; }
    /// <summary>Evidence supporting FailureCause, including an unknown terminal failure.</summary>
    public AgentFailureEvidence FailureEvidence { get; init; }
    /// <summary>Unresolved questions/approvals, including nonblocking requests during running work.</summary>
    public IReadOnlyList<AgentPendingInput> PendingInputs { get; init; } = Array.Empty<AgentPendingInput>();
    /// <summary>Explicit provider answers correlated to known requests; absence from PendingInputs alone is not a resolution.</summary>
    public IReadOnlyList<AgentInputResolution> ResolvedInputs { get; init; } = Array.Empty<AgentInputResolution>();
    /// <summary>Sources contributing state/activity metadata; title-only records do not establish liveness.</summary>
    public AgentObservationSources ObservationSources { get; init; }
    /// <summary>When session-log state was last parsed; cache reuse does not advance it. Desktop refresh time is on the snapshot.</summary>
    public DateTimeOffset? ObservedAtUtc { get; init; }
    /// <summary>Matching open pull requests, linked only by repository and exact head branch.</summary>
    public IReadOnlyList<AgentPullRequestState> PullRequests { get; init; } = Array.Empty<AgentPullRequestState>();
}

/// <summary>Identity of a request answered through its matching provider response.</summary>
public sealed record AgentInputResolution {
    /// <summary>Provider request identifier within this session.</summary>
    public string Id { get; init; } = string.Empty;
    /// <summary>Original request turn, when available.</summary>
    public string? TurnId { get; init; }
}

/// <summary>Open GitHub work authored by, or requesting review from, the selected user.</summary>
public sealed record AgentPullRequestState {
    /// <summary>Canonical pull request URL.</summary>
    public string Url { get; init; } = string.Empty;
    /// <summary>Pull request title.</summary>
    public string Title { get; init; } = string.Empty;
    /// <summary>Target repository in owner/name form.</summary>
    public string Repository { get; init; } = string.Empty;
    /// <summary>Source repository; important for fork branch identity.</summary>
    public string? HeadRepository { get; init; }
    /// <summary>Source branch.</summary>
    public string Branch { get; init; } = string.Empty;
    /// <summary>Whether the request remains open.</summary>
    public bool IsOpen { get; init; } = true;
    /// <summary>Whether GitHub reports the request as merged; merged requests cannot reopen.</summary>
    public bool IsMerged { get; init; }
    /// <summary>Whether the selected user authored this request.</summary>
    public bool IsAuthor { get; init; }
    /// <summary>Whether review is requested from the selected user, not merely from someone else.</summary>
    public bool ReviewRequested { get; init; }
    /// <summary>Current head-commit check rollup.</summary>
    public AgentCheckState Checks { get; init; }
    /// <summary>Check sources could not be fully revalidated; Checks may retain a prior observation for the same head.</summary>
    public bool ChecksUnavailable { get; init; }
    /// <summary>Link to the head commit's checks.</summary>
    public string? ChecksUrl { get; init; }
}

/// <summary>Actionable attention categories.</summary>
public enum AgentAttentionKind {
    /// <summary>A person must answer or approve.</summary>
    WaitingOnPerson,
    /// <summary>An authored open PR has failing checks.</summary>
    FailedChecks,
    /// <summary>The selected user has a review request.</summary>
    ReviewRequested,
    /// <summary>An inactive session has an open PR.</summary>
    Stale,
    /// <summary>The last turn failed.</summary>
    FailedTurn,
    /// <summary>The last turn was explicitly interrupted.</summary>
    InterruptedTurn,
    /// <summary>A turn has no terminal event and is no longer recently active.</summary>
    IncompleteTurn,
    /// <summary>An explicit request exists, but the observed log cannot confirm a correlated response.</summary>
    UnconfirmedInput
}

/// <summary>A rule result referencing a session or PR without copying transcript content.</summary>
public sealed record AgentAttentionItem {
    /// <summary>Reason for attention.</summary>
    public AgentAttentionKind Kind { get; init; }
    /// <summary>Session provider, if this item refers to a session.</summary>
    public string? Provider { get; init; }
    /// <summary>Session identifier, if applicable.</summary>
    public string? SessionId { get; init; }
    /// <summary>Session machine identity. Combine with Provider and SessionId rather than title.</summary>
    public string? Machine { get; init; }
    /// <summary>Originating root, when this attention item refers to a session.</summary>
    public string? SourceRootId { get; init; }
    /// <summary>Correlated input identifier for a question/approval, when available.</summary>
    public string? InputId { get; init; }
    /// <summary>Session or PR title, when available.</summary>
    public string? Title { get; init; }
    /// <summary>PR, CI, or other navigation link.</summary>
    public string? Url { get; init; }
}

/// <summary>A content-free source diagnostic. Messages never include provider payloads or exception text.</summary>
public sealed record AgentSourceDiagnostic {
    /// <summary>Source provider.</summary>
    public string Provider { get; init; } = string.Empty;
    /// <summary>Affected root when known; a null value applies to the provider or whole refresh.</summary>
    public string? SourceRootId { get; init; }
    /// <summary>Stable code such as unreadable-source or malformed-record.</summary>
    public string Code { get; init; } = string.Empty;
    /// <summary>Number of affected files or records.</summary>
    public int Count { get; init; } = 1;
}

/// <summary>Portable snapshot for tray, native, CLI and other consumers.</summary>
public sealed record AgentWorkSnapshot {
    /// <summary>Time at which local collection finished and session state was evaluated. Optional remote enrichment follows.</summary>
    public DateTimeOffset CapturedAtUtc { get; init; }
    /// <summary>Last successful GitHub revalidation, independent of local capture time; null before remote collection.</summary>
    public DateTimeOffset? GitHubCheckedAtUtc { get; init; }
    /// <summary>Last successful optional Desktop metadata callback; null before success. A failure retains the prior time and reports a diagnostic.</summary>
    public DateTimeOffset? MetadataCheckedAtUtc { get; init; }
    /// <summary>Local session-log work performed, if collected by the local service.</summary>
    public AgentWorkScanStatistics? Scan { get; init; }
    /// <summary>Deduplicated sessions, newest first.</summary>
    public IReadOnlyList<AgentSessionState> Sessions { get; init; } = Array.Empty<AgentSessionState>();
    /// <summary>Open PRs, including those without a local session.</summary>
    public IReadOnlyList<AgentPullRequestState> PullRequests { get; init; } = Array.Empty<AgentPullRequestState>();
    /// <summary>Pure-rule attention results.</summary>
    public IReadOnlyList<AgentAttentionItem> NeedsAttention { get; init; } = Array.Empty<AgentAttentionItem>();
    /// <summary>Partial-source failures; an empty session list alone does not imply healthy sources.</summary>
    public IReadOnlyList<AgentSourceDiagnostic> Diagnostics { get; init; } = Array.Empty<AgentSourceDiagnostic>();
}
