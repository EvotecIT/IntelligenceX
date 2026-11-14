using IntelligenceX.Ledger.Contracts;
using IntelligenceX.Ledger.Reporting;

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

    /// <summary>Known checkout paths for a recorded PR or exact observed branch commit.</summary>
    public async Task<PullRequestCheckout[]> PullRequestCheckoutsAsync(string repo, int number, string head,
        string branch, CancellationToken token = default) {
        repo = RepositoryOverlapEvaluator.NormalizeRepo(repo);
        await using var db = await _database.OpenSessionAsync(path, token);
        return (await db.QueryAsListAsync("""
            SELECT DISTINCT json_extract(i.json,'$.machineId'),json_extract(i.json,'$.session.repository.worktree'),
              json_extract(i.json,'$.session.repository.branch')
            FROM repository_sessions s JOIN items i ON i.id=s.item
            WHERE s.repo=@repo AND json_extract(i.json,'$.session.repository.worktree') IS NOT NULL
              AND json_extract(i.json,'$.session.repository.gitState')!='workspace-missing'
              AND (EXISTS(SELECT 1 FROM json_each(i.json,'$.session.repository.pullRequests') p
                    WHERE lower(p.value)=@url)
                OR (json_extract(i.json,'$.session.repository.branch')=@branch
                  AND lower(json_extract(i.json,'$.session.repository.commit'))=@head))
            ORDER BY 1,2 LIMIT 6
            """, row => new PullRequestCheckout(row.GetString(0), row.GetString(1), row.IsDBNull(2) ? null : row.GetString(2)),
            new Dictionary<string, object?> { ["repo"] = repo, ["url"] = $"https://github.com/{repo}/pull/{number}",
                ["head"] = head.ToLowerInvariant(), ["branch"] = branch }, cancellationToken: token)).ToArray();
    }
}
