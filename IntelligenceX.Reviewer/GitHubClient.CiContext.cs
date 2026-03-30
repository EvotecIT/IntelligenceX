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
        var checkRuns = new List<GitHubCheckRunInfo>();

        while (true) {
            var shaToken = Uri.EscapeDataString(headSha);
            var url = $"/repos/{owner}/{repo}/commits/{shaToken}/check-runs?per_page=100&page={page}";
            var json = await GetJsonAsync(url, cancellationToken).ConfigureAwait(false);
            var pageRuns = GitHubCiSignals.ParseCheckRuns(json.AsObject());
            if (pageRuns.Count == 0) {
                break;
            }

            checkRuns.AddRange(pageRuns);

            if (pageRuns.Count < 100) {
                break;
            }
            page++;
        }

        var snapshot = GitHubCiSignals.SummarizeCheckRuns(checkRuns);
        return new ReviewCheckSnapshot(
            snapshot.PassedCount,
            snapshot.FailedCount,
            snapshot.PendingCount,
            snapshot.FailedChecks
                .Select(item => new ReviewCheckRun(item.Name, item.Status, item.Conclusion, item.DetailsUrl))
                .ToList());
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
}
