using System;
using System.Collections.Generic;
using System.Linq;

namespace IntelligenceX.Telemetry.GitHub;

internal static class GitHubDashboardRepositoryRanking {
    internal static IReadOnlyList<GitHubRepoInfo> BuildTopRepositories(
        IEnumerable<GitHubRepoInfo>? repositories,
        int limit = 8) {
        if (repositories is null || limit <= 0) {
            return Array.Empty<GitHubRepoInfo>();
        }

        return repositories
            .Where(static repository => !string.IsNullOrWhiteSpace(repository?.NameWithOwner))
            .GroupBy(
                static repository => GitHubRepositoryIdentity.NormalizeNameWithOwner(repository.NameWithOwner),
                StringComparer.OrdinalIgnoreCase)
            .Select(static group => group
                .OrderByDescending(static repository => repository.Stars)
                .ThenByDescending(static repository => repository.Forks)
                .ThenBy(static repository => repository.NameWithOwner, StringComparer.OrdinalIgnoreCase)
                .First())
            .OrderByDescending(static repository => repository.Stars)
            .ThenByDescending(static repository => repository.Forks)
            .ThenBy(static repository => repository.NameWithOwner, StringComparer.OrdinalIgnoreCase)
            .Take(limit)
            .ToArray();
    }
}
