using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using IntelligenceX.Cli.GitHub;

namespace IntelligenceX.Cli.Todo;

internal static partial class IssueReviewRunner {
    private static async Task<List<IssueReviewCandidateIssue>> FetchOpenIssuesAsync(string repo, int maxIssues) {
        var (code, stdout, stderr) = await GhCli.RunAsync(
            TimeSpan.FromSeconds(90),
            "issue", "list",
            "--repo", repo,
            "--state", "open",
            "--limit", maxIssues.ToString(CultureInfo.InvariantCulture),
            "--json", "number,title,body,url,updatedAt,labels").ConfigureAwait(false);
        if (code != 0) {
            throw new InvalidOperationException(
                string.IsNullOrWhiteSpace(stderr)
                    ? "Failed to query open issues with gh issue list."
                    : stderr.Trim());
        }

        using var doc = JsonDocument.Parse(stdout);
        var values = new List<IssueReviewCandidateIssue>();
        if (doc.RootElement.ValueKind != JsonValueKind.Array) {
            return values;
        }

        foreach (var issue in doc.RootElement.EnumerateArray()) {
            var number = ReadInt(issue, "number");
            if (number <= 0) {
                continue;
            }
            var title = ReadString(issue, "title");
            var body = ReadString(issue, "body");
            var url = ReadString(issue, "url");
            var updatedAt = ReadDate(issue, "updatedAt");
            var labels = ReadLabels(issue);
            values.Add(new IssueReviewCandidateIssue(number, title, body, url, updatedAt, labels));
        }
        return values;
    }

    private static async Task<PullRequestReference?> TryFetchPullRequestReferenceAsync(string repo, int number) {
        var (code, stdout, stderr) = await GhCli.RunAsync(
            TimeSpan.FromSeconds(60),
            "pr", "view",
            number.ToString(CultureInfo.InvariantCulture),
            "--repo", repo,
            "--json", "number,title,url,state,mergedAt,closedAt").ConfigureAwait(false);
        if (code != 0) {
            if (!string.IsNullOrWhiteSpace(stderr) &&
                stderr.IndexOf("Not Found", StringComparison.OrdinalIgnoreCase) >= 0) {
                return null;
            }
            Console.Error.WriteLine(
                $"Warning: failed to fetch PR #{number} for {repo}: {(string.IsNullOrWhiteSpace(stderr) ? "unknown error" : stderr.Trim())}");
            return null;
        }

        using var doc = JsonDocument.Parse(stdout);
        var root = doc.RootElement;
        var parsedNumber = ReadInt(root, "number");
        if (parsedNumber <= 0) {
            return null;
        }

        var title = ReadString(root, "title");
        var url = ReadString(root, "url");
        var state = ReadString(root, "state");
        var mergedAt = ReadNullableDate(root, "mergedAt");
        var closedAt = ReadNullableDate(root, "closedAt");
        return new PullRequestReference(parsedNumber, title, url, state, mergedAt, closedAt);
    }

    internal static IReadOnlyList<int> ExtractPullRequestReferences(string repo, string text) {
        if (string.IsNullOrWhiteSpace(text)) {
            return Array.Empty<int>();
        }

        var numbers = new HashSet<int>();
        var (owner, name) = SplitRepo(repo);
        foreach (Match match in PullRequestRef.Matches(text)) {
            if (TryExtractNumber(match, "num", out var value)) {
                numbers.Add(value);
            }
        }

        foreach (Match match in RepoPullRequestRef.Matches(text)) {
            var refOwner = match.Groups["owner"].Value;
            var refName = match.Groups["repo"].Value;
            if (!refOwner.Equals(owner, StringComparison.OrdinalIgnoreCase) ||
                !refName.Equals(name, StringComparison.OrdinalIgnoreCase)) {
                continue;
            }
            if (TryExtractNumber(match, "num", out var value)) {
                numbers.Add(value);
            }
        }

        foreach (Match match in PullRequestUrlRef.Matches(text)) {
            var refOwner = match.Groups["owner"].Value;
            var refName = match.Groups["repo"].Value;
            if (!refOwner.Equals(owner, StringComparison.OrdinalIgnoreCase) ||
                !refName.Equals(name, StringComparison.OrdinalIgnoreCase)) {
                continue;
            }
            if (TryExtractNumber(match, "num", out var value)) {
                numbers.Add(value);
            }
        }

        return numbers
            .OrderBy(value => value)
            .ToList();
    }

