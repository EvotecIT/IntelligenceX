using IntelligenceX.Ledger.Contracts;

namespace IntelligenceX.Ledger.Storage;

public sealed partial class LedgerStore {
    private static string TaskLinkKey(string item, TaskScope scope) => LedgerScope.Id(item, scope.TaskId, scope.Generation.ToString());
    private sealed record TaskPullRequestState(bool Known, string[] Open);

    private TaskPullRequestState TaskPullRequests(LedgerItem item, WorkFact work, GlobalSnapshot global, LinkObservation? declared,
        string[] owned, Dictionary<string, PullRequestPositive> positives, HashSet<string> positive, DateTimeOffset now) {
        var localScope = item.WorkDimensionScopes.TryGetValue("pr", out var observedScope) ? observedScope : work.Scope;
        var local = LedgerScope.SameTask(localScope, item.Scope) && work.PullRequestsKnown == true &&
            work.PullRequestsAtUtc is { } checkedAt && now - checkedAt <= rules.EvidenceMaxAge &&
            !owned.Any(id => positives.TryGetValue(id, out var proof) && proof.ObservedAtUtc > checkedAt);
        var scopedLinks = declared?.Fact is { Complete: true } links && LedgerScope.SameTask(links.Scope, item.Scope) &&
            now - links.CheckedAtUtc <= rules.EvidenceMaxAge;
        var closed = owned.All(id => global.Proofs.TryGetValue(id, out var proof) && !positive.Contains(id) &&
            (proof.Fact.State == "merged" || proof.Fact.State == "closed" && now - proof.Fact.CheckedAtUtc <= rules.EvidenceMaxAge));
        var open = local ? work.OpenPullRequests.Where(owned.Contains) : owned.Where(positive.Contains);
        open = open.Concat(owned.Where(id => global.Proofs.TryGetValue(id, out var proof) && proof.Fact.State == "open" &&
            (!local || proof.Fact.CheckedAtUtc > work.PullRequestsAtUtc)));
        return new(local || scopedLinks && closed, open.Distinct(StringComparer.Ordinal).ToArray());
    }
}
