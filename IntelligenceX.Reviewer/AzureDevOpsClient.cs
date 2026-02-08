using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.RegularExpressions;
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
    private static readonly SocketsHttpHandler SharedHandler = new() {
        PooledConnectionLifetime = TimeSpan.FromMinutes(10),
        PooledConnectionIdleTimeout = TimeSpan.FromMinutes(2)
    };
    private static readonly Regex SensitiveTokenPattern =
        new Regex("(?i)(authorization|token|pat)\\s*[:=]\\s*(?:bearer\\s+|basic\\s+)?[^\\s,;]+",
            RegexOptions.Compiled);
    private static readonly Regex SensitiveSchemePattern =
        new Regex("(?i)\\b(bearer|basic)\\s+[^\\s,;]+", RegexOptions.Compiled);
    private readonly HttpClient _http;

    /// <summary>
    /// Initializes a new Azure DevOps client for the specified base URL and auth scheme.
    /// </summary>
    /// <param name="baseUri">Azure DevOps organization base URI.</param>
    /// <param name="token">Authentication token (PAT or bearer).</param>
    /// <param name="authScheme">Authentication scheme to use.</param>
    public AzureDevOpsClient(Uri baseUri, string token, AzureDevOpsAuthScheme authScheme) {
        _http = new HttpClient(SharedHandler, disposeHandler: false) { BaseAddress = EnsureTrailingSlash(baseUri) };
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
        var ids = await GetIterationIdsAsync(project, repositoryId, pullRequestId, cancellationToken).ConfigureAwait(false);
        return ids.Count == 0 ? null : ids[^1];
    }

    /// <summary>
    /// Returns the list of iteration ids for a pull request (ascending order).
    /// </summary>
    public async Task<IReadOnlyList<int>> GetIterationIdsAsync(string project, string repositoryId, int pullRequestId,
        CancellationToken cancellationToken) {
        var url = $"{Escape(project)}/_apis/git/repositories/{Escape(repositoryId)}/pullRequests/{pullRequestId}/iterations?api-version={ApiVersion}";
        var json = await GetJsonAsync(url, cancellationToken).ConfigureAwait(false);
        var array = json.AsObject()?.GetArray("value");
        if (array is null || array.Count == 0) {
            return Array.Empty<int>();
        }
        var ids = new List<int>();
        foreach (var entry in array) {
            var id = entry.AsObject()?.GetInt64("id");
            if (!id.HasValue || id.Value <= 0 || id.Value > int.MaxValue) {
                continue;
            }
            ids.Add((int)id.Value);
        }
        if (ids.Count == 0) {
            return Array.Empty<int>();
        }
        ids.Sort();
        return ids;
    }

    /// <summary>
    /// Retrieves the file changes for the pull request (latest state across iterations).
    /// </summary>
    public async Task<IReadOnlyList<PullRequestFile>> GetPullRequestChangesAsync(string project, string repositoryId,
        int pullRequestId, CancellationToken cancellationToken) {
        var baseUrl = $"{Escape(project)}/_apis/git/repositories/{Escape(repositoryId)}/pullRequests/{pullRequestId}/changes?api-version={ApiVersion}";
        var files = new Dictionary<string, PullRequestFile>(StringComparer.Ordinal);
        string? continuationToken = null;
        do {
            var url = baseUrl;
            if (!string.IsNullOrWhiteSpace(continuationToken)) {
                url += "&continuationToken=" + Uri.EscapeDataString(continuationToken);
            }

            var (json, nextToken) = await GetJsonWithContinuationAsync(url, cancellationToken).ConfigureAwait(false);
            continuationToken = nextToken;
            var obj = json.AsObject();
            // ADO APIs have returned "changeEntries" and "changes" across versions.
            var entries = obj?.GetArray("changeEntries") ?? obj?.GetArray("changes");
            if (entries is null || entries.Count == 0) {
                continue;
            }

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
                files[normalized] = new PullRequestFile(normalized, changeType, null);
            }
        } while (!string.IsNullOrWhiteSpace(continuationToken));

        if (files.Count == 0) {
            return Array.Empty<PullRequestFile>();
        }
        return files.Values.OrderBy(file => file.Filename, StringComparer.Ordinal).ToList();
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
    /// Creates a new pull request discussion thread attached to a file/line position.
    /// </summary>
    public async Task CreatePullRequestInlineThreadAsync(string project, string repositoryId, int pullRequestId,
        string filePath, int lineNumber, string content, CancellationToken cancellationToken) {
        if (string.IsNullOrWhiteSpace(filePath)) {
            throw new ArgumentException("File path is required.", nameof(filePath));
        }
        if (lineNumber <= 0) {
            throw new ArgumentOutOfRangeException(nameof(lineNumber), "Line number must be positive.");
        }
        if (string.IsNullOrWhiteSpace(content)) {
            return;
        }

        var url = $"{Escape(project)}/_apis/git/repositories/{Escape(repositoryId)}/pullRequests/{pullRequestId}/threads?api-version={ApiVersion}";

        // Azure DevOps expects a leading '/' in threadContext filePath.
        var normalizedPath = filePath.Replace('\\', '/').Trim();
        normalizedPath = normalizedPath.TrimStart('/');
        if (string.IsNullOrWhiteSpace(normalizedPath)) {
            throw new ArgumentException("File path is required.", nameof(filePath));
        }
        // Avoid surprising server-side path resolution behavior.
        if (normalizedPath.Split('/').Any(segment => segment == "..")) {
            throw new ArgumentException("File path must not contain '..' segments.", nameof(filePath));
        }
        normalizedPath = "/" + normalizedPath;
        // ADO threadContext offsets are 0-based character offsets within the line; default to column 0.
        const int offset = 0;

        var payload = new JsonObject()
            .Add("comments", new JsonArray().Add(new JsonObject()
                .Add("parentCommentId", RootCommentId)
                .Add("content", content)
                .Add("commentType", CommentTypeText)))
            .Add("status", ThreadStatusActive)
            .Add("threadContext", new JsonObject()
                .Add("filePath", normalizedPath)
                .Add("rightFileStart", new JsonObject()
                    .Add("line", lineNumber)
                    .Add("offset", offset))
                .Add("rightFileEnd", new JsonObject()
                    .Add("line", lineNumber)
                    .Add("offset", offset)));

        await PostJsonAsync(url, payload, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Lists pull request threads (discussion + inline).
    /// </summary>
    public async Task<IReadOnlyList<AzureDevOpsPullRequestThread>> ListPullRequestThreadsAsync(string project,
        string repositoryId, int pullRequestId, CancellationToken cancellationToken) {
        var url = $"{Escape(project)}/_apis/git/repositories/{Escape(repositoryId)}/pullRequests/{pullRequestId}/threads?api-version={ApiVersion}";
        var json = await GetJsonAsync(url, cancellationToken).ConfigureAwait(false);
        var array = json.AsObject()?.GetArray("value");
        if (array is null || array.Count == 0) {
            return Array.Empty<AzureDevOpsPullRequestThread>();
        }

        return array
            .Select(entry => {
                var obj = entry.AsObject();
                if (obj is null) {
                    return null;
                }

                var context = obj.GetObject("threadContext");
                var filePath = context?.GetString("filePath");
                var rightStartLine = context?.GetObject("rightFileStart")?.GetInt64("line");
                var line = rightStartLine.HasValue && rightStartLine.Value > 0 && rightStartLine.Value <= int.MaxValue
                    ? (int)rightStartLine.Value
                    : (int?)null;

                var commentArray = obj.GetArray("comments");
                IReadOnlyList<string> comments = Array.Empty<string>();
                if (commentArray is not null && commentArray.Count > 0) {
                    var filtered = commentArray
                        .Select(comment => comment.AsObject()?.GetString("content"))
                        .Where(body => !string.IsNullOrWhiteSpace(body))
                        .Select(body => body!)
                        .ToArray();
                    if (filtered.Length > 0) {
                        comments = filtered;
                    }
                }

                return new AzureDevOpsPullRequestThread(filePath, line, comments);
            })
            .Where(thread => thread is not null)
            .Select(thread => thread!)
            .ToList();
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
            throw new InvalidOperationException($"Azure DevOps API request failed ({(int)response.StatusCode}): {SanitizeErrorContent(content)}");
        }
        return JsonLite.Parse(content) ?? JsonValue.Null;
    }

    private async Task<(JsonValue Json, string? ContinuationToken)> GetJsonWithContinuationAsync(string url,
        CancellationToken cancellationToken) {
        using var response = await _http.GetAsync(url, cancellationToken).ConfigureAwait(false);
        var content = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode) {
            throw new InvalidOperationException($"Azure DevOps API request failed ({(int)response.StatusCode}): {SanitizeErrorContent(content)}");
        }

        var token = TryGetContinuationToken(response);
        var json = JsonLite.Parse(content) ?? JsonValue.Null;
        return (json, token);
    }

    private static string? TryGetContinuationToken(HttpResponseMessage response) {
        if (response.Headers.TryGetValues("x-ms-continuationtoken", out var values)) {
            return values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value));
        }
        if (response.Headers.TryGetValues("x-ms-continuation-token", out var alternateValues)) {
            return alternateValues.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value));
        }
        return null;
    }

    private async Task<JsonValue> PostJsonAsync(string url, JsonObject payload, CancellationToken cancellationToken) {
        var json = JsonLite.Serialize(JsonValue.From(payload));
        using var content = new StringContent(json, Encoding.UTF8, "application/json");
        using var response = await _http.PostAsync(url, content, cancellationToken).ConfigureAwait(false);
        var responseText = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode) {
            throw new InvalidOperationException($"Azure DevOps API request failed ({(int)response.StatusCode}): {SanitizeErrorContent(responseText)}");
        }
        return JsonLite.Parse(responseText) ?? JsonValue.Null;
    }

    private static Uri EnsureTrailingSlash(Uri baseUri) {
        var text = baseUri.ToString();
        return text.EndsWith("/", StringComparison.Ordinal) ? baseUri : new Uri(text + "/", UriKind.Absolute);
    }

    private static string Escape(string value) => Uri.EscapeDataString(value ?? string.Empty);

    private static string SanitizeErrorContent(string? content) {
        if (string.IsNullOrWhiteSpace(content)) {
            return "empty response";
        }

        var text = content.Trim();
        if (TryExtractJsonMessage(text, out var message)) {
            text = message;
        }

        text = text.Replace("\r", " ").Replace("\n", " ").Trim();
        text = SensitiveTokenPattern.Replace(text, "$1: ***");
        text = SensitiveSchemePattern.Replace(text, "$1 ***");

        const int maxLength = 400;
        if (text.Length > maxLength) {
            text = text.Substring(0, maxLength) + "...";
        }

        return text;
    }

    private static bool TryExtractJsonMessage(string text, out string message) {
        message = string.Empty;
        JsonValue? value;
        try {
            value = JsonLite.Parse(text);
        } catch {
            return false;
        }

        var obj = value?.AsObject();
        if (obj is null) {
            return false;
        }

        var result = obj.GetString("message")
                     ?? obj.GetString("Message")
                     ?? obj.GetString("errorMessage")
                     ?? obj.GetString("error");

        if (string.IsNullOrWhiteSpace(result)) {
            return false;
        }

        message = result.Trim();
        return true;
    }
}

internal sealed record AzureDevOpsPullRequestThread(string? FilePath, int? Line, IReadOnlyList<string> Comments);

internal sealed record AzureDevOpsPullRequest(int PullRequestId, string Title, string? Description, bool IsDraft,
    string RepositoryId, string RepositoryName, string Project, string? SourceCommit, string? TargetCommit);
