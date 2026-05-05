using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using IntelligenceX.GitHub;

namespace IntelligenceX.Reviewer;

internal sealed partial class GitHubClient {
    public async Task<ReviewCheckSnapshot> GetCheckSnapshotAsync(string owner, string repo, string? headSha,
        CancellationToken cancellationToken) {
        if (string.IsNullOrWhiteSpace(owner) || string.IsNullOrWhiteSpace(repo) || string.IsNullOrWhiteSpace(headSha)) {
            return new ReviewCheckSnapshot(0, 0, 0, Array.Empty<ReviewCheckRun>());
        }

        var page = 1;
        var runs = new List<ReviewCheckRun>();

        while (true) {
            var shaToken = Uri.EscapeDataString(headSha);
            var url = $"/repos/{owner}/{repo}/commits/{shaToken}/check-runs?per_page=100&page={page}";
            var json = await GetJsonAsync(url, cancellationToken).ConfigureAwait(false);
            var pageRuns = GitHubCiSignals.ParseCheckRuns(json.AsObject());
            if (pageRuns.Count == 0) {
                break;
            }

            runs.AddRange(pageRuns.Select(item => new ReviewCheckRun(item.Name, item.Status, item.Conclusion, item.DetailsUrl)));

            if (pageRuns.Count < 100) {
                break;
            }
            page++;
        }

        runs.AddRange(await GetCommitStatusRunsAsync(owner, repo, headSha, cancellationToken).ConfigureAwait(false));
        return new ReviewCheckSnapshot(runs);
    }

    public async Task<IReadOnlyList<ReviewWorkflowRun>> GetFailedWorkflowRunsAsync(string owner, string repo, string? headSha,
        int maxResults, CancellationToken cancellationToken) {
        if (string.IsNullOrWhiteSpace(owner) || string.IsNullOrWhiteSpace(repo) || string.IsNullOrWhiteSpace(headSha) || maxResults <= 0) {
            return Array.Empty<ReviewWorkflowRun>();
        }

        var endpoint = $"/repos/{owner}/{repo}/actions/runs?head_sha={Uri.EscapeDataString(headSha)}&per_page=100";
        var json = await GetJsonAsync(endpoint, cancellationToken).ConfigureAwait(false);
        return GitHubCiSignals.ParseFailedWorkflowRuns(json.AsObject(), headSha, maxResults)
            .Select(item => new ReviewWorkflowRun(item.RunId, item.Name, item.Status, item.Conclusion, item.Url))
            .ToList();
    }

    public async Task<GitHubWorkflowFailureEvidence?> GetWorkflowFailureEvidenceAsync(string owner, string repo, string? runId,
        int maxChars, CancellationToken cancellationToken) {
        if (string.IsNullOrWhiteSpace(owner) || string.IsNullOrWhiteSpace(repo) || string.IsNullOrWhiteSpace(runId) || maxChars <= 0) {
            return null;
        }

        var page = 1;
        var jobs = new List<GitHubWorkflowJobInfo>();
        while (true) {
            var endpoint = $"/repos/{owner}/{repo}/actions/runs/{Uri.EscapeDataString(runId)}/jobs?per_page=100&page={page}";
            var json = await GetJsonAsync(endpoint, cancellationToken).ConfigureAwait(false);
            var pageJobs = GitHubCiSignals.ParseWorkflowJobs(json.AsObject());
            if (pageJobs.Count == 0) {
                break;
            }

            jobs.AddRange(pageJobs);
            if (pageJobs.Count < 100) {
                break;
            }

            page++;
        }

        var evidence = GitHubCiSignals.SummarizeWorkflowFailureEvidence(jobs, maxChars);
        return evidence.HasData ? evidence : null;
    }

    private async Task<IReadOnlyList<ReviewCheckRun>> GetCommitStatusRunsAsync(string owner, string repo, string headSha,
        CancellationToken cancellationToken) {
        var shaToken = Uri.EscapeDataString(headSha);
        try {
            var json = await GetJsonAsync($"/repos/{owner}/{repo}/commits/{shaToken}/status", cancellationToken)
                .ConfigureAwait(false);
            return ParseCommitStatusRuns(json.AsObject());
        } catch (InvalidOperationException ex) when (ex.Message.Contains("404 Not Found", StringComparison.OrdinalIgnoreCase)) {
            return Array.Empty<ReviewCheckRun>();
        }
    }

    internal static IReadOnlyList<ReviewCheckRun> ParseCommitStatusRunsForTests(IntelligenceX.Json.JsonObject? root) =>
        ParseCommitStatusRuns(root);

    private static IReadOnlyList<ReviewCheckRun> ParseCommitStatusRuns(IntelligenceX.Json.JsonObject? root) {
        var statuses = root?.GetArray("statuses");
        if (statuses is null || statuses.Count == 0) {
            return Array.Empty<ReviewCheckRun>();
        }

        var runsByContext = new Dictionary<string, CommitStatusRunCandidate>(StringComparer.OrdinalIgnoreCase);
        var fallbackOrder = 0;
        foreach (var item in statuses) {
            var obj = item.AsObject();
            if (obj is null) {
                continue;
            }

            var context = obj.GetString("context") ?? obj.GetString("description") ?? "legacy-status";
            var state = obj.GetString("state") ?? string.Empty;
            var (status, conclusion) = MapCommitStatusState(state);
            var candidate = new CommitStatusRunCandidate(
                new ReviewCheckRun($"status: {context}", status, conclusion, obj.GetString("target_url")),
                ParseGitHubDateTime(obj.GetString("updated_at")) ?? ParseGitHubDateTime(obj.GetString("created_at")),
                fallbackOrder++);

            if (!runsByContext.TryGetValue(context, out var existing) || IsNewerStatusCandidate(candidate, existing)) {
                runsByContext[context] = candidate;
            }
        }

        return runsByContext.Values
            .OrderBy(item => item.FallbackOrder)
            .Select(item => item.Run)
            .ToList();
    }

    private static bool IsNewerStatusCandidate(CommitStatusRunCandidate candidate, CommitStatusRunCandidate existing) {
        if (candidate.Timestamp is not null && existing.Timestamp is not null) {
            return candidate.Timestamp > existing.Timestamp;
        }

        if (candidate.Timestamp is not null) {
            return true;
        }

        return false;
    }

    private static (string Status, string? Conclusion) MapCommitStatusState(string state) {
        if (state.Equals("success", StringComparison.OrdinalIgnoreCase)) {
            return ("completed", "success");
        }
        if (state.Equals("failure", StringComparison.OrdinalIgnoreCase) ||
            state.Equals("error", StringComparison.OrdinalIgnoreCase)) {
            return ("completed", "failure");
        }

        // Fail closed for future or malformed legacy states: auto-approval must wait instead of treating them as passed.
        return ("pending", null);
    }

    private readonly record struct CommitStatusRunCandidate(ReviewCheckRun Run, DateTimeOffset? Timestamp,
        int FallbackOrder);
}
