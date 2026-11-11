using System.Text.Json.Serialization;

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
    public TaskScope? Scope { get; set; }
    public string? NextActor { get; set; }
    public string? AcceptanceBy { get; set; }
    public string? ResultRef { get; set; }
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
    /// <summary>Peer-message receipt with only native started/terminal stages, outside normal work projections.</summary>
    [JsonIgnore]
    public bool IsMessageEvidence => Kind == "delivery" && Session?.SourceMessageId is not null &&
        !Deliveries.Values.Any(delivery => delivery.Stages.Any(stage =>
            stage.Key is not ("started" or "terminal") && stage.Value.Knowledge == "known"));
    public string? ParentItemId { get; init; }
    public required DateTimeOffset FirstObservedAtUtc { get; init; }
    public SessionFact? Session { get; init; }
    public TaskScope? Scope { get; init; }
    public EvidenceRef? ScopeEvidence { get; init; }
    public SupersessionFact? Supersession { get; init; }
    public string? CandidateRef { get; init; }
    public DateTimeOffset? CandidateAtUtc { get; init; }
    public EvidenceRef? CandidateEvidence { get; init; }
    public NativeControlFact? Control { get; init; }
    public EvidenceRef? ControlEvidence { get; init; }
    public SafetyFact? Safety { get; init; }
    public EvidenceRef? SafetyEvidence { get; init; }
    public Dictionary<string, DeclarationObservation> Declarations { get; init; } = [];
    public EvidenceRef? SessionEvidence { get; init; }
    public EvidenceRef? OwnershipEvidence { get; init; }
    public WorkFact? Work { get; init; }
    public WorkFact? ReportedWork { get; init; }
    public Dictionary<string, EvidenceRef> WorkDimensionEvidence { get; init; } = [];
    public Dictionary<string, TaskScope?> WorkDimensionScopes { get; init; } = [];
    public Dictionary<string, DateTimeOffset> ConditionFirstObservedAtUtc { get; init; } = [];
    public WaitFact? ImplicitReviewWait { get; init; }
    public Dictionary<string, WaitFact> InferredReviewWaits { get; init; } = [];
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

    /// <summary>Direct observation references supporting this projection, without duplicating event payloads.</summary>
    public EvidenceRef[] Evidence() => new[] { SessionEvidence, OwnershipEvidence, WorkEvidence, ClaimEvidence, PullRequestEvidence,
            ControlEvidence, SafetyEvidence, CandidateEvidence, ScopeEvidence, Supersession?.Evidence }.OfType<EvidenceRef>().Concat(WorkDimensionEvidence.Values).Concat(PullRequestProofEvidence)
        .Concat(Declarations.Values.SelectMany(d => new[] { d.Evidence, d.EpisodeEvidence }.OfType<EvidenceRef>())).Concat(Deliveries.Values.SelectMany(d => d.Stages.Values).SelectMany(s => s.Sources()))
        .DistinctBy(e => e.EventId).ToArray();
}

public sealed record RuleMatch(string Verdict, string ReasonCode, DateTimeOffset SinceUtc, string SinceBasis) {
    public string EpisodeId { get; init; } = "";
    public TaskScope? Scope { get; init; }
    public EvidenceRef[] Evidence { get; init; } = [];
    public WaitFact? Wait { get; init; }
}
public sealed record Verdict(string Value, string ReasonCode, DateTimeOffset SinceUtc, string SinceBasis,
    DateTimeOffset EvaluatedAtUtc, int RulesVersion, string[] AlsoMatched, bool LastKnown = false, string[]? MissingFacts = null) {
    public string EpisodeId { get; init; } = "";
    public RuleMatch[] Matches { get; init; } = [];
    public DateTimeOffset? LastProgressAtUtc { get; init; }
    public HoldView? Hold { get; init; }
    public WakeEligibilityView? WakeEligibility { get; init; }
}

public sealed record MachineRow(string Id, string State, DateTimeOffset? LastHeartbeatAtUtc,
    Verdict? Verdict, string[] ExpectedReporters);

public sealed record ItemPage(string SnapshotId, DateTimeOffset AsOfUtc, LedgerItem[] Items, string? NextCursor);
