using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using IntelligenceX.Telemetry.Usage;

namespace IntelligenceX.Telemetry.GitHub;

/// <summary>
/// Contract for persisted GitHub fork observations.
/// </summary>
public interface IGitHubRepositoryForkSnapshotStore {
    /// <summary>
    /// Inserts or replaces a fork snapshot.
    /// </summary>
    /// <param name="snapshot">Fork snapshot to persist.</param>
    void Upsert(GitHubRepositoryForkSnapshotRecord snapshot);

    /// <summary>
    /// Returns fork snapshots for one parent repository ordered by capture time and fork name.
    /// </summary>
    /// <param name="parentRepositoryNameWithOwner">Parent repository in owner/name form.</param>
    /// <returns>Ordered fork snapshots for the parent repository.</returns>
    IReadOnlyList<GitHubRepositoryForkSnapshotRecord> GetByParentRepository(string parentRepositoryNameWithOwner);

    /// <summary>
    /// Returns fork snapshots for one fork repository ordered by capture time.
    /// </summary>
    /// <param name="forkRepositoryNameWithOwner">Fork repository in owner/name form.</param>
    /// <returns>Ordered fork snapshots for the fork repository.</returns>
    IReadOnlyList<GitHubRepositoryForkSnapshotRecord> GetByForkRepository(string forkRepositoryNameWithOwner);

    /// <summary>
    /// Returns all known fork snapshots ordered by parent, capture time, and fork name.
    /// </summary>
    /// <returns>All persisted fork snapshots.</returns>
    IReadOnlyList<GitHubRepositoryForkSnapshotRecord> GetAll();
}

