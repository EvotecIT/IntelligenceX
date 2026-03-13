using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;

namespace IntelligenceX.Cli.GitHub;

internal sealed class GitHubOverviewDataCollector {
    private const int TrailingDays = 365;
    private readonly GitHubContributionCalendarClient _calendarClient;
    private readonly Func<IReadOnlyList<string>, Task<GitHubRepositoryImpactSummary>> _queryRepositoryImpactAsync;

    public GitHubOverviewDataCollector()
        : this(
            new GitHubContributionCalendarClient(),
            owners => new GitHubRepositoryImpactClient().GetRepositoryImpactAsync(owners)) {
    }

    internal GitHubOverviewDataCollector(
        GitHubContributionCalendarClient calendarClient,
        Func<IReadOnlyList<string>, Task<GitHubRepositoryImpactSummary>> queryRepositoryImpactAsync) {
        _calendarClient = calendarClient ?? throw new ArgumentNullException(nameof(calendarClient));
        _queryRepositoryImpactAsync = queryRepositoryImpactAsync ?? throw new ArgumentNullException(nameof(queryRepositoryImpactAsync));
    }

    public async Task<GitHubOverviewDataSnapshot> CollectAsync(string login, IReadOnlyList<string>? repositoryOwners = null) {
        if (string.IsNullOrWhiteSpace(login)) {
            throw new InvalidOperationException("GitHub user login is required.");
        }

        var trimmedLogin = login.Trim();
        var endUtc = DateTimeOffset.UtcNow.Date;
        var startUtc = endUtc.AddDays(-(TrailingDays - 1));
        var currentYearStartUtc = new DateTimeOffset(endUtc.Year, 1, 1, 0, 0, 0, TimeSpan.Zero);
        var previousYearStartUtc = currentYearStartUtc.AddYears(-1);
        var previousYearEndUtc = CreateComparableYearEnd(previousYearStartUtc.Year, endUtc.Month, endUtc.Day);

        var calendar = await _calendarClient
            .GetUserContributionCalendarAsync(trimmedLogin, startUtc, endUtc)
            .ConfigureAwait(false);
        var previousYearCalendar = await _calendarClient
            .GetUserContributionCalendarAsync(trimmedLogin, previousYearStartUtc, previousYearEndUtc)
            .ConfigureAwait(false);

        var owners = NormalizeOwners(trimmedLogin, repositoryOwners);
        var repositoryImpact = owners.Count == 0
            ? null
            : await _queryRepositoryImpactAsync(owners).ConfigureAwait(false);

        return new GitHubOverviewDataSnapshot(
            RequestedLogin: trimmedLogin,
            StartUtc: startUtc,
            EndUtc: endUtc,
            Calendar: calendar,
            PreviousYearCalendar: previousYearCalendar,
            RepositoryImpact: repositoryImpact,
            RepositoryOwners: owners,
            OwnerImpactOnly: false);
    }

    public async Task<GitHubOverviewDataSnapshot> CollectOwnerImpactOnlyAsync(IReadOnlyList<string> repositoryOwners) {
        var owners = NormalizeOwners(login: null, repositoryOwners);
        if (owners.Count == 0) {
            throw new InvalidOperationException("At least one GitHub owner is required.");
        }

        var endUtc = DateTimeOffset.UtcNow.Date;
        var startUtc = endUtc.AddDays(-(TrailingDays - 1));
        var repositoryImpact = await _queryRepositoryImpactAsync(owners).ConfigureAwait(false);

        return new GitHubOverviewDataSnapshot(
            RequestedLogin: null,
            StartUtc: startUtc,
            EndUtc: endUtc,
            Calendar: null,
            PreviousYearCalendar: null,
            RepositoryImpact: repositoryImpact,
            RepositoryOwners: owners,
            OwnerImpactOnly: true);
    }

    private static IReadOnlyList<string> NormalizeOwners(string? login, IReadOnlyList<string>? repositoryOwners) {
        var values = (repositoryOwners ?? Array.Empty<string>())
            .Where(static value => !string.IsNullOrWhiteSpace(value))
            .Select(static value => value.Trim());

        if (!string.IsNullOrWhiteSpace(login)) {
            values = values.Append(login.Trim());
        }

        return values
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static DateTimeOffset CreateComparableYearEnd(int year, int month, int day) {
        var clampedDay = Math.Min(day, DateTime.DaysInMonth(year, month));
        return new DateTimeOffset(year, month, clampedDay, 0, 0, 0, TimeSpan.Zero);
    }
}
