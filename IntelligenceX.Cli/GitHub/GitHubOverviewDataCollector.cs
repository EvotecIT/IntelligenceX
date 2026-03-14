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
    private readonly Func<string, Task<IReadOnlyList<string>>> _resolveCorrelatedOwnersAsync;

    public GitHubOverviewDataCollector()
        : this(
            new GitHubContributionCalendarClient(),
            owners => new GitHubRepositoryImpactClient().GetRepositoryImpactAsync(owners),
            login => new GitHubOwnerScopeResolver().ResolveAdministeredOwnersAsync(login)) {
    }

    internal GitHubOverviewDataCollector(
        GitHubContributionCalendarClient calendarClient,
        Func<IReadOnlyList<string>, Task<GitHubRepositoryImpactSummary>> queryRepositoryImpactAsync,
        Func<string, Task<IReadOnlyList<string>>>? resolveCorrelatedOwnersAsync = null) {
        _calendarClient = calendarClient ?? throw new ArgumentNullException(nameof(calendarClient));
        _queryRepositoryImpactAsync = queryRepositoryImpactAsync ?? throw new ArgumentNullException(nameof(queryRepositoryImpactAsync));
        _resolveCorrelatedOwnersAsync = resolveCorrelatedOwnersAsync ?? (_ => Task.FromResult<IReadOnlyList<string>>(Array.Empty<string>()));
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

        var explicitlyRequestedOwners = (repositoryOwners ?? Array.Empty<string>())
            .Where(static value => !string.IsNullOrWhiteSpace(value))
            .Select(static value => value.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var correlatedOwners = await ResolveCorrelatedOwnersAsync(trimmedLogin, explicitlyRequestedOwners).ConfigureAwait(false);
        var owners = NormalizeOwners(trimmedLogin, explicitlyRequestedOwners, correlatedOwners);
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
            AutoCorrelatedOwners: correlatedOwners,
            OwnerImpactOnly: false);
    }

    public async Task<GitHubOverviewDataSnapshot> CollectOwnerImpactOnlyAsync(IReadOnlyList<string> repositoryOwners) {
        var owners = NormalizeOwners(
            login: null,
            explicitlyRequestedOwners: (repositoryOwners ?? Array.Empty<string>())
                .Where(static value => !string.IsNullOrWhiteSpace(value))
                .Select(static value => value.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray(),
            correlatedOwners: Array.Empty<string>());
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
            AutoCorrelatedOwners: Array.Empty<string>(),
            OwnerImpactOnly: true);
    }

    private async Task<IReadOnlyList<string>> ResolveCorrelatedOwnersAsync(string login, IReadOnlyList<string> explicitlyRequestedOwners) {
        var correlatedOwners = await _resolveCorrelatedOwnersAsync(login.Trim()).ConfigureAwait(false);
        var excludedOwners = new HashSet<string>(StringComparer.OrdinalIgnoreCase) {
            login.Trim()
        };

        foreach (var owner in explicitlyRequestedOwners ?? Array.Empty<string>()) {
            excludedOwners.Add(owner);
        }

        return correlatedOwners
            .Where(static value => !string.IsNullOrWhiteSpace(value))
            .Select(static value => value.Trim())
            .Where(value => !excludedOwners.Contains(value))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static IReadOnlyList<string> NormalizeOwners(
        string? login,
        IReadOnlyList<string> explicitlyRequestedOwners,
        IReadOnlyList<string> correlatedOwners) {
        IEnumerable<string> values = explicitlyRequestedOwners ?? Array.Empty<string>();

        if (!string.IsNullOrWhiteSpace(login)) {
            values = values.Append(login.Trim());
        }

        values = values.Concat(correlatedOwners ?? Array.Empty<string>());

        return values
            .Where(static value => !string.IsNullOrWhiteSpace(value))
            .Select(static value => value.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static DateTimeOffset CreateComparableYearEnd(int year, int month, int day) {
        var clampedDay = Math.Min(day, DateTime.DaysInMonth(year, month));
        return new DateTimeOffset(year, month, clampedDay, 0, 0, 0, TimeSpan.Zero);
    }
}
