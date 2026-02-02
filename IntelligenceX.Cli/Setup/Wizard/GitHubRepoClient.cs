using System;
using System.Collections.Generic;
using System.Globalization;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace IntelligenceX.Cli.Setup.Wizard;

internal sealed class GitHubRepoClient : IDisposable {
    private readonly HttpClient _http;

    public GitHubRepoClient(string token, string apiBaseUrl) {
        _http = new HttpClient {
            BaseAddress = new Uri(apiBaseUrl)
        };
        _http.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("IntelligenceX.Cli", "1.0"));
        _http.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
        _http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        _http.DefaultRequestHeaders.Add("X-GitHub-Api-Version", "2022-11-28");
    }

    public void Dispose() => _http.Dispose();

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
        } catch {
            return null;
        }
    }

    private async Task<JsonElement> GetJsonAsync(string url) {
        using var response = await _http.GetAsync(url).ConfigureAwait(false);
        var content = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
        if (!response.IsSuccessStatusCode) {
            throw new InvalidOperationException($"GitHub API request failed ({(int)response.StatusCode}): {content}");
        }
        using var doc = JsonDocument.Parse(content);
        return doc.RootElement.Clone();
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

        info = new RepositoryInfo(fullName, isPrivate, updatedAt);
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
        public RepositoryInfo(string fullName, bool isPrivate, DateTimeOffset? updatedAt) {
            FullName = fullName;
            Private = isPrivate;
            UpdatedAt = updatedAt;
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
}
