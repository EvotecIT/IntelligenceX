using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;

namespace IntelligenceX.Cli.GitHub;

internal sealed class GitHubContributionCalendarClient {
    private const int MaxWindowDays = 365;
    private readonly Func<string, DateTimeOffset, DateTimeOffset, Task<JsonElement>> _queryWindowAsync;

    public GitHubContributionCalendarClient()
        : this(QueryContributionCalendarWindowAsync) {
    }

    internal GitHubContributionCalendarClient(Func<string, DateTimeOffset, DateTimeOffset, Task<JsonElement>> queryWindowAsync) {
        _queryWindowAsync = queryWindowAsync ?? throw new ArgumentNullException(nameof(queryWindowAsync));
    }

    public async Task<GitHubContributionCalendar> GetUserContributionCalendarAsync(
        string login,
        DateTimeOffset from,
        DateTimeOffset to) {
        if (string.IsNullOrWhiteSpace(login)) {
            throw new InvalidOperationException("GitHub user login is required.");
        }
        if (to < from) {
            throw new InvalidOperationException("GitHub contribution calendar end date must be on or after the start date.");
        }

        var days = new List<GitHubContributionDay>();
        string? resolvedLogin = null;
        string? resolvedName = null;
        string? resolvedUrl = null;

        var dedupedDays = new Dictionary<DateTime, GitHubContributionDay>();
        var windowStart = NormalizeDay(from);
        var endDay = NormalizeDay(to);
        while (windowStart <= endDay) {
            var windowEnd = windowStart.AddDays(MaxWindowDays - 1);
            if (windowEnd > endDay) {
                windowEnd = endDay;
            }

            var window = await GetContributionCalendarWindowAsync(login, windowStart, windowEnd).ConfigureAwait(false);
            if (string.IsNullOrWhiteSpace(resolvedLogin)) {
                resolvedLogin = window.Login;
            }
            if (string.IsNullOrWhiteSpace(resolvedName) && !string.IsNullOrWhiteSpace(window.Name)) {
                resolvedName = window.Name;
            }
            if (string.IsNullOrWhiteSpace(resolvedUrl) && !string.IsNullOrWhiteSpace(window.ProfileUrl)) {
                resolvedUrl = window.ProfileUrl;
            }

            foreach (var day in window.Days) {
                dedupedDays[day.Date] = day;
            }

            windowStart = windowEnd.AddDays(1);
        }

        if (string.IsNullOrWhiteSpace(resolvedLogin)) {
            throw new InvalidOperationException($"GitHub user '{login}' was not found.");
        }

        days.AddRange(dedupedDays
            .OrderBy(static pair => pair.Key)
            .Select(static pair => pair.Value));

        var totalContributions = days.Sum(static day => day.ContributionCount);
        var summary = days.Count == 0
            ? new GitHubContributionCollectionSummary()
            : await GetContributionCollectionSummaryAsync(login, windowStart: NormalizeDay(from), windowEnd: NormalizeDay(to)).ConfigureAwait(false);
        return new GitHubContributionCalendar(resolvedLogin, resolvedName, resolvedUrl, totalContributions, days, summary);
    }

    private async Task<GitHubContributionCalendar> GetContributionCalendarWindowAsync(
        string login,
        DateTimeOffset from,
        DateTimeOffset to) {
        var root = await _queryWindowAsync(login, from, to).ConfigureAwait(false);
        return ParseContributionCalendarWindow(login, root);
    }

    private async Task<GitHubContributionCollectionSummary> GetContributionCollectionSummaryAsync(
        string login,
        DateTimeOffset windowStart,
        DateTimeOffset windowEnd) {
        var root = await _queryWindowAsync(login, windowStart, windowEnd).ConfigureAwait(false);
        if (!GitHubGraphQlCli.TryGetProperty(root, "data", out var data) ||
            !GitHubGraphQlCli.TryGetProperty(data, "user", out var user) ||
            !GitHubGraphQlCli.TryGetProperty(user, "contributionsCollection", out var collection) ||
            collection.ValueKind != JsonValueKind.Object) {
            return new GitHubContributionCollectionSummary();
        }

        return new GitHubContributionCollectionSummary(
            commitContributions: ReadInt32(collection, "totalCommitContributions"),
            issueContributions: ReadInt32(collection, "totalIssueContributions"),
            pullRequestContributions: ReadInt32(collection, "totalPullRequestContributions"),
            pullRequestReviewContributions: ReadInt32(collection, "totalPullRequestReviewContributions"),
            repositoryContributions: ReadInt32(collection, "totalRepositoryContributions"),
            restrictedContributions: ReadInt32(collection, "restrictedContributionsCount"),
            repositoriesWithCommits: ReadInt32(collection, "totalRepositoriesWithContributedCommits"),
            repositoriesWithIssues: ReadInt32(collection, "totalRepositoriesWithContributedIssues"),
            repositoriesWithPullRequests: ReadInt32(collection, "totalRepositoriesWithContributedPullRequests"),
            repositoriesWithPullRequestReviews: ReadInt32(collection, "totalRepositoriesWithContributedPullRequestReviews"));
    }

