namespace IntelligenceX.Ledger.Contracts;

/// <summary>Flat verdict contract consumed by read-only CLI, tray and tile clients.</summary>
public sealed record LedgerItemView(string Id, string Kind, string MachineId, string Provider,
    string Verdict, string ReasonCode, DateTimeOffset SinceUtc, string SinceBasis,
    DateTimeOffset EvaluatedAtUtc, int RulesVersion, string[] AlsoMatched, bool LastKnown,
    WaitFact? Wait, EvidenceRef[] Evidence, Dictionary<string, DeliveryState> DeliveryStages, string[] Coverage,
    string? AccountId, string[] MissingFacts, Guid? SourceSessionId) {
    public string EpisodeId { get; init; } = "";
    public RuleMatch[] Matches { get; init; } = [];
    public DateTimeOffset? LastProgressAtUtc { get; init; }
    public HoldView? Hold { get; init; }
    public WakeEligibilityView? WakeEligibility { get; init; }
    public TaskScope? Scope { get; init; }
    public string? NextActor { get; init; }
    public string? AcceptanceBy { get; init; }
    public string? ResultRef { get; init; }
    public string? ParentItemId { get; init; }
    public string? TurnId { get; init; }
    public SafetyFact? Safety { get; init; }
    public string? CandidateRef { get; init; }
    public static LedgerItemView From(LedgerItem item) {
        var verdict = item.Current ?? throw new InvalidOperationException("Unevaluated item.");
        var declaration = item.Declarations.Values.Where(d => LedgerScope.Applies(d.Fact.Scope, item.Scope, item.Session?.TurnId ?? item.Scope?.TurnId) &&
            (d.Fact.CandidateRef is null || item.CandidateRef is null || d.Fact.CandidateRef == item.CandidateRef)).MaxBy(d => d.Fact.RecordedAtUtc)?.Fact;
        var delivery = item.Deliveries.Values.Where(d => d.Scope is null || LedgerScope.SameTask(d.Scope, item.Scope)).MaxBy(d => d.LastStageOrderAtUtc);
        return new(item.Id, item.Kind, item.MachineId, item.Provider, verdict.Value, verdict.ReasonCode,
            verdict.SinceUtc, verdict.SinceBasis, verdict.EvaluatedAtUtc, verdict.RulesVersion, verdict.AlsoMatched,
            verdict.LastKnown, verdict.Matches.FirstOrDefault(m => m.EpisodeId == verdict.EpisodeId)?.Wait ?? item.Work?.Wait ?? item.ImplicitReviewWait, item.Evidence(),
            item.Deliveries, item.Coverage, item.Session?.AccountId, verdict.MissingFacts ?? [], item.Session?.SourceSessionId) {
                EpisodeId = verdict.EpisodeId, Matches = verdict.Matches, LastProgressAtUtc = verdict.LastProgressAtUtc,
                Hold = verdict.Hold, WakeEligibility = verdict.WakeEligibility, Scope = item.Scope,
                NextActor = verdict.Hold?.NextActor ?? declaration?.NextActor ?? delivery?.NextActor,
                AcceptanceBy = verdict.Hold?.AcceptanceBy ?? declaration?.AcceptanceBy ?? delivery?.AcceptanceBy,
                ResultRef = verdict.Hold?.ResultRef ?? declaration?.ResultRef ?? delivery?.ResultRef,
                ParentItemId = item.ParentItemId, TurnId = item.Session?.TurnId, Safety = item.Safety, CandidateRef = item.CandidateRef
            };
    }
}