    internal static IssueReviewAssessment AssessIssueForApplicability(
        IssueReviewCandidateIssue issue,
        string repo,
        IReadOnlyDictionary<int, PullRequestReference> pullRequestsByNumber,
        DateTimeOffset nowUtc,
        int staleDays) {
        var defaultPolicy = BuildPolicy(Array.Empty<string>(), Array.Empty<string>());
        return AssessIssueForApplicability(
            issue,
            repo,
            pullRequestsByNumber,
            nowUtc,
            staleDays,
            defaultPolicy,
            previousCandidateStreak: 0,
            minConsecutiveCandidatesForClose: 1);
    }

    internal static IssueReviewAssessment AssessIssueForApplicability(
        IssueReviewCandidateIssue issue,
        string repo,
        IReadOnlyDictionary<int, PullRequestReference> pullRequestsByNumber,
        DateTimeOffset nowUtc,
        int staleDays,
        IssueReviewPolicy policy,
        int previousCandidateStreak,
        int minConsecutiveCandidatesForClose) {
        var linkedPullRequests = ExtractPullRequestReferences(repo, $"{issue.Title}\n{issue.Body}");
        var linkedStates = new List<string>();
        foreach (var number in linkedPullRequests) {
            if (pullRequestsByNumber.TryGetValue(number, out var pullRequest)) {
                var state = NormalizePullRequestState(pullRequest);
                linkedStates.Add($"#{number}:{state}");
            } else {
                linkedStates.Add($"#{number}:unknown");
            }
        }

        var ageDays = Math.Round(Math.Max(0, (nowUtc - issue.UpdatedAtUtc).TotalDays), 1, MidpointRounding.AwayFromZero);
        var isInfra = IsInfraBlocker(issue);

        if (!isInfra) {
            return new IssueReviewAssessment(
                issue.Number,
                issue.Title,
                issue.Url,
                false,
                "out-of-scope",
                false,
                0,
                ageDays,
                linkedPullRequests,
                linkedStates,
                "Issue is not classified as infra blocker.",
                issue.Labels);
        }

        if (issue.Labels.Any(label => policy.AutoCloseDenyLabels.Contains(label))) {
            return new IssueReviewAssessment(
                issue.Number,
                issue.Title,
                issue.Url,
                true,
                "needs-review",
                false,
                0,
                ageDays,
                linkedPullRequests,
                linkedStates,
                "Denied/protected label present; leaving for maintainer review.",
                issue.Labels);
        }

        if (linkedPullRequests.Count == 0) {
            if (ageDays >= staleDays) {
                return new IssueReviewAssessment(
                    issue.Number,
                    issue.Title,
                    issue.Url,
                    true,
                    "needs-review",
                    false,
                    0,
                    ageDays,
                    linkedPullRequests,
                    linkedStates,
                    $"Infra blocker is stale ({ageDays.ToString("0.0", CultureInfo.InvariantCulture)}d) with no linked PR reference.",
                    issue.Labels);
            }

            return new IssueReviewAssessment(
                issue.Number,
                issue.Title,
                issue.Url,
                true,
                "active",
                false,
                0,
                ageDays,
                linkedPullRequests,
                linkedStates,
                "Infra blocker has no linked PR reference yet.",
                issue.Labels);
        }

        var missingReferences = linkedPullRequests
            .Where(number => !pullRequestsByNumber.ContainsKey(number))
            .ToList();
        if (missingReferences.Count > 0) {
            return new IssueReviewAssessment(
                issue.Number,
                issue.Title,
                issue.Url,
                true,
                "needs-review",
                false,
                0,
                ageDays,
                linkedPullRequests,
                linkedStates,
                $"Linked PR references could not be resolved: {string.Join(", ", missingReferences.Select(value => $"#{value}"))}.",
                issue.Labels);
        }

        var linkedReferences = linkedPullRequests
            .Select(number => pullRequestsByNumber[number])
            .ToList();
        var hasOpenPr = linkedReferences.Any(reference =>
            NormalizePullRequestState(reference).Equals("open", StringComparison.OrdinalIgnoreCase));
        if (hasOpenPr) {
            return new IssueReviewAssessment(
                issue.Number,
                issue.Title,
                issue.Url,
                true,
                "active",
                false,
                0,
                ageDays,
                linkedPullRequests,
                linkedStates,
                "At least one linked PR is still open.",
                issue.Labels);
        }

        var allResolved = linkedReferences.All(reference => {
            var state = NormalizePullRequestState(reference);
            return state is "merged" or "closed";
        });
        if (allResolved) {
            var candidateStreak = Math.Max(0, previousCandidateStreak) + 1;
            var reason = "All linked PRs are resolved (merged/closed).";
            var eligibleForAutoClose = true;
            if (policy.AutoCloseAllowLabels.Count > 0 &&
                !issue.Labels.Any(label => policy.AutoCloseAllowLabels.Contains(label))) {
                eligibleForAutoClose = false;
                reason += $" Missing allow label for auto-close ({string.Join(", ", policy.AutoCloseAllowLabels.OrderBy(value => value, StringComparer.OrdinalIgnoreCase))}).";
            }
            if (candidateStreak < minConsecutiveCandidatesForClose) {
                eligibleForAutoClose = false;
                reason += $" Candidate streak {candidateStreak}/{minConsecutiveCandidatesForClose}.";
            }

            return new IssueReviewAssessment(
                issue.Number,
                issue.Title,
                issue.Url,
                true,
                "no-longer-applicable",
                eligibleForAutoClose,
                candidateStreak,
                ageDays,
                linkedPullRequests,
                linkedStates,
                reason,
                issue.Labels);
        }

        return new IssueReviewAssessment(
            issue.Number,
            issue.Title,
            issue.Url,
            true,
            "needs-review",
            false,
            0,
            ageDays,
            linkedPullRequests,
            linkedStates,
            "Linked PR states are inconclusive.",
            issue.Labels);
    }

