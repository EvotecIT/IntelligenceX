using System;
using System.Collections.Generic;
using System.Globalization;
using System.Diagnostics;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace IntelligenceX.Cli.Setup.Wizard;

internal sealed class GitHubRepoClient : IDisposable {
    private readonly HttpClient _http;
    private readonly bool _ownsHttpClient;

    public GitHubRepoClient(string token, string apiBaseUrl) {
        _http = new HttpClient {
            BaseAddress = new Uri(apiBaseUrl)
        };
        _ownsHttpClient = true;
        ConfigureDefaultHeaders(_http, token);
    }

    internal GitHubRepoClient(HttpClient httpClient, string token = "test-token", bool ownsHttpClient = false) {
        _http = httpClient;
        _ownsHttpClient = ownsHttpClient;
        ConfigureDefaultHeaders(_http, token);
    }

    public void Dispose() {
        if (_ownsHttpClient) {
            _http.Dispose();
        }
    }

    public async Task<List<RepositoryInfo>> ListRepositoriesAsync() {
        var repos = new List<RepositoryInfo>();
        var page = 1;
        while (true) {
            var url = $"/user/repos?per_page=100&page={page}&affiliation=owner,collaborator,organization_member";
            var json = await GetJsonAsync(url).ConfigureAwait(false);
            if (json.ValueKind != JsonValueKind.Array || json.GetArrayLength() == 0) {
                break;
            }
            foreach (var item in json.EnumerateArray()) {
                if (TryParseRepository(item, out var info)) {
                    repos.Add(info);
                }
            }
            if (json.GetArrayLength() < 100) {
                break;
            }
            page++;
        }
        return repos;
    }

    public async Task<List<RepositoryInfo>> ListInstallationRepositoriesAsync() {
        var json = await GetJsonAsync("/installation/repositories").ConfigureAwait(false);
        if (json.ValueKind != JsonValueKind.Object || !json.TryGetProperty("repositories", out var repoArray)) {
            return new List<RepositoryInfo>();
        }

        var repos = new List<RepositoryInfo>();
        foreach (var item in repoArray.EnumerateArray()) {
            if (TryParseRepository(item, out var info)) {
                repos.Add(info);
            }
        }
        return repos;
    }

    public async Task<string> GetDefaultBranchAsync(string owner, string repo) {
        var json = await GetJsonAsync($"/repos/{owner}/{repo}").ConfigureAwait(false);
        return json.GetProperty("default_branch").GetString() ?? "main";
    }

    public async Task<RepoFile?> TryGetFileAsync(string owner, string repo, string path, string branch) {
        try {
            var json = await GetJsonAsync($"/repos/{owner}/{repo}/contents/{path}?ref={branch}")
                .ConfigureAwait(false);
            if (!json.TryGetProperty("content", out var contentProperty)) {
                return null;
            }
            var content = contentProperty.GetString();
            var sha = json.GetProperty("sha").GetString();
            if (string.IsNullOrWhiteSpace(content) || string.IsNullOrWhiteSpace(sha)) {
                return null;
            }

            var normalized = content.Replace("\n", string.Empty).Replace("\r", string.Empty);
            var bytes = Convert.FromBase64String(normalized);
            var text = Encoding.UTF8.GetString(bytes);
            return new RepoFile(sha, text);
        } catch (HttpRequestException ex) {
            Trace.TraceWarning($"GitHub file fetch HTTP failure for {owner}/{repo}/{path}@{branch}: {ex.Message}");
            return null;
        } catch (OperationCanceledException) {
            // Preserve cancellation semantics for callers that enforce timeouts/cancellation tokens.
            throw;
        } catch (JsonException ex) {
            Trace.TraceWarning($"GitHub file fetch JSON parse failure for {owner}/{repo}/{path}@{branch}: {ex.Message}");
            return null;
        } catch (FormatException ex) {
            Trace.TraceWarning($"GitHub file fetch base64 decode failure for {owner}/{repo}/{path}@{branch}: {ex.Message}");
            return null;
        } catch (InvalidOperationException ex) {
            Trace.TraceWarning($"GitHub file fetch failed for {owner}/{repo}/{path}@{branch}: {ex.GetType().Name}: {ex.Message}");
            return null;
        } catch (KeyNotFoundException ex) {
            Trace.TraceWarning($"GitHub file fetch payload missing fields for {owner}/{repo}/{path}@{branch}: {ex.Message}");
            return null;
        }
    }

