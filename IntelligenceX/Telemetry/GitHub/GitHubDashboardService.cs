using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace IntelligenceX.Telemetry.GitHub;

/// <summary>
/// Native GitHub dashboard service for shared observability scenarios.
/// </summary>
public sealed class GitHubDashboardService : IDisposable {
    private readonly HttpClient _http;
    private readonly bool _disposeHttpClient;

    /// <summary>
    /// Initializes a dashboard service with a GitHub token.
    /// </summary>
    public GitHubDashboardService(string token, string? apiBaseUrl = null)
        : this(CreateHttpClient(token, apiBaseUrl), disposeHttpClient: true) {
    }

    internal GitHubDashboardService(HttpClient httpClient, bool disposeHttpClient) {
        _http = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        _disposeHttpClient = disposeHttpClient;
    }

    /// <summary>
    /// Creates a dashboard snapshot for the authenticated user or an explicitly supplied login.
    /// </summary>
    /// <param name="login">Optional user login override. When omitted, the authenticated user is used.</param>
    /// <param name="cancellationToken">Optional cancellation token.</param>
    /// <returns>GitHub dashboard data.</returns>
    public async Task<GitHubDashboardData> FetchAsync(string? login = null, CancellationToken cancellationToken = default) {
        var effectiveLogin = string.IsNullOrWhiteSpace(login)
            ? await GetAuthenticatedLoginAsync(cancellationToken).ConfigureAwait(false)
            : login!.Trim();
        if (string.IsNullOrWhiteSpace(effectiveLogin)) {
            throw new InvalidOperationException("Unable to determine authenticated GitHub login.");
        }
        var normalizedLogin = effectiveLogin!;

        var contributionsTask = FetchContributionsAsync(normalizedLogin, cancellationToken);
        var topRepositoriesTask = FetchTopRepositoriesAsync(normalizedLogin, cancellationToken);
        await Task.WhenAll(contributionsTask, topRepositoriesTask).ConfigureAwait(false);

        return new GitHubDashboardData(
            normalizedLogin,
            contributionsTask.Result,
            topRepositoriesTask.Result);
    }

    /// <summary>
    /// Resolves the authenticated GitHub login from the API.
    /// </summary>
    public async Task<string?> GetAuthenticatedLoginAsync(CancellationToken cancellationToken = default) {
        using var document = await GetJsonAsync("/user", cancellationToken).ConfigureAwait(false);
        return document.RootElement.TryGetProperty("login", out var login) && login.ValueKind == JsonValueKind.String
            ? NormalizeOptional(login.GetString())
            : null;
    }

    /// <summary>
    /// Returns a GitHub token from common environment variables when available.
    /// </summary>
    public static string? ResolveTokenFromEnvironment() {
        return NormalizeOptional(Environment.GetEnvironmentVariable("INTELLIGENCEX_GITHUB_TOKEN"))
               ?? NormalizeOptional(Environment.GetEnvironmentVariable("GITHUB_TOKEN"))
               ?? NormalizeOptional(Environment.GetEnvironmentVariable("GH_TOKEN"));
    }

    /// <inheritdoc />
    public void Dispose() {
        if (_disposeHttpClient) {
            _http.Dispose();
        }
    }