    private static async Task<bool> TryCloseIssueAsync(string repo, int issueNumber, string closeReason) {
        var (code, _, err) = await GhCli.RunAsync(
            TimeSpan.FromSeconds(45),
            "api",
            "--method", "PATCH",
            $"repos/{repo}/issues/{issueNumber.ToString(CultureInfo.InvariantCulture)}",
            "-f", "state=closed",
            "-f", $"state_reason={closeReason}").ConfigureAwait(false);
        if (code == 0) {
            return true;
        }

        Console.Error.WriteLine(
            $"Warning: failed to close issue #{issueNumber} ({repo}): {(string.IsNullOrWhiteSpace(err) ? "unknown error" : err.Trim())}");
        return false;
    }

    private static async Task<int?> TryFetchReopenedCountAsync(string repo, int issueNumber) {
        var (code, stdout, stderr) = await GhCli.RunAsync(
            TimeSpan.FromSeconds(45),
            "api",
            $"repos/{repo}/issues/{issueNumber.ToString(CultureInfo.InvariantCulture)}/events?per_page=100").ConfigureAwait(false);
        if (code != 0) {
            Console.Error.WriteLine(
                $"Warning: failed to query issue events for #{issueNumber}: {(string.IsNullOrWhiteSpace(stderr) ? "unknown error" : stderr.Trim())}");
            return null;
        }

        try {
            using var doc = JsonDocument.Parse(stdout);
            if (doc.RootElement.ValueKind != JsonValueKind.Array) {
                return 0;
            }

            var count = 0;
            foreach (var entry in doc.RootElement.EnumerateArray()) {
                var kind = ReadString(entry, "event");
                if (kind.Equals("reopened", StringComparison.OrdinalIgnoreCase)) {
                    count++;
                }
            }
            return count;
        } catch (Exception ex) {
            Console.Error.WriteLine($"Warning: failed to parse issue events for #{issueNumber}: {ex.Message}");
            return null;
        }
    }

