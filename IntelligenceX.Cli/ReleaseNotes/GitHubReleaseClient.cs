using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace IntelligenceX.Cli.ReleaseNotes;

internal sealed class GitHubReleaseClient : IDisposable {
    private readonly HttpClient _http;

    public GitHubReleaseClient(string token, string apiBaseUrl = "https://api.github.com") {
        _http = new HttpClient {
            BaseAddress = new Uri(apiBaseUrl)
        };
        _http.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("IntelligenceX.Cli", "1.0"));
        _http.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
        _http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        _http.DefaultRequestHeaders.Add("X-GitHub-Api-Version", "2022-11-28");
    }

    public void Dispose() => _http.Dispose();

    public async Task<string?> GetDefaultBranchAsync(string owner, string repo) {
        using var response = await _http.GetAsync($"/repos/{owner}/{repo}").ConfigureAwait(false);
        var content = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
        if (!response.IsSuccessStatusCode) {
            return null;
        }
        using var doc = JsonDocument.Parse(content);
        return doc.RootElement.GetProperty("default_branch").GetString();
    }

    public async Task<PullRequestInfo?> CreatePullRequestAsync(string owner, string repo, string title, string head, string @base, string body) {
        var payload = new {
            title,
            head,
            @base,
            body
        };
        using var response = await PostJsonAsync($"/repos/{owner}/{repo}/pulls", payload).ConfigureAwait(false);
        var json = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
        if (response.StatusCode == System.Net.HttpStatusCode.UnprocessableEntity) {
            if (IsPullRequestAlreadyExists(json)) {
                return null;
            }
            throw new InvalidOperationException($"GitHub API request failed (422): {json}");
        }
        if (!response.IsSuccessStatusCode) {
            throw new InvalidOperationException($"GitHub API request failed ({(int)response.StatusCode}): {json}");
        }
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        var url = root.GetProperty("html_url").GetString() ?? string.Empty;
        var number = root.GetProperty("number").GetInt32();
        return new PullRequestInfo(number, url);
    }

    private static bool IsPullRequestAlreadyExists(string json) {
        try {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            if (root.TryGetProperty("errors", out var errors) && errors.ValueKind == JsonValueKind.Array) {
                foreach (var error in errors.EnumerateArray()) {
                    if (error.TryGetProperty("message", out var messageProp)) {
                        var message = messageProp.GetString();
                        if (!string.IsNullOrWhiteSpace(message) &&
                            message.Contains("already exists", StringComparison.OrdinalIgnoreCase)) {
                            return true;
                        }
                    }
                }
            }
            if (root.TryGetProperty("message", out var rootMessage)) {
                var message = rootMessage.GetString();
                if (!string.IsNullOrWhiteSpace(message) &&
                    message.Contains("already exists", StringComparison.OrdinalIgnoreCase)) {
                    return true;
                }
            }
        } catch {
            return false;
        }
        return false;
    }

    public async Task AddLabelsAsync(string owner, string repo, int number, IReadOnlyList<string> labels) {
        if (labels.Count == 0) {
            return;
        }
        var payload = new {
            labels
        };
        using var response = await PostJsonAsync($"/repos/{owner}/{repo}/issues/{number}/labels", payload).ConfigureAwait(false);
        var json = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
        if (!response.IsSuccessStatusCode) {
            throw new InvalidOperationException($"GitHub API request failed ({(int)response.StatusCode}): {json}");
        }
    }

    private async Task<HttpResponseMessage> PostJsonAsync(string url, object payload) {
        var json = JsonSerializer.Serialize(payload);
        var content = new StringContent(json, Encoding.UTF8, "application/json");
        return await _http.PostAsync(url, content).ConfigureAwait(false);
    }
}

internal readonly record struct PullRequestInfo(int Number, string Url);
