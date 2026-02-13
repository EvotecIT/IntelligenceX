using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

using DBAClientX;

namespace IntelligenceX.Chat.Service.Profiles;

/// <summary>
/// SQLite-backed profile store (file-based).
/// </summary>
internal sealed class SqliteServiceProfileStore : IServiceProfileStore, IDisposable {
    private readonly string _dbPath;
    private readonly JsonSerializerOptions _json;

    private readonly SQLite _db = new();

    public SqliteServiceProfileStore(string dbPath) {
        if (string.IsNullOrWhiteSpace(dbPath)) throw new ArgumentException("Database path cannot be empty.", nameof(dbPath));
        _dbPath = dbPath;
        _json = new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

        var dir = Path.GetDirectoryName(_dbPath);
        if (!string.IsNullOrWhiteSpace(dir) && !Directory.Exists(dir)) {
            Directory.CreateDirectory(dir);
        }

        EnsureSchema();
    }

    private void EnsureSchema() {
        _db.ExecuteNonQuery(_dbPath, @"
CREATE TABLE IF NOT EXISTS ix_service_profiles (
  name TEXT PRIMARY KEY,
  json TEXT NOT NULL,
  updated_utc TEXT NOT NULL
);");
    }

    public Task<ServiceProfile?> GetAsync(string name, CancellationToken cancellationToken) {
        cancellationToken.ThrowIfCancellationRequested();
        if (string.IsNullOrWhiteSpace(name)) {
            return Task.FromResult<ServiceProfile?>(null);
        }

        var json = _db.ExecuteScalar(
            _dbPath,
            "SELECT json FROM ix_service_profiles WHERE name = @name",
            parameters: new Dictionary<string, object?> { ["@name"] = name.Trim() }) as string;

        if (string.IsNullOrWhiteSpace(json)) {
            return Task.FromResult<ServiceProfile?>(null);
        }

        try {
            var profile = JsonSerializer.Deserialize<ServiceProfile>(json!, _json);
            return Task.FromResult(profile);
        } catch (Exception ex) {
            throw new InvalidOperationException($"Failed to parse profile '{name}'.", ex);
        }
    }

    public Task UpsertAsync(string name, ServiceProfile profile, CancellationToken cancellationToken) {
        cancellationToken.ThrowIfCancellationRequested();
        if (string.IsNullOrWhiteSpace(name)) throw new ArgumentException("Profile name cannot be empty.", nameof(name));
        if (profile == null) throw new ArgumentNullException(nameof(profile));

        var json = JsonSerializer.Serialize(profile, _json);
        var now = DateTime.UtcNow.ToString("O");
        _db.ExecuteNonQuery(
            _dbPath,
            @"
INSERT INTO ix_service_profiles (name, json, updated_utc)
VALUES (@name, @json, @updated_utc)
ON CONFLICT(name) DO UPDATE SET
  json = excluded.json,
  updated_utc = excluded.updated_utc;",
            parameters: new Dictionary<string, object?> {
                ["@name"] = name.Trim(),
                ["@json"] = json,
                ["@updated_utc"] = now
            });
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<string>> ListNamesAsync(CancellationToken cancellationToken) {
        cancellationToken.ThrowIfCancellationRequested();

        var list = new List<string>();
        var result = _db.Query(_dbPath, "SELECT name FROM ix_service_profiles ORDER BY name");
        if (result is System.Data.DataTable dt) {
            foreach (System.Data.DataRow row in dt.Rows) {
                var v = row[0]?.ToString();
                if (!string.IsNullOrWhiteSpace(v)) {
                    list.Add(v!);
                }
            }
        }
        return Task.FromResult<IReadOnlyList<string>>(list);
    }

    public void Dispose() {
        _db.Dispose();
    }
}