    internal static IssueReviewAssessment EnrichWithActionSignals(
        IssueReviewAssessment assessment,
        IssueReviewCandidateIssue issue,
        IReadOnlyDictionary<int, PullRequestReference> pullRequestsByNumber,
        DateTimeOffset nowUtc,
        int minAutoCloseConfidence,
        int? reopenedCount) {
        var signals = new List<string>();
        var proposedAction = assessment.Classification.ToLowerInvariant() switch {
            "no-longer-applicable" when assessment.EligibleForAutoClose => "close",
            "active" => "keep-open",
            "out-of-scope" => "ignore",
            _ => "needs-human-review"
        };
        var confidence = assessment.Classification.ToLowerInvariant() switch {
            "no-longer-applicable" => 72,
            "active" => 78,
            "out-of-scope" => 82,
            _ => 58
        };

        if (assessment.AgeDays >= 30) {
            confidence += 10;
            signals.Add("stale_days_bucket:>=30d(+10)");
        } else if (assessment.AgeDays >= 14) {
            confidence += 6;
            signals.Add("stale_days_bucket:14-29d(+6)");
        } else if (assessment.AgeDays <= 2) {
            confidence -= 16;
            signals.Add("stale_days_bucket:<=2d(-16)");
        }

        if (assessment.AgeDays <= 2) {
            confidence -= 15;
            signals.Add("recent_issue_activity:high(-15)");
        } else if (assessment.AgeDays <= 7) {
            confidence -= 8;
            signals.Add("recent_issue_activity:medium(-8)");
        } else {
            confidence += 4;
            signals.Add("recent_issue_activity:low(+4)");
        }

        if (issue.Labels.Any(label => label.Equals("ix/decision:accept", StringComparison.OrdinalIgnoreCase))) {
            confidence -= 25;
            signals.Add("maintainer_decision_accept(-25)");
            proposedAction = "needs-human-review";
        }

        var linkedResolvedAges = new List<double>();
        foreach (var number in assessment.LinkedPullRequests) {
            if (!pullRequestsByNumber.TryGetValue(number, out var reference)) {
                continue;
            }
            var resolvedAt = reference.MergedAtUtc ?? reference.ClosedAtUtc;
            if (!resolvedAt.HasValue) {
                continue;
            }
            linkedResolvedAges.Add(Math.Max(0, (nowUtc - resolvedAt.Value).TotalDays));
        }
        if (linkedResolvedAges.Count > 0) {
            var minLinkedPrAge = linkedResolvedAges.Min();
            if (minLinkedPrAge < 3) {
                confidence -= 12;
                signals.Add("linked_pr_age:<3d(-12)");
            } else if (minLinkedPrAge >= 14) {
                confidence += 8;
                signals.Add("linked_pr_age:>=14d(+8)");
            } else {
                signals.Add("linked_pr_age:3-13d(+0)");
            }
        }

        var reopenSignalCount = Math.Max(0, reopenedCount ?? 0);
        if (reopenedCount.HasValue) {
            if (reopenSignalCount > 0) {
                var penalty = Math.Min(35, reopenSignalCount * 12);
                confidence -= penalty;
                signals.Add($"reopened_count:{reopenSignalCount}(-{penalty})");
            } else {
                confidence += 4;
                signals.Add("reopened_count:0(+4)");
            }
        }

        confidence = Math.Clamp(confidence, 0, 100);
        if (proposedAction.Equals("close", StringComparison.OrdinalIgnoreCase)) {
            if (reopenSignalCount > 0 || assessment.AgeDays <= 2 || confidence < minAutoCloseConfidence) {
                proposedAction = "needs-human-review";
            }
        }

        return assessment with {
            ProposedAction = proposedAction,
            ActionConfidence = confidence,
            ConfidenceSignals = signals,
            ReopenedCount = reopenSignalCount
        };
    }

    private static async Task TryCommentOnClosedIssueAsync(string repo, IssueReviewAssessment assessment, string closeReason) {
        var body = new StringBuilder();
        body.AppendLine(ManagedCommentMarker);
        body.AppendLine("Closed by IntelligenceX issue-review automation.");
        body.AppendLine();
        body.AppendLine($"- Classification: `{assessment.Classification}`");
        body.AppendLine($"- Reason: {assessment.Reason}");
        body.AppendLine($"- Proposed action: `{assessment.ProposedAction}` ({assessment.ActionConfidence}/100)");
        body.AppendLine($"- Close reason: `{closeReason}`");
        if (assessment.LinkedPullRequestStates.Count > 0) {
            body.AppendLine($"- Linked PR states: {string.Join(", ", assessment.LinkedPullRequestStates)}");
        }

        var (code, _, err) = await GhCli.RunAsync(
            TimeSpan.FromSeconds(45),
            "api",
            "--method", "POST",
            $"repos/{repo}/issues/{assessment.Number.ToString(CultureInfo.InvariantCulture)}/comments",
            "-f", $"body={body.ToString().TrimEnd()}").ConfigureAwait(false);
        if (code != 0) {
            Console.Error.WriteLine(
                $"Warning: failed to add managed close note to issue #{assessment.Number}: {(string.IsNullOrWhiteSpace(err) ? "unknown error" : err.Trim())}");
        }
    }

}
