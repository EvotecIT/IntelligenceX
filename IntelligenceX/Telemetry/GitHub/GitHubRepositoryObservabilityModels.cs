using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using IntelligenceX.Telemetry.Usage;

namespace IntelligenceX.Telemetry.GitHub;

/// <summary>
/// Contract for persisted GitHub repository watch definitions.
/// </summary>
public interface IGitHubRepositoryWatchStore {
    /// <summary>
    /// Inserts or replaces a watch definition.
    /// </summary>
    /// <param name="watch">Watch definition to persist.</param>
    void Upsert(GitHubRepositoryWatchRecord watch);

    /// <summary>
    /// Looks up a watch by its stable identifier.
    /// </summary>
    /// <param name="id">Stable watch identifier.</param>
    /// <param name="watch">Resolved watch when one exists.</param>
    /// <returns><see langword="true"/> when the watch exists.</returns>
    bool TryGet(string id, out GitHubRepositoryWatchRecord watch);

    /// <summary>
    /// Looks up a watch by canonical repository name.
    /// </summary>
    /// <param name="repositoryNameWithOwner">Repository name in owner/name form.</param>
    /// <param name="watch">Resolved watch when one exists.</param>
    /// <returns><see langword="true"/> when the watch exists.</returns>
    bool TryGetByRepository(string repositoryNameWithOwner, out GitHubRepositoryWatchRecord watch);

    /// <summary>
    /// Returns all known watch definitions.
    /// </summary>
    /// <returns>Ordered repository watch definitions.</returns>
    IReadOnlyList<GitHubRepositoryWatchRecord> GetAll();
}

/// <summary>
/// Contract for persisted GitHub repository snapshots.
/// </summary>
public interface IGitHubRepositorySnapshotStore {
    /// <summary>
    /// Inserts or replaces a repository snapshot.
    /// </summary>
    /// <param name="snapshot">Snapshot to persist.</param>
    void Upsert(GitHubRepositorySnapshotRecord snapshot);

    /// <summary>
    /// Looks up a snapshot by its stable identifier.
    /// </summary>
    /// <param name="id">Stable snapshot identifier.</param>
    /// <param name="snapshot">Resolved snapshot when one exists.</param>
    /// <returns><see langword="true"/> when the snapshot exists.</returns>
    bool TryGet(string id, out GitHubRepositorySnapshotRecord snapshot);

    /// <summary>
    /// Returns the newest snapshot for a watch when one exists.
    /// </summary>
    /// <param name="watchId">Stable watch identifier.</param>
    /// <param name="snapshot">Newest snapshot when one exists.</param>
    /// <returns><see langword="true"/> when at least one snapshot exists.</returns>
    bool TryGetLatest(string watchId, out GitHubRepositorySnapshotRecord snapshot);

    /// <summary>
    /// Returns all snapshots for a single watch ordered by capture time.
    /// </summary>
    /// <param name="watchId">Stable watch identifier.</param>
    /// <returns>Ordered snapshots for the watch.</returns>
    IReadOnlyList<GitHubRepositorySnapshotRecord> GetByWatch(string watchId);

    /// <summary>
    /// Returns all known snapshots ordered by repository and capture time.
    /// </summary>
    /// <returns>All repository snapshots.</returns>
    IReadOnlyList<GitHubRepositorySnapshotRecord> GetAll();
}

/// <summary>
/// Canonical GitHub repository watch definition for tray and reporting features.
/// </summary>
public sealed class GitHubRepositoryWatchRecord {
    /// <summary>
    /// Initializes a new repository watch definition.
    /// </summary>
    /// <param name="id">Stable watch identifier.</param>
    /// <param name="repositoryNameWithOwner">Repository name in owner/name form.</param>
    /// <param name="createdAtUtc">UTC timestamp when the watch was created.</param>
    public GitHubRepositoryWatchRecord(
        string id,
        string repositoryNameWithOwner,
        DateTimeOffset createdAtUtc) {
        if (string.IsNullOrWhiteSpace(id)) {
            throw new ArgumentException("Watch id is required.", nameof(id));
        }

        Id = id.Trim();
        RepositoryNameWithOwner = GitHubRepositoryIdentity.NormalizeNameWithOwner(repositoryNameWithOwner);
        CreatedAtUtc = createdAtUtc.ToUniversalTime();
        Enabled = true;
    }

