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
        CancellationToken cancellationToken) {
        var comments = new List<IssueComment>();
        var page = 1;
        while (true) {
            var url = $"/repos/{owner}/{repo}/issues/{number}/comments?per_page=100&page={page}";
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
                comments.Add(new IssueComment(id, body));
            }
            if (array.Count < 100) {
                break;
            }
            page++;
        }
        return comments;
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
}
