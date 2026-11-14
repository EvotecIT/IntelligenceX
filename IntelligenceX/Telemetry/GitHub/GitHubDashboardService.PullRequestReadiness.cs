using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace IntelligenceX.Telemetry.GitHub;

public sealed partial class GitHubDashboardService {
    private const int ReadinessPageLimit = 100;
    private const int ReadinessThreadPreviewLimit = 5;
    private const int ReadinessThreadTextLimit = 240;
    private const string ReadinessMetadataQuery = """
        query($owner: String!, $repo: String!, $number: Int!) {
          repository(owner: $owner, name: $repo) {
            pullRequest(number: $number) {
              headRefOid headRefName state isDraft mergeable mergeStateStatus reviewDecision
              commits(last: 1) { nodes { commit { oid committedDate } } }
              baseRef { branchProtectionRule {
                requiresApprovingReviews requiredApprovingReviewCount requiresCodeOwnerReviews
                requireLastPushApproval dismissesStaleReviews requiresConversationResolution
                requiresStatusChecks requiresStrictStatusChecks requiredStatusCheckContexts
              } }
            }
          }
        }
        """;

    /// <summary>Reads a bounded, uncached readiness report using this service's existing GitHub credentials.</summary>
    /// <param name="repository">Repository in owner/name form.</param>
    /// <param name="number">Positive pull request number.</param>
    /// <param name="cancellationToken">Cancels the read, which also has a two-minute deadline.</param>
    /// <returns>Exact-head checks, review evidence, merge gates, and a readiness verdict.</returns>
    /// <remarks>All connections are paginated, up to 100 pages each. Incomplete or unavailable reads throw.
    /// A head change during collection produces a waiting report. Push time is unavailable from these fields.</remarks>
    public async Task<GitHubPullRequestReadiness> FetchPullRequestReadinessAsync(string repository, int number,
        CancellationToken cancellationToken = default) {
        if (number < 1) throw new ArgumentOutOfRangeException(nameof(number));
        var (owner, name) = SplitRepositoryNameWithOwner(repository);
        using var deadline = new CancellationTokenSource(TimeSpan.FromMinutes(2));
        using var bounded = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, deadline.Token);
        var token = bounded.Token;
        var initial = await ReadReadinessMetadataAsync(owner, name, number, token).ConfigureAwait(false);
        var head = ReadString(initial, "headRefOid");
        if (string.IsNullOrWhiteSpace(head)) throw new InvalidOperationException("GitHub readiness head is unavailable.");

