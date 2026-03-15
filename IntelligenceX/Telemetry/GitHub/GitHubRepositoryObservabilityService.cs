using System;
using System.Collections.Generic;
using System.Linq;

namespace IntelligenceX.Telemetry.GitHub;

/// <summary>
/// Coordinates repository watch management and persisted snapshot history.
/// </summary>
public sealed class GitHubRepositoryObservabilityService {
    private readonly IGitHubRepositoryWatchStore _watchStore;
    private readonly IGitHubRepositorySnapshotStore _snapshotStore;

    /// <summary>
    /// Initializes a new observability service.
    /// </summary>
    /// <param name="watchStore">Watch-definition store.</param>
    /// <param name="snapshotStore">Snapshot-history store.</param>
    public GitHubRepositoryObservabilityService(
        IGitHubRepositoryWatchStore watchStore,
        IGitHubRepositorySnapshotStore snapshotStore) {
        _watchStore = watchStore ?? throw new ArgumentNullException(nameof(watchStore));
        _snapshotStore = snapshotStore ?? throw new ArgumentNullException(nameof(snapshotStore));
    }

    /// <summary>
    /// Ensures a watch exists for the supplied repository.
    /// </summary>
    /// <param name="repositoryNameWithOwner">Repository name in owner/name form.</param>
    /// <param name="createdAtUtc">Optional watch creation time.</param>
    /// <param name="displayName">Optional user-facing label.</param>
    /// <param name="category">Optional grouping label.</param>
    /// <param name="notes">Optional freeform notes.</param>
    /// <param name="enabled">Optional enabled-state override.</param>
    /// <returns>The persisted watch definition.</returns>
    public GitHubRepositoryWatchRecord EnsureWatch(
        string repositoryNameWithOwner,
        DateTimeOffset? createdAtUtc = null,
        string? displayName = null,
        string? category = null,
        string? notes = null,
        bool? enabled = null) {
        if (_watchStore.TryGetByRepository(repositoryNameWithOwner, out var existing)) {
            var updated = new GitHubRepositoryWatchRecord(existing.Id, existing.RepositoryNameWithOwner, existing.CreatedAtUtc) {
                DisplayName = displayName ?? existing.DisplayName,
                Category = category ?? existing.Category,
                Notes = notes ?? existing.Notes,
                Enabled = enabled ?? existing.Enabled
            };
            _watchStore.Upsert(updated);
            return updated;
        }

        var watch = new GitHubRepositoryWatchRecord(
            GitHubRepositoryWatchRecord.CreateStableId(repositoryNameWithOwner),
            repositoryNameWithOwner,
            createdAtUtc ?? DateTimeOffset.UtcNow) {
            DisplayName = displayName,
            Category = category,
            Notes = notes,
            Enabled = enabled ?? true
        };
        _watchStore.Upsert(watch);
        return watch;
    }

    /// <summary>
    /// Returns all known watches.
    /// </summary>
    /// <param name="enabledOnly">When true, returns only enabled watches.</param>
    /// <returns>Ordered watch definitions.</returns>
    public IReadOnlyList<GitHubRepositoryWatchRecord> GetWatches(bool enabledOnly = false) {
        var watches = _watchStore.GetAll();
        if (!enabledOnly) {
            return watches;
        }

        return watches.Where(static watch => watch.Enabled).ToArray();
    }

    /// <summary>
    /// Persists a snapshot and returns its latest delta versus the prior snapshot.
    /// </summary>
    /// <param name="snapshot">Snapshot to persist.</param>
    /// <returns>Delta versus the immediately prior snapshot when available.</returns>
    public GitHubRepositorySnapshotDelta RecordSnapshot(GitHubRepositorySnapshotRecord snapshot) {
        if (snapshot is null) {
            throw new ArgumentNullException(nameof(snapshot));
        }

        var previous = GetPreviousSnapshot(snapshot.WatchId, snapshot.Id, snapshot.CapturedAtUtc);
        _snapshotStore.Upsert(snapshot);
        return GitHubRepositorySnapshotAnalytics.CreateDelta(previous, snapshot);
    }

    /// <summary>
    /// Returns all persisted snapshots.
    /// </summary>
    /// <param name="watchId">Optional watch identifier filter.</param>
    /// <returns>Ordered snapshot history.</returns>
    public IReadOnlyList<GitHubRepositorySnapshotRecord> GetSnapshots(string? watchId = null) {
        if (string.IsNullOrWhiteSpace(watchId)) {
            return _snapshotStore.GetAll();
        }

        return _snapshotStore.GetByWatch(watchId!.Trim());
    }

    /// <summary>
    /// Returns the latest per-watch deltas across known watches.
    /// </summary>
    /// <param name="enabledOnly">When true, includes only enabled watches.</param>
    /// <returns>Latest per-watch deltas ordered by repository.</returns>
    public IReadOnlyList<GitHubRepositorySnapshotDelta> GetLatestDeltas(bool enabledOnly = true) {
        var deltas = new List<GitHubRepositorySnapshotDelta>();
        foreach (var watch in GetWatches(enabledOnly)) {
            var snapshots = _snapshotStore.GetByWatch(watch.Id);
            if (snapshots.Count == 0) {
                continue;
            }

            var latest = snapshots[snapshots.Count - 1];
            var previous = snapshots.Count > 1 ? snapshots[snapshots.Count - 2] : null;
            deltas.Add(GitHubRepositorySnapshotAnalytics.CreateDelta(previous, latest));
        }

        return deltas
            .OrderBy(static delta => delta.RepositoryNameWithOwner, StringComparer.OrdinalIgnoreCase)
            .ThenByDescending(static delta => delta.CurrentCapturedAtUtc)
            .ToArray();
    }

    private GitHubRepositorySnapshotRecord? GetPreviousSnapshot(string watchId, string snapshotId, DateTimeOffset capturedAtUtc) {
        return _snapshotStore.GetByWatch(watchId)
            .Where(snapshot =>
                !string.Equals(snapshot.Id, snapshotId, StringComparison.OrdinalIgnoreCase) &&
                snapshot.CapturedAtUtc <= capturedAtUtc)
            .OrderByDescending(static snapshot => snapshot.CapturedAtUtc)
            .ThenByDescending(snapshot => snapshot.Id, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault();
    }
}
