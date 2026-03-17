using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using IntelligenceX.Telemetry.GitHub;

namespace IntelligenceX.Tray.Services;

/// <summary>
/// GitHub data service. Uses authenticated API when a token is available,
/// falls back to public REST API for repo data when given a username.
/// </summary>
public sealed class GitHubService {
    private static readonly HttpClient SharedClient = CreateClient();
    private const int PublicOrgRepoConcurrency = 3;

    /// <summary>
    /// Fetch GitHub data. Tries authenticated API first, then public API with explicit login.
    /// </summary>
    public async Task<GitHubDashboardData?> FetchAsync(string? login = null, CancellationToken ct = default) {
        var token = GitHubDashboardService.ResolveTokenFromEnvironment();
        var normalizedLogin = string.IsNullOrWhiteSpace(login) ? null : login.Trim();

        // Authenticated path: full data (contributions + repos)
        if (!string.IsNullOrWhiteSpace(token)) {
            using var dashboard = new GitHubDashboardService(token);
            try {
                return await dashboard.FetchAsync(normalizedLogin, ct).ConfigureAwait(false);
            } catch (OperationCanceledException) {
                throw;
            } catch (Exception) {
                if (!string.IsNullOrWhiteSpace(normalizedLogin)) {
                    // Keep the username-based public path recoverable even when a stale token is present.
                    return await FetchPublicAsync(normalizedLogin, ct).ConfigureAwait(false);
                }

                throw;
            }
        }

        // Public path: repos only, requires a username
        if (string.IsNullOrWhiteSpace(normalizedLogin)) return null;

        return await FetchPublicAsync(normalizedLogin!, ct).ConfigureAwait(false);
    }

    /// <summary>
    /// Fetches public repo data for a username without authentication.
    /// </summary>
    private static async Task<GitHubDashboardData?> FetchPublicAsync(string login, CancellationToken ct) {
        var repos = new List<GitHubRepoInfo>();

        // Fetch user's public repos sorted by stars
        var userRepos = await FetchPublicReposAsync($"/users/{Uri.EscapeDataString(login)}/repos?sort=stars&direction=desc&per_page=10&type=owner", ct);
        repos.AddRange(userRepos);

        // Fetch user's orgs and their repos with a small concurrency cap to avoid rate-limit spikes.
        var orgsJson = await GetPublicJsonAsync($"/users/{Uri.EscapeDataString(login)}/orgs", ct);
        if (orgsJson != null) {
            using var orgsDoc = JsonDocument.Parse(orgsJson);
            var orgLogins = orgsDoc.RootElement.EnumerateArray()
                .Select(org => org.TryGetProperty("login", out var l) ? l.GetString() : null)
                .OfType<string>()
                .Where(static loginValue => !string.IsNullOrWhiteSpace(loginValue))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            using var gate = new SemaphoreSlim(PublicOrgRepoConcurrency);
            var orgTasks = orgLogins.Select(async orgLogin => {
                await gate.WaitAsync(ct).ConfigureAwait(false);
                try {
                    return await FetchPublicReposAsync(
                        $"/orgs/{Uri.EscapeDataString(orgLogin)}/repos?sort=stars&direction=desc&per_page=10&type=public",
                        ct).ConfigureAwait(false);
                } finally {
                    gate.Release();
                }
            });

            var orgResults = await Task.WhenAll(orgTasks).ConfigureAwait(false);
            foreach (var orgRepos in orgResults) {
                repos.AddRange(orgRepos);
            }
        }

        var topRepos = GitHubDashboardRepositoryRanking.BuildTopRepositories(repos, limit: 8);

        // No contribution data available via public API (would need GraphQL + token)
        var contribs = new GitHubContribData();
        return new GitHubDashboardData(login, contribs, topRepos);
    }

