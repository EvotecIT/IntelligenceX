using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text.Json;
using System.Threading.Tasks;

namespace IntelligenceX.Cli.GitHub;

internal sealed class GitHubContributionCalendarClient {
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

        var totalContributions = 0;
        var days = new List<GitHubContributionDay>();
        string? resolvedLogin = null;
        string? resolvedName = null;
        string? resolvedUrl = null;

        var windowStart = from;
        while (windowStart <= to) {
            var nextWindowExclusive = windowStart.AddYears(1);
            var windowEnd = nextWindowExclusive <= to
                ? nextWindowExclusive.AddDays(-1)
                : to;

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

            totalContributions += window.TotalContributions;
            foreach (var day in window.Days) {
                if (days.Count == 0 || days[days.Count - 1].Date != day.Date) {
                    days.Add(day);
                }
            }

            windowStart = windowEnd.AddDays(1);
        }

        if (string.IsNullOrWhiteSpace(resolvedLogin)) {
            throw new InvalidOperationException($"GitHub user '{login}' was not found.");
        }

        return new GitHubContributionCalendar(resolvedLogin, resolvedName, resolvedUrl, totalContributions, days);
    }

    private static async Task<GitHubContributionCalendar> GetContributionCalendarWindowAsync(
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

        var root = await GitHubGraphQlCli.QueryAsync(
            query,
            TimeSpan.FromSeconds(90),
            ("login", login.Trim()),
            ("from", from.ToUniversalTime().ToString("o", CultureInfo.InvariantCulture)),
            ("to", to.ToUniversalTime().ToString("o", CultureInfo.InvariantCulture))).ConfigureAwait(false);

        if (!GitHubGraphQlCli.TryGetProperty(root, "data", out var data) ||
            !GitHubGraphQlCli.TryGetProperty(data, "user", out var user) ||
            user.ValueKind != JsonValueKind.Object) {
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
                    if (!DateTime.TryParse(dateText, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var parsedDate)) {
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
}

internal sealed class GitHubContributionCalendar {
    public GitHubContributionCalendar(
        string login,
        string? name,
        string? profileUrl,
        int totalContributions,
        IReadOnlyList<GitHubContributionDay> days) {
        Login = login;
        Name = name;
        ProfileUrl = profileUrl;
        TotalContributions = totalContributions;
        Days = days ?? Array.Empty<GitHubContributionDay>();
    }

    public string Login { get; }
    public string? Name { get; }
    public string? ProfileUrl { get; }
    public int TotalContributions { get; }
    public IReadOnlyList<GitHubContributionDay> Days { get; }
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