/// <summary>
/// Canonical persisted observation of a fork at a point in time.
/// </summary>
public sealed class GitHubRepositoryForkSnapshotRecord {
    /// <summary>
    /// Initializes a new fork snapshot record.
    /// </summary>
    /// <param name="id">Stable snapshot identifier.</param>
    /// <param name="parentRepositoryNameWithOwner">Parent repository in owner/name form.</param>
    /// <param name="forkRepositoryNameWithOwner">Fork repository in owner/name form.</param>
    /// <param name="capturedAtUtc">UTC time when the fork observation was captured.</param>
    /// <param name="score">Computed usefulness score.</param>
    /// <param name="tier">Computed usefulness tier.</param>
    /// <param name="stars">Fork star count.</param>
    /// <param name="forks">Fork count of the fork itself.</param>
    /// <param name="watchers">Fork watcher count.</param>
    /// <param name="openIssues">Fork open issue count.</param>
    public GitHubRepositoryForkSnapshotRecord(
        string id,
        string parentRepositoryNameWithOwner,
        string forkRepositoryNameWithOwner,
        DateTimeOffset capturedAtUtc,
        double score,
        string tier,
        int stars,
        int forks,
        int watchers,
        int openIssues) {
        if (string.IsNullOrWhiteSpace(id)) {
            throw new ArgumentException("Fork snapshot id is required.", nameof(id));
        }
        if (string.IsNullOrWhiteSpace(tier)) {
            throw new ArgumentException("Tier is required.", nameof(tier));
        }

        Id = id.Trim();
        ParentRepositoryNameWithOwner = GitHubRepositoryIdentity.NormalizeNameWithOwner(parentRepositoryNameWithOwner);
        ForkRepositoryNameWithOwner = GitHubRepositoryIdentity.NormalizeNameWithOwner(forkRepositoryNameWithOwner);
        CapturedAtUtc = capturedAtUtc.ToUniversalTime();
        Score = score;
        Tier = tier.Trim();
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
    /// Gets the parent repository in owner/name form.
    /// </summary>
    public string ParentRepositoryNameWithOwner { get; }

    /// <summary>
    /// Gets the fork repository in owner/name form.
    /// </summary>
    public string ForkRepositoryNameWithOwner { get; }

    /// <summary>
    /// Gets the UTC time when the observation was captured.
    /// </summary>
    public DateTimeOffset CapturedAtUtc { get; }

    /// <summary>
    /// Gets the computed usefulness score.
    /// </summary>
    public double Score { get; }

    /// <summary>
    /// Gets the computed usefulness tier.
    /// </summary>
    public string Tier { get; }

    /// <summary>
    /// Gets the fork star count.
    /// </summary>
    public int Stars { get; }

    /// <summary>
    /// Gets the fork count of the fork itself.
    /// </summary>
    public int Forks { get; }

    /// <summary>
    /// Gets the fork watcher count.
    /// </summary>
    public int Watchers { get; }

    /// <summary>
    /// Gets the fork open issue count.
    /// </summary>
    public int OpenIssues { get; }

    /// <summary>
    /// Gets or sets the optional fork URL.
    /// </summary>
    public string? Url { get; set; }

    /// <summary>
    /// Gets or sets the optional fork description.
    /// </summary>
    public string? Description { get; set; }

    /// <summary>
    /// Gets or sets the optional primary language.
    /// </summary>
    public string? PrimaryLanguage { get; set; }

    /// <summary>
    /// Gets or sets the optional last pushed timestamp.
    /// </summary>
    public DateTimeOffset? PushedAtUtc { get; set; }

    /// <summary>
    /// Gets or sets the optional last updated timestamp.
    /// </summary>
    public DateTimeOffset? UpdatedAtUtc { get; set; }

    /// <summary>
    /// Gets or sets the optional creation timestamp.
    /// </summary>
    public DateTimeOffset? CreatedAtUtc { get; set; }

    /// <summary>
    /// Gets or sets whether the fork is archived.
    /// </summary>
    public bool IsArchived { get; set; }

    /// <summary>
    /// Gets or sets the compact reason summary used for the score.
    /// </summary>
    public string? ReasonsSummary { get; set; }

    /// <summary>
    /// Creates a deterministic snapshot identifier from parent, fork, and capture time.
    /// </summary>
    /// <param name="parentRepositoryNameWithOwner">Parent repository in owner/name form.</param>
    /// <param name="forkRepositoryNameWithOwner">Fork repository in owner/name form.</param>
    /// <param name="capturedAtUtc">UTC capture time.</param>
    /// <returns>Stable fork snapshot identifier.</returns>
    public static string CreateStableId(
        string parentRepositoryNameWithOwner,
        string forkRepositoryNameWithOwner,
        DateTimeOffset capturedAtUtc) {
        var canonical = GitHubRepositoryIdentity.NormalizeNameWithOwner(parentRepositoryNameWithOwner).ToLowerInvariant()
                        + "|"
                        + GitHubRepositoryIdentity.NormalizeNameWithOwner(forkRepositoryNameWithOwner).ToLowerInvariant()
                        + "|"
                        + capturedAtUtc.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture);
        return "ghforksnap_" + UsageTelemetryIdentity.ComputeStableHash(canonical, 12);
    }
}

/// <summary>
/// Latest status change for a tracked fork.
/// </summary>
public sealed class GitHubRepositoryForkChange {
    internal GitHubRepositoryForkChange(
        string parentRepositoryNameWithOwner,
        string forkRepositoryNameWithOwner,
        DateTimeOffset? previousCapturedAtUtc,
        DateTimeOffset currentCapturedAtUtc,
        double score,
        double scoreDelta,
        int stars,
        int starDelta,
        int watchers,
        int watcherDelta,
        string tier,
        string status) {
        ParentRepositoryNameWithOwner = parentRepositoryNameWithOwner;
        ForkRepositoryNameWithOwner = forkRepositoryNameWithOwner;
        PreviousCapturedAtUtc = previousCapturedAtUtc;
        CurrentCapturedAtUtc = currentCapturedAtUtc;
        Score = score;
        ScoreDelta = scoreDelta;
        Stars = stars;
        StarDelta = starDelta;
        Watchers = watchers;
        WatcherDelta = watcherDelta;
        Tier = tier;
        Status = status;
    }

    /// <summary>
    /// Gets the parent repository in owner/name form.
    /// </summary>
    public string ParentRepositoryNameWithOwner { get; }

    /// <summary>
    /// Gets the fork repository in owner/name form.
    /// </summary>
    public string ForkRepositoryNameWithOwner { get; }

    /// <summary>
    /// Gets the previous observation time when available.
    /// </summary>
    public DateTimeOffset? PreviousCapturedAtUtc { get; }

    /// <summary>
    /// Gets the latest observation time.
    /// </summary>
    public DateTimeOffset CurrentCapturedAtUtc { get; }