    /// <summary>
    /// Gets the stable watch identifier.
    /// </summary>
    public string Id { get; }

    /// <summary>
    /// Gets the canonical repository name in owner/name form.
    /// </summary>
    public string RepositoryNameWithOwner { get; }

    /// <summary>
    /// Gets the repository owner login.
    /// </summary>
    public string Owner => GitHubRepositoryIdentity.SplitNameWithOwner(RepositoryNameWithOwner).Owner;

    /// <summary>
    /// Gets the repository name.
    /// </summary>
    public string Repository => GitHubRepositoryIdentity.SplitNameWithOwner(RepositoryNameWithOwner).Repository;

    /// <summary>
    /// Gets or sets the optional user-facing label.
    /// </summary>
    public string? DisplayName { get; set; }

    /// <summary>
    /// Gets or sets the optional watch category or grouping label.
    /// </summary>
    public string? Category { get; set; }

    /// <summary>
    /// Gets or sets optional freeform notes for the watch.
    /// </summary>
    public string? Notes { get; set; }

    /// <summary>
    /// Gets or sets whether the watch is active.
    /// </summary>
    public bool Enabled { get; set; }

    /// <summary>
    /// Gets the UTC timestamp when the watch was created.
    /// </summary>
    public DateTimeOffset CreatedAtUtc { get; }

    /// <summary>
    /// Creates a deterministic watch identifier from a repository name.
    /// </summary>
    /// <param name="repositoryNameWithOwner">Repository name in owner/name form.</param>
    /// <returns>Stable repository watch identifier.</returns>
    public static string CreateStableId(string repositoryNameWithOwner) {
        var normalized = GitHubRepositoryIdentity.NormalizeNameWithOwner(repositoryNameWithOwner);
        return "ghwatch_" + UsageTelemetryIdentity.ComputeStableHash(normalized.ToLowerInvariant(), 12);
    }
}

/// <summary>
/// Canonical repository snapshot used to track stars, forks, watchers, and related trend metrics over time.
/// </summary>
public sealed class GitHubRepositorySnapshotRecord {
    /// <summary>
    /// Initializes a new repository snapshot.
    /// </summary>
    /// <param name="id">Stable snapshot identifier.</param>
    /// <param name="watchId">Stable watch identifier.</param>
    /// <param name="repositoryNameWithOwner">Repository name in owner/name form.</param>
    /// <param name="capturedAtUtc">UTC time when the snapshot was captured.</param>
    /// <param name="stars">Repository star count.</param>
    /// <param name="forks">Repository fork count.</param>
    /// <param name="watchers">Repository watcher count.</param>
    /// <param name="openIssues">Repository open issue count.</param>
    public GitHubRepositorySnapshotRecord(
        string id,
        string watchId,
        string repositoryNameWithOwner,
        DateTimeOffset capturedAtUtc,
        int stars,
        int forks,
        int watchers,
        int openIssues) {
        if (string.IsNullOrWhiteSpace(id)) {
            throw new ArgumentException("Snapshot id is required.", nameof(id));
        }
        if (string.IsNullOrWhiteSpace(watchId)) {
            throw new ArgumentException("Watch id is required.", nameof(watchId));
        }

        Id = id.Trim();
        WatchId = watchId.Trim();
        RepositoryNameWithOwner = GitHubRepositoryIdentity.NormalizeNameWithOwner(repositoryNameWithOwner);
        CapturedAtUtc = capturedAtUtc.ToUniversalTime();
        Stars = Math.Max(0, stars);
        Forks = Math.Max(0, forks);
        Watchers = Math.Max(0, watchers);
        OpenIssues = Math.Max(0, openIssues);
    }

    /// <summary>
    /// Gets the stable snapshot identifier.
    /// </summary>
    public string Id { get; }

    /// <summary>
    /// Gets the watch definition that produced the snapshot.
    /// </summary>
    public string WatchId { get; }

    /// <summary>
    /// Gets the canonical repository name in owner/name form.
    /// </summary>
    public string RepositoryNameWithOwner { get; }

    /// <summary>
    /// Gets the UTC time when the snapshot was captured.
    /// </summary>
    public DateTimeOffset CapturedAtUtc { get; }

    /// <summary>
    /// Gets the repository star count.
    /// </summary>
    public int Stars { get; }

    /// <summary>
    /// Gets the repository fork count.
    /// </summary>
    public int Forks { get; }

