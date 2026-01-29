using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using IntelligenceX.Json;

namespace IntelligenceX.Reviewer;

internal sealed class GitHubClient : IDisposable {
    private readonly HttpClient _http;

    public GitHubClient(string token, string? baseUrl = null) {
        _http = new HttpClient {
            BaseAddress = new Uri(baseUrl ?? "https://api.github.com")
        };
        _http.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("IntelligenceX.Reviewer", "1.0"));
        _http.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
        _http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        _http.DefaultRequestHeaders.Add("X-GitHub-Api-Version", "2022-11-28");
    }

    public async Task<IReadOnlyList<PullRequestFile>> GetPullRequestFilesAsync(string owner, string repo, int number,
        CancellationToken cancellationToken) {
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
        return files;
    }

    public async Task<PullRequestContext> GetPullRequestAsync(string owner, string repo, int number,
        CancellationToken cancellationToken) {
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
        var headSha = obj.GetObject("head")?.GetString("sha");
        var repoFullName = obj.GetObject("base")?.GetObject("repo")?.GetString("full_name")
            ?? $"{owner}/{repo}";

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

        return new PullRequestContext(repoFullName, owner, repo, prNumber, title, body, draft, headSha, labels);
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
        var commentLimit = Math.Min(Math.Max(1, maxComments), 100);
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
            nodes{
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

            var response = await PostGraphQlAsync(payload, cancellationToken).ConfigureAwait(false);
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
                var commentNodes = commentsObj?.GetArray("nodes");
                var comments = new List<PullRequestReviewThreadComment>();
                if (commentNodes is not null) {
                    foreach (var comment in commentNodes) {
                        if (comments.Count >= maxComments) {
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
                        comments.Add(new PullRequestReviewThreadComment(body, author, path, line.HasValue ? (int?)line.Value : null));
                    }
                }
                threads.Add(new PullRequestReviewThread(id, isResolved, isOutdated, comments));
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
        await PostGraphQlAsync(payload, cancellationToken).ConfigureAwait(false);
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
        var response = await PostJsonAsync($"/repos/{owner}/{repo}/issues/{number}/comments", payload, cancellationToken)
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
        await PatchJsonAsync($"/repos/{owner}/{repo}/pulls/{number}", payload, cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task UpdateIssueCommentAsync(string owner, string repo, long commentId, string body,
        CancellationToken cancellationToken) {
        var payload = new JsonObject().Add("body", body);
        await PatchJsonAsync($"/repos/{owner}/{repo}/issues/comments/{commentId}", payload, cancellationToken)
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
        var response = await PostJsonAsync($"/repos/{owner}/{repo}/pulls/{number}/comments", payload, cancellationToken)
            .ConfigureAwait(false);
        var obj = response.AsObject();
        var author = obj?.GetObject("user")?.GetString("login");
        var responsePath = obj?.GetString("path") ?? path;
        var responseLine = obj?.GetInt64("line");
        return new PullRequestReviewComment(body, author, responsePath, responseLine.HasValue ? (int?)responseLine.Value : line);
    }

    public void Dispose() => _http.Dispose();

    private async Task<JsonValue> GetJsonAsync(string url, CancellationToken cancellationToken) {
        using var response = await _http.GetAsync(url, cancellationToken).ConfigureAwait(false);
        var content = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode) {
            throw new InvalidOperationException($"GitHub API request failed ({(int)response.StatusCode}): {content}");
        }
        return JsonLite.Parse(content) ?? JsonValue.Null;
    }

    private async Task<JsonValue> PostJsonAsync(string url, JsonObject payload, CancellationToken cancellationToken) {
        var json = JsonLite.Serialize(JsonValue.From(payload));
        using var content = new StringContent(json, Encoding.UTF8, "application/json");
        using var response = await _http.PostAsync(url, content, cancellationToken).ConfigureAwait(false);
        var responseText = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode) {
            throw new InvalidOperationException($"GitHub API request failed ({(int)response.StatusCode}): {responseText}");
        }
        return JsonLite.Parse(responseText) ?? JsonValue.Null;
    }

    private async Task<JsonValue> PostGraphQlAsync(JsonObject payload, CancellationToken cancellationToken) {
        var json = JsonLite.Serialize(JsonValue.From(payload));
        using var content = new StringContent(json, Encoding.UTF8, "application/json");
        using var response = await _http.PostAsync("/graphql", content, cancellationToken).ConfigureAwait(false);
        var responseText = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode) {
            throw new InvalidOperationException($"GitHub API request failed ({(int)response.StatusCode}): {responseText}");
        }
        var parsed = JsonLite.Parse(responseText) ?? JsonValue.Null;
        var errors = parsed.AsObject()?.GetArray("errors");
        if (errors is not null && errors.Count > 0) {
            throw new InvalidOperationException($"GitHub GraphQL request returned errors: {responseText}");
        }
        return parsed;
    }

    private async Task PatchJsonAsync(string url, JsonObject payload, CancellationToken cancellationToken) {
        var json = JsonLite.Serialize(JsonValue.From(payload));
        using var content = new StringContent(json, Encoding.UTF8, "application/json");
        using var request = new HttpRequestMessage(new HttpMethod("PATCH"), url) { Content = content };
        using var response = await _http.SendAsync(request, cancellationToken).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode) {
            var responseText = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            throw new InvalidOperationException($"GitHub API request failed ({(int)response.StatusCode}): {responseText}");
        }
    }

    private static string ParseRepoFullName(string url) {
        if (string.IsNullOrWhiteSpace(url)) {
            return string.Empty;
        }
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri)) {
            return string.Empty;
        }
        var segments = uri.AbsolutePath.Trim('/').Split('/');
        if (segments.Length < 2) {
            return string.Empty;
        }
        return $"{segments[^2]}/{segments[^1]}";
    }
}
