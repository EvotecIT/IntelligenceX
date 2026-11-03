namespace IntelligenceX.Ledger.Contracts;

public sealed record EvidenceRef(Guid EventId, string Source, string SourceVersion,
    DateTimeOffset ObservedAtUtc, DateTimeOffset ReceivedAtUtc);
public sealed record PullRequestPositive(DateTimeOffset ObservedAtUtc, EvidenceRef Evidence);

public sealed record StageObservation(string Knowledge, DateTimeOffset? AtUtc = null,
    Guid? EventId = null, string? Outcome = null, EvidenceRef? Evidence = null, string? CauseCode = null,
    EvidenceRef? TimeEvidence = null, EvidenceRef? OutcomeEvidence = null, EvidenceRef? CauseEvidence = null) {
    /// <summary>First receipt and any direct evidence that filled previously unavailable fields.</summary>
    public IEnumerable<EvidenceRef> Sources() => new[] { Evidence, TimeEvidence, OutcomeEvidence, CauseEvidence }.OfType<EvidenceRef>();
}

public sealed record DeliveryState {
    public required string Id { get; init; }
    public string? LastStage { get; set; }
    public DateTimeOffset? LastStageAtUtc { get; set; }
    public DateTimeOffset? DispositionObservedAtUtc { get; set; }
    public DateTimeOffset? LastStageOrderAtUtc { get; set; }
    public bool LastStageTimeIsInferred { get; set; }
    public string? TargetItemId { get; set; }
    public Dictionary<string, StageObservation> Stages { get; init; } =
        new[] { "requested", "admitted", "submitted", "started", "terminal", "accepted", "held", "uncertain" }
            .ToDictionary(x => x, _ => new StageObservation("unknown"));
}

/// <summary>Persisted evidence projection. It never represents unavailable state as idle.</summary>
public sealed record LedgerItem {
    public required string Id { get; init; }
    public required string MachineId { get; init; }
    public required string Provider { get; init; }
    public string Kind { get; init; } = "session";
    public required DateTimeOffset FirstObservedAtUtc { get; init; }
    public SessionFact? Session { get; init; }
    public EvidenceRef? SessionEvidence { get; init; }
    public EvidenceRef? OwnershipEvidence { get; init; }
    public WorkFact? Work { get; init; }
    public WorkFact? ReportedWork { get; init; }
    public Dictionary<string, EvidenceRef> WorkDimensionEvidence { get; init; } = [];
    public Dictionary<string, DateTimeOffset> ConditionFirstObservedAtUtc { get; init; } = [];
    public WaitFact? ImplicitReviewWait { get; init; }
    public EvidenceRef? WorkEvidence { get; init; }
    public EvidenceRef? GlobalEvidence { get; init; }
    public EvidenceRef? ClaimEvidence { get; init; }
    public EvidenceRef? PullRequestEvidence { get; init; }
    public string[] LinkedPullRequestIds { get; init; } = [];
    public string[] RepositoryBranchKeys { get; init; } = [];
    public Dictionary<string, PullRequestPositive> PositivePullRequests { get; init; } = [];
    public DateTimeOffset? LinkedEvidenceChangedAtUtc { get; init; }
    public EvidenceRef[] PullRequestProofEvidence { get; init; } = [];
    public Dictionary<string, DeliveryState> Deliveries { get; init; } = [];
    public string[] Coverage { get; init; } = [];
    public Verdict? Current { get; init; }
}

public sealed record RuleMatch(string Verdict, string ReasonCode, DateTimeOffset SinceUtc, string SinceBasis);
public sealed record Verdict(string Value, string ReasonCode, DateTimeOffset SinceUtc, string SinceBasis,
    DateTimeOffset EvaluatedAtUtc, int RulesVersion, string[] AlsoMatched, bool LastKnown = false, string[]? MissingFacts = null);

public sealed record MachineRow(string Id, string State, DateTimeOffset? LastHeartbeatAtUtc,
    Verdict? Verdict, string[] ExpectedReporters);

public sealed record ItemPage(string SnapshotId, DateTimeOffset AsOfUtc, LedgerItem[] Items, string? NextCursor);