    public async Task<PullRequestInfo?> TryGetPullRequestAsync(string owner, string repo, int number) {
        try {
            var json = await GetJsonAsync($"/repos/{owner}/{repo}/pulls/{number}").ConfigureAwait(false);
            var headRef = json.GetProperty("head").GetProperty("ref").GetString();
            var baseRef = json.GetProperty("base").GetProperty("ref").GetString();
            var url = json.TryGetProperty("html_url", out var htmlUrl) ? htmlUrl.GetString() : null;
            return new PullRequestInfo(number, headRef, baseRef, url);
        } catch (HttpRequestException ex) {
            Trace.TraceWarning($"GitHub pull request fetch HTTP failure for {owner}/{repo}#{number}: {ex.Message}");
            return null;
        } catch (OperationCanceledException) {
            // Preserve cancellation semantics for callers that enforce timeouts/cancellation tokens.
            throw;
        } catch (JsonException ex) {
            Trace.TraceWarning($"GitHub pull request fetch JSON parse failure for {owner}/{repo}#{number}: {ex.Message}");
            return null;
        } catch (InvalidOperationException ex) {
            Trace.TraceWarning($"GitHub pull request fetch failed for {owner}/{repo}#{number}: {ex.GetType().Name}: {ex.Message}");
            return null;
        } catch (KeyNotFoundException ex) {
            Trace.TraceWarning($"GitHub pull request payload missing fields for {owner}/{repo}#{number}: {ex.Message}");
            return null;
        }
    }

    public Task<SecretLookupResult> TryRepoSecretExistsAsync(string owner, string repo, string name) {
        return TrySecretExistsAsync($"/repos/{owner}/{repo}/actions/secrets/{name}");
    }

    public Task<SecretLookupResult> TryOrgSecretExistsAsync(string org, string name) {
        return TrySecretExistsAsync($"/orgs/{org}/actions/secrets/{name}");
    }

    private async Task<JsonElement> GetJsonAsync(string url) {
        using var response = await _http.GetAsync(url).ConfigureAwait(false);
        var content = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
        if (!response.IsSuccessStatusCode) {
            throw new InvalidOperationException(FormatGitHubFailure(response, content));
        }
        using var doc = JsonDocument.Parse(content);
        return doc.RootElement.Clone();
    }