    private static async Task<JsonElement> QueryContributionCalendarWindowAsync(
        string login,
        DateTimeOffset from,
        DateTimeOffset to) {
        var query = """
query($login: String!, $from: DateTime!, $to: DateTime!) {
  user(login: $login) {
    login
    name
    url
    contributionsCollection(from: $from, to: $to) {
      restrictedContributionsCount
      totalCommitContributions
      totalIssueContributions
      totalPullRequestContributions
      totalPullRequestReviewContributions
      totalRepositoryContributions
      totalRepositoriesWithContributedCommits
      totalRepositoriesWithContributedIssues
      totalRepositoriesWithContributedPullRequests
      totalRepositoriesWithContributedPullRequestReviews
      contributionCalendar {
        totalContributions
        weeks {
          contributionDays {
            color
            contributionCount
            contributionLevel
            date
            weekday
          }
        }
      }
    }
  }
}
""";

        return await GitHubGraphQlCli.QueryAsync(
            query,
            TimeSpan.FromSeconds(90),
            ("login", login.Trim()),
            ("from", NormalizeDay(from).ToString("o", CultureInfo.InvariantCulture)),
            ("to", NormalizeDay(to).AddDays(1).AddTicks(-1).ToString("o", CultureInfo.InvariantCulture))).ConfigureAwait(false);
    }

    private static GitHubContributionCalendar ParseContributionCalendarWindow(string login, JsonElement root) {
        if (!GitHubGraphQlCli.TryGetProperty(root, "data", out var data) ||
            data.ValueKind != JsonValueKind.Object ||
            !GitHubGraphQlCli.TryGetProperty(data, "user", out var user)) {
            throw new InvalidOperationException("GitHub GraphQL response missing user contribution data.");
        }
        if (user.ValueKind == JsonValueKind.Null) {
            throw new InvalidOperationException($"GitHub user '{login}' was not found.");
        }
        if (user.ValueKind != JsonValueKind.Object) {
            throw new InvalidOperationException("GitHub GraphQL response missing user contribution data.");
        }

        var resolvedLogin = GitHubGraphQlCli.ReadString(user, "login");
        var resolvedName = GitHubGraphQlCli.ReadString(user, "name");
        var resolvedUrl = GitHubGraphQlCli.ReadString(user, "url");
        if (string.IsNullOrWhiteSpace(resolvedLogin)) {
            throw new InvalidOperationException($"GitHub user '{login}' was not found.");
        }

        if (!GitHubGraphQlCli.TryGetProperty(user, "contributionsCollection", out var collection) ||
            !GitHubGraphQlCli.TryGetProperty(collection, "contributionCalendar", out var calendar) ||
            calendar.ValueKind != JsonValueKind.Object) {
            throw new InvalidOperationException("GitHub contribution calendar was not returned by GraphQL.");
        }

        var totalContributions = 0;
        if (GitHubGraphQlCli.TryGetProperty(calendar, "totalContributions", out var totalValue) &&
            totalValue.ValueKind == JsonValueKind.Number &&
            totalValue.TryGetInt32(out var parsedTotal)) {
            totalContributions = parsedTotal;
        }

        var days = new List<GitHubContributionDay>();
        if (GitHubGraphQlCli.TryGetProperty(calendar, "weeks", out var weeks) && weeks.ValueKind == JsonValueKind.Array) {
            foreach (var week in weeks.EnumerateArray()) {
                if (!GitHubGraphQlCli.TryGetProperty(week, "contributionDays", out var contributionDays) ||
                    contributionDays.ValueKind != JsonValueKind.Array) {
                    continue;
                }

                foreach (var day in contributionDays.EnumerateArray()) {
                    var dateText = GitHubGraphQlCli.ReadString(day, "date");
                    if (!TryParseGitHubContributionDate(dateText, out var parsedDate)) {
                        continue;
                    }

                    var count = 0;
                    if (GitHubGraphQlCli.TryGetProperty(day, "contributionCount", out var countValue) &&
                        countValue.ValueKind == JsonValueKind.Number) {
                        count = countValue.GetInt32();
                    }

                    var weekday = 0;
                    if (GitHubGraphQlCli.TryGetProperty(day, "weekday", out var weekdayValue) &&
                        weekdayValue.ValueKind == JsonValueKind.Number) {
                        weekday = weekdayValue.GetInt32();
                    }

                    days.Add(new GitHubContributionDay(
                        parsedDate.Date,
                        count,
                        GitHubGraphQlCli.ReadString(day, "color"),
                        GitHubGraphQlCli.ReadString(day, "contributionLevel"),
                        weekday));
                }
            }
        }
        return new GitHubContributionCalendar(resolvedLogin, resolvedName, resolvedUrl, totalContributions, days);
    }

