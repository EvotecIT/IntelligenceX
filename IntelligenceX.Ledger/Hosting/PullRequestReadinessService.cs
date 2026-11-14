using IntelligenceX.Ledger.Contracts;
using IntelligenceX.Ledger.Reporting;
using IntelligenceX.Ledger.Storage;
using IntelligenceX.Telemetry.GitHub;

namespace IntelligenceX.Ledger.Hosting;

/// <summary>Bounded 60-second GitHub cache, joined with the reporter's existing checkout evidence.</summary>
internal sealed class PullRequestReadinessService(LedgerStore store) {
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly Dictionary<string, Entry> _cache = new(StringComparer.Ordinal);
    private sealed record Entry(PullRequestReadiness Value);

    internal async Task<PullRequestReadiness> ReadAsync(string repo, int pr, CancellationToken token) {
        repo = RepositoryOverlapEvaluator.NormalizeRepo(repo);
        if (pr <= 0) throw new ArgumentException("A positive pull request number is required.");
        var key = repo + "#" + pr;
        await _gate.WaitAsync(token);
        try {
            var now = DateTimeOffset.UtcNow;
            _cache.TryGetValue(key, out var previous);
            if (previous is not null && now - previous.Value.CheckedAtUtc < TimeSpan.FromSeconds(60)) return previous.Value;
            PullRequestReadiness value;
            try {
                using var deadline = CancellationTokenSource.CreateLinkedTokenSource(token);
                deadline.CancelAfter(TimeSpan.FromSeconds(12));
                var credentials = await GitHubCredentialResolver.ResolveAsync(deadline.Token);
                if (!credentials.HasToken) throw new InvalidOperationException("GitHub credentials unavailable.");
                using var github = new GitHubDashboardService(credentials.Token!);
                var report = await github.FetchPullRequestReadinessAsync(repo, pr, deadline.Token);
                var rechecked = report.HeadSha == report.ObservedHeadSha && !report.Reasons.Contains("head-changed-during-read");
                var checkouts = await store.PullRequestCheckoutsAsync(repo, pr, report.HeadSha, report.HeadBranch, deadline.Token);
                var reasons = report.Reasons.ToList();
                var protection = report.BranchProtection;
                value = new() {
                    Repo = report.Repository, Pr = pr, HeadSha = report.ObservedHeadSha,
                    ChecksHeadSha = report.HeadSha == report.ObservedHeadSha ? null : report.HeadSha, State = report.State.ToLowerInvariant(),
                    Checks = report.ChecksState.ToString().ToLowerInvariant(),
                    FailingChecks = report.Checks.Where(check => check.State == GitHubReadinessCheckState.Failing).Select(check => check.Name).Take(10).ToArray(),
                    PendingChecks = report.Checks.Where(check => check.State is GitHubReadinessCheckState.Pending or GitHubReadinessCheckState.Unknown).Select(check => check.Name).Take(10).ToArray(),
                    FailingCheckCount = report.Checks.Count(check => check.State == GitHubReadinessCheckState.Failing),
                    PendingCheckCount = report.Checks.Count(check => check.State is GitHubReadinessCheckState.Pending or GitHubReadinessCheckState.Unknown),
                    UnresolvedThreads = report.UnresolvedThreadCount,
                    Threads = report.UnresolvedThreads.Select(thread => new PullRequestThread(thread.Path, thread.Text)).ToArray(),
                    ReviewerPending = report.PendingReviewers.Count > 0,
                    PendingReviewers = report.PendingReviewers.Take(5).Select(reviewer => reviewer.Actor).ToArray(),
                    RecheckedAfterObservedHead = rechecked, CheckedAtUtc = report.ObservedAtUtc, Draft = report.IsDraft,
                    Mergeable = report.Mergeable.ToLowerInvariant(), MergeState = report.MergeStateStatus.ToLowerInvariant(),
                    ReviewDecision = report.ReviewDecision?.ToLowerInvariant(),
                    Protection = protection is null ? null : new(protection.RequiresApprovingReviews,
                        protection.RequiredApprovingReviewCount, protection.RequiresCodeOwnerReviews,
                        protection.RequireLastPushApproval, protection.RequiresConversationResolution,
                        protection.RequiresStatusChecks, protection.RequiresStrictStatusChecks),
                    Verdict = report.Verdict.ToString().ToLowerInvariant(),
                    Reasons = reasons.ToArray(), Cleanup = checkouts.Take(5).ToArray(),
                    Coverage = report.EvidenceGaps.Concat(["cache:max-age-60s", "recheck:initial-and-final-head-in-one-call", "cleanup:reporter-observed"]).ToArray(),
                    Truncated = report.Checks.Count(check => check.State == GitHubReadinessCheckState.Failing) > 10 ||
                        report.Checks.Count(check => check.State is GitHubReadinessCheckState.Pending or GitHubReadinessCheckState.Unknown) > 10 ||
                        report.PendingReviewers.Count > 5 || checkouts.Length > 5
                };
            } catch (Exception error) when (error is HttpRequestException or InvalidOperationException or System.Text.Json.JsonException) {
                value = Unavailable(repo, pr, now, "github-read-unavailable");
            } catch (OperationCanceledException) when (!token.IsCancellationRequested) {
                value = Unavailable(repo, pr, now, "github-read-timeout");
            }
            if (_cache.Count >= 32 && !_cache.ContainsKey(key))
                _cache.Remove(_cache.MinBy(pair => pair.Value.Value.CheckedAtUtc).Key);
            _cache[key] = new(value);
            return value;
        } finally { _gate.Release(); }
    }

    private static PullRequestReadiness Unavailable(string repo, int pr, DateTimeOffset now, string reason) =>
        new() { Repo = repo, Pr = pr, CheckedAtUtc = now, Reasons = [reason], Coverage = ["github:unavailable"] };
}