    private static void ConfigureDefaultHeaders(HttpClient http, string token) {
        if (!http.DefaultRequestHeaders.UserAgent.Any(value =>
                string.Equals(value.Product?.Name, "IntelligenceX.Cli", StringComparison.OrdinalIgnoreCase))) {
            http.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("IntelligenceX.Cli", "1.0"));
        }
        if (!http.DefaultRequestHeaders.Accept.Any(value =>
                string.Equals(value.MediaType, "application/vnd.github+json", StringComparison.OrdinalIgnoreCase))) {
            http.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
        }
        http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        if (http.DefaultRequestHeaders.Contains("X-GitHub-Api-Version")) {
            http.DefaultRequestHeaders.Remove("X-GitHub-Api-Version");
        }
        http.DefaultRequestHeaders.Add("X-GitHub-Api-Version", "2022-11-28");
    }

    private static string FormatGitHubFailure(HttpResponseMessage response, string body) {
        var msg = $"GitHub API request failed ({(int)response.StatusCode}): {body}";
        if (response.Headers.TryGetValues("X-Accepted-GitHub-Permissions", out var accepted)) {
            var joined = string.Join(", ", accepted);
            if (!string.IsNullOrWhiteSpace(joined)) {
                msg += $"{Environment.NewLine}Accepted permissions: {joined}";
            }
        }
        if (response.Headers.TryGetValues("X-OAuth-Scopes", out var scopes)) {
            var joined = string.Join(", ", scopes);
            if (!string.IsNullOrWhiteSpace(joined)) {
                msg += $"{Environment.NewLine}Token scopes: {joined}";
            }
        }
        return msg;
    }

    private async Task<SecretLookupResult> TrySecretExistsAsync(string url) {
        try {
            using var response = await _http.GetAsync(url).ConfigureAwait(false);
            if (response.StatusCode == System.Net.HttpStatusCode.NotFound) {
                return SecretLookupResult.Missing();
            }
            if (response.IsSuccessStatusCode) {
                return SecretLookupResult.Present();
            }
            var statusCode = (int)response.StatusCode;
            if (response.StatusCode == System.Net.HttpStatusCode.Unauthorized) {
                Trace.TraceWarning($"GitHub secret lookup unauthorized ({statusCode}).");
                return SecretLookupResult.Unauthorized($"GitHub API returned {statusCode} Unauthorized.");
            }
            if (response.StatusCode == System.Net.HttpStatusCode.Forbidden) {
                Trace.TraceWarning($"GitHub secret lookup forbidden ({statusCode}).");
                return SecretLookupResult.Forbidden($"GitHub API returned {statusCode} Forbidden.");
            }
            if (statusCode == 429) {
                Trace.TraceWarning("GitHub secret lookup rate limited (429).");
                return SecretLookupResult.RateLimited("GitHub API returned 429 Too Many Requests.");
            }
            Trace.TraceWarning($"GitHub secret lookup failed ({statusCode}).");
            return SecretLookupResult.Unknown($"GitHub API returned {statusCode} {response.ReasonPhrase ?? "Error"}.");
        } catch (OperationCanceledException) {
            // Preserve cancellation semantics for callers that enforce timeouts/cancellation tokens.
            throw;
        } catch (HttpRequestException ex) {
            Trace.TraceWarning($"GitHub secret lookup HTTP failure: {ex.Message}");
            return SecretLookupResult.Unknown("GitHub secret lookup failed due to an HTTP client error.");
        } catch (InvalidOperationException ex) {
            Trace.TraceWarning($"GitHub secret lookup client failure: {ex.Message}");
            return SecretLookupResult.Unknown("GitHub secret lookup failed due to an HTTP client configuration error.");
        }
    }

    private static bool TryParseRepository(JsonElement item, out RepositoryInfo info) {
        info = null!;
        var fullName = item.GetProperty("full_name").GetString() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(fullName)) {
            return false;
        }

        var isPrivate = item.GetProperty("private").GetBoolean();
        DateTimeOffset? updatedAt = null;
        if (item.TryGetProperty("updated_at", out var updatedProperty)
            && updatedProperty.ValueKind == JsonValueKind.String
            && DateTimeOffset.TryParse(updatedProperty.GetString(), CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var parsed)) {
            updatedAt = parsed;
        }

        bool canPush = false;
        bool canAdmin = false;
        if (item.TryGetProperty("permissions", out var permProp) && permProp.ValueKind == JsonValueKind.Object) {
            if (permProp.TryGetProperty("push", out var pushProp) && pushProp.ValueKind == JsonValueKind.True) {
                canPush = true;
            }
            if (permProp.TryGetProperty("admin", out var adminProp) && adminProp.ValueKind == JsonValueKind.True) {
                canAdmin = true;
            }
        }

        info = new RepositoryInfo(fullName, isPrivate, updatedAt, canPush, canAdmin);
        return true;
    }

    /// <summary>
    /// Repository metadata returned by the GitHub API.
    /// </summary>
    public sealed class RepositoryInfo {
        /// <summary>
        /// Initializes a new repository info record.
        /// </summary>
        /// <param name="fullName">Repository full name in owner/name format.</param>
        /// <param name="isPrivate">True when the repository is private.</param>
        /// <param name="updatedAt">Last update timestamp if available.</param>
        /// <param name="canPush">True when the user can push to this repo.</param>
        /// <param name="canAdmin">True when the user has admin access to this repo.</param>
        public RepositoryInfo(string fullName, bool isPrivate, DateTimeOffset? updatedAt, bool canPush = false, bool canAdmin = false) {
            FullName = fullName;
            Private = isPrivate;
            UpdatedAt = updatedAt;
            CanPush = canPush;
            CanAdmin = canAdmin;
        }

        /// <summary>
        /// Repository full name in owner/name format.
        /// </summary>
        public string FullName { get; }

        /// <summary>
        /// Indicates whether the repository is private.
        /// </summary>
        public bool Private { get; }

        /// <summary>
        /// Last update timestamp if provided by the API.
        /// </summary>
        public DateTimeOffset? UpdatedAt { get; }

        /// <summary>
        /// Indicates whether the user can push to this repository.
        /// </summary>
        public bool CanPush { get; }

        /// <summary>
        /// Indicates whether the user has admin access to this repository.
        /// </summary>
        public bool CanAdmin { get; }
    }

    /// <summary>
    /// Represents a file payload returned by the GitHub contents API.
    /// </summary>
    public sealed class RepoFile {
        /// <summary>
        /// Initializes a new repository file payload.
        /// </summary>
        /// <param name="sha">Git blob SHA for the file.</param>
        /// <param name="content">Decoded file content.</param>
        public RepoFile(string sha, string content) {
            Sha = sha;
            Content = content;
        }

        /// <summary>
        /// Git blob SHA for the file.
        /// </summary>
        public string Sha { get; }

        /// <summary>
        /// Decoded file content.
        /// </summary>
        public string Content { get; }
    }

    public sealed class PullRequestInfo {
        public PullRequestInfo(int number, string? headRef, string? baseRef, string? url) {
            Number = number;
            HeadRef = headRef;
            BaseRef = baseRef;
            Url = url;
        }

        public int Number { get; }
        public string? HeadRef { get; }
        public string? BaseRef { get; }
        public string? Url { get; }
    }

    public sealed class SecretLookupResult {
        private SecretLookupResult(bool? exists, string status, string? note) {
            Exists = exists;
            Status = status;
            Note = note;
        }

        public bool? Exists { get; }
        public string Status { get; }
        public string? Note { get; }

        public static SecretLookupResult Present() => new(true, "present", null);
        public static SecretLookupResult Missing() => new(false, "missing", null);
        public static SecretLookupResult Unauthorized(string note) => new(null, "unauthorized", note);
        public static SecretLookupResult Forbidden(string note) => new(null, "forbidden", note);
        public static SecretLookupResult RateLimited(string note) => new(null, "rate_limited", note);
        public static SecretLookupResult Unknown(string note) => new(null, "unknown", note);
    }
}
