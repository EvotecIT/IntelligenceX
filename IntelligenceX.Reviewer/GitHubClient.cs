using System;
using System.Globalization;
using System.Collections.Generic;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using IntelligenceX.Json;

namespace IntelligenceX.Reviewer;

internal sealed partial class GitHubClient : IDisposable {
    private const int DefaultMaxConcurrency = 4;
    private const int DefaultRetryAttempts = 3;
    private static readonly TimeSpan DefaultRetryBudgetWindow = TimeSpan.FromSeconds(15);
    private readonly HttpClient _http;
    private readonly Dictionary<string, PullRequestContext> _pullRequestCache = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, IReadOnlyList<PullRequestFile>> _pullRequestFilesCache = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, CompareFilesResult> _compareFilesCache = new(StringComparer.OrdinalIgnoreCase);
    private readonly SemaphoreSlim _requestGate;
    private readonly int _maxConcurrency;

    public GitHubClient(string token, string? baseUrl = null, int maxConcurrency = DefaultMaxConcurrency) {
        _maxConcurrency = Math.Max(1, maxConcurrency);
        _requestGate = new SemaphoreSlim(_maxConcurrency, _maxConcurrency);
        _http = new HttpClient {
            BaseAddress = new Uri(baseUrl ?? "https://api.github.com")
        };
        _http.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("IntelligenceX.Reviewer", "1.0"));
        _http.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
        // Bearer is compatible with GitHub App installation tokens and PATs.
        _http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        _http.DefaultRequestHeaders.Add("X-GitHub-Api-Version", "2022-11-28");
    }

    internal int MaxConcurrency => _maxConcurrency;

    public async Task<IReadOnlyList<PullRequestFile>> GetPullRequestFilesAsync(string owner, string repo, int number,
        CancellationToken cancellationToken) {
        var cacheKey = BuildPullRequestKey(owner, repo, number);
        if (_pullRequestFilesCache.TryGetValue(cacheKey, out var cached)) {
            return cached;
        }
        var files = new List<PullRequestFile>();
        var page = 1;
        while (true) {
            var url = $"/repos/{owner}/{repo}/pulls/{number}/files?per_page=100&page={page}";
            var json = await GetJsonAsync(url, cancellationToken).ConfigureAwait(false);
            var array = json.AsArray();
            if (array is null || array.Count == 0) {
                break;
            }
            foreach (var item in array) {
                var obj = item.AsObject();
                if (obj is null) {
                    continue;
                }
                var filename = obj.GetString("filename") ?? string.Empty;
                var status = obj.GetString("status") ?? string.Empty;
                var patch = obj.GetString("patch");
                files.Add(new PullRequestFile(filename, status, patch));
            }
            if (array.Count < 100) {
                break;
            }
            page++;
        }
        _pullRequestFilesCache[cacheKey] = files;
        return files;
    }

    public async Task<PullRequestContext> GetPullRequestAsync(string owner, string repo, int number,
        CancellationToken cancellationToken) {
        var cacheKey = BuildPullRequestKey(owner, repo, number);
        if (_pullRequestCache.TryGetValue(cacheKey, out var cached)) {
            return cached;
        }
        var json = await GetJsonAsync($"/repos/{owner}/{repo}/pulls/{number}", cancellationToken)
            .ConfigureAwait(false);
        var obj = json.AsObject();
        if (obj is null) {
            throw new InvalidOperationException("Invalid pull request response.");
        }

        var title = obj.GetString("title") ?? string.Empty;
        var body = obj.GetString("body");
        var draft = obj.GetBoolean("draft");
        var prNumber = (int)(obj.GetInt64("number") ?? number);
        var head = obj.GetObject("head");
        var baseRef = obj.GetObject("base");
        var headSha = head?.GetString("sha");
        var baseSha = baseRef?.GetString("sha");
        var repoFullName = baseRef?.GetObject("repo")?.GetString("full_name")
            ?? $"{owner}/{repo}";
        var headRepo = head?.GetObject("repo");
        var headRepoFullName = headRepo?.GetString("full_name");
        var isFork = headRepo?.GetBoolean("fork") ?? false;
        var authorAssociation = obj.GetString("author_association");

        var labels = new List<string>();
        var labelsArray = obj.GetArray("labels");
        if (labelsArray is not null) {
            foreach (var item in labelsArray) {
                var labelObj = item.AsObject();
                var name = labelObj?.GetString("name");
                if (!string.IsNullOrWhiteSpace(name)) {
                    labels.Add(name);
                }
            }
        }

        var context = new PullRequestContext(repoFullName, owner, repo, prNumber, title, body, draft, headSha, baseSha,
            labels, headRepoFullName, isFork, authorAssociation);
        _pullRequestCache[cacheKey] = context;
        return context;
    }

    public async Task<CompareFilesResult> GetCompareFilesAsync(string owner, string repo, string baseSha, string headSha,
        CancellationToken cancellationToken) {
        if (string.IsNullOrWhiteSpace(baseSha) || string.IsNullOrWhiteSpace(headSha)) {
            return new CompareFilesResult(Array.Empty<PullRequestFile>(), false);
        }
        if (string.Equals(baseSha, headSha, StringComparison.OrdinalIgnoreCase)) {
            return new CompareFilesResult(Array.Empty<PullRequestFile>(), false);
        }
        var compareKey = BuildCompareKey(owner, repo, baseSha, headSha);
        if (_compareFilesCache.TryGetValue(compareKey, out var cached)) {
            return cached;
        }

        var files = new List<PullRequestFile>();
        var seenFiles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var page = 1;
        var truncated = false;
        const int MaxPages = 20;
        while (true) {
            var baseToken = Uri.EscapeDataString(baseSha);
            var headToken = Uri.EscapeDataString(headSha);
            var url = $"/repos/{owner}/{repo}/compare/{baseToken}...{headToken}?per_page=100&page={page}";
            var json = await GetJsonAsync(url, cancellationToken).ConfigureAwait(false);
            var obj = json.AsObject();
            var array = obj?.GetArray("files");
            if (array is null || array.Count == 0) {
                break;
            }
            var added = 0;
            foreach (var item in array) {
                var fileObj = item.AsObject();
                if (fileObj is null) {
                    continue;
                }
                var filename = fileObj.GetString("filename") ?? string.Empty;
                var status = fileObj.GetString("status") ?? string.Empty;
                var patch = fileObj.GetString("patch");
                if (string.IsNullOrWhiteSpace(filename) || !seenFiles.Add(filename)) {
                    continue;
                }
                files.Add(new PullRequestFile(filename, status, patch));
                added++;
            }
            if (array.Count < 100 || added == 0) {
                break;
            }
            if (page >= MaxPages) {
                truncated = true;
                break;
            }
            page++;
        }

        if (truncated) {
            Console.Error.WriteLine("Compare API results truncated after 2000 files. Consider using pull request files endpoint for full coverage.");
        }
        var result = new CompareFilesResult(files, truncated);
        _compareFilesCache[compareKey] = result;
        return result;
    }

    public async Task<IReadOnlyList<IssueComment>> ListIssueCommentsAsync(string owner, string repo, int number,
        int maxResults, CancellationToken cancellationToken) {
        if (maxResults <= 0) {
            return Array.Empty<IssueComment>();
        }
        var comments = new List<IssueComment>();
        var page = 1;
        while (true) {
            var url = $"/repos/{owner}/{repo}/issues/{number}/comments?per_page=100&page={page}&sort=created&direction=desc";
            var json = await GetJsonAsync(url, cancellationToken).ConfigureAwait(false);
            var array = json.AsArray();
            if (array is null || array.Count == 0) {
                break;
            }
            foreach (var item in array) {
                var obj = item.AsObject();
                if (obj is null) {
                    continue;
                }
                var id = obj.GetInt64("id") ?? 0;
                var body = obj.GetString("body") ?? string.Empty;
                var author = obj.GetObject("user")?.GetString("login");
                comments.Add(new IssueComment(id, body, author));
                if (maxResults > 0 && comments.Count >= maxResults) {
                    break;
                }
            }
            if (array.Count < 100 || (maxResults > 0 && comments.Count >= maxResults)) {
                break;
            }
            page++;
        }
        return comments;
    }

    public async Task<IReadOnlyList<PullRequestReviewComment>> ListPullRequestReviewCommentsAsync(string owner, string repo, int number,
        int maxResults, CancellationToken cancellationToken) {
        if (maxResults <= 0) {
            return Array.Empty<PullRequestReviewComment>();
        }
        var comments = new List<PullRequestReviewComment>();
        var page = 1;
        while (true) {
            var url = $"/repos/{owner}/{repo}/pulls/{number}/comments?per_page=100&page={page}&sort=created&direction=desc";
            var json = await GetJsonAsync(url, cancellationToken).ConfigureAwait(false);
            var array = json.AsArray();
            if (array is null || array.Count == 0) {
                break;
            }
            foreach (var item in array) {
                var obj = item.AsObject();
                if (obj is null) {
                    continue;
                }
                var body = obj.GetString("body") ?? string.Empty;
                var author = obj.GetObject("user")?.GetString("login");
                var path = obj.GetString("path");
                var line = obj.GetInt64("line");
                comments.Add(new PullRequestReviewComment(body, author, path, line.HasValue ? (int?)line.Value : null));
                if (maxResults > 0 && comments.Count >= maxResults) {
                    break;
                }
            }
            if (array.Count < 100 || (maxResults > 0 && comments.Count >= maxResults)) {
                break;
            }
            page++;
        }
        return comments;
    }

    public async Task<IReadOnlyList<PullRequestReviewThread>> ListPullRequestReviewThreadsAsync(string owner, string repo, int number,
        int maxThreads, int maxComments, CancellationToken cancellationToken) {
        if (maxThreads <= 0 || maxComments <= 0) {
            return Array.Empty<PullRequestReviewThread>();
        }

        var threads = new List<PullRequestReviewThread>();
        var commentLimit = Math.Min(maxComments, 100);
        string? cursor = null;
        while (threads.Count < maxThreads) {
            var payload = new JsonObject()
                .Add("query", @"query($owner:String!,$name:String!,$number:Int!,$cursor:String,$commentLimit:Int!){
  repository(owner:$owner,name:$name){
    pullRequest(number:$number){
      reviewThreads(first:50, after:$cursor){
        nodes{
          id
          isResolved
          isOutdated
          comments(first:$commentLimit){
            totalCount
            nodes{
              databaseId
              createdAt
              body
              path
              line
              author{ login }
            }
          }
        }
        pageInfo{ hasNextPage endCursor }
      }
    }
  }
}")
                .Add("variables", new JsonObject()
                    .Add("owner", owner)
                    .Add("name", repo)
                    .Add("number", number)
                    .Add("cursor", cursor)
                    .Add("commentLimit", commentLimit));

            var response = await PostGraphQlAsync(payload, cancellationToken, allowRetries: true).ConfigureAwait(false);
            var root = response.AsObject();
            var data = root?.GetObject("data");
            var repoObj = data?.GetObject("repository");
            var prObj = repoObj?.GetObject("pullRequest");
            var threadsObj = prObj?.GetObject("reviewThreads");
            var nodes = threadsObj?.GetArray("nodes");
            if (nodes is null || nodes.Count == 0) {
                break;
            }

            foreach (var node in nodes) {
                if (threads.Count >= maxThreads) {
                    break;
                }
                var obj = node.AsObject();
                if (obj is null) {
                    continue;
                }
                var id = obj.GetString("id") ?? string.Empty;
                var isResolved = obj.GetBoolean("isResolved");
                var isOutdated = obj.GetBoolean("isOutdated");
                var commentsObj = obj.GetObject("comments");
                var totalComments = (int)(commentsObj?.GetInt64("totalCount") ?? 0);
                var commentNodes = commentsObj?.GetArray("nodes");
                var comments = new List<PullRequestReviewThreadComment>();
                if (commentNodes is not null) {
                    foreach (var comment in commentNodes) {
                        if (comments.Count >= commentLimit) {
                            break;
                        }
                        var commentObj = comment.AsObject();
                        if (commentObj is null) {
                            continue;
                        }
                        var body = commentObj.GetString("body") ?? string.Empty;
                        var author = commentObj.GetObject("author")?.GetString("login");
                        var path = commentObj.GetString("path");
                        var line = commentObj.GetInt64("line");
                        var databaseId = commentObj.GetInt64("databaseId");
                        var createdAtRaw = commentObj.GetString("createdAt");
                        DateTimeOffset? createdAt = null;
                        if (!string.IsNullOrWhiteSpace(createdAtRaw) &&
                            DateTimeOffset.TryParse(createdAtRaw, CultureInfo.InvariantCulture,
                                DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var parsed)) {
                            createdAt = parsed;
                        }
                        comments.Add(new PullRequestReviewThreadComment(databaseId, createdAt, body, author, path,
                            line.HasValue ? (int?)line.Value : null));
                    }
                }
                threads.Add(new PullRequestReviewThread(id, isResolved, isOutdated, totalComments, comments));
            }

            var pageInfo = threadsObj?.GetObject("pageInfo");
            var hasNext = pageInfo?.GetBoolean("hasNextPage") ?? false;
            if (!hasNext) {
                break;
            }
            cursor = pageInfo?.GetString("endCursor");
            if (string.IsNullOrWhiteSpace(cursor)) {
                break;
            }
        }

        return threads;
    }

    public async Task ResolveReviewThreadAsync(string threadId, CancellationToken cancellationToken) {
        if (string.IsNullOrWhiteSpace(threadId)) {
            return;
        }
        var payload = new JsonObject()
            .Add("query", @"mutation($id:ID!){
  resolveReviewThread(input:{threadId:$id}){
    thread{ id isResolved }
  }
}")
            .Add("variables", new JsonObject().Add("id", threadId));
        // Mutations have side effects; do not retry under transport uncertainty.
        await PostGraphQlAsync(payload, cancellationToken, allowRetries: false).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<RelatedPullRequest>> SearchPullRequestsAsync(string query, int maxResults,
        CancellationToken cancellationToken) {
        if (string.IsNullOrWhiteSpace(query) || maxResults <= 0) {
            return Array.Empty<RelatedPullRequest>();
        }
        var results = new List<RelatedPullRequest>();
        var page = 1;
        while (results.Count < maxResults) {
            var url = $"/search/issues?q={Uri.EscapeDataString(query)}&per_page=100&page={page}";
            var json = await GetJsonAsync(url, cancellationToken).ConfigureAwait(false);
            var root = json.AsObject();
            var items = root?.GetArray("items");
            if (items is null || items.Count == 0) {
                break;
            }
            foreach (var item in items) {
                var obj = item.AsObject();
                if (obj is null || obj.GetObject("pull_request") is null) {
                    continue;
                }
                var title = obj.GetString("title") ?? string.Empty;
                var urlValue = obj.GetString("html_url") ?? string.Empty;
                var number = obj.GetInt64("number") ?? 0;
                var repoUrl = obj.GetString("repository_url") ?? string.Empty;
                var repoFullName = ParseRepoFullName(repoUrl);
                if (string.IsNullOrWhiteSpace(urlValue) || string.IsNullOrWhiteSpace(repoFullName) || number <= 0) {
                    continue;
                }
                results.Add(new RelatedPullRequest(title, urlValue, repoFullName, (int)number));
                if (results.Count >= maxResults) {
                    break;
                }
            }
            if (items.Count < 100) {
                break;
            }
            page++;
        }
        return results;
    }

    public async Task<IssueComment> CreateIssueCommentAsync(string owner, string repo, int number, string body,
        CancellationToken cancellationToken) {
        var payload = new JsonObject().Add("body", body);
        // Non-idempotent write: do not retry (avoid duplicate comments).
        var response = await PostJsonAsync($"/repos/{owner}/{repo}/issues/{number}/comments", payload, cancellationToken,
                allowRetries: false)
            .ConfigureAwait(false);
        var obj = response.AsObject();
        var id = obj?.GetInt64("id") ?? 0;
        return new IssueComment(id, body);
    }

    public async Task UpdatePullRequestAsync(string owner, string repo, int number, string title, string body,
        CancellationToken cancellationToken) {
        var payload = new JsonObject()
            .Add("title", title)
            .Add("body", body);
        // Even if the patch is conceptually idempotent, retrying PATCH under transport uncertainty can duplicate effects.
        await PatchJsonAsync($"/repos/{owner}/{repo}/pulls/{number}", payload, cancellationToken, allowRetries: false)
            .ConfigureAwait(false);
    }

    public async Task UpdateIssueCommentAsync(string owner, string repo, long commentId, string body,
        CancellationToken cancellationToken) {
        var payload = new JsonObject().Add("body", body);
        // Even if the patch is conceptually idempotent, retrying PATCH under transport uncertainty can duplicate effects.
        await PatchJsonAsync($"/repos/{owner}/{repo}/issues/comments/{commentId}", payload, cancellationToken, allowRetries: false)
            .ConfigureAwait(false);
    }

    public async Task<PullRequestReviewComment> CreatePullRequestReviewCommentAsync(string owner, string repo, int number,
        string body, string commitId, string path, int line, CancellationToken cancellationToken) {
        var payload = new JsonObject()
            .Add("body", body)
            .Add("commit_id", commitId)
            .Add("path", path)
            .Add("line", line)
            .Add("side", "RIGHT");
        // Non-idempotent write: do not retry (avoid duplicate comments).
        var response = await PostJsonAsync($"/repos/{owner}/{repo}/pulls/{number}/comments", payload, cancellationToken,
                allowRetries: false)
            .ConfigureAwait(false);
        var obj = response.AsObject();
        var author = obj?.GetObject("user")?.GetString("login");
        var responsePath = obj?.GetString("path") ?? path;
        var responseLine = obj?.GetInt64("line");
        return new PullRequestReviewComment(body, author, responsePath, responseLine.HasValue ? (int?)responseLine.Value : line);
    }

    public async Task CreatePullRequestReviewCommentReplyAsync(string owner, string repo, int number, long inReplyTo,
        string body, CancellationToken cancellationToken) {
        var payload = new JsonObject()
            .Add("body", body)
            .Add("in_reply_to", inReplyTo);
        // Non-idempotent write: do not retry (avoid duplicate comments).
        await PostJsonAsync($"/repos/{owner}/{repo}/pulls/{number}/comments", payload, cancellationToken, allowRetries: false)
            .ConfigureAwait(false);
    }

    public void Dispose() {
        _http.Dispose();
        _requestGate.Dispose();
    }

    private async Task<JsonValue> GetJsonAsync(string url, CancellationToken cancellationToken) {
        return await WithGateAsync(async () => {
            var retryBudgetStart = DateTimeOffset.UtcNow;
            for (var attempt = 1; attempt <= DefaultRetryAttempts; attempt++) {
                cancellationToken.ThrowIfCancellationRequested();
                try {
                    using (var response = await _http.GetAsync(url, cancellationToken).ConfigureAwait(false)) {
                        var content = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
                        if (attempt < DefaultRetryAttempts && TryGetRetryDelay(response, content, attempt, out var delay)) {
                            if (TryScheduleRetry(retryBudgetStart, ref delay)) {
                                await Task.Delay(delay, cancellationToken).ConfigureAwait(false);
                                continue;
                            }
                            // No retry budget left: surface the current response as an error.
                        }
                        if (!response.IsSuccessStatusCode) {
                            throw new InvalidOperationException(
                                FormatApiError("GET", url, response, content));
                        }
                        return JsonLite.Parse(content) ?? JsonValue.Null;
                    }
                } catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested && attempt < DefaultRetryAttempts) {
                    // Timeout / transport cancellation (not user cancellation): retry with backoff.
                    var delay = ComputeBackoff(attempt, maxSeconds: 8);
                    if (TryScheduleRetry(retryBudgetStart, ref delay)) {
                        await Task.Delay(delay, cancellationToken).ConfigureAwait(false);
                        continue;
                    }
                    throw;
                } catch (HttpRequestException) when (attempt < DefaultRetryAttempts) {
                    // Transient transport failure: retry with backoff.
                    var delay = ComputeBackoff(attempt, maxSeconds: 8);
                    if (TryScheduleRetry(retryBudgetStart, ref delay)) {
                        await Task.Delay(delay, cancellationToken).ConfigureAwait(false);
                        continue;
                    }
                    throw;
                }
            }
            throw new InvalidOperationException($"GitHub API request failed (GET {url}) after {DefaultRetryAttempts} attempts.");
        }, cancellationToken).ConfigureAwait(false);
    }

    private Task<JsonValue> PostJsonAsync(string url, JsonObject payload, CancellationToken cancellationToken) {
        return PostJsonAsync(url, payload, cancellationToken, allowRetries: true);
    }

    private async Task<JsonValue> PostJsonAsync(string url, JsonObject payload, CancellationToken cancellationToken, bool allowRetries) {
        return await WithGateAsync(async () => {
            var json = JsonLite.Serialize(JsonValue.From(payload));
            var attempts = allowRetries ? DefaultRetryAttempts : 1;
            var retryBudgetStart = DateTimeOffset.UtcNow;
            for (var attempt = 1; attempt <= attempts; attempt++) {
                cancellationToken.ThrowIfCancellationRequested();
                try {
                    using (var content = new StringContent(json, Encoding.UTF8, "application/json"))
                    using (var response = await _http.PostAsync(url, content, cancellationToken).ConfigureAwait(false)) {
                        var responseText = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
                        if (attempt < attempts && TryGetRetryDelay(response, responseText, attempt, out var delay)) {
                            if (TryScheduleRetry(retryBudgetStart, ref delay)) {
                                await Task.Delay(delay, cancellationToken).ConfigureAwait(false);
                                continue;
                            }
                            // No retry budget left: surface the current response as an error.
                        }
                        if (!response.IsSuccessStatusCode) {
                            throw new InvalidOperationException(
                                FormatApiError("POST", url, response, responseText));
                        }
                        return JsonLite.Parse(responseText) ?? JsonValue.Null;
                    }
                } catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested && attempt < attempts) {
                    var delay = ComputeBackoff(attempt, maxSeconds: 8);
                    if (TryScheduleRetry(retryBudgetStart, ref delay)) {
                        await Task.Delay(delay, cancellationToken).ConfigureAwait(false);
                        continue;
                    }
                    throw;
                } catch (HttpRequestException) when (attempt < attempts) {
                    var delay = ComputeBackoff(attempt, maxSeconds: 8);
                    if (TryScheduleRetry(retryBudgetStart, ref delay)) {
                        await Task.Delay(delay, cancellationToken).ConfigureAwait(false);
                        continue;
                    }
                    throw;
                }
            }
            throw new InvalidOperationException($"GitHub API request failed (POST {url}) after {attempts} attempts.");
        }, cancellationToken).ConfigureAwait(false);
    }

    private Task<JsonValue> PostGraphQlAsync(JsonObject payload, CancellationToken cancellationToken) {
        return PostGraphQlAsync(payload, cancellationToken, allowRetries: true);
    }

    private async Task<JsonValue> PostGraphQlAsync(JsonObject payload, CancellationToken cancellationToken, bool allowRetries) {
        return await WithGateAsync(async () => {
            var json = JsonLite.Serialize(JsonValue.From(payload));
            const string url = "/graphql";
            var attempts = allowRetries ? DefaultRetryAttempts : 1;
            var retryBudgetStart = DateTimeOffset.UtcNow;
            for (var attempt = 1; attempt <= attempts; attempt++) {
                cancellationToken.ThrowIfCancellationRequested();
                try {
                    using (var content = new StringContent(json, Encoding.UTF8, "application/json"))
                    using (var response = await _http.PostAsync(url, content, cancellationToken).ConfigureAwait(false)) {
                        var responseText = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
                        if (attempt < attempts && TryGetRetryDelay(response, responseText, attempt, out var delay)) {
                            if (TryScheduleRetry(retryBudgetStart, ref delay)) {
                                await Task.Delay(delay, cancellationToken).ConfigureAwait(false);
                                continue;
                            }
                            // No retry budget left: surface the current response as an error.
                        }
                        if (!response.IsSuccessStatusCode) {
                            throw new InvalidOperationException(
                                FormatApiError("POST", url, response, responseText));
                        }
                        var parsed = JsonLite.Parse(responseText) ?? JsonValue.Null;
                        var errors = parsed.AsObject()?.GetArray("errors");
                        if (errors is not null && errors.Count > 0) {
                            throw new InvalidOperationException($"GitHub GraphQL request returned errors: {Truncate(responseText)}");
                        }
                        return parsed;
                    }
                } catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested && attempt < attempts) {
                    var delay = ComputeBackoff(attempt, maxSeconds: 8);
                    if (TryScheduleRetry(retryBudgetStart, ref delay)) {
                        await Task.Delay(delay, cancellationToken).ConfigureAwait(false);
                        continue;
                    }
                    throw;
                } catch (HttpRequestException) when (attempt < attempts) {
                    var delay = ComputeBackoff(attempt, maxSeconds: 8);
                    if (TryScheduleRetry(retryBudgetStart, ref delay)) {
                        await Task.Delay(delay, cancellationToken).ConfigureAwait(false);
                        continue;
                    }
                    throw;
                }
            }
            throw new InvalidOperationException($"GitHub API request failed (POST {url}) after {attempts} attempts.");
        }, cancellationToken).ConfigureAwait(false);
    }

    private Task PatchJsonAsync(string url, JsonObject payload, CancellationToken cancellationToken) {
        return PatchJsonAsync(url, payload, cancellationToken, allowRetries: false);
    }

    private async Task PatchJsonAsync(string url, JsonObject payload, CancellationToken cancellationToken, bool allowRetries) {
        await WithGateAsync(async () => {
            var json = JsonLite.Serialize(JsonValue.From(payload));
            var attempts = allowRetries ? DefaultRetryAttempts : 1;
            var retryBudgetStart = DateTimeOffset.UtcNow;
            for (var attempt = 1; attempt <= attempts; attempt++) {
                cancellationToken.ThrowIfCancellationRequested();
                try {
                    using (var content = new StringContent(json, Encoding.UTF8, "application/json"))
                    using (var request = new HttpRequestMessage(new HttpMethod("PATCH"), url) { Content = content })
                    using (var response = await _http.SendAsync(request, cancellationToken).ConfigureAwait(false)) {
                        var responseText = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
                        if (attempt < attempts && TryGetRetryDelay(response, responseText, attempt, out var delay)) {
                            if (TryScheduleRetry(retryBudgetStart, ref delay)) {
                                await Task.Delay(delay, cancellationToken).ConfigureAwait(false);
                                continue;
                            }
                            // No retry budget left: surface the current response as an error.
                        }
                        if (!response.IsSuccessStatusCode) {
                            throw new InvalidOperationException(
                                FormatApiError("PATCH", url, response, responseText));
                        }
                        break;
                    }
                } catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested && attempt < attempts) {
                    var delay = ComputeBackoff(attempt, maxSeconds: 8);
                    if (TryScheduleRetry(retryBudgetStart, ref delay)) {
                        await Task.Delay(delay, cancellationToken).ConfigureAwait(false);
                        continue;
                    }
                    throw;
                } catch (HttpRequestException) when (attempt < attempts) {
                    var delay = ComputeBackoff(attempt, maxSeconds: 8);
                    if (TryScheduleRetry(retryBudgetStart, ref delay)) {
                        await Task.Delay(delay, cancellationToken).ConfigureAwait(false);
                        continue;
                    }
                    throw;
                }
            }
            return 0;
        }, cancellationToken).ConfigureAwait(false);
    }

}