    private async Task<GitHubContribData> FetchContributionsAsync(string login, CancellationToken cancellationToken) {
        var now = DateTimeOffset.UtcNow;
        var from = now.AddDays(-29).Date;
        var to = now.Date;
        const string query = """
query($login: String!, $from: DateTime!, $to: DateTime!) {
  user(login: $login) {
    contributionsCollection(from: $from, to: $to) {
      totalCommitContributions
      totalIssueContributions
      totalPullRequestContributions
      totalPullRequestReviewContributions
      contributionCalendar {
        totalContributions
        weeks {
          contributionDays {
            date
            contributionCount
            color
          }
        }
      }
    }
  }
}
""";

        using var document = await QueryGraphQlAsync(
            query,
            new Dictionary<string, object?> {
                ["login"] = login,
                ["from"] = from.ToString("yyyy-MM-ddT00:00:00Z", CultureInfo.InvariantCulture),
                ["to"] = to.ToString("yyyy-MM-ddT23:59:59Z", CultureInfo.InvariantCulture)
            },
            cancellationToken).ConfigureAwait(false);

        var root = document.RootElement;
        if (!TryGetProperty(root, "data", out var data) ||
            !TryGetProperty(data, "user", out var user) ||
            !TryGetProperty(user, "contributionsCollection", out var collection)) {
            return new GitHubContribData();
        }

        var dailyContributions = new List<GitHubDailyContrib>();
        if (TryGetProperty(collection, "contributionCalendar", out var calendar) &&
            TryGetProperty(calendar, "weeks", out var weeks) &&
            weeks.ValueKind == JsonValueKind.Array) {
            foreach (var week in weeks.EnumerateArray()) {
                if (!TryGetProperty(week, "contributionDays", out var days) || days.ValueKind != JsonValueKind.Array) {
                    continue;
                }

                foreach (var day in days.EnumerateArray()) {
                    var dateText = ReadString(day, "date");
                    if (!TryParseIsoDay(dateText, out var parsedDay)) {
                        continue;
                    }

                    dailyContributions.Add(new GitHubDailyContrib(
                        parsedDay,
                        ReadInt(day, "contributionCount"),
                        NormalizeOptional(ReadString(day, "color"))));
                }
            }
        }

        return new GitHubContribData(
            totalContributions: TryGetProperty(collection, "contributionCalendar", out var contributionCalendar)
                                ? ReadInt(contributionCalendar, "totalContributions")
                                : 0,
            totalCommits: ReadInt(collection, "totalCommitContributions"),
            totalIssues: ReadInt(collection, "totalIssueContributions"),
            totalPrs: ReadInt(collection, "totalPullRequestContributions"),
            totalReviews: ReadInt(collection, "totalPullRequestReviewContributions"),
            dailyContributions: dailyContributions
                .OrderBy(static day => day.Date)
                .ToArray());
    }

    private async Task<IReadOnlyList<GitHubRepoInfo>> FetchTopRepositoriesAsync(string login, CancellationToken cancellationToken) {
        var owners = new List<string> { login };
        owners.AddRange(await FetchUserOrganizationsAsync(cancellationToken).ConfigureAwait(false));

        var repositories = new List<GitHubRepoInfo>();
        foreach (var owner in owners
                     .Where(static owner => !string.IsNullOrWhiteSpace(owner))
                     .Distinct(StringComparer.OrdinalIgnoreCase)) {
            repositories.AddRange(await FetchOwnerRepositoriesAsync(owner, cancellationToken).ConfigureAwait(false));
        }

        return GitHubDashboardRepositoryRanking.BuildTopRepositories(repositories, limit: 8);
    }

