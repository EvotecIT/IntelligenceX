#if !NETSTANDARD2_0
using System;
using System.Collections.Generic;
using System.Data;
using System.Globalization;
using System.IO;
using DBAClientX;

namespace IntelligenceX.Telemetry.GitHub;

/// <summary>
/// SQLite-backed store for repository watch definitions.
/// </summary>
public sealed class SqliteGitHubRepositoryWatchStore : IGitHubRepositoryWatchStore, IDisposable {
    private readonly object _gate = new();
    private readonly SQLite _db = new();
    private readonly string _dbPath;

    /// <summary>
    /// Initializes a new SQLite-backed watch store.
    /// </summary>
    public SqliteGitHubRepositoryWatchStore(string dbPath) {
        if (string.IsNullOrWhiteSpace(dbPath)) {
            throw new ArgumentException("Database path cannot be empty.", nameof(dbPath));
        }

        _dbPath = Path.GetFullPath(dbPath.Trim());
        SqliteGitHubObservabilitySchema.EnsureSchema(_db, _dbPath);
    }

    /// <inheritdoc />
    public void Upsert(GitHubRepositoryWatchRecord watch) {
        if (watch is null) {
            throw new ArgumentNullException(nameof(watch));
        }

        lock (_gate) {
            _db.ExecuteNonQuery(
                _dbPath,
                @"
INSERT INTO ix_github_repository_watches (
  id,
  repository_name_with_owner,
  display_name,
  category,
  notes,
  enabled,
  created_at_utc
)
VALUES (
  @id,
  @repository_name_with_owner,
  @display_name,
  @category,
  @notes,
  @enabled,
  @created_at_utc
)
ON CONFLICT(id) DO UPDATE SET
  repository_name_with_owner = excluded.repository_name_with_owner,
  display_name = excluded.display_name,
  category = excluded.category,
  notes = excluded.notes,
  enabled = excluded.enabled,
  created_at_utc = excluded.created_at_utc;",
                parameters: new Dictionary<string, object?> {
                    ["@id"] = watch.Id,
                    ["@repository_name_with_owner"] = watch.RepositoryNameWithOwner,
                    ["@display_name"] = SqliteGitHubObservabilitySchema.Normalize(watch.DisplayName),
                    ["@category"] = SqliteGitHubObservabilitySchema.Normalize(watch.Category),
                    ["@notes"] = SqliteGitHubObservabilitySchema.Normalize(watch.Notes),
                    ["@enabled"] = watch.Enabled ? 1 : 0,
                    ["@created_at_utc"] = watch.CreatedAtUtc.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture)
                });
        }
    }

    /// <inheritdoc />
    public bool TryGet(string id, out GitHubRepositoryWatchRecord watch) {
        if (string.IsNullOrWhiteSpace(id)) {
            watch = null!;
            return false;
        }

        lock (_gate) {
            var table = SqliteGitHubObservabilitySchema.QueryAsTable(_db.Query(
                _dbPath,
                @"
SELECT
  id,
  repository_name_with_owner,
  display_name,
  category,
  notes,
  enabled,
  created_at_utc
FROM ix_github_repository_watches
WHERE id = @id
LIMIT 1;",
                parameters: new Dictionary<string, object?> {
                    ["@id"] = id.Trim()
                }));

            if (table is null || table.Rows.Count == 0) {
                watch = null!;
                return false;
            }

            watch = ReadWatch(table.Rows[0]);
            return true;
        }
    }

    /// <inheritdoc />
    public bool TryGetByRepository(string repositoryNameWithOwner, out GitHubRepositoryWatchRecord watch) {
        var normalized = GitHubRepositoryIdentity.NormalizeNameWithOwner(repositoryNameWithOwner);
        lock (_gate) {
            var table = SqliteGitHubObservabilitySchema.QueryAsTable(_db.Query(
                _dbPath,
                @"
SELECT
  id,
  repository_name_with_owner,
  display_name,
  category,
  notes,
  enabled,
  created_at_utc
FROM ix_github_repository_watches
WHERE repository_name_with_owner = @repository_name_with_owner
LIMIT 1;",
                parameters: new Dictionary<string, object?> {
                    ["@repository_name_with_owner"] = normalized
                }));

            if (table is null || table.Rows.Count == 0) {
                watch = null!;
                return false;
            }

            watch = ReadWatch(table.Rows[0]);
            return true;
        }
    }

    /// <inheritdoc />
    public IReadOnlyList<GitHubRepositoryWatchRecord> GetAll() {
        lock (_gate) {
            var table = SqliteGitHubObservabilitySchema.QueryAsTable(_db.Query(
                _dbPath,
                @"
SELECT
  id,
  repository_name_with_owner,
  display_name,
  category,
  notes,
  enabled,
  created_at_utc
FROM ix_github_repository_watches
ORDER BY repository_name_with_owner, id;"));

            if (table is null || table.Rows.Count == 0) {
                return Array.Empty<GitHubRepositoryWatchRecord>();
            }

            var watches = new List<GitHubRepositoryWatchRecord>(table.Rows.Count);
            foreach (DataRow row in table.Rows) {
                watches.Add(ReadWatch(row));
            }
            return watches;
        }
    }

    /// <inheritdoc />
    public void Dispose() {
        _db.Dispose();
    }

    private static GitHubRepositoryWatchRecord ReadWatch(DataRow row) {
        var watch = new GitHubRepositoryWatchRecord(
            row["id"]?.ToString() ?? string.Empty,
            row["repository_name_with_owner"]?.ToString() ?? string.Empty,
            SqliteGitHubObservabilitySchema.ReadDateTimeOffset(row, "created_at_utc")) {
            DisplayName = SqliteGitHubObservabilitySchema.ReadOptionalString(row, "display_name"),
            Category = SqliteGitHubObservabilitySchema.ReadOptionalString(row, "category"),
            Notes = SqliteGitHubObservabilitySchema.ReadOptionalString(row, "notes"),
            Enabled = SqliteGitHubObservabilitySchema.ReadBoolean(row, "enabled")
        };
        return watch;
    }
}

/// <summary>
/// SQLite-backed store for repository snapshots.
/// </summary>
public sealed class SqliteGitHubRepositorySnapshotStore : IGitHubRepositorySnapshotStore, IDisposable {
    private readonly object _gate = new();
    private readonly SQLite _db = new();
    private readonly string _dbPath;

    /// <summary>
    /// Initializes a new SQLite-backed snapshot store.
    /// </summary>
    public SqliteGitHubRepositorySnapshotStore(string dbPath) {
        if (string.IsNullOrWhiteSpace(dbPath)) {
            throw new ArgumentException("Database path cannot be empty.", nameof(dbPath));
        }

        _dbPath = Path.GetFullPath(dbPath.Trim());
        SqliteGitHubObservabilitySchema.EnsureSchema(_db, _dbPath);
    }

    /// <inheritdoc />
    public void Upsert(GitHubRepositorySnapshotRecord snapshot) {
        if (snapshot is null) {
            throw new ArgumentNullException(nameof(snapshot));
        }

        lock (_gate) {
            _db.ExecuteNonQuery(
                _dbPath,
                @"
INSERT INTO ix_github_repository_snapshots (
  id,
  watch_id,
  repository_name_with_owner,
  captured_at_utc,
  stars,
  forks,
  watchers,
  open_issues,
  description,
  primary_language,
  url,
  pushed_at_utc,
  is_archived,
  is_fork
)
VALUES (
  @id,
  @watch_id,
  @repository_name_with_owner,
  @captured_at_utc,
  @stars,
  @forks,
  @watchers,
  @open_issues,
  @description,
  @primary_language,
  @url,
  @pushed_at_utc,
  @is_archived,
  @is_fork
)
ON CONFLICT(id) DO UPDATE SET
  watch_id = excluded.watch_id,
  repository_name_with_owner = excluded.repository_name_with_owner,
  captured_at_utc = excluded.captured_at_utc,
  stars = excluded.stars,
  forks = excluded.forks,
  watchers = excluded.watchers,
  open_issues = excluded.open_issues,
  description = excluded.description,
  primary_language = excluded.primary_language,
  url = excluded.url,
  pushed_at_utc = excluded.pushed_at_utc,
  is_archived = excluded.is_archived,
  is_fork = excluded.is_fork;",
                parameters: new Dictionary<string, object?> {
                    ["@id"] = snapshot.Id,
                    ["@watch_id"] = snapshot.WatchId,
                    ["@repository_name_with_owner"] = snapshot.RepositoryNameWithOwner,
                    ["@captured_at_utc"] = snapshot.CapturedAtUtc.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture),
                    ["@stars"] = snapshot.Stars,
                    ["@forks"] = snapshot.Forks,
                    ["@watchers"] = snapshot.Watchers,
                    ["@open_issues"] = snapshot.OpenIssues,
                    ["@description"] = SqliteGitHubObservabilitySchema.Normalize(snapshot.Description),
                    ["@primary_language"] = SqliteGitHubObservabilitySchema.Normalize(snapshot.PrimaryLanguage),
                    ["@url"] = SqliteGitHubObservabilitySchema.Normalize(snapshot.Url),
                    ["@pushed_at_utc"] = snapshot.PushedAtUtc?.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture),
                    ["@is_archived"] = snapshot.IsArchived ? 1 : 0,
                    ["@is_fork"] = snapshot.IsFork ? 1 : 0
                });
        }
    }

    /// <inheritdoc />
    public bool TryGet(string id, out GitHubRepositorySnapshotRecord snapshot) {
        if (string.IsNullOrWhiteSpace(id)) {
            snapshot = null!;
            return false;
        }

        lock (_gate) {
            var table = SqliteGitHubObservabilitySchema.QueryAsTable(_db.Query(
                _dbPath,
                @"
SELECT
  id,
  watch_id,
  repository_name_with_owner,
  captured_at_utc,
  stars,
  forks,
  watchers,
  open_issues,
  description,
  primary_language,
  url,
  pushed_at_utc,
  is_archived,
  is_fork
FROM ix_github_repository_snapshots
WHERE id = @id
LIMIT 1;",
                parameters: new Dictionary<string, object?> {
                    ["@id"] = id.Trim()
                }));

            if (table is null || table.Rows.Count == 0) {
                snapshot = null!;
                return false;
            }

            snapshot = ReadSnapshot(table.Rows[0]);
            return true;
        }
    }

    /// <inheritdoc />
    public bool TryGetLatest(string watchId, out GitHubRepositorySnapshotRecord snapshot) {
        if (string.IsNullOrWhiteSpace(watchId)) {
            snapshot = null!;
            return false;
        }

        lock (_gate) {
            var table = SqliteGitHubObservabilitySchema.QueryAsTable(_db.Query(
                _dbPath,
                @"
SELECT
  id,
  watch_id,
  repository_name_with_owner,
  captured_at_utc,
  stars,
  forks,
  watchers,
  open_issues,
  description,
  primary_language,
  url,
  pushed_at_utc,
  is_archived,
  is_fork
FROM ix_github_repository_snapshots
WHERE watch_id = @watch_id
ORDER BY captured_at_utc DESC, id DESC
LIMIT 1;",
                parameters: new Dictionary<string, object?> {
                    ["@watch_id"] = watchId.Trim()
                }));

            if (table is null || table.Rows.Count == 0) {
                snapshot = null!;
                return false;
            }

            snapshot = ReadSnapshot(table.Rows[0]);
            return true;
        }
    }

    /// <inheritdoc />
    public IReadOnlyList<GitHubRepositorySnapshotRecord> GetByWatch(string watchId) {
        if (string.IsNullOrWhiteSpace(watchId)) {
            return Array.Empty<GitHubRepositorySnapshotRecord>();
        }

        lock (_gate) {
            var table = SqliteGitHubObservabilitySchema.QueryAsTable(_db.Query(
                _dbPath,
                @"
SELECT
  id,
  watch_id,
  repository_name_with_owner,
  captured_at_utc,
  stars,
  forks,
  watchers,
  open_issues,
  description,
  primary_language,
  url,
  pushed_at_utc,
  is_archived,
  is_fork
FROM ix_github_repository_snapshots
WHERE watch_id = @watch_id
ORDER BY captured_at_utc, id;",
                parameters: new Dictionary<string, object?> {
                    ["@watch_id"] = watchId.Trim()
                }));

            if (table is null || table.Rows.Count == 0) {
                return Array.Empty<GitHubRepositorySnapshotRecord>();
            }

            var snapshots = new List<GitHubRepositorySnapshotRecord>(table.Rows.Count);
            foreach (DataRow row in table.Rows) {
                snapshots.Add(ReadSnapshot(row));
            }
            return snapshots;
        }
    }

    /// <inheritdoc />
    public IReadOnlyList<GitHubRepositorySnapshotRecord> GetAll() {
        lock (_gate) {
            var table = SqliteGitHubObservabilitySchema.QueryAsTable(_db.Query(
                _dbPath,
                @"
SELECT
  id,
  watch_id,
  repository_name_with_owner,
  captured_at_utc,
  stars,
  forks,
  watchers,
  open_issues,
  description,
  primary_language,
  url,
  pushed_at_utc,
  is_archived,
  is_fork
FROM ix_github_repository_snapshots
ORDER BY repository_name_with_owner, captured_at_utc, id;"));

            if (table is null || table.Rows.Count == 0) {
                return Array.Empty<GitHubRepositorySnapshotRecord>();
            }

            var snapshots = new List<GitHubRepositorySnapshotRecord>(table.Rows.Count);
            foreach (DataRow row in table.Rows) {
                snapshots.Add(ReadSnapshot(row));
            }
            return snapshots;
        }
    }

    /// <inheritdoc />
    public void Dispose() {
        _db.Dispose();
    }

    private static GitHubRepositorySnapshotRecord ReadSnapshot(DataRow row) {
        var snapshot = new GitHubRepositorySnapshotRecord(
            row["id"]?.ToString() ?? string.Empty,
            row["watch_id"]?.ToString() ?? string.Empty,
            row["repository_name_with_owner"]?.ToString() ?? string.Empty,
            SqliteGitHubObservabilitySchema.ReadDateTimeOffset(row, "captured_at_utc"),
            SqliteGitHubObservabilitySchema.ReadInt32(row, "stars"),
            SqliteGitHubObservabilitySchema.ReadInt32(row, "forks"),
            SqliteGitHubObservabilitySchema.ReadInt32(row, "watchers"),
            SqliteGitHubObservabilitySchema.ReadInt32(row, "open_issues")) {
            Description = SqliteGitHubObservabilitySchema.ReadOptionalString(row, "description"),
            PrimaryLanguage = SqliteGitHubObservabilitySchema.ReadOptionalString(row, "primary_language"),
            Url = SqliteGitHubObservabilitySchema.ReadOptionalString(row, "url"),
            PushedAtUtc = SqliteGitHubObservabilitySchema.ReadNullableDateTimeOffset(row, "pushed_at_utc"),
            IsArchived = SqliteGitHubObservabilitySchema.ReadBoolean(row, "is_archived"),
            IsFork = SqliteGitHubObservabilitySchema.ReadBoolean(row, "is_fork")
        };
        return snapshot;
    }
}

/// <summary>
/// SQLite-backed store for persisted fork observations.
/// </summary>
public sealed class SqliteGitHubRepositoryForkSnapshotStore : IGitHubRepositoryForkSnapshotStore, IDisposable {
    private readonly object _gate = new();
    private readonly SQLite _db = new();
    private readonly string _dbPath;

    /// <summary>
    /// Initializes a new SQLite-backed fork snapshot store.
    /// </summary>
    public SqliteGitHubRepositoryForkSnapshotStore(string dbPath) {
        if (string.IsNullOrWhiteSpace(dbPath)) {
            throw new ArgumentException("Database path cannot be empty.", nameof(dbPath));
        }

        _dbPath = Path.GetFullPath(dbPath.Trim());
        SqliteGitHubObservabilitySchema.EnsureSchema(_db, _dbPath);
    }

    /// <inheritdoc />
    public void Upsert(GitHubRepositoryForkSnapshotRecord snapshot) {
        if (snapshot is null) {
            throw new ArgumentNullException(nameof(snapshot));
        }

        lock (_gate) {
            _db.ExecuteNonQuery(
                _dbPath,
                @"
INSERT INTO ix_github_repository_fork_snapshots (
  id,
  parent_repository_name_with_owner,
  fork_repository_name_with_owner,
  captured_at_utc,
  score,
  tier,
  stars,
  forks,
  watchers,
  open_issues,
  url,
  description,
  primary_language,
  pushed_at_utc,
  updated_at_utc,
  created_at_utc,
  is_archived,
  reasons_summary
)
VALUES (
  @id,
  @parent_repository_name_with_owner,
  @fork_repository_name_with_owner,
  @captured_at_utc,
  @score,
  @tier,
  @stars,
  @forks,
  @watchers,
  @open_issues,
  @url,
  @description,
  @primary_language,
  @pushed_at_utc,
  @updated_at_utc,
  @created_at_utc,
  @is_archived,
  @reasons_summary
)
ON CONFLICT(id) DO UPDATE SET
  parent_repository_name_with_owner = excluded.parent_repository_name_with_owner,
  fork_repository_name_with_owner = excluded.fork_repository_name_with_owner,
  captured_at_utc = excluded.captured_at_utc,
  score = excluded.score,
  tier = excluded.tier,
  stars = excluded.stars,
  forks = excluded.forks,
  watchers = excluded.watchers,
  open_issues = excluded.open_issues,
  url = excluded.url,
  description = excluded.description,
  primary_language = excluded.primary_language,
  pushed_at_utc = excluded.pushed_at_utc,
  updated_at_utc = excluded.updated_at_utc,
  created_at_utc = excluded.created_at_utc,
  is_archived = excluded.is_archived,
  reasons_summary = excluded.reasons_summary;",
                parameters: new Dictionary<string, object?> {
                    ["@id"] = snapshot.Id,
                    ["@parent_repository_name_with_owner"] = snapshot.ParentRepositoryNameWithOwner,
                    ["@fork_repository_name_with_owner"] = snapshot.ForkRepositoryNameWithOwner,
                    ["@captured_at_utc"] = snapshot.CapturedAtUtc.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture),
                    ["@score"] = snapshot.Score,
                    ["@tier"] = snapshot.Tier,
                    ["@stars"] = snapshot.Stars,
                    ["@forks"] = snapshot.Forks,
                    ["@watchers"] = snapshot.Watchers,
                    ["@open_issues"] = snapshot.OpenIssues,
                    ["@url"] = SqliteGitHubObservabilitySchema.Normalize(snapshot.Url),
                    ["@description"] = SqliteGitHubObservabilitySchema.Normalize(snapshot.Description),
                    ["@primary_language"] = SqliteGitHubObservabilitySchema.Normalize(snapshot.PrimaryLanguage),
                    ["@pushed_at_utc"] = snapshot.PushedAtUtc?.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture),
                    ["@updated_at_utc"] = snapshot.UpdatedAtUtc?.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture),
                    ["@created_at_utc"] = snapshot.CreatedAtUtc?.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture),
                    ["@is_archived"] = snapshot.IsArchived ? 1 : 0,
                    ["@reasons_summary"] = SqliteGitHubObservabilitySchema.Normalize(snapshot.ReasonsSummary)
                });
        }
    }

    /// <inheritdoc />
    public void MarkParentRepositoryCaptured(string parentRepositoryNameWithOwner, DateTimeOffset capturedAtUtc) {
        var normalized = GitHubRepositoryIdentity.NormalizeNameWithOwner(parentRepositoryNameWithOwner);
        var normalizedCapturedAtUtc = capturedAtUtc.ToUniversalTime();
        lock (_gate) {
            _db.ExecuteNonQuery(
                _dbPath,
                @"
INSERT INTO ix_github_repository_fork_capture_status (
  parent_repository_name_with_owner,
  captured_at_utc
)
VALUES (
  @parent_repository_name_with_owner,
  @captured_at_utc
)
ON CONFLICT(parent_repository_name_with_owner) DO UPDATE SET
  captured_at_utc = excluded.captured_at_utc;",
                parameters: new Dictionary<string, object?> {
                    ["@parent_repository_name_with_owner"] = normalized,
                    ["@captured_at_utc"] = normalizedCapturedAtUtc.ToString("O", CultureInfo.InvariantCulture)
                });
        }
    }

    /// <inheritdoc />
    public IReadOnlyList<GitHubRepositoryForkSnapshotRecord> GetByParentRepository(string parentRepositoryNameWithOwner) {
        var normalized = GitHubRepositoryIdentity.NormalizeNameWithOwner(parentRepositoryNameWithOwner);
        lock (_gate) {
            var table = SqliteGitHubObservabilitySchema.QueryAsTable(_db.Query(
                _dbPath,
                @"
SELECT
  id,
  parent_repository_name_with_owner,
  fork_repository_name_with_owner,
  captured_at_utc,
  score,
  tier,
  stars,
  forks,
  watchers,
  open_issues,
  url,
  description,
  primary_language,
  pushed_at_utc,
  updated_at_utc,
  created_at_utc,
  is_archived,
  reasons_summary
FROM ix_github_repository_fork_snapshots
WHERE parent_repository_name_with_owner = @parent_repository_name_with_owner
ORDER BY captured_at_utc, fork_repository_name_with_owner, id;",
                parameters: new Dictionary<string, object?> {
                    ["@parent_repository_name_with_owner"] = normalized
                }));

            return ReadForkSnapshots(table);
        }
    }

    /// <inheritdoc />
    public IReadOnlyList<GitHubRepositoryForkSnapshotRecord> GetByForkRepository(string forkRepositoryNameWithOwner) {
        var normalized = GitHubRepositoryIdentity.NormalizeNameWithOwner(forkRepositoryNameWithOwner);
        lock (_gate) {
            var table = SqliteGitHubObservabilitySchema.QueryAsTable(_db.Query(
                _dbPath,
                @"
SELECT
  id,
  parent_repository_name_with_owner,
  fork_repository_name_with_owner,
  captured_at_utc,
  score,
  tier,
  stars,
  forks,
  watchers,
  open_issues,
  url,
  description,
  primary_language,
  pushed_at_utc,
  updated_at_utc,
  created_at_utc,
  is_archived,
  reasons_summary
FROM ix_github_repository_fork_snapshots
WHERE fork_repository_name_with_owner = @fork_repository_name_with_owner
ORDER BY captured_at_utc, parent_repository_name_with_owner, id;",
                parameters: new Dictionary<string, object?> {
                    ["@fork_repository_name_with_owner"] = normalized
                }));

            return ReadForkSnapshots(table);
        }
    }

    /// <inheritdoc />
    public IReadOnlyList<GitHubRepositoryForkSnapshotRecord> GetAll() {
        lock (_gate) {
            var table = SqliteGitHubObservabilitySchema.QueryAsTable(_db.Query(
                _dbPath,
                @"
SELECT
  id,
  parent_repository_name_with_owner,
  fork_repository_name_with_owner,
  captured_at_utc,
  score,
  tier,
  stars,
  forks,
  watchers,
  open_issues,
  url,
  description,
  primary_language,
  pushed_at_utc,
  updated_at_utc,
  created_at_utc,
  is_archived,
  reasons_summary
FROM ix_github_repository_fork_snapshots
ORDER BY parent_repository_name_with_owner, captured_at_utc, fork_repository_name_with_owner, id;"));

            return ReadForkSnapshots(table);
        }
    }

    /// <inheritdoc />
    public DateTimeOffset? GetLatestCaptureAtUtcByParentRepository(string parentRepositoryNameWithOwner) {
        var normalized = GitHubRepositoryIdentity.NormalizeNameWithOwner(parentRepositoryNameWithOwner);
        lock (_gate) {
            var table = SqliteGitHubObservabilitySchema.QueryAsTable(_db.Query(
                _dbPath,
                @"
SELECT captured_at_utc
FROM (
  SELECT captured_at_utc
  FROM ix_github_repository_fork_snapshots
  WHERE parent_repository_name_with_owner = @parent_repository_name_with_owner
  UNION ALL
  SELECT captured_at_utc
  FROM ix_github_repository_fork_capture_status
  WHERE parent_repository_name_with_owner = @parent_repository_name_with_owner
)
ORDER BY captured_at_utc DESC
LIMIT 1;",
                parameters: new Dictionary<string, object?> {
                    ["@parent_repository_name_with_owner"] = normalized
                }));

            if (table is null || table.Rows.Count == 0) {
                return null;
            }

            return SqliteGitHubObservabilitySchema.ReadNullableDateTimeOffset(table.Rows[0], "captured_at_utc");
        }
    }

    /// <inheritdoc />
    public void Dispose() {
        _db.Dispose();
    }

    private static IReadOnlyList<GitHubRepositoryForkSnapshotRecord> ReadForkSnapshots(DataTable? table) {
        if (table is null || table.Rows.Count == 0) {
            return Array.Empty<GitHubRepositoryForkSnapshotRecord>();
        }

        var snapshots = new List<GitHubRepositoryForkSnapshotRecord>(table.Rows.Count);
        foreach (DataRow row in table.Rows) {
            snapshots.Add(ReadForkSnapshot(row));
        }

        return snapshots;
    }

    private static GitHubRepositoryForkSnapshotRecord ReadForkSnapshot(DataRow row) {
        var snapshot = new GitHubRepositoryForkSnapshotRecord(
            row["id"]?.ToString() ?? string.Empty,
            row["parent_repository_name_with_owner"]?.ToString() ?? string.Empty,
            row["fork_repository_name_with_owner"]?.ToString() ?? string.Empty,
            SqliteGitHubObservabilitySchema.ReadDateTimeOffset(row, "captured_at_utc"),
            SqliteGitHubObservabilitySchema.ReadDouble(row, "score"),
            row["tier"]?.ToString() ?? string.Empty,
            SqliteGitHubObservabilitySchema.ReadInt32(row, "stars"),
            SqliteGitHubObservabilitySchema.ReadInt32(row, "forks"),
            SqliteGitHubObservabilitySchema.ReadInt32(row, "watchers"),
            SqliteGitHubObservabilitySchema.ReadInt32(row, "open_issues")) {
            Url = SqliteGitHubObservabilitySchema.ReadOptionalString(row, "url"),
            Description = SqliteGitHubObservabilitySchema.ReadOptionalString(row, "description"),
            PrimaryLanguage = SqliteGitHubObservabilitySchema.ReadOptionalString(row, "primary_language"),
            PushedAtUtc = SqliteGitHubObservabilitySchema.ReadNullableDateTimeOffset(row, "pushed_at_utc"),
            UpdatedAtUtc = SqliteGitHubObservabilitySchema.ReadNullableDateTimeOffset(row, "updated_at_utc"),
            CreatedAtUtc = SqliteGitHubObservabilitySchema.ReadNullableDateTimeOffset(row, "created_at_utc"),
            IsArchived = SqliteGitHubObservabilitySchema.ReadBoolean(row, "is_archived"),
            ReasonsSummary = SqliteGitHubObservabilitySchema.ReadOptionalString(row, "reasons_summary")
        };
        return snapshot;
    }
}

/// <summary>
/// SQLite-backed store for persisted stargazer observations.
/// </summary>
internal sealed class SqliteGitHubRepositoryStargazerSnapshotStore : IGitHubRepositoryStargazerSnapshotStore, IDisposable {
    private readonly object _gate = new();
    private readonly SQLite _db = new();
    private readonly string _dbPath;

    /// <summary>
    /// Initializes a new SQLite-backed stargazer snapshot store.
    /// </summary>
    public SqliteGitHubRepositoryStargazerSnapshotStore(string dbPath) {
        if (string.IsNullOrWhiteSpace(dbPath)) {
            throw new ArgumentException("Database path cannot be empty.", nameof(dbPath));
        }

        _dbPath = Path.GetFullPath(dbPath.Trim());
        SqliteGitHubObservabilitySchema.EnsureSchema(_db, _dbPath);
    }

    /// <inheritdoc />
    public void Upsert(GitHubRepositoryStargazerSnapshotRecord snapshot) {
        if (snapshot is null) {
            throw new ArgumentNullException(nameof(snapshot));
        }

        lock (_gate) {
            _db.ExecuteNonQuery(
                _dbPath,
                @"
INSERT INTO ix_github_repository_stargazer_snapshots (
  id,
  repository_name_with_owner,
  stargazer_login,
  captured_at_utc,
  starred_at_utc,
  profile_url,
  avatar_url
)
VALUES (
  @id,
  @repository_name_with_owner,
  @stargazer_login,
  @captured_at_utc,
  @starred_at_utc,
  @profile_url,
  @avatar_url
)
ON CONFLICT(id) DO UPDATE SET
  repository_name_with_owner = excluded.repository_name_with_owner,
  stargazer_login = excluded.stargazer_login,
  captured_at_utc = excluded.captured_at_utc,
  starred_at_utc = excluded.starred_at_utc,
  profile_url = excluded.profile_url,
  avatar_url = excluded.avatar_url;",
                parameters: new Dictionary<string, object?> {
                    ["@id"] = snapshot.Id,
                    ["@repository_name_with_owner"] = snapshot.RepositoryNameWithOwner,
                    ["@stargazer_login"] = snapshot.StargazerLogin,
                    ["@captured_at_utc"] = snapshot.CapturedAtUtc.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture),
                    ["@starred_at_utc"] = snapshot.StarredAtUtc?.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture),
                    ["@profile_url"] = SqliteGitHubObservabilitySchema.Normalize(snapshot.ProfileUrl),
                    ["@avatar_url"] = SqliteGitHubObservabilitySchema.Normalize(snapshot.AvatarUrl)
                });
        }
    }

    /// <inheritdoc />
    public void MarkRepositoryCaptured(string repositoryNameWithOwner, DateTimeOffset capturedAtUtc) {
        var normalized = GitHubRepositoryIdentity.NormalizeNameWithOwner(repositoryNameWithOwner);
        var normalizedCapturedAtUtc = capturedAtUtc.ToUniversalTime();
        lock (_gate) {
            _db.ExecuteNonQuery(
                _dbPath,
                @"
INSERT INTO ix_github_repository_stargazer_capture_status (
  repository_name_with_owner,
  captured_at_utc
)
VALUES (
  @repository_name_with_owner,
  @captured_at_utc
)
ON CONFLICT(repository_name_with_owner) DO UPDATE SET
  captured_at_utc = excluded.captured_at_utc;",
                parameters: new Dictionary<string, object?> {
                    ["@repository_name_with_owner"] = normalized,
                    ["@captured_at_utc"] = normalizedCapturedAtUtc.ToString("O", CultureInfo.InvariantCulture)
                });
        }
    }

    /// <inheritdoc />
    public IReadOnlyList<GitHubRepositoryStargazerSnapshotRecord> GetByRepository(string repositoryNameWithOwner) {
        var normalized = GitHubRepositoryIdentity.NormalizeNameWithOwner(repositoryNameWithOwner);
        lock (_gate) {
            var table = SqliteGitHubObservabilitySchema.QueryAsTable(_db.Query(
                _dbPath,
                @"
SELECT
  id,
  repository_name_with_owner,
  stargazer_login,
  captured_at_utc,
  starred_at_utc,
  profile_url,
  avatar_url
FROM ix_github_repository_stargazer_snapshots
WHERE repository_name_with_owner = @repository_name_with_owner
ORDER BY captured_at_utc, stargazer_login, id;",
                parameters: new Dictionary<string, object?> {
                    ["@repository_name_with_owner"] = normalized
                }));

            return ReadStargazerSnapshots(table);
        }
    }

    /// <inheritdoc />
    public IReadOnlyList<GitHubRepositoryStargazerSnapshotRecord> GetByStargazer(string stargazerLogin) {
        var normalized = stargazerLogin?.Trim();
        if (string.IsNullOrWhiteSpace(normalized)) {
            return Array.Empty<GitHubRepositoryStargazerSnapshotRecord>();
        }

        lock (_gate) {
            var table = SqliteGitHubObservabilitySchema.QueryAsTable(_db.Query(
                _dbPath,
                @"
SELECT
  id,
  repository_name_with_owner,
  stargazer_login,
  captured_at_utc,
  starred_at_utc,
  profile_url,
  avatar_url
FROM ix_github_repository_stargazer_snapshots
WHERE stargazer_login = @stargazer_login COLLATE NOCASE
ORDER BY captured_at_utc, repository_name_with_owner, id;",
                parameters: new Dictionary<string, object?> {
                    ["@stargazer_login"] = normalized
                }));

            return ReadStargazerSnapshots(table);
        }
    }

    /// <inheritdoc />
    public IReadOnlyList<GitHubRepositoryStargazerSnapshotRecord> GetAll() {
        lock (_gate) {
            var table = SqliteGitHubObservabilitySchema.QueryAsTable(_db.Query(
                _dbPath,
                @"
SELECT
  id,
  repository_name_with_owner,
  stargazer_login,
  captured_at_utc,
  starred_at_utc,
  profile_url,
  avatar_url
FROM ix_github_repository_stargazer_snapshots
ORDER BY repository_name_with_owner, captured_at_utc, stargazer_login, id;"));

            return ReadStargazerSnapshots(table);
        }
    }

    /// <inheritdoc />
    public DateTimeOffset? GetLatestCaptureAtUtcByRepository(string repositoryNameWithOwner) {
        var normalized = GitHubRepositoryIdentity.NormalizeNameWithOwner(repositoryNameWithOwner);
        lock (_gate) {
            var table = SqliteGitHubObservabilitySchema.QueryAsTable(_db.Query(
                _dbPath,
                @"
SELECT captured_at_utc
FROM (
  SELECT captured_at_utc
  FROM ix_github_repository_stargazer_snapshots
  WHERE repository_name_with_owner = @repository_name_with_owner
  UNION ALL
  SELECT captured_at_utc
  FROM ix_github_repository_stargazer_capture_status
  WHERE repository_name_with_owner = @repository_name_with_owner
)
ORDER BY captured_at_utc DESC
LIMIT 1;",
                parameters: new Dictionary<string, object?> {
                    ["@repository_name_with_owner"] = normalized
                }));

            if (table is null || table.Rows.Count == 0) {
                return null;
            }

            return SqliteGitHubObservabilitySchema.ReadNullableDateTimeOffset(table.Rows[0], "captured_at_utc");
        }
    }

    /// <inheritdoc />
    public void Dispose() {
        _db.Dispose();
    }

    private static IReadOnlyList<GitHubRepositoryStargazerSnapshotRecord> ReadStargazerSnapshots(DataTable? table) {
        if (table is null || table.Rows.Count == 0) {
            return Array.Empty<GitHubRepositoryStargazerSnapshotRecord>();
        }

        var snapshots = new List<GitHubRepositoryStargazerSnapshotRecord>(table.Rows.Count);
        foreach (DataRow row in table.Rows) {
            snapshots.Add(ReadStargazerSnapshot(row));
        }

        return snapshots;
    }

    private static GitHubRepositoryStargazerSnapshotRecord ReadStargazerSnapshot(DataRow row) {
        var snapshot = new GitHubRepositoryStargazerSnapshotRecord(
            row["id"]?.ToString() ?? string.Empty,
            row["repository_name_with_owner"]?.ToString() ?? string.Empty,
            row["stargazer_login"]?.ToString() ?? string.Empty,
            SqliteGitHubObservabilitySchema.ReadDateTimeOffset(row, "captured_at_utc")) {
            StarredAtUtc = SqliteGitHubObservabilitySchema.ReadNullableDateTimeOffset(row, "starred_at_utc"),
            ProfileUrl = SqliteGitHubObservabilitySchema.ReadOptionalString(row, "profile_url"),
            AvatarUrl = SqliteGitHubObservabilitySchema.ReadOptionalString(row, "avatar_url")
        };
        return snapshot;
    }
}

internal static class SqliteGitHubObservabilitySchema {
    public static void EnsureSchema(SQLite db, string dbPath) {
        if (db is null) {
            throw new ArgumentNullException(nameof(db));
        }
        if (string.IsNullOrWhiteSpace(dbPath)) {
            throw new ArgumentException("Database path cannot be empty.", nameof(dbPath));
        }

        var fullPath = Path.GetFullPath(dbPath.Trim());
        var directory = Path.GetDirectoryName(fullPath);
        if (!string.IsNullOrWhiteSpace(directory) && !Directory.Exists(directory)) {
            Directory.CreateDirectory(directory);
        }

        db.ExecuteNonQuery(
            fullPath,
            @"
CREATE TABLE IF NOT EXISTS ix_github_repository_watches (
  id TEXT PRIMARY KEY,
  repository_name_with_owner TEXT NOT NULL,
  display_name TEXT NULL,
  category TEXT NULL,
  notes TEXT NULL,
  enabled INTEGER NOT NULL,
  created_at_utc TEXT NOT NULL
);

CREATE TABLE IF NOT EXISTS ix_github_repository_snapshots (
  id TEXT PRIMARY KEY,
  watch_id TEXT NOT NULL,
  repository_name_with_owner TEXT NOT NULL,
  captured_at_utc TEXT NOT NULL,
  stars INTEGER NOT NULL,
  forks INTEGER NOT NULL,
  watchers INTEGER NOT NULL,
  open_issues INTEGER NOT NULL,
  description TEXT NULL,
  primary_language TEXT NULL,
  url TEXT NULL,
  pushed_at_utc TEXT NULL,
  is_archived INTEGER NOT NULL,
  is_fork INTEGER NOT NULL
);

CREATE TABLE IF NOT EXISTS ix_github_repository_fork_snapshots (
  id TEXT PRIMARY KEY,
  parent_repository_name_with_owner TEXT NOT NULL,
  fork_repository_name_with_owner TEXT NOT NULL,
  captured_at_utc TEXT NOT NULL,
  score REAL NOT NULL,
  tier TEXT NOT NULL,
  stars INTEGER NOT NULL,
  forks INTEGER NOT NULL,
  watchers INTEGER NOT NULL,
  open_issues INTEGER NOT NULL,
  url TEXT NULL,
  description TEXT NULL,
  primary_language TEXT NULL,
  pushed_at_utc TEXT NULL,
  updated_at_utc TEXT NULL,
  created_at_utc TEXT NULL,
  is_archived INTEGER NOT NULL,
  reasons_summary TEXT NULL
);

CREATE TABLE IF NOT EXISTS ix_github_repository_fork_capture_status (
  parent_repository_name_with_owner TEXT PRIMARY KEY,
  captured_at_utc TEXT NOT NULL
);

CREATE TABLE IF NOT EXISTS ix_github_repository_stargazer_snapshots (
  id TEXT PRIMARY KEY,
  repository_name_with_owner TEXT NOT NULL,
  stargazer_login TEXT NOT NULL,
  captured_at_utc TEXT NOT NULL,
  starred_at_utc TEXT NULL,
  profile_url TEXT NULL,
  avatar_url TEXT NULL
);

CREATE TABLE IF NOT EXISTS ix_github_repository_stargazer_capture_status (
  repository_name_with_owner TEXT PRIMARY KEY,
  captured_at_utc TEXT NOT NULL
);

CREATE INDEX IF NOT EXISTS ix_github_repository_watches_repo
  ON ix_github_repository_watches(repository_name_with_owner);

CREATE INDEX IF NOT EXISTS ix_github_repository_snapshots_watch_time
  ON ix_github_repository_snapshots(watch_id, captured_at_utc);

CREATE INDEX IF NOT EXISTS ix_github_repository_snapshots_repo_time
  ON ix_github_repository_snapshots(repository_name_with_owner, captured_at_utc);");
        db.ExecuteNonQuery(
            fullPath,
            @"
CREATE INDEX IF NOT EXISTS ix_github_repository_fork_snapshots_parent_time
  ON ix_github_repository_fork_snapshots(parent_repository_name_with_owner, captured_at_utc);

CREATE INDEX IF NOT EXISTS ix_github_repository_fork_snapshots_fork_time
  ON ix_github_repository_fork_snapshots(fork_repository_name_with_owner, captured_at_utc);");
        db.ExecuteNonQuery(
            fullPath,
            @"
CREATE INDEX IF NOT EXISTS ix_github_repository_fork_capture_status_time
  ON ix_github_repository_fork_capture_status(captured_at_utc);");
        db.ExecuteNonQuery(
            fullPath,
            @"
CREATE INDEX IF NOT EXISTS ix_github_repository_stargazer_snapshots_repo_time
  ON ix_github_repository_stargazer_snapshots(repository_name_with_owner, captured_at_utc);

CREATE INDEX IF NOT EXISTS ix_github_repository_stargazer_snapshots_login_time
  ON ix_github_repository_stargazer_snapshots(stargazer_login, captured_at_utc);");
        db.ExecuteNonQuery(
            fullPath,
            @"
CREATE INDEX IF NOT EXISTS ix_github_repository_stargazer_snapshots_login_time_nocase
  ON ix_github_repository_stargazer_snapshots(stargazer_login COLLATE NOCASE, captured_at_utc);");
        db.ExecuteNonQuery(
            fullPath,
            @"
CREATE INDEX IF NOT EXISTS ix_github_repository_stargazer_capture_status_time
  ON ix_github_repository_stargazer_capture_status(captured_at_utc);");
    }

    public static string? Normalize(string? value) {
        var trimmed = value?.Trim();
        return string.IsNullOrWhiteSpace(trimmed) ? null : trimmed;
    }

    public static DataTable? QueryAsTable(object? queryResult) {
        if (queryResult is DataTable dataTable) {
            return dataTable;
        }

        if (queryResult is DataSet dataSet && dataSet.Tables.Count > 0) {
            return dataSet.Tables[0];
        }

        return null;
    }

    public static string? ReadOptionalString(DataRow row, string columnName) {
        if (row.Table.Columns.Contains(columnName) == false || row[columnName] is DBNull) {
            return null;
        }

        var value = row[columnName]?.ToString()?.Trim();
        return string.IsNullOrWhiteSpace(value) ? null : value;
    }

    public static bool ReadBoolean(DataRow row, string columnName) {
        if (row.Table.Columns.Contains(columnName) == false || row[columnName] is DBNull) {
            return false;
        }

        return row[columnName] switch {
            bool boolValue => boolValue,
            sbyte int8Value => int8Value != 0,
            byte uint8Value => uint8Value != 0,
            short int16Value => int16Value != 0,
            ushort uint16Value => uint16Value != 0,
            int int32Value => int32Value != 0,
            uint uint32Value => uint32Value != 0,
            long int64Value => int64Value != 0,
            ulong uint64Value => uint64Value != 0,
            string text when bool.TryParse(text, out var parsed) => parsed,
            string text when int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed) => parsed != 0,
            _ => false
        };
    }

    public static int ReadInt32(DataRow row, string columnName) {
        if (row.Table.Columns.Contains(columnName) == false || row[columnName] is DBNull) {
            return 0;
        }

        return row[columnName] switch {
            int int32Value => int32Value,
            long int64Value => unchecked((int)int64Value),
            short int16Value => int16Value,
            byte uint8Value => uint8Value,
            string text when int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed) => parsed,
            _ => 0
        };
    }

    public static double ReadDouble(DataRow row, string columnName) {
        if (row.Table.Columns.Contains(columnName) == false || row[columnName] is DBNull) {
            return 0d;
        }

        return row[columnName] switch {
            double doubleValue => doubleValue,
            float floatValue => floatValue,
            decimal decimalValue => (double)decimalValue,
            int int32Value => int32Value,
            long int64Value => int64Value,
            string text when double.TryParse(text, NumberStyles.Float | NumberStyles.AllowThousands, CultureInfo.InvariantCulture, out var parsed) => parsed,
            _ => 0d
        };
    }

    public static DateTimeOffset ReadDateTimeOffset(DataRow row, string columnName) {
        return ReadNullableDateTimeOffset(row, columnName) ?? DateTimeOffset.MinValue;
    }

    public static DateTimeOffset? ReadNullableDateTimeOffset(DataRow row, string columnName) {
        var text = ReadOptionalString(row, columnName);
        if (string.IsNullOrWhiteSpace(text)) {
            return null;
        }

        return DateTimeOffset.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var value)
            ? value.ToUniversalTime()
            : null;
    }
}
#endif