    /// <summary>
    /// Gets the latest usefulness score.
    /// </summary>
    public double Score { get; }

    /// <summary>
    /// Gets the usefulness score delta versus the prior observation.
    /// </summary>
    public double ScoreDelta { get; }

    /// <summary>
    /// Gets the latest star count.
    /// </summary>
    public int Stars { get; }

    /// <summary>
    /// Gets the star delta versus the prior observation.
    /// </summary>
    public int StarDelta { get; }

    /// <summary>
    /// Gets the latest watcher count.
    /// </summary>
    public int Watchers { get; }

    /// <summary>
    /// Gets the watcher delta versus the prior observation.
    /// </summary>
    public int WatcherDelta { get; }

    /// <summary>
    /// Gets the latest usefulness tier.
    /// </summary>
    public string Tier { get; }

    /// <summary>
    /// Gets the derived status label such as new, rising, steady, cooling, or archived.
    /// </summary>
    public string Status { get; }
}

/// <summary>
/// Analytics helpers for persisted fork history.
/// </summary>
public static class GitHubRepositoryForkHistoryAnalytics {
    /// <summary>
    /// Builds one latest change record per fork.
    /// </summary>
    /// <param name="snapshots">Fork snapshots to analyze.</param>
    /// <returns>Latest per-fork changes ordered by parent repository and score.</returns>
    public static IReadOnlyList<GitHubRepositoryForkChange> BuildLatestChanges(IEnumerable<GitHubRepositoryForkSnapshotRecord> snapshots) {
        if (snapshots is null) {
            throw new ArgumentNullException(nameof(snapshots));
        }

        var changes = new List<GitHubRepositoryForkChange>();
        foreach (var group in snapshots
                     .GroupBy(static snapshot => snapshot.ParentRepositoryNameWithOwner + "|" + snapshot.ForkRepositoryNameWithOwner, StringComparer.OrdinalIgnoreCase)
                     .OrderBy(static group => group.Key, StringComparer.OrdinalIgnoreCase)) {
            var ordered = group
                .OrderBy(static snapshot => snapshot.CapturedAtUtc)
                .ThenBy(static snapshot => snapshot.Id, StringComparer.OrdinalIgnoreCase)
                .ToArray();
            if (ordered.Length == 0) {
                continue;
            }

            var current = ordered[ordered.Length - 1];
            var previous = ordered.Length > 1 ? ordered[ordered.Length - 2] : null;
            changes.Add(CreateChange(previous, current));
        }

        return changes
            .OrderBy(static change => change.ParentRepositoryNameWithOwner, StringComparer.OrdinalIgnoreCase)
            .ThenByDescending(static change => change.Score)
            .ThenBy(static change => change.ForkRepositoryNameWithOwner, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    /// <summary>
    /// Creates one status change between two fork snapshots.
    /// </summary>
    /// <param name="previous">Previous observation when available.</param>
    /// <param name="current">Current observation.</param>
    /// <returns>Latest fork change record.</returns>
    public static GitHubRepositoryForkChange CreateChange(
        GitHubRepositoryForkSnapshotRecord? previous,
        GitHubRepositoryForkSnapshotRecord current) {
        if (current is null) {
            throw new ArgumentNullException(nameof(current));
        }

        var scoreDelta = Math.Round(current.Score - (previous?.Score ?? 0d), 2);
        var starDelta = current.Stars - (previous?.Stars ?? 0);
        var watcherDelta = current.Watchers - (previous?.Watchers ?? 0);
        var status = previous is null
            ? "new"
            : current.IsArchived && !previous.IsArchived
                ? "archived"
                : scoreDelta >= 10d || starDelta >= 3 || watcherDelta >= 2
                    ? "rising"
                    : scoreDelta <= -10d
                        ? "cooling"
                        : "steady";

        return new GitHubRepositoryForkChange(
            current.ParentRepositoryNameWithOwner,
            current.ForkRepositoryNameWithOwner,
            previous?.CapturedAtUtc,
            current.CapturedAtUtc,
            current.Score,
            scoreDelta,
            current.Stars,
            starDelta,
            current.Watchers,
            watcherDelta,
            current.Tier,
            status);
    }
}
