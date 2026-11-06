using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using IntelligenceX.Telemetry.Agents;

namespace IntelligenceX.Telemetry.GitHub;

public sealed partial class GitHubDashboardService {
    private readonly SemaphoreSlim _agentWorkGate = new(1, 1);
    private readonly ConcurrentDictionary<string, AgentConditionalResponse> _agentResponses = new(StringComparer.Ordinal);
    private string? _agentWorkLogin;
#if NET8_0_OR_GREATER
    private readonly TimeProvider _agentWorkTimeProvider = TimeProvider.System;

    internal GitHubDashboardService(HttpClient client, bool disposeHttpClient, TimeProvider timeProvider)
        : this(client, disposeHttpClient) { _agentWorkTimeProvider = timeProvider; }
#endif

    private async Task<T> GetAgentJsonAsync<T>(string path, Func<JsonElement, HttpResponseMessage, T> parse, CancellationToken cancellationToken) where T : class {
        var timeout = _http.Timeout;
        if (timeout == Timeout.InfiniteTimeSpan || timeout > TimeSpan.FromSeconds(30)) timeout = TimeSpan.FromSeconds(30);
#if NET8_0_OR_GREATER
        using var deadline = new CancellationTokenSource(timeout, _agentWorkTimeProvider);
#else
        using var deadline = new CancellationTokenSource(timeout);
#endif
        using var bounded = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, deadline.Token);
        cancellationToken = bounded.Token;
        _agentResponses.TryGetValue(path, out var cached);
        using var request = new HttpRequestMessage(HttpMethod.Get, path);
        if (cached?.ETag is not null) request.Headers.TryAddWithoutValidation("If-None-Match", cached.ETag);
        // Buffering keeps the deadline active through body consumption, including a peer stalling after headers.
        using var response = await _http.SendAsync(request, HttpCompletionOption.ResponseContentRead, cancellationToken).ConfigureAwait(false);
        if (response.StatusCode == HttpStatusCode.NotModified && cached?.Value is T unchanged) return unchanged;
        if (!response.IsSuccessStatusCode) throw new HttpRequestException("GitHub work request failed with HTTP " + (int)response.StatusCode + ".");
        using var stream = await response.Content.ReadAsStreamAsync().ConfigureAwait(false);
        using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken).ConfigureAwait(false);
        var value = parse(document.RootElement, response);
        // Bound a long-running service's per-head cache. Entries hold normalized fields, not raw JSON or PR bodies.
        if (_agentResponses.Count >= 1024) _agentResponses.Clear();
        _agentResponses[path] = new AgentConditionalResponse(response.Headers.ETag?.ToString(), value);
        return value;
    }

    private AgentSearchPage ParseAgentSearch(JsonElement root, HttpResponseMessage response) {
        if (!root.TryGetProperty("items", out var items) || items.ValueKind != JsonValueKind.Array ||
            !root.TryGetProperty("total_count", out var total) || !total.TryGetInt32(out var count) || count < 0 ||
            !root.TryGetProperty("incomplete_results", out var incomplete) || incomplete.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
            throw new InvalidOperationException("GitHub returned an incomplete pull request search.");
        var paths = new List<string>();
        foreach (var item in items.EnumerateArray()) {
            var url = ReadStringFromNested(item, "pull_request", "url");
            if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || _http.BaseAddress is null
                || uri.Scheme != _http.BaseAddress.Scheme || uri.Authority != _http.BaseAddress.Authority)
                throw new InvalidOperationException("GitHub returned an invalid pull request endpoint.");
            paths.Add(uri.PathAndQuery);
        }
        return new AgentSearchPage(paths, count, incomplete.GetBoolean(), AgentHasNext(response));
    }

    private async Task<AgentPullRequestState> ReadAgentPullRequestAsync(string path, bool review, string user, CancellationToken cancellationToken) {
        var detail = await GetAgentJsonAsync(path, ParseAgentPullRequest, cancellationToken).ConfigureAwait(false);
        var pr = detail.PullRequest with { IsAuthor = string.Equals(detail.Author, user, StringComparison.OrdinalIgnoreCase), ReviewRequested = review };
        if (!pr.IsOpen || detail.Sha is null || pr.Repository is null) return pr;
        var parts = SplitRepositoryNameWithOwner(pr.Repository);
        var commitPath = "/repos/" + Uri.EscapeDataString(parts.Owner) + "/" + Uri.EscapeDataString(parts.Repository)
            + "/commits/" + Uri.EscapeDataString(detail.Sha);
        var status = await GetAgentJsonAsync(commitPath + "/status", static (json, _) => {
            var count = ReadInt(json, "total_count");
            return new AgentStatus(count == 0 ? AgentCheckState.Unknown : ReadAgentStatus(ReadString(json, "state")));
        }, cancellationToken).ConfigureAwait(false);
        var checks = AgentCheckState.Unknown;
        for (var page = 1; ; page++) {
            var runs = await GetAgentJsonAsync(commitPath + "/check-runs?per_page=100&page=" + page, ParseAgentChecks, cancellationToken).ConfigureAwait(false);
            checks = MergeAgentChecks(checks, runs.State);
            if (!runs.HasNext) break;
            if (page >= 100) throw new InvalidOperationException("GitHub check pagination exceeds 10,000 results.");
        }
        return pr with { Checks = MergeAgentChecks(status.State, checks) };
    }

    private static AgentPullRequestDetail ParseAgentPullRequest(JsonElement json, HttpResponseMessage _) {
        if (ReadString(json, "state") is not ("open" or "closed")) throw new InvalidOperationException("GitHub returned an unknown pull request state.");
        if (ReadString(json, "state") == "closed" && (!json.TryGetProperty("merged", out var merged) ||
            merged.ValueKind is not (JsonValueKind.True or JsonValueKind.False)))
            throw new InvalidOperationException("GitHub returned a closed pull request without merge state.");
        var head = AgentSessionLogReader.Object(json, "head");
        var repo = NormalizeOptional(ReadStringFromNested(AgentSessionLogReader.Object(json, "base"), "repo", "full_name"));
        var sha = NormalizeOptional(ReadString(head, "sha"));
        var url = NormalizeOptional(ReadString(json, "html_url"));
        if (url is null || repo is null || !Uri.TryCreate(url, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps)
            throw new InvalidOperationException("GitHub returned incomplete pull request metadata.");
        return new AgentPullRequestDetail(new AgentPullRequestState { Url = url, Title = ReadString(json, "title"), Repository = repo,
            HeadRepository = NormalizeOptional(ReadStringFromNested(head, "repo", "full_name")), Branch = ReadString(head, "ref"),
            IsOpen = ReadString(json, "state") == "open", IsMerged = ReadBool(json, "merged"),
            ChecksUrl = sha is null ? null : new Uri(uri, "/" + repo + "/commit/" + sha + "/checks").AbsoluteUri
        }, sha, ReadStringFromNested(json, "user", "login"));
    }

    private static AgentCheckPage ParseAgentChecks(JsonElement json, HttpResponseMessage response) {
        if (!json.TryGetProperty("check_runs", out var runs) || runs.ValueKind != JsonValueKind.Array)
            throw new InvalidOperationException("GitHub returned incomplete check metadata.");
        var state = AgentCheckState.Unknown;
        foreach (var run in runs.EnumerateArray()) {
            var next = ReadString(run, "status") != "completed" ? AgentCheckState.Pending : ReadString(run, "conclusion") switch {
                "success" or "neutral" or "skipped" => AgentCheckState.Success,
                "failure" or "timed_out" or "cancelled" or "action_required" or "startup_failure" => AgentCheckState.Failure,
                _ => AgentCheckState.Unknown
            };
            state = MergeAgentChecks(state, next);
        }
        return new AgentCheckPage(state, AgentHasNext(response));
    }

    private static AgentCheckState ReadAgentStatus(string? state) => state switch {
        "success" => AgentCheckState.Success, "failure" or "error" => AgentCheckState.Failure,
        "pending" => AgentCheckState.Pending, _ => AgentCheckState.Unknown
    };
    private static AgentCheckState MergeAgentChecks(AgentCheckState left, AgentCheckState right) =>
        left == AgentCheckState.Failure || right == AgentCheckState.Failure ? AgentCheckState.Failure
        : left == AgentCheckState.Pending || right == AgentCheckState.Pending ? AgentCheckState.Pending
        : left == AgentCheckState.Success || right == AgentCheckState.Success ? AgentCheckState.Success : AgentCheckState.Unknown;
    private static bool AgentHasNext(HttpResponseMessage response) => response.Headers.TryGetValues("Link", out var links)
        && links.Any(static link => link.IndexOf("rel=\"next\"", StringComparison.OrdinalIgnoreCase) >= 0);

    private sealed record AgentConditionalResponse(string? ETag, object Value);
    private sealed record AgentSearchPage(IReadOnlyList<string> Paths, int Total, bool Incomplete, bool HasNext);
    private sealed record AgentPullRequestDetail(AgentPullRequestState PullRequest, string? Sha, string? Author);
    private sealed record AgentCheckPage(AgentCheckState State, bool HasNext);
    private sealed record AgentStatus(AgentCheckState State);
}
