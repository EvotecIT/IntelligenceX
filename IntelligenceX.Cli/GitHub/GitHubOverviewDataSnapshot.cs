using System;
using System.Collections.Generic;

namespace IntelligenceX.Cli.GitHub;

internal sealed record GitHubOverviewDataSnapshot(
    string? RequestedLogin,
    DateTimeOffset StartUtc,
    DateTimeOffset EndUtc,
    GitHubContributionCalendar? Calendar,
    GitHubContributionCalendar? PreviousYearCalendar,
    GitHubRepositoryImpactSummary? RepositoryImpact,
    IReadOnlyList<string> RepositoryOwners,
    IReadOnlyList<string> AutoCorrelatedOwners,
    bool OwnerImpactOnly);