        var checksTask = ReadReadinessPagesAsync(owner, name, number, head, "contexts", """
            __typename
            ... on CheckRun { name status conclusion isRequired(pullRequestNumber: $number) }
            ... on StatusContext { context state isRequired(pullRequestNumber: $number) }
            """, token);
        var threadsTask = ReadReadinessPagesAsync(owner, name, number, head, "reviewThreads", """
            isResolved isOutdated path comments(first: 1) { nodes { bodyText } }
            """, token);
        var reviewsTask = ReadReadinessPagesAsync(owner, name, number, head, "reviews", """
            id author { login } state submittedAt createdAt commit { oid }
            """, token);
        var requestsTask = ReadReadinessPagesAsync(owner, name, number, head, "reviewRequests", """
            requestedReviewer {
              __typename
              ... on User { login }
              ... on Bot { login }
              ... on Mannequin { login }
              ... on Team { combinedSlug }
              ... on EnterpriseTeam { combinedSlug }
            }
            """, token);
        var eyesTask = ReadReadinessPagesAsync(owner, name, number, head, "reactions", """
            user { login } createdAt
            """, token);
        await Task.WhenAll(checksTask, threadsTask, reviewsTask, requestsTask, eyesTask).ConfigureAwait(false);
        var final = await ReadReadinessMetadataAsync(owner, name, number, token).ConfigureAwait(false);
        var observedHead = ReadString(final, "headRefOid");
        var protection = ParseReadinessProtection(final);
        var checkPage = checksTask.Result;
        var checks = checkPage.Nodes.Select(ParseReadinessCheck).ToList();
        foreach (var required in protection?.RequiredStatusCheckContexts ?? Array.Empty<string>()) {
            if (!checks.Any(check => string.Equals(check.Name, required, StringComparison.Ordinal))) {
                checks.Add(new GitHubReadinessCheck(required, GitHubReadinessCheckState.Pending, "EXPECTED", null, true));
            }
        }
        var checksState = CombineReadinessChecks(checks, checkPage.State, protection?.RequiresStatusChecks == true);
        var unresolved = threadsTask.Result.Nodes.Where(thread => !thread.GetProperty("isResolved").GetBoolean()).ToArray();
        var reviews = ParseReadinessReviews(reviewsTask.Result.Nodes, head);
        var pending = ParseReadinessPendingReviewers(requestsTask.Result.Nodes, reviewsTask.Result.Nodes,
            eyesTask.Result.Nodes, reviews);
        var blockers = new List<string>();
        var waiting = new List<string>();
        var state = ReadString(final, "state");
        var draft = final.GetProperty("isDraft").GetBoolean();
        var mergeable = ReadString(final, "mergeable");
        var mergeState = ReadString(final, "mergeStateStatus");
        var decision = NormalizeOptional(ReadString(final, "reviewDecision"));
        if (state != "OPEN") blockers.Add("pull-request-not-open");
        if (draft) blockers.Add("draft");
        if (checksState == GitHubReadinessCheckState.Failing) blockers.Add("checks-failing");
        if (unresolved.Length > 0) blockers.Add("unresolved-review-threads");
        if (decision == "CHANGES_REQUESTED" || HasReadinessChangesRequested(reviewsTask.Result.Nodes)) blockers.Add("changes-requested");
        if (mergeable == "CONFLICTING" || mergeState == "DIRTY") blockers.Add("merge-conflicts");
        if (mergeState == "BEHIND" && protection?.RequiresStrictStatusChecks == true) blockers.Add("branch-behind");
        if (checksState == GitHubReadinessCheckState.Pending) waiting.Add("checks-pending");
        if (checksState == GitHubReadinessCheckState.Unknown) waiting.Add("checks-unknown");
        if (pending.Count > 0) waiting.Add("reviewers-pending");
        if (decision == "REVIEW_REQUIRED") waiting.Add("required-approval-pending");
        if (protection?.RequiresApprovingReviews == true && decision is null &&
            (protection.RequiredApprovingReviewCount > 0 || protection.RequiresCodeOwnerReviews || protection.RequireLastPushApproval))
            waiting.Add("required-approval-unknown");
        if (decision is not (null or "APPROVED" or "REVIEW_REQUIRED" or "CHANGES_REQUESTED")) waiting.Add("review-decision-unknown");
        if (mergeable is not ("MERGEABLE" or "CONFLICTING")) waiting.Add("mergeability-unknown");
        if (mergeState == "BLOCKED" && blockers.Count == 0 && waiting.Count == 0) blockers.Add("github-merge-gate-blocked");
        if (mergeState is not ("CLEAN" or "UNSTABLE" or "BLOCKED" or "DIRTY") &&
            !(mergeState == "BEHIND" && protection?.RequiresStrictStatusChecks == true)) waiting.Add("merge-state-pending");
        // UNSTABLE is an observed server-side restriction even if individual contexts changed during the read.
        if (mergeState == "UNSTABLE" && checksState != GitHubReadinessCheckState.Failing) waiting.Add("merge-state-unstable");
        var headChanged = head != observedHead || checkPage.HeadChanged || threadsTask.Result.HeadChanged ||
            reviewsTask.Result.HeadChanged || requestsTask.Result.HeadChanged || eyesTask.Result.HeadChanged;
        if (headChanged) waiting.Add("head-changed-during-read");
        var committed = initial.GetProperty("commits").GetProperty("nodes").EnumerateArray()
            .Select(node => node.GetProperty("commit")).FirstOrDefault(commit => ReadString(commit, "oid") == head);
        return new GitHubPullRequestReadiness {
            Repository = owner + "/" + name, Number = number, HeadSha = head, HeadBranch = ReadString(initial, "headRefName"), ObservedHeadSha = observedHead,
            ObservedAtUtc = DateTimeOffset.UtcNow, HeadCommittedAtUtc = TryParseTimestamp(ReadString(committed, "committedDate")),
            State = state, IsDraft = draft, Mergeable = mergeable, MergeStateStatus = mergeState, ReviewDecision = decision,
            BranchProtection = protection, ChecksState = checksState, Checks = checks.ToArray(),
            UnresolvedThreadCount = unresolved.Length,
            UnresolvedThreads = unresolved.Take(ReadinessThreadPreviewLimit).Select(ParseReadinessThread).ToArray(),
            Reviews = reviews, PendingReviewers = pending,
            Verdict = headChanged ? GitHubReadinessVerdict.Waiting : blockers.Count > 0 ? GitHubReadinessVerdict.Blocked
                : waiting.Count > 0 ? GitHubReadinessVerdict.Waiting : GitHubReadinessVerdict.Ready,
            Reasons = blockers.Concat(waiting).ToArray(),
            EvidenceGaps = new[] { "push-timestamp-unavailable", "classic-branch-protection-only", "eyes-on-pull-request-only" }
        };
    }

    private async Task<JsonElement> ReadReadinessMetadataAsync(string owner, string repo, int number, CancellationToken token) {
        using var document = await QueryGraphQlAsync(ReadinessMetadataQuery,
            new Dictionary<string, object?> { ["owner"] = owner, ["repo"] = repo, ["number"] = number }, token).ConfigureAwait(false);
        return ReadinessObject(ReadinessObject(ReadinessObject(document.RootElement, "data"), "repository"), "pullRequest").Clone();
    }

    private async Task<(List<JsonElement> Nodes, string? State, bool HeadChanged)> ReadReadinessPagesAsync(
        string owner, string repo, int number, string head, string field, string selection, CancellationToken token) {
        var isChecks = field == "contexts";
        var arguments = "first: 100, after: $after" + (field == "reactions" ? ", content: EYES" : string.Empty);
        var connection = field + "(" + arguments + ") { nodes { " + selection + " } pageInfo { hasNextPage endCursor } }";
        var query = "query($owner: String!, $repo: String!, $number: Int!, $after: String" + (isChecks ? ", $head: GitObjectID!" : string.Empty)
            + ") { repository(owner: $owner, name: $repo) { "
            + (isChecks ? "object(oid: $head) { ... on Commit { oid statusCheckRollup { state " + connection + " } } }"
                : "pullRequest(number: $number) { headRefOid " + connection + " }") + " } }";
        var nodes = new List<JsonElement>();
        var cursors = new HashSet<string>(StringComparer.Ordinal);
        string? after = null;
        string? state = null;
        var headChanged = false;
        for (var page = 0; page < ReadinessPageLimit; page++) {
            var variables = new Dictionary<string, object?> { ["owner"] = owner, ["repo"] = repo, ["number"] = number, ["after"] = after };
            if (isChecks) variables["head"] = head;
            using var document = await QueryGraphQlAsync(query, variables, token).ConfigureAwait(false);
            var repository = ReadinessObject(ReadinessObject(document.RootElement, "data"), "repository");
            var parent = ReadinessObject(repository, isChecks ? "object" : "pullRequest");
            if (isChecks) {
                if (ReadString(parent, "oid") != head) throw new InvalidOperationException("GitHub readiness check commit does not match the head.");
                if (parent.TryGetProperty("statusCheckRollup", out var rollup) && rollup.ValueKind == JsonValueKind.Null) return (nodes, null, headChanged);
                parent = ReadinessObject(parent, "statusCheckRollup");
                state = ReadString(parent, "state");
            } else {
                headChanged |= ReadString(parent, "headRefOid") != head;
            }
            var result = ReadinessObject(parent, field);
            var batch = result.GetProperty("nodes");
            if (batch.ValueKind != JsonValueKind.Array) throw new InvalidOperationException("GitHub readiness connection nodes are unavailable.");
            foreach (var node in batch.EnumerateArray()) {
                if (node.ValueKind != JsonValueKind.Object) throw new InvalidOperationException("GitHub readiness connection contains an unavailable node.");
                nodes.Add(node.Clone());
            }
            var info = ReadinessObject(result, "pageInfo");
            if (!info.GetProperty("hasNextPage").GetBoolean()) return (nodes, state, headChanged);
            after = NormalizeOptional(ReadString(info, "endCursor"));
            if (after is null || !cursors.Add(after)) throw new InvalidOperationException("GitHub readiness pagination did not advance.");
        }
        throw new InvalidOperationException("GitHub readiness exceeded its pagination limit.");
    }

    private static JsonElement ReadinessObject(JsonElement parent, string name) {
        if (!TryGetProperty(parent, name, out var value) || value.ValueKind != JsonValueKind.Object)
            throw new InvalidOperationException("GitHub readiness evidence is unavailable (" + name + ").");
        return value;
    }

    private static GitHubReadinessBranchProtection? ParseReadinessProtection(JsonElement pr) {
        var baseRef = pr.GetProperty("baseRef");
        if (baseRef.ValueKind == JsonValueKind.Null) return null;
        var rule = baseRef.GetProperty("branchProtectionRule");
        if (rule.ValueKind == JsonValueKind.Null) return null;
        return new GitHubReadinessBranchProtection {
            RequiresApprovingReviews = rule.GetProperty("requiresApprovingReviews").GetBoolean(),
            RequiredApprovingReviewCount = rule.GetProperty("requiredApprovingReviewCount").GetInt32(),
            RequiresCodeOwnerReviews = rule.GetProperty("requiresCodeOwnerReviews").GetBoolean(),
            RequireLastPushApproval = rule.GetProperty("requireLastPushApproval").GetBoolean(),
            DismissesStaleReviews = rule.GetProperty("dismissesStaleReviews").GetBoolean(),
            RequiresConversationResolution = rule.GetProperty("requiresConversationResolution").GetBoolean(),
            RequiresStatusChecks = rule.GetProperty("requiresStatusChecks").GetBoolean(),
            RequiresStrictStatusChecks = rule.GetProperty("requiresStrictStatusChecks").GetBoolean(),
            RequiredStatusCheckContexts = rule.GetProperty("requiredStatusCheckContexts").EnumerateArray()
                .Select(value => value.GetString() ?? string.Empty).ToArray()
        };
    }

    private static GitHubReadinessCheck ParseReadinessCheck(JsonElement node) {
        var type = ReadString(node, "__typename");
        var status = ReadString(node, type == "CheckRun" ? "status" : "state");
        var conclusion = NormalizeOptional(ReadString(node, "conclusion"));
        var state = type switch {
            "CheckRun" when status == "COMPLETED" => conclusion switch {
                "SUCCESS" or "NEUTRAL" or "SKIPPED" => GitHubReadinessCheckState.Green,
                "FAILURE" or "CANCELLED" or "TIMED_OUT" or "ACTION_REQUIRED" or "STARTUP_FAILURE" => GitHubReadinessCheckState.Failing,
                "STALE" => GitHubReadinessCheckState.Pending,
                _ => GitHubReadinessCheckState.Unknown
            },
            "CheckRun" when status is "QUEUED" or "IN_PROGRESS" or "WAITING" or "PENDING" or "REQUESTED" => GitHubReadinessCheckState.Pending,
            "StatusContext" when status == "SUCCESS" => GitHubReadinessCheckState.Green,
            "StatusContext" when status is "FAILURE" or "ERROR" => GitHubReadinessCheckState.Failing,
            "StatusContext" when status is "PENDING" or "EXPECTED" => GitHubReadinessCheckState.Pending,
            _ => GitHubReadinessCheckState.Unknown
        };
        var name = ReadString(node, type == "CheckRun" ? "name" : "context");
        if (string.IsNullOrWhiteSpace(name)) state = GitHubReadinessCheckState.Unknown;
        bool? required = TryGetProperty(node, "isRequired", out var value) && value.ValueKind is JsonValueKind.True or JsonValueKind.False
            ? value.GetBoolean() : null;
        return new GitHubReadinessCheck(name, state, status, conclusion, required);
    }

    private static GitHubReadinessCheckState CombineReadinessChecks(List<GitHubReadinessCheck> checks, string? rollup, bool required) {
        if (rollup is "FAILURE" or "ERROR" || checks.Any(check => check.State == GitHubReadinessCheckState.Failing)) return GitHubReadinessCheckState.Failing;
        if (checks.Any(check => check.State == GitHubReadinessCheckState.Unknown)) return GitHubReadinessCheckState.Unknown;
        if (rollup is "PENDING" or "EXPECTED" || checks.Any(check => check.State == GitHubReadinessCheckState.Pending) || required && checks.Count == 0)
            return GitHubReadinessCheckState.Pending;
        return rollup is null or "SUCCESS" ? GitHubReadinessCheckState.Green : GitHubReadinessCheckState.Unknown;
    }

    private static GitHubReadinessThread ParseReadinessThread(JsonElement thread) {
        var first = thread.GetProperty("comments").GetProperty("nodes").EnumerateArray().FirstOrDefault();
        var text = string.Join(" ", ReadString(first, "bodyText").Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        if (text.Length > ReadinessThreadTextLimit) text = text.Substring(0, ReadinessThreadTextLimit - 1) + "…";
        return new GitHubReadinessThread(ReadString(thread, "path"), text, thread.GetProperty("isOutdated").GetBoolean());
    }

    private static IReadOnlyList<GitHubReadinessReview> ParseReadinessReviews(List<JsonElement> nodes, string head) => nodes
        .Where(node => ReadString(node, "state") != "PENDING" && TryParseTimestamp(ReadString(node, "submittedAt")).HasValue)
        .GroupBy(ReadinessReviewActorKey, StringComparer.OrdinalIgnoreCase)
        .Select(group => group.OrderByDescending(node => TryParseTimestamp(ReadString(node, "submittedAt"))).First())
        .Select(node => new GitHubReadinessReview(ReadinessActor(node, "author"), ReadString(node, "state"),
            TryParseTimestamp(ReadString(node, "submittedAt"))!.Value, NormalizeOptional(ReadStringFromNested(node, "commit", "oid")),
            ReadStringFromNested(node, "commit", "oid") == head))
        .OrderBy(review => review.Actor, StringComparer.OrdinalIgnoreCase).ToArray();

    private static bool HasReadinessChangesRequested(List<JsonElement> nodes) => nodes
        .Where(node => ReadString(node, "state") is "APPROVED" or "CHANGES_REQUESTED" or "DISMISSED")
        .GroupBy(ReadinessReviewActorKey, StringComparer.OrdinalIgnoreCase)
        .Any(group => ReadString(group.OrderByDescending(node => TryParseTimestamp(ReadString(node, "submittedAt"))).First(), "state") == "CHANGES_REQUESTED");

    private static string ReadinessReviewActorKey(JsonElement node) {
        var actor = ReadinessActor(node, "author");
        return actor == "(unavailable)" ? "id:" + node.GetProperty("id").GetString() : "login:" + actor;
    }

    private static IReadOnlyList<GitHubReadinessPendingReviewer> ParseReadinessPendingReviewers(List<JsonElement> requests,
        List<JsonElement> reviewNodes, List<JsonElement> eyes, IReadOnlyList<GitHubReadinessReview> reviews) {
        var pending = requests.Select(node => {
            var reviewer = node.GetProperty("requestedReviewer");
            var actor = NormalizeOptional(ReadString(reviewer, "combinedSlug")) ?? NormalizeOptional(ReadString(reviewer, "login")) ?? "(unavailable)";
            return new GitHubReadinessPendingReviewer(actor, "review-request", null);
        }).ToList();
        pending.AddRange(reviewNodes.Where(node => ReadString(node, "state") == "PENDING")
            .Select(node => new GitHubReadinessPendingReviewer(ReadinessActor(node, "author"), "pending-review", TryParseTimestamp(ReadString(node, "createdAt")))));
        foreach (var reaction in eyes) {
            var actor = ReadinessActor(reaction, "user");
            var at = TryParseTimestamp(ReadString(reaction, "createdAt"));
            // A later submitted review by the same known actor supersedes this reaction only.
            if (actor != "(unavailable)" && at.HasValue && reviews.Any(review =>
                string.Equals(review.Actor, actor, StringComparison.OrdinalIgnoreCase) && review.SubmittedAtUtc > at.Value)) continue;
            pending.Add(new GitHubReadinessPendingReviewer(actor, "eyes-reaction", at));
        }
        return pending.OrderBy(item => item.Actor, StringComparer.OrdinalIgnoreCase).ThenBy(item => item.Source, StringComparer.Ordinal).ToArray();
    }

    private static string ReadinessActor(JsonElement node, string field) =>
        NormalizeOptional(ReadStringFromNested(node, field, "login")) ?? "(unavailable)";
}
