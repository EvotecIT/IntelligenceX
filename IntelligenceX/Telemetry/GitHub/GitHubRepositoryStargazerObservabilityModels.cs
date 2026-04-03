using System;
using System.Collections.Generic;
using System.Globalization;
using IntelligenceX.Telemetry.Usage;

namespace IntelligenceX.Telemetry.GitHub;

/// <summary>
/// Contract for persisted GitHub stargazer observations.
/// </summary>
public interface IGitHubRepositoryStargazerSnapshotStore {
    /// <summary>
    /// Inserts or replaces a stargazer snapshot.
    /// </summary>
    /// <param name="snapshot">Stargazer snapshot to persist.</param>
    void Upsert(GitHubRepositoryStargazerSnapshotRecord snapshot);

    /// <summary>
    /// Returns stargazer snapshots for one repository ordered by capture time and stargazer login.
    /// </summary>
    /// <param name="repositoryNameWithOwner">Repository in owner/name form.</param>
    /// <returns>Ordered stargazer snapshots for the repository.</returns>
    IReadOnlyList<GitHubRepositoryStargazerSnapshotRecord> GetByRepository(string repositoryNameWithOwner);

    /// <summary>
    /// Returns stargazer snapshots for one GitHub login ordered by capture time and repository.
    /// </summary>
    /// <param name="stargazerLogin">GitHub login of the stargazer.</param>
    /// <returns>Ordered stargazer snapshots for the login.</returns>
    IReadOnlyList<GitHubRepositoryStargazerSnapshotRecord> GetByStargazer(string stargazerLogin);

    /// <summary>
    /// Returns all known stargazer snapshots ordered by repository, capture time, and login.
    /// </summary>
    /// <returns>All persisted stargazer snapshots.</returns>
    IReadOnlyList<GitHubRepositoryStargazerSnapshotRecord> GetAll();
}

/// <summary>
/// Canonical persisted observation of a repository stargazer at a point in time.
/// </summary>
public sealed class GitHubRepositoryStargazerSnapshotRecord {
    /// <summary>
    /// Initializes a new stargazer snapshot record.
    /// </summary>
    /// <param name="id">Stable snapshot identifier.</param>
    /// <param name="repositoryNameWithOwner">Repository in owner/name form.</param>
    /// <param name="stargazerLogin">GitHub login of the stargazer.</param>
    /// <param name="capturedAtUtc">UTC time when the stargazer observation was captured.</param>
    public GitHubRepositoryStargazerSnapshotRecord(
        string id,
        string repositoryNameWithOwner,
        string stargazerLogin,
        DateTimeOffset capturedAtUtc) {
        if (string.IsNullOrWhiteSpace(id)) {
            throw new ArgumentException("Stargazer snapshot id is required.", nameof(id));
        }
        if (string.IsNullOrWhiteSpace(stargazerLogin)) {
            throw new ArgumentException("Stargazer login is required.", nameof(stargazerLogin));
        }

        Id = id.Trim();
        RepositoryNameWithOwner = GitHubRepositoryIdentity.NormalizeNameWithOwner(repositoryNameWithOwner);
        StargazerLogin = stargazerLogin.Trim();
        CapturedAtUtc = capturedAtUtc.ToUniversalTime();
    }

    /// <summary>
    /// Gets the stable snapshot identifier.
    /// </summary>
    public string Id { get; }

    /// <summary>
    /// Gets the repository in owner/name form.
    /// </summary>
    public string RepositoryNameWithOwner { get; }

    /// <summary>
    /// Gets the GitHub login of the stargazer.
    /// </summary>
    public string StargazerLogin { get; }

    /// <summary>
    /// Gets the UTC time when the observation was captured.
    /// </summary>
    public DateTimeOffset CapturedAtUtc { get; }

    /// <summary>
    /// Gets or sets the UTC time when the repository was originally starred when available.
    /// </summary>
    public DateTimeOffset? StarredAtUtc { get; set; }

    /// <summary>
    /// Gets or sets the optional stargazer profile URL.
    /// </summary>
    public string? ProfileUrl { get; set; }

    /// <summary>
    /// Gets or sets the optional stargazer avatar URL.
    /// </summary>
    public string? AvatarUrl { get; set; }

    /// <summary>
    /// Creates a deterministic snapshot identifier from repository, login, and capture time.
    /// </summary>
    /// <param name="repositoryNameWithOwner">Repository in owner/name form.</param>
    /// <param name="stargazerLogin">GitHub login of the stargazer.</param>
    /// <param name="capturedAtUtc">UTC capture time.</param>
    /// <returns>Stable stargazer snapshot identifier.</returns>
    public static string CreateStableId(
        string repositoryNameWithOwner,
        string stargazerLogin,
        DateTimeOffset capturedAtUtc) {
        var canonical = GitHubRepositoryIdentity.NormalizeNameWithOwner(repositoryNameWithOwner).ToLowerInvariant()
                        + "|"
                        + stargazerLogin.Trim().ToLowerInvariant()
                        + "|"
                        + capturedAtUtc.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture);
        return "ghstarsnap_" + UsageTelemetryIdentity.ComputeStableHash(canonical, 12);
    }
}