    /// <summary>
    /// Gets the repository watcher count.
    /// </summary>
    public int Watchers { get; }

    /// <summary>
    /// Gets the repository open issue count.
    /// </summary>
    public int OpenIssues { get; }

    /// <summary>
    /// Gets or sets the optional repository description.
    /// </summary>
    public string? Description { get; set; }

    /// <summary>
    /// Gets or sets the optional primary language.
    /// </summary>
    public string? PrimaryLanguage { get; set; }

    /// <summary>
    /// Gets or sets the optional repository URL.
    /// </summary>
    public string? Url { get; set; }

    /// <summary>
    /// Gets or sets the last pushed timestamp when available.
    /// </summary>
    public DateTimeOffset? PushedAtUtc { get; set; }

    /// <summary>
    /// Gets or sets whether the repository is archived.
    /// </summary>
    public bool IsArchived { get; set; }

    /// <summary>
    /// Gets or sets whether the repository itself is a fork.
    /// </summary>
    public bool IsFork { get; set; }

    /// <summary>
    /// Creates a deterministic snapshot id from watch and capture time.
    /// </summary>
    /// <param name="watchId">Stable watch identifier.</param>
    /// <param name="capturedAtUtc">UTC capture time for the snapshot.</param>
    /// <returns>Stable repository snapshot identifier.</returns>
    public static string CreateStableId(string watchId, DateTimeOffset capturedAtUtc) {
        if (string.IsNullOrWhiteSpace(watchId)) {
            throw new ArgumentException("Watch id is required.", nameof(watchId));
        }

        var canonical = watchId.Trim() + "|" + capturedAtUtc.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture);
        return "ghsnap_" + UsageTelemetryIdentity.ComputeStableHash(canonical, 12);
    }
}

/// <summary>
/// Delta between two repository snapshots.
/// </summary>
public sealed class GitHubRepositorySnapshotDelta {
    internal GitHubRepositorySnapshotDelta(
        string repositoryNameWithOwner,
        DateTimeOffset? previousCapturedAtUtc,
        DateTimeOffset currentCapturedAtUtc,
        int stars,
        int forks,
        int watchers,
        int openIssues,
        int starDelta,
        int forkDelta,
        int watcherDelta,
        int openIssueDelta) {
        RepositoryNameWithOwner = repositoryNameWithOwner;
        PreviousCapturedAtUtc = previousCapturedAtUtc;
        CurrentCapturedAtUtc = currentCapturedAtUtc;
        Stars = stars;
        Forks = forks;
        Watchers = watchers;
        OpenIssues = openIssues;
        StarDelta = starDelta;
        ForkDelta = forkDelta;
        WatcherDelta = watcherDelta;
        OpenIssueDelta = openIssueDelta;
    }

    /// <summary>
    /// Gets the repository name in owner/name form.
    /// </summary>
    public string RepositoryNameWithOwner { get; }

    /// <summary>
    /// Gets the prior snapshot time when available.
    /// </summary>
    public DateTimeOffset? PreviousCapturedAtUtc { get; }

    /// <summary>
    /// Gets the current snapshot time.
    /// </summary>
    public DateTimeOffset CurrentCapturedAtUtc { get; }

    /// <summary>
    /// Gets the current star count.
    /// </summary>
    public int Stars { get; }

    /// <summary>
    /// Gets the current fork count.
    /// </summary>
    public int Forks { get; }

    /// <summary>
    /// Gets the current watcher count.
    /// </summary>
    public int Watchers { get; }

    /// <summary>
    /// Gets the current open issue count.
    /// </summary>
    public int OpenIssues { get; }

    /// <summary>
    /// Gets the star delta versus the prior snapshot.
    /// </summary>
    public int StarDelta { get; }

    /// <summary>
    /// Gets the fork delta versus the prior snapshot.
    /// </summary>
    public int ForkDelta { get; }

    /// <summary>
    /// Gets the watcher delta versus the prior snapshot.
    /// </summary>
    public int WatcherDelta { get; }

    /// <summary>
    /// Gets the open issue delta versus the prior snapshot.
    /// </summary>
    public int OpenIssueDelta { get; }
}

