using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using IntelligenceX.Json;

namespace IntelligenceX.Reviewer;

/// <summary>
/// Lightweight Azure DevOps REST API client for pull request review operations.
/// </summary>
internal sealed class AzureDevOpsClient : IDisposable {
    private const string ApiVersion = "7.1";
    private const string DefaultChangeType = "edit";
    private const int RootCommentId = 0;
    private const int CommentTypeText = 1;
    private const int ThreadStatusActive = 1;
    private readonly HttpClient _http;

    /// <summary>
    /// Initializes a new Azure DevOps client for the specified base URL and auth scheme.
    /// </summary>
    /// <param name="baseUri">Azure DevOps organization base URI.</param>
    /// <param name="token">Authentication token (PAT or bearer).</param>
    /// <param name="authScheme">Authentication scheme to use.</param>
    public AzureDevOpsClient(Uri baseUri, string token, AzureDevOpsAuthScheme authScheme) {
        _http = new HttpClient { BaseAddress = EnsureTrailingSlash(baseUri) };
        _http.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("IntelligenceX.Reviewer", "1.0"));
        _http.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        _http.DefaultRequestHeaders.Authorization = BuildAuthHeader(token, authScheme);
    }

    /// <summary>
    /// Retrieves pull request metadata.
    /// </summary>
    public async Task<AzureDevOpsPullRequest> GetPullRequestAsync(string project, int pullRequestId, CancellationToken cancellationToken) {
        var url = $"{Escape(project)}/_apis/git/pullrequests/{pullRequestId}?api-version={ApiVersion}";
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

    /// <summary>
    /// Returns the most recent pull request iteration id.
    /// </summary>
    public async Task<int?> GetLatestIterationIdAsync(string project, string repositoryId, int pullRequestId,
        CancellationToken cancellationToken) {
        var url = $"{Escape(project)}/_apis/git/repositories/{Escape(repositoryId)}/pullRequests/{pullRequestId}/iterations?api-version={ApiVersion}";
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
            if (id.Value > int.MaxValue) {
                continue;
            }
            var value = (int)id.Value;
            max = !max.HasValue || value > max ? value : max;
        }
        return max;
    }

    /// <summary>
    /// Retrieves the file changes for a specific pull request iteration.
    /// </summary>
    public async Task<IReadOnlyList<PullRequestFile>> GetPullRequestChangesAsync(string project, string repositoryId,
        int pullRequestId, int iterationId, CancellationToken cancellationToken) {
        var url = $"{Escape(project)}/_apis/git/repositories/{Escape(repositoryId)}/pullRequests/{pullRequestId}/iterations/{iterationId}/changes?api-version={ApiVersion}";
        var json = await GetJsonAsync(url, cancellationToken).ConfigureAwait(false);
        var obj = json.AsObject();
        // ADO APIs have returned "changeEntries" and "changes" across versions.
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
            var changeType = change.GetString("changeType") ?? DefaultChangeType;
            files.Add(new PullRequestFile(normalized, changeType, null));
        }
        return files;
    }

    /// <summary>
    /// Creates a new pull request discussion thread with a single comment.
    /// </summary>
    public async Task CreatePullRequestThreadAsync(string project, string repositoryId, int pullRequestId, string content,
        CancellationToken cancellationToken) {
        var url = $"{Escape(project)}/_apis/git/repositories/{Escape(repositoryId)}/pullRequests/{pullRequestId}/threads?api-version={ApiVersion}";
        var payload = new JsonObject()
            .Add("comments", new JsonArray().Add(new JsonObject()
                .Add("parentCommentId", RootCommentId)
                .Add("content", content)
                .Add("commentType", CommentTypeText)))
            .Add("status", ThreadStatusActive);
        await PostJsonAsync(url, payload, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Disposes of the underlying HTTP client.
    /// </summary>
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
