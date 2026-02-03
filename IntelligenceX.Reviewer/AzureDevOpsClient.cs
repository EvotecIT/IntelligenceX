using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using IntelligenceX.Json;

namespace IntelligenceX.Reviewer;

internal sealed class AzureDevOpsClient : IDisposable {
    private readonly HttpClient _http;

    public AzureDevOpsClient(Uri baseUri, string token, AzureDevOpsAuthScheme authScheme) {
        _http = new HttpClient { BaseAddress = EnsureTrailingSlash(baseUri) };
        _http.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("IntelligenceX.Reviewer", "1.0"));
        _http.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        _http.DefaultRequestHeaders.Authorization = BuildAuthHeader(token, authScheme);
    }

    public async Task<AzureDevOpsPullRequest> GetPullRequestAsync(string project, int pullRequestId, CancellationToken cancellationToken) {
        var url = $"{Escape(project)}/_apis/git/pullrequests/{pullRequestId}?api-version=7.1";
        var json = await GetJsonAsync(url, cancellationToken).ConfigureAwait(false);
        var obj = json.AsObject();
        if (obj is null) {
            throw new InvalidOperationException("Invalid Azure DevOps pull request response.");
        }

        var title = obj.GetString("title") ?? string.Empty;
        var description = obj.GetString("description");
        var isDraft = obj.GetBoolean("isDraft");
        var repository = obj.GetObject("repository");
        var repoId = repository?.GetString("id") ?? string.Empty;
        var repoName = repository?.GetString("name") ?? string.Empty;
        var projectName = repository?.GetObject("project")?.GetString("name") ?? project;
        var sourceCommit = obj.GetObject("lastMergeSourceCommit")?.GetString("commitId");
        var targetCommit = obj.GetObject("lastMergeTargetCommit")?.GetString("commitId");

        return new AzureDevOpsPullRequest(pullRequestId, title, description, isDraft, repoId, repoName, projectName,
            sourceCommit, targetCommit);
    }

    public async Task<int?> GetLatestIterationIdAsync(string project, string repositoryId, int pullRequestId,
        CancellationToken cancellationToken) {
        var url = $"{Escape(project)}/_apis/git/repositories/{Escape(repositoryId)}/pullRequests/{pullRequestId}/iterations?api-version=7.1";
        var json = await GetJsonAsync(url, cancellationToken).ConfigureAwait(false);
        var array = json.AsObject()?.GetArray("value");
        if (array is null || array.Count == 0) {
            return null;
        }
        int? max = null;
        foreach (var entry in array) {
            var id = entry.AsObject()?.GetInt64("id");
            if (!id.HasValue) {
                continue;
            }
            var value = (int)id.Value;
            max = !max.HasValue || value > max ? value : max;
        }
        return max;
    }

    public async Task<IReadOnlyList<PullRequestFile>> GetPullRequestChangesAsync(string project, string repositoryId,
        int pullRequestId, int iterationId, CancellationToken cancellationToken) {
        var url = $"{Escape(project)}/_apis/git/repositories/{Escape(repositoryId)}/pullRequests/{pullRequestId}/iterations/{iterationId}/changes?api-version=7.1";
        var json = await GetJsonAsync(url, cancellationToken).ConfigureAwait(false);
        var obj = json.AsObject();
        var entries = obj?.GetArray("changeEntries") ?? obj?.GetArray("changes");
        if (entries is null || entries.Count == 0) {
            return Array.Empty<PullRequestFile>();
        }

        var files = new List<PullRequestFile>();
        foreach (var entry in entries) {
            var change = entry.AsObject();
            if (change is null) {
                continue;
            }
            var item = change.GetObject("item");
            var path = item?.GetString("path");
            if (string.IsNullOrWhiteSpace(path)) {
                continue;
            }
            var normalized = path.TrimStart('/');
            var changeType = change.GetString("changeType") ?? "edit";
            files.Add(new PullRequestFile(normalized, changeType, null));
        }
        return files;
    }

    public async Task CreatePullRequestThreadAsync(string project, string repositoryId, int pullRequestId, string content,
        CancellationToken cancellationToken) {
        var url = $"{Escape(project)}/_apis/git/repositories/{Escape(repositoryId)}/pullRequests/{pullRequestId}/threads?api-version=7.1";
        var payload = new JsonObject()
            .Add("comments", new JsonArray().Add(new JsonObject()
                .Add("parentCommentId", 0)
                .Add("content", content)
                .Add("commentType", 1)))
            .Add("status", 1);
        await PostJsonAsync(url, payload, cancellationToken).ConfigureAwait(false);
    }

    public void Dispose() => _http.Dispose();

    private static AuthenticationHeaderValue BuildAuthHeader(string token, AzureDevOpsAuthScheme scheme) {
        if (scheme == AzureDevOpsAuthScheme.Basic) {
            var raw = $":{token}";
            var bytes = Encoding.UTF8.GetBytes(raw);
            var encoded = Convert.ToBase64String(bytes);
            return new AuthenticationHeaderValue("Basic", encoded);
        }
        return new AuthenticationHeaderValue("Bearer", token);
    }

    private async Task<JsonValue> GetJsonAsync(string url, CancellationToken cancellationToken) {
        using var response = await _http.GetAsync(url, cancellationToken).ConfigureAwait(false);
        var content = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode) {
            throw new InvalidOperationException($"Azure DevOps API request failed ({(int)response.StatusCode}): {content}");
        }
        return JsonLite.Parse(content) ?? JsonValue.Null;
    }

    private async Task<JsonValue> PostJsonAsync(string url, JsonObject payload, CancellationToken cancellationToken) {
        var json = JsonLite.Serialize(JsonValue.From(payload));
        using var content = new StringContent(json, Encoding.UTF8, "application/json");
        using var response = await _http.PostAsync(url, content, cancellationToken).ConfigureAwait(false);
        var responseText = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode) {
            throw new InvalidOperationException($"Azure DevOps API request failed ({(int)response.StatusCode}): {responseText}");
        }
        return JsonLite.Parse(responseText) ?? JsonValue.Null;
    }

    private static Uri EnsureTrailingSlash(Uri baseUri) {
        var text = baseUri.ToString();
        return text.EndsWith("/", StringComparison.Ordinal) ? baseUri : new Uri(text + "/", UriKind.Absolute);
    }

    private static string Escape(string value) => Uri.EscapeDataString(value ?? string.Empty);
}

internal sealed record AzureDevOpsPullRequest(int PullRequestId, string Title, string? Description, bool IsDraft,
    string RepositoryId, string RepositoryName, string Project, string? SourceCommit, string? TargetCommit);
