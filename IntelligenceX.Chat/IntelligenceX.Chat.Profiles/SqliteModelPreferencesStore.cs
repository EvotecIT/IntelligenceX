using System;
using System.Collections.Generic;
using System.Data;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using DBAClientX;

namespace IntelligenceX.Chat.Profiles;

/// <summary>
/// SQLite-backed store for model favorites and recents per profile.
/// </summary>
internal sealed class SqliteModelPreferencesStore : IDisposable {
    private const string FavoritesTable = "ix_model_favorites";
    private const string RecentsTable = "ix_model_recents";

    private readonly string _dbPath;
    private readonly SQLite _db = new();

    public SqliteModelPreferencesStore(string dbPath) {
        if (string.IsNullOrWhiteSpace(dbPath)) throw new ArgumentException("Database path cannot be empty.", nameof(dbPath));
        _dbPath = dbPath;

        var dir = Path.GetDirectoryName(_dbPath);
        if (!string.IsNullOrWhiteSpace(dir) && !Directory.Exists(dir)) {
            Directory.CreateDirectory(dir);
        }

        EnsureSchema();
    }

    private void EnsureSchema() {
        _db.ExecuteNonQuery(_dbPath, $@"
CREATE TABLE IF NOT EXISTS {FavoritesTable} (
  profile_name TEXT NOT NULL,
  model TEXT NOT NULL,
  updated_utc TEXT NOT NULL,
  PRIMARY KEY (profile_name, model)
);

CREATE TABLE IF NOT EXISTS {RecentsTable} (
  profile_name TEXT NOT NULL,
  model TEXT NOT NULL,
  last_used_utc TEXT NOT NULL,
  use_count INTEGER NOT NULL,
  PRIMARY KEY (profile_name, model)
);

CREATE INDEX IF NOT EXISTS ix_model_favorites_profile ON {FavoritesTable}(profile_name);
CREATE INDEX IF NOT EXISTS ix_model_recents_profile_last_used ON {RecentsTable}(profile_name, last_used_utc);
");
    }

    public Task<IReadOnlyList<string>> ListFavoritesAsync(string profileName, CancellationToken cancellationToken) {
        cancellationToken.ThrowIfCancellationRequested();
        var profile = NormalizeProfileName(profileName);
        if (profile.Length == 0) {
            return Task.FromResult<IReadOnlyList<string>>(Array.Empty<string>());
        }

        var list = new List<string>();
        var result = _db.Query(
            _dbPath,
            $"SELECT model FROM {FavoritesTable} WHERE profile_name = @name ORDER BY model",
            parameters: new Dictionary<string, object?> { ["@name"] = profile }) as DataTable;

        if (result is not null) {
            foreach (DataRow row in result.Rows) {
                var m = row[0]?.ToString();
                if (!string.IsNullOrWhiteSpace(m)) {
                    list.Add(m!.Trim());
                }
            }
        }

        return Task.FromResult<IReadOnlyList<string>>(list);
    }

    public Task SetFavoriteAsync(string profileName, string model, bool isFavorite, CancellationToken cancellationToken) {
        cancellationToken.ThrowIfCancellationRequested();
        var profile = NormalizeProfileName(profileName);
        var normalizedModel = NormalizeModel(model);
        if (profile.Length == 0) throw new ArgumentException("Profile name cannot be empty.", nameof(profileName));
        if (normalizedModel.Length == 0) throw new ArgumentException("Model cannot be empty.", nameof(model));

        var now = DateTime.UtcNow.ToString("O");
        if (isFavorite) {
            _db.ExecuteNonQuery(
                _dbPath,
                $@"
INSERT INTO {FavoritesTable} (profile_name, model, updated_utc)
VALUES (@name, @model, @updated_utc)
ON CONFLICT(profile_name, model) DO UPDATE SET updated_utc = excluded.updated_utc;",
                parameters: new Dictionary<string, object?> {
                    ["@name"] = profile,
                    ["@model"] = normalizedModel,
                    ["@updated_utc"] = now
                });
        } else {
            _db.ExecuteNonQuery(
                _dbPath,
                $"DELETE FROM {FavoritesTable} WHERE profile_name = @name AND model = @model;",
                parameters: new Dictionary<string, object?> { ["@name"] = profile, ["@model"] = normalizedModel });
        }

        return Task.CompletedTask;
    }

    public Task RecordRecentAsync(string profileName, string model, int maxRecentsPerProfile, CancellationToken cancellationToken) {
        cancellationToken.ThrowIfCancellationRequested();
        var profile = NormalizeProfileName(profileName);
        var normalizedModel = NormalizeModel(model);
        if (profile.Length == 0 || normalizedModel.Length == 0) {
            return Task.CompletedTask;
        }

        if (maxRecentsPerProfile <= 0) {
            maxRecentsPerProfile = 50;
        }

        var now = DateTime.UtcNow.ToString("O");
        _db.ExecuteNonQuery(
            _dbPath,
            $@"
INSERT INTO {RecentsTable} (profile_name, model, last_used_utc, use_count)
VALUES (@name, @model, @last_used_utc, 1)
ON CONFLICT(profile_name, model) DO UPDATE SET
  last_used_utc = excluded.last_used_utc,
  use_count = {RecentsTable}.use_count + 1;",
            parameters: new Dictionary<string, object?> {
                ["@name"] = profile,
                ["@model"] = normalizedModel,
                ["@last_used_utc"] = now
            });

        // Cap recents per profile (simple LRU by last_used_utc).
        _db.ExecuteNonQuery(
            _dbPath,
            $@"
DELETE FROM {RecentsTable}
WHERE profile_name = @name
  AND model NOT IN (
    SELECT model FROM {RecentsTable}
    WHERE profile_name = @name
    ORDER BY last_used_utc DESC
    LIMIT @limit
  );",
            parameters: new Dictionary<string, object?> { ["@name"] = profile, ["@limit"] = maxRecentsPerProfile });

        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<string>> ListRecentsAsync(string profileName, int max, CancellationToken cancellationToken) {
        cancellationToken.ThrowIfCancellationRequested();
        var profile = NormalizeProfileName(profileName);
        if (profile.Length == 0) {
            return Task.FromResult<IReadOnlyList<string>>(Array.Empty<string>());
        }

        if (max <= 0) {
            max = 10;
        }

        var list = new List<string>();
        var result = _db.Query(
            _dbPath,
            $@"
SELECT model
FROM {RecentsTable}
WHERE profile_name = @name
ORDER BY last_used_utc DESC
LIMIT @limit;",
            parameters: new Dictionary<string, object?> { ["@name"] = profile, ["@limit"] = max }) as DataTable;

        if (result is not null) {
            foreach (DataRow row in result.Rows) {
                var m = row[0]?.ToString();
                if (!string.IsNullOrWhiteSpace(m)) {
                    list.Add(m!.Trim());
                }
            }
        }

        return Task.FromResult<IReadOnlyList<string>>(list);
    }

    private static string NormalizeProfileName(string? name) => (name ?? string.Empty).Trim();
    private static string NormalizeModel(string? model) => (model ?? string.Empty).Trim();

    public void Dispose() {
        _db.Dispose();
    }
}

