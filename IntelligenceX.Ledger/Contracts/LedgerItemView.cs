namespace IntelligenceX.Ledger.Contracts;

/// <summary>Flat verdict contract consumed by read-only CLI, tray and tile clients.</summary>
public sealed record LedgerItemView(string Id, string Kind, string MachineId, string Provider,
    string Verdict, string ReasonCode, DateTimeOffset SinceUtc, string SinceBasis,
    DateTimeOffset EvaluatedAtUtc, int RulesVersion, string[] AlsoMatched, bool LastKnown,
    WaitFact? Wait, EvidenceRef[] Evidence, Dictionary<string, DeliveryState> DeliveryStages, string[] Coverage,
    string? AccountId, string[] MissingFacts, Guid? SourceSessionId) {
    public static LedgerItemView From(LedgerItem item) {
        var verdict = item.Current ?? throw new InvalidOperationException("Unevaluated item.");
        return new(item.Id, item.Kind, item.MachineId, item.Provider, verdict.Value, verdict.ReasonCode,
            verdict.SinceUtc, verdict.SinceBasis, verdict.EvaluatedAtUtc, verdict.RulesVersion, verdict.AlsoMatched,
            verdict.LastKnown, item.Work?.Wait ?? item.ImplicitReviewWait, new[] { item.SessionEvidence, item.OwnershipEvidence, item.WorkEvidence, item.ClaimEvidence, item.PullRequestEvidence }.OfType<EvidenceRef>()
                .Concat(item.WorkDimensionEvidence.Values).Concat(item.PullRequestProofEvidence).Concat(item.Deliveries.Values.SelectMany(d => d.Stages.Values).SelectMany(s => s.Sources())).DistinctBy(e => e.EventId).ToArray(),
            item.Deliveries, item.Coverage, item.Session?.AccountId, verdict.MissingFacts ?? [], item.Session?.SourceSessionId);
    }
}