    private static DateTimeOffset NormalizeDay(DateTimeOffset value) {
        return new DateTimeOffset(value.UtcDateTime.Date, TimeSpan.Zero);
    }

    private static int ReadInt32(JsonElement obj, string name) {
        if (GitHubGraphQlCli.TryGetProperty(obj, name, out var value) &&
            value.ValueKind == JsonValueKind.Number &&
            value.TryGetInt32(out var parsed)) {
            return parsed;
        }

        return 0;
    }

    private static bool TryParseGitHubContributionDate(string? value, out DateTime parsedDateUtc) {
        parsedDateUtc = default;
        if (string.IsNullOrWhiteSpace(value)) {
            return false;
        }

        if (!DateTime.TryParseExact(
                value.Trim(),
                "yyyy-MM-dd",
                CultureInfo.InvariantCulture,
                DateTimeStyles.None,
                out var parsed)) {
            return false;
        }

        parsedDateUtc = DateTime.SpecifyKind(parsed.Date, DateTimeKind.Utc);
        return true;
    }
}

internal sealed class GitHubContributionCalendar {
    public GitHubContributionCalendar(
        string login,
        string? name,
        string? profileUrl,
        int totalContributions,
        IReadOnlyList<GitHubContributionDay> days,
        GitHubContributionCollectionSummary? summary = null) {
        Login = login;
        Name = name;
        ProfileUrl = profileUrl;
        TotalContributions = totalContributions;
        Days = days ?? Array.Empty<GitHubContributionDay>();
        Summary = summary ?? new GitHubContributionCollectionSummary();
    }

    public string Login { get; }
    public string? Name { get; }
    public string? ProfileUrl { get; }
    public int TotalContributions { get; }
    public IReadOnlyList<GitHubContributionDay> Days { get; }
    public GitHubContributionCollectionSummary Summary { get; }
}

internal sealed class GitHubContributionCollectionSummary {
    public GitHubContributionCollectionSummary(
        int commitContributions = 0,
        int issueContributions = 0,
        int pullRequestContributions = 0,
        int pullRequestReviewContributions = 0,
        int repositoryContributions = 0,
        int restrictedContributions = 0,
        int repositoriesWithCommits = 0,
        int repositoriesWithIssues = 0,
        int repositoriesWithPullRequests = 0,
        int repositoriesWithPullRequestReviews = 0) {
        CommitContributions = Math.Max(0, commitContributions);
        IssueContributions = Math.Max(0, issueContributions);
        PullRequestContributions = Math.Max(0, pullRequestContributions);
        PullRequestReviewContributions = Math.Max(0, pullRequestReviewContributions);
        RepositoryContributions = Math.Max(0, repositoryContributions);
        RestrictedContributions = Math.Max(0, restrictedContributions);
        RepositoriesWithCommits = Math.Max(0, repositoriesWithCommits);
        RepositoriesWithIssues = Math.Max(0, repositoriesWithIssues);
        RepositoriesWithPullRequests = Math.Max(0, repositoriesWithPullRequests);
        RepositoriesWithPullRequestReviews = Math.Max(0, repositoriesWithPullRequestReviews);
    }

    public int CommitContributions { get; }
    public int IssueContributions { get; }
    public int PullRequestContributions { get; }
    public int PullRequestReviewContributions { get; }
    public int RepositoryContributions { get; }
    public int RestrictedContributions { get; }
    public int RepositoriesWithCommits { get; }
    public int RepositoriesWithIssues { get; }
    public int RepositoriesWithPullRequests { get; }
    public int RepositoriesWithPullRequestReviews { get; }
}

internal sealed class GitHubContributionDay {
    public GitHubContributionDay(DateTime date, int contributionCount, string? color, string? contributionLevel, int weekday) {
        Date = date.Date;
        ContributionCount = contributionCount;
        Color = color;
        ContributionLevel = contributionLevel;
        Weekday = weekday;
    }

    public DateTime Date { get; }
    public int ContributionCount { get; }
    public string? Color { get; }
    public string? ContributionLevel { get; }
    public int Weekday { get; }
}
