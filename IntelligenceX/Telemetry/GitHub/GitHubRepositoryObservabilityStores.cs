using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;

namespace IntelligenceX.Telemetry.GitHub;

/// <summary>
/// Thread-safe in-memory store for repository watch definitions.
/// </summary>
public sealed class InMemoryGitHubRepositoryWatchStore : IGitHubRepositoryWatchStore {
    private readonly ConcurrentDictionary<string, GitHubRepositoryWatchRecord> _watches =
        new(StringComparer.OrdinalIgnoreCase);

    /// <inheritdoc />
    public void Upsert(GitHubRepositoryWatchRecord watch) {
        if (watch is null) {
            throw new ArgumentNullException(nameof(watch));
        }

        _watches[watch.Id] = watch;
    }

    /// <inheritdoc />
    public bool TryGet(string id, out GitHubRepositoryWatchRecord watch) {
        if (string.IsNullOrWhiteSpace(id)) {
            watch = null!;
            return false;
        }

        return _watches.TryGetValue(id.Trim(), out watch!);
    }

    /// <inheritdoc />
    public bool TryGetByRepository(string repositoryNameWithOwner, out GitHubRepositoryWatchRecord watch) {
        var normalized = GitHubRepositoryIdentity.NormalizeNameWithOwner(repositoryNameWithOwner);
        watch = _watches.Values.FirstOrDefault(value =>
            string.Equals(value.RepositoryNameWithOwner, normalized, StringComparison.OrdinalIgnoreCase))!;
        return watch is not null;
    }

    /// <inheritdoc />
    public IReadOnlyList<GitHubRepositoryWatchRecord> GetAll() {
        return _watches.Values
            .OrderBy(value => value.RepositoryNameWithOwner, StringComparer.OrdinalIgnoreCase)
            .ThenBy(value => value.Id, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }
}

/// <summary>
/// Thread-safe in-memory store for repository snapshots.
/// </summary>
public sealed class InMemoryGitHubRepositorySnapshotStore : IGitHubRepositorySnapshotStore {
    private readonly ConcurrentDictionary<string, GitHubRepositorySnapshotRecord> _snapshots =
        new(StringComparer.OrdinalIgnoreCase);

    /// <inheritdoc />
    public void Upsert(GitHubRepositorySnapshotRecord snapshot) {
        if (snapshot is null) {
            throw new ArgumentNullException(nameof(snapshot));
        }

        _snapshots[snapshot.Id] = snapshot;
    }

    /// <inheritdoc />
    public bool TryGet(string id, out GitHubRepositorySnapshotRecord snapshot) {
        if (string.IsNullOrWhiteSpace(id)) {
            snapshot = null!;
            return false;
        }

        return _snapshots.TryGetValue(id.Trim(), out snapshot!);
    }

    /// <inheritdoc />
    public bool TryGetLatest(string watchId, out GitHubRepositorySnapshotRecord snapshot) {
        if (string.IsNullOrWhiteSpace(watchId)) {
            snapshot = null!;
            return false;
        }

        snapshot = _snapshots.Values
            .Where(value => string.Equals(value.WatchId, watchId.Trim(), StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(static value => value.CapturedAtUtc)
            .ThenByDescending(value => value.Id, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault()!;
        return snapshot is not null;
    }

    /// <inheritdoc />
    public IReadOnlyList<GitHubRepositorySnapshotRecord> GetByWatch(string watchId) {
        if (string.IsNullOrWhiteSpace(watchId)) {
            return Array.Empty<GitHubRepositorySnapshotRecord>();
        }

        return _snapshots.Values
            .Where(value => string.Equals(value.WatchId, watchId.Trim(), StringComparison.OrdinalIgnoreCase))
            .OrderBy(static value => value.CapturedAtUtc)
            .ThenBy(value => value.Id, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    /// <inheritdoc />
    public IReadOnlyList<GitHubRepositorySnapshotRecord> GetAll() {
        return _snapshots.Values
            .OrderBy(value => value.RepositoryNameWithOwner, StringComparer.OrdinalIgnoreCase)
            .ThenBy(static value => value.CapturedAtUtc)
            .ThenBy(value => value.Id, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }
}
