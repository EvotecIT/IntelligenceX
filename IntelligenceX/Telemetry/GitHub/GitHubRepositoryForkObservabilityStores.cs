using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;

namespace IntelligenceX.Telemetry.GitHub;

internal sealed class InMemoryGitHubRepositoryForkSnapshotStore : IGitHubRepositoryForkSnapshotStore {
    private readonly ConcurrentDictionary<string, GitHubRepositoryForkSnapshotRecord> _snapshots =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, DateTimeOffset> _capturedAtByParentRepository =
        new(StringComparer.OrdinalIgnoreCase);

    /// <inheritdoc />
    public void Upsert(GitHubRepositoryForkSnapshotRecord snapshot) {
        if (snapshot is null) {
            throw new ArgumentNullException(nameof(snapshot));
        }

        _snapshots[snapshot.Id] = snapshot;
        _capturedAtByParentRepository[snapshot.ParentRepositoryNameWithOwner] = snapshot.CapturedAtUtc;
    }

    /// <inheritdoc />
    public void MarkParentRepositoryCaptured(string parentRepositoryNameWithOwner, DateTimeOffset capturedAtUtc) {
        var normalized = GitHubRepositoryIdentity.NormalizeNameWithOwner(parentRepositoryNameWithOwner);
        _capturedAtByParentRepository[normalized] = capturedAtUtc.ToUniversalTime();
    }

    /// <inheritdoc />
    public IReadOnlyList<GitHubRepositoryForkSnapshotRecord> GetByParentRepository(string parentRepositoryNameWithOwner) {
        var normalized = GitHubRepositoryIdentity.NormalizeNameWithOwner(parentRepositoryNameWithOwner);
        return _snapshots.Values
            .Where(value => string.Equals(value.ParentRepositoryNameWithOwner, normalized, StringComparison.OrdinalIgnoreCase))
            .OrderBy(static value => value.CapturedAtUtc)
            .ThenBy(value => value.ForkRepositoryNameWithOwner, StringComparer.OrdinalIgnoreCase)
            .ThenBy(value => value.Id, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    /// <inheritdoc />
    public IReadOnlyList<GitHubRepositoryForkSnapshotRecord> GetByForkRepository(string forkRepositoryNameWithOwner) {
        var normalized = GitHubRepositoryIdentity.NormalizeNameWithOwner(forkRepositoryNameWithOwner);
        return _snapshots.Values
            .Where(value => string.Equals(value.ForkRepositoryNameWithOwner, normalized, StringComparison.OrdinalIgnoreCase))
            .OrderBy(static value => value.CapturedAtUtc)
            .ThenBy(value => value.ParentRepositoryNameWithOwner, StringComparer.OrdinalIgnoreCase)
            .ThenBy(value => value.Id, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    /// <inheritdoc />
    public IReadOnlyList<GitHubRepositoryForkSnapshotRecord> GetAll() {
        return _snapshots.Values
            .OrderBy(value => value.ParentRepositoryNameWithOwner, StringComparer.OrdinalIgnoreCase)
            .ThenBy(static value => value.CapturedAtUtc)
            .ThenBy(value => value.ForkRepositoryNameWithOwner, StringComparer.OrdinalIgnoreCase)
            .ThenBy(value => value.Id, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    /// <inheritdoc />
    public DateTimeOffset? GetLatestCaptureAtUtcByParentRepository(string parentRepositoryNameWithOwner) {
        var normalized = GitHubRepositoryIdentity.NormalizeNameWithOwner(parentRepositoryNameWithOwner);
        var latestSnapshotCaptureAtUtc = _snapshots.Values
            .Where(value => string.Equals(value.ParentRepositoryNameWithOwner, normalized, StringComparison.OrdinalIgnoreCase))
            .Select(static value => (DateTimeOffset?)value.CapturedAtUtc)
            .OrderByDescending(static value => value)
            .FirstOrDefault();
        return _capturedAtByParentRepository.TryGetValue(normalized, out var captureStatusAtUtc)
            ? !latestSnapshotCaptureAtUtc.HasValue || captureStatusAtUtc > latestSnapshotCaptureAtUtc.Value
                ? captureStatusAtUtc
                : latestSnapshotCaptureAtUtc
            : latestSnapshotCaptureAtUtc;
    }
}
