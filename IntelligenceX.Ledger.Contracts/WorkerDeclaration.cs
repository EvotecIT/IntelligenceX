namespace IntelligenceX.Ledger.Contracts;

/// <summary>Verified task identity. Generation increases at an explicitly observed task boundary within an item.</summary>
public sealed record TaskScope(string TaskId, long Generation, string? TurnId = null);

/// <summary>Metadata declared at a real wait, operation, question or handoff boundary. References are opaque local identifiers.</summary>
public sealed record WorkerDeclaration {
    public required string Id { get; init; }
    public required TaskScope Scope { get; init; }
    public required string CheckpointRef { get; init; }
    public required string ConditionRef { get; init; }
    public required string Kind { get; init; }
    public required string NextActor { get; init; }
    public string? AcceptanceBy { get; init; }
    public string? ResultRef { get; init; }
    public string? CandidateRef { get; init; }
    public required bool Blocking { get; init; }
    public required DateTimeOffset RecordedAtUtc { get; init; }
    public DateTimeOffset? ReviewAfterUtc { get; init; }
    public DateTimeOffset? DueAtUtc { get; init; }
    public bool Resolved { get; init; }
    public string? HoldType { get; init; }
}

/// <summary>Direct native control observation. A declaration cannot supply or override a human Stop or pause.</summary>
public sealed record NativeControlFact(TaskScope Scope, string EpisodeId, string State, DateTimeOffset AtUtc);

/// <summary>Evidence-backed observation under the versioned urgent-safety policy. It grants no execution authority.</summary>
public sealed record SafetyFact(TaskScope Scope, string EpisodeId, string Case, string PolicyId, int PolicyVersion,
    string EvidenceRef, DateTimeOffset AtUtc, string? ManualAction = null, bool Resolved = false);

public sealed record DeclarationObservation(WorkerDeclaration Fact, EvidenceRef Evidence);
public sealed record HoldView(string Type, string EpisodeId, TaskScope? Scope, string? NextActor = null,
    string? AcceptanceBy = null, string? ResultRef = null, bool Quiet = false);
public sealed record WakeEligibilityView(string State, string ReasonCode, string? ConditionRef,
    EvidenceRef[] Evidence, string[] MissingFacts);
