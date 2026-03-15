using System;
using System.Globalization;
using IntelligenceX.Telemetry.GitHub;

namespace IntelligenceX.Cli.GitHub;

internal static class GitHubRepositoryObservabilityMapper {
    public static GitHubRepositoryWatchRecord CreateWatch(
        string repositoryNameWithOwner,
        DateTimeOffset? createdAtUtc = null,
        string? displayName = null,
        string? category = null,
        string? notes = null) {
        var watch = new GitHubRepositoryWatchRecord(
            GitHubRepositoryWatchRecord.CreateStableId(repositoryNameWithOwner),
            repositoryNameWithOwner,
            createdAtUtc ?? DateTimeOffset.UtcNow) {
            DisplayName = displayName,
            Category = category,
            Notes = notes
        };

        return watch;
    }

    public static GitHubRepositorySnapshotRecord CreateSnapshot(
        GitHubRepositoryImpactRepository repository,
        DateTimeOffset capturedAtUtc,
        string? watchId = null) {
        ArgumentNullException.ThrowIfNull(repository);

        var snapshot = new GitHubRepositorySnapshotRecord(
            GitHubRepositorySnapshotRecord.CreateStableId(
                watchId ?? GitHubRepositoryWatchRecord.CreateStableId(repository.NameWithOwner),
                capturedAtUtc),
            watchId ?? GitHubRepositoryWatchRecord.CreateStableId(repository.NameWithOwner),
            repository.NameWithOwner,
            capturedAtUtc,
            repository.Stars,
            repository.Forks,
            repository.Watchers,
            repository.OpenIssues) {
            Description = repository.Description,
            PrimaryLanguage = repository.PrimaryLanguage,
            Url = repository.Url,
            PushedAtUtc = TryParseGitHubTimestamp(repository.PushedAt),
            IsArchived = repository.IsArchived,
            IsFork = repository.IsFork
        };

        return snapshot;
    }

    public static GitHubRepositoryForkSnapshotRecord CreateForkSnapshot(
        string parentRepositoryNameWithOwner,
        GitHubRepositoryForkInsight insight,
        DateTimeOffset capturedAtUtc) {
        ArgumentNullException.ThrowIfNull(insight);

        var snapshot = new GitHubRepositoryForkSnapshotRecord(
            GitHubRepositoryForkSnapshotRecord.CreateStableId(
                parentRepositoryNameWithOwner,
                insight.Fork.RepositoryNameWithOwner,
                capturedAtUtc),
            parentRepositoryNameWithOwner,
            insight.Fork.RepositoryNameWithOwner,
            capturedAtUtc,
            insight.Score,
            insight.Tier,
            insight.Fork.Stars,
            insight.Fork.Forks,
            insight.Fork.Watchers,
            insight.Fork.OpenIssues) {
            Url = insight.Fork.Url,
            Description = insight.Fork.Description,
            PrimaryLanguage = insight.Fork.PrimaryLanguage,
            PushedAtUtc = TryParseGitHubTimestamp(insight.Fork.PushedAt),
            UpdatedAtUtc = TryParseGitHubTimestamp(insight.Fork.UpdatedAt),
            CreatedAtUtc = TryParseGitHubTimestamp(insight.Fork.CreatedAt),
            IsArchived = insight.Fork.IsArchived,
            ReasonsSummary = insight.Reasons.Count == 0 ? null : string.Join(" | ", insight.Reasons)
        };

        return snapshot;
    }

    private static DateTimeOffset? TryParseGitHubTimestamp(string? value) {
        if (string.IsNullOrWhiteSpace(value)) {
            return null;
        }

        return DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var parsed)
            ? parsed.ToUniversalTime()
            : null;
    }
}