    private async Task<IReadOnlyList<string>> FetchUserOrganizationsAsync(CancellationToken cancellationToken) {
        using var document = await GetJsonAsync("/user/orgs", cancellationToken).ConfigureAwait(false);
        if (document.RootElement.ValueKind != JsonValueKind.Array) {
            return Array.Empty<string>();
        }

        return document.RootElement
            .EnumerateArray()
            .Select(static org => ReadString(org, "login"))
            .Where(static login => !string.IsNullOrWhiteSpace(login))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private async Task<IReadOnlyList<GitHubRepoInfo>> FetchOwnerRepositoriesAsync(string owner, CancellationToken cancellationToken) {
        const string query = """
query($login: String!) {
  repositoryOwner(login: $login) {
    repositories(
      first: 10
      orderBy: { field: STARGAZERS, direction: DESC }
      privacy: PUBLIC
    ) {
      nodes {
        nameWithOwner
        stargazerCount
        forkCount
        description
        primaryLanguage {
          name
          color
        }
      }
    }
  }
}
""";

        using var document = await QueryGraphQlAsync(
            query,
            new Dictionary<string, object?> {
                ["login"] = owner
            },
            cancellationToken).ConfigureAwait(false);

        var root = document.RootElement;
        if (!TryGetProperty(root, "data", out var data) ||
            !TryGetProperty(data, "repositoryOwner", out var ownerNode) ||
            ownerNode.ValueKind == JsonValueKind.Null ||
            !TryGetProperty(ownerNode, "repositories", out var repositoriesConnection) ||
            !TryGetProperty(repositoriesConnection, "nodes", out var nodes) ||
            nodes.ValueKind != JsonValueKind.Array) {
            return Array.Empty<GitHubRepoInfo>();
        }

        var repositories = new List<GitHubRepoInfo>();
        foreach (var node in nodes.EnumerateArray()) {
            var nameWithOwner = NormalizeOptional(ReadString(node, "nameWithOwner"));
            if (string.IsNullOrWhiteSpace(nameWithOwner)) {
                continue;
            }

            string? language = null;
            string? languageColor = null;
            if (TryGetProperty(node, "primaryLanguage", out var primaryLanguage) &&
                primaryLanguage.ValueKind == JsonValueKind.Object) {
                language = NormalizeOptional(ReadString(primaryLanguage, "name"));
                languageColor = NormalizeOptional(ReadString(primaryLanguage, "color"));
            }

            repositories.Add(new GitHubRepoInfo(
                nameWithOwner!,
                ReadInt(node, "stargazerCount"),
                ReadInt(node, "forkCount"),
                NormalizeOptional(ReadString(node, "description")),
                language,
                languageColor));
        }

        return repositories;
    }

    private static HttpClient CreateHttpClient(string token, string? apiBaseUrl) {
        if (string.IsNullOrWhiteSpace(token)) {
            throw new ArgumentException("GitHub token is required.", nameof(token));
        }

        var http = new HttpClient {
            BaseAddress = new Uri(string.IsNullOrWhiteSpace(apiBaseUrl) ? "https://api.github.com" : apiBaseUrl)
        };
        http.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("IntelligenceX", "1.0"));
        http.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
        http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token.Trim());
        http.DefaultRequestHeaders.Add("X-GitHub-Api-Version", "2022-11-28");
        return http;
    }

    private async Task<JsonDocument> GetJsonAsync(string path, CancellationToken cancellationToken) {
        using var response = await _http.GetAsync(path, cancellationToken).ConfigureAwait(false);
        var content = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
        if (!response.IsSuccessStatusCode) {
            throw new InvalidOperationException(
                "GitHub API request failed (" + (int)response.StatusCode + "): " + content);
        }

        return JsonDocument.Parse(content);
    }

    private async Task<JsonDocument> QueryGraphQlAsync(
        string query,
        IReadOnlyDictionary<string, object?> variables,
        CancellationToken cancellationToken) {
        var payload = JsonSerializer.Serialize(new {
            query,
            variables
        }) ?? string.Empty;
        using var request = new HttpRequestMessage(HttpMethod.Post, "/graphql") {
            Content = new StringContent(payload, Encoding.UTF8, "application/json")
        };
        using var response = await _http.SendAsync(request, cancellationToken).ConfigureAwait(false);
        var content = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
        if (!response.IsSuccessStatusCode) {
            throw new InvalidOperationException(
                "GitHub GraphQL request failed (" + (int)response.StatusCode + "): " + content);
        }

        var document = JsonDocument.Parse(content);
        if (document.RootElement.TryGetProperty("errors", out var errors) &&
            errors.ValueKind == JsonValueKind.Array &&
            errors.GetArrayLength() > 0) {
            var messages = errors
                .EnumerateArray()
                .Select(static error => ReadString(error, "message"))
                .Where(static message => !string.IsNullOrWhiteSpace(message))
                .ToArray();
            throw new InvalidOperationException("GitHub GraphQL returned errors: " + string.Join(" | ", messages));
        }

        return document;
    }

    private static string ReadString(JsonElement obj, string name) {
        return TryGetProperty(obj, name, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString() ?? string.Empty
            : string.Empty;
    }

    private static int ReadInt(JsonElement obj, string name) {
        return TryGetProperty(obj, name, out var value) && value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out var parsed)
            ? parsed
            : 0;
    }

    private static bool TryGetProperty(JsonElement obj, string name, out JsonElement value) {
        value = default;
        return obj.ValueKind == JsonValueKind.Object && obj.TryGetProperty(name, out value);
    }

    private static bool TryParseIsoDay(string? value, out DateTime parsedDay) {
        parsedDay = default;
        if (string.IsNullOrWhiteSpace(value)) {
            return false;
        }

        if (!DateTime.TryParseExact(
                value!.Trim(),
                "yyyy-MM-dd",
                CultureInfo.InvariantCulture,
                DateTimeStyles.None,
                out var parsed)) {
            return false;
        }

        parsedDay = parsed.Date;
        return true;
    }

    private static string? NormalizeOptional(string? value) {
        return string.IsNullOrWhiteSpace(value) ? null : value!.Trim();
    }
}