    private static async Task<List<GitHubRepoInfo>> FetchPublicReposAsync(string endpoint, CancellationToken ct) {
        var repos = new List<GitHubRepoInfo>();
        var json = await GetPublicJsonAsync(endpoint, ct);
        if (json == null) return repos;

        using var doc = JsonDocument.Parse(json);
        foreach (var node in doc.RootElement.EnumerateArray()) {
            var name = node.TryGetProperty("full_name", out var fn) ? fn.GetString() : null;
            if (name == null) continue;
            if (node.TryGetProperty("fork", out var fork) && fork.GetBoolean()) continue;

            string? lang = null, langColor = null;
            if (node.TryGetProperty("language", out var langEl) && langEl.ValueKind == JsonValueKind.String) {
                lang = langEl.GetString();
                langColor = ResolveLanguageColor(lang);
            }

            repos.Add(new GitHubRepoInfo(
                name,
                node.TryGetProperty("stargazers_count", out var s) ? s.GetInt32() : 0,
                node.TryGetProperty("forks_count", out var f) ? f.GetInt32() : 0,
                node.TryGetProperty("description", out var d) && d.ValueKind == JsonValueKind.String ? d.GetString() : null,
                lang,
                langColor));
        }

        return repos;
    }

    private static async Task<string?> GetPublicJsonAsync(string endpoint, CancellationToken ct) {
        var url = endpoint.StartsWith("http") ? endpoint : $"https://api.github.com{endpoint}";
        try {
            using var response = await SharedClient.GetAsync(url, ct).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode) {
                var status = response.StatusCode;
                if (status == System.Net.HttpStatusCode.NotFound) {
                    throw new InvalidOperationException("GitHub could not find that public user or resource.");
                }

                if (status == System.Net.HttpStatusCode.Unauthorized) {
                    throw new InvalidOperationException("GitHub rejected the public API request. Try again later or configure a GitHub token.");
                }

                if (status == System.Net.HttpStatusCode.Forbidden) {
                    var remaining = response.Headers.TryGetValues("X-RateLimit-Remaining", out var values)
                        ? values.FirstOrDefault()
                        : null;
                    if (string.Equals(remaining, "0", StringComparison.OrdinalIgnoreCase)) {
                        throw new InvalidOperationException("GitHub public API rate limit was exceeded. Try again later or set GITHUB_TOKEN for authenticated requests.");
                    }

                    throw new InvalidOperationException("GitHub denied the public API request. Try again later or configure a GitHub token.");
                }

                if ((int)status >= 500) {
                    throw new InvalidOperationException(
                        "GitHub public API is temporarily unavailable (HTTP "
                        + ((int)status).ToString(System.Globalization.CultureInfo.InvariantCulture)
                        + ").");
                }

                throw new InvalidOperationException(
                    "GitHub public API request failed with HTTP "
                    + ((int)status).ToString(System.Globalization.CultureInfo.InvariantCulture)
                    + ".");
            }
            return await response.Content.ReadAsStringAsync(ct);
        } catch (OperationCanceledException) {
            throw; // Don't swallow cancellation
        } catch (HttpRequestException ex) {
            System.Diagnostics.Debug.WriteLine(
                $"GitHub API error for {url}: {ex.Message}");
            return null;
        }
    }

    private static HttpClient CreateClient() {
        var client = new HttpClient();
        client.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("IntelligenceX-Tray", "0.1"));
        client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
        client.Timeout = TimeSpan.FromSeconds(30);
        return client;
    }

    private static string? ResolveLanguageColor(string? lang) => lang?.ToLowerInvariant() switch {
        "powershell" => "#012456",
        "c#" => "#178600",
        "javascript" => "#f1e05a",
        "typescript" => "#3178c6",
        "python" => "#3572A5",
        "go" => "#00ADD8",
        "rust" => "#dea584",
        "java" => "#b07219",
        "html" => "#e34c26",
        "css" => "#563d7c",
        "ruby" => "#701516",
        "php" => "#4F5D95",
        "swift" => "#F05138",
        "kotlin" => "#A97BFF",
        "dart" => "#00B4AB",
        "shell" => "#89e051",
        _ => "#8b949e"
    };
}