/// <summary>
/// Helpers for repository watch identity and normalization.
/// </summary>
public static class GitHubRepositoryIdentity {
    /// <summary>
    /// Normalizes a repository name into owner/name form.
    /// </summary>
    /// <param name="repositoryNameWithOwner">Repository name in owner/name form.</param>
    /// <returns>Canonical repository name.</returns>
    public static string NormalizeNameWithOwner(string repositoryNameWithOwner) {
        if (string.IsNullOrWhiteSpace(repositoryNameWithOwner)) {
            throw new ArgumentException("Repository name is required.", nameof(repositoryNameWithOwner));
        }

        var parts = repositoryNameWithOwner.Trim()
            .Split(new[] { '/' }, StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length != 2) {
            throw new ArgumentException("Repository name must be in owner/name format.", nameof(repositoryNameWithOwner));
        }

        var owner = parts[0].Trim();
        var repository = parts[1].Trim();
        if (string.IsNullOrWhiteSpace(owner) || string.IsNullOrWhiteSpace(repository)) {
            throw new ArgumentException("Repository name must be in owner/name format.", nameof(repositoryNameWithOwner));
        }

        return owner + "/" + repository;
    }

    /// <summary>
    /// Splits a canonical repository name into owner and repository parts.
    /// </summary>
    /// <param name="repositoryNameWithOwner">Repository name in owner/name form.</param>
    /// <returns>Owner and repository pair.</returns>
    public static (string Owner, string Repository) SplitNameWithOwner(string repositoryNameWithOwner) {
        var normalized = NormalizeNameWithOwner(repositoryNameWithOwner);
        var separator = normalized.IndexOf('/');
        return (normalized.Substring(0, separator), normalized.Substring(separator + 1));
    }
}

/// <summary>
/// Analytics helpers for repository snapshots.
/// </summary>
public static class GitHubRepositorySnapshotAnalytics {
    /// <summary>
    /// Builds day-over-day deltas for the supplied snapshots, using the latest capture per UTC day.
    /// </summary>
    /// <param name="snapshots">Snapshots to analyze.</param>
    /// <returns>Daily deltas grouped by repository.</returns>
    public static IReadOnlyList<GitHubRepositorySnapshotDelta> BuildDailyDeltas(IEnumerable<GitHubRepositorySnapshotRecord> snapshots) {
        if (snapshots is null) {
            throw new ArgumentNullException(nameof(snapshots));
        }

        var deltas = new List<GitHubRepositorySnapshotDelta>();
        foreach (var repositoryGroup in snapshots
                     .GroupBy(static snapshot => snapshot.RepositoryNameWithOwner, StringComparer.OrdinalIgnoreCase)
                     .OrderBy(static group => group.Key, StringComparer.OrdinalIgnoreCase)) {
            GitHubRepositorySnapshotRecord? previous = null;
            foreach (var current in repositoryGroup
                         .OrderBy(static snapshot => snapshot.CapturedAtUtc)
                         .GroupBy(static snapshot => snapshot.CapturedAtUtc.UtcDateTime.Date)
                         .Select(static group => group.OrderByDescending(static snapshot => snapshot.CapturedAtUtc).First())
                         .OrderBy(static snapshot => snapshot.CapturedAtUtc)) {
                deltas.Add(CreateDelta(previous, current));
                previous = current;
            }
        }

        return deltas;
    }

    /// <summary>
    /// Creates a delta from two snapshots of the same repository.
    /// </summary>
    /// <param name="previous">Previous snapshot when available.</param>
    /// <param name="current">Current snapshot.</param>
    /// <returns>Snapshot delta.</returns>
    public static GitHubRepositorySnapshotDelta CreateDelta(
        GitHubRepositorySnapshotRecord? previous,
        GitHubRepositorySnapshotRecord current) {
        if (current is null) {
            throw new ArgumentNullException(nameof(current));
        }

        if (previous is not null &&
            !string.Equals(previous.RepositoryNameWithOwner, current.RepositoryNameWithOwner, StringComparison.OrdinalIgnoreCase)) {
            throw new ArgumentException("Snapshot delta inputs must refer to the same repository.", nameof(previous));
        }

        return new GitHubRepositorySnapshotDelta(
            current.RepositoryNameWithOwner,
            previous?.CapturedAtUtc,
            current.CapturedAtUtc,
            current.Stars,
            current.Forks,
            current.Watchers,
            current.OpenIssues,
            current.Stars - (previous?.Stars ?? 0),
            current.Forks - (previous?.Forks ?? 0),
            current.Watchers - (previous?.Watchers ?? 0),
            current.OpenIssues - (previous?.OpenIssues ?? 0));
    }
}