/// <summary>
/// Shared GitHub dashboard data for tray or diagnostics UIs.
/// </summary>
public sealed class GitHubDashboardData {
    /// <summary>
    /// Initializes dashboard data.
    /// </summary>
    public GitHubDashboardData(string login, GitHubContribData contributions, IReadOnlyList<GitHubRepoInfo> topRepos) {
        Login = string.IsNullOrWhiteSpace(login) ? string.Empty : login.Trim();
        Contributions = contributions ?? new GitHubContribData();
        TopRepos = topRepos ?? Array.Empty<GitHubRepoInfo>();
    }

    /// <summary>
    /// Gets the login this dashboard represents.
    /// </summary>
    public string Login { get; }

    /// <summary>
    /// Gets aggregated contribution data.
    /// </summary>
    public GitHubContribData Contributions { get; }

    /// <summary>
    /// Gets top repositories by stars/forks.
    /// </summary>
    public IReadOnlyList<GitHubRepoInfo> TopRepos { get; }
}

/// <summary>
/// Contribution summary for the dashboard window.
/// </summary>
public sealed class GitHubContribData {
    /// <summary>
    /// Initializes contribution data.
    /// </summary>
    public GitHubContribData(
        int totalContributions = 0,
        int totalCommits = 0,
        int totalIssues = 0,
        int totalPrs = 0,
        int totalReviews = 0,
        IReadOnlyList<GitHubDailyContrib>? dailyContributions = null) {
        TotalContributions = Math.Max(0, totalContributions);
        TotalCommits = Math.Max(0, totalCommits);
        TotalIssues = Math.Max(0, totalIssues);
        TotalPRs = Math.Max(0, totalPrs);
        TotalReviews = Math.Max(0, totalReviews);
        DailyContributions = dailyContributions ?? Array.Empty<GitHubDailyContrib>();
    }

    /// <summary>
    /// Gets total contributions in the dashboard window.
    /// </summary>
    public int TotalContributions { get; }

    /// <summary>
    /// Gets total commit contributions in the dashboard window.
    /// </summary>
    public int TotalCommits { get; }

    /// <summary>
    /// Gets total issue contributions in the dashboard window.
    /// </summary>
    public int TotalIssues { get; }

    /// <summary>
    /// Gets total pull request contributions in the dashboard window.
    /// </summary>
    public int TotalPRs { get; }

    /// <summary>
    /// Gets total review contributions in the dashboard window.
    /// </summary>
    public int TotalReviews { get; }

    /// <summary>
    /// Gets daily contribution records for the dashboard window.
    /// </summary>
    public IReadOnlyList<GitHubDailyContrib> DailyContributions { get; }
}

/// <summary>
/// Daily contribution entry for the dashboard.
/// </summary>
public sealed class GitHubDailyContrib {
    /// <summary>
    /// Initializes a daily contribution record.
    /// </summary>
    public GitHubDailyContrib(DateTime date, int count, string? color) {
        Date = date.Date;
        Count = Math.Max(0, count);
        Color = color;
    }

    /// <summary>
    /// Gets the UTC day represented by this record.
    /// </summary>
    public DateTime Date { get; }

    /// <summary>
    /// Gets the contribution count for the day.
    /// </summary>
    public int Count { get; }

    /// <summary>
    /// Gets the optional GitHub contribution color for the day.
    /// </summary>
    public string? Color { get; }
}

/// <summary>
/// Repository summary for the dashboard.
/// </summary>
public sealed class GitHubRepoInfo {
    /// <summary>
    /// Initializes a repository summary.
    /// </summary>
    public GitHubRepoInfo(
        string nameWithOwner,
        int stars,
        int forks,
        string? description,
        string? language,
        string? languageColor) {
        NameWithOwner = string.IsNullOrWhiteSpace(nameWithOwner) ? string.Empty : nameWithOwner.Trim();
        Stars = Math.Max(0, stars);
        Forks = Math.Max(0, forks);
        Description = description;
        Language = language;
        LanguageColor = languageColor;
    }

    /// <summary>
    /// Gets the canonical owner/repository name.
    /// </summary>
    public string NameWithOwner { get; }

    /// <summary>
    /// Gets the repository star count.
    /// </summary>
    public int Stars { get; }

    /// <summary>
    /// Gets the repository fork count.
    /// </summary>
    public int Forks { get; }

    /// <summary>
    /// Gets the optional repository description.
    /// </summary>
    public string? Description { get; }

    /// <summary>
    /// Gets the optional primary language name.
    /// </summary>
    public string? Language { get; }

    /// <summary>
    /// Gets the optional primary language color.
    /// </summary>
    public string? LanguageColor { get; }
}
