#if !NETSTANDARD2_0
using System;
using System.Collections.Generic;
using System.Data;
using System.Globalization;
using System.IO;
using System.Linq;
using DBAClientX;

namespace IntelligenceX.Telemetry.Usage;

/// <summary>
/// SQLite-backed source-root store for usage telemetry discovery.
/// </summary>
public sealed class SqliteSourceRootStore : ISourceRootStore, IDisposable {
    private readonly object _gate = new();
    private readonly SQLite _db = new();
    private readonly string _dbPath;

    /// <summary>
    /// Initializes a new SQLite-backed source-root store.
    /// </summary>
    /// <param name="dbPath">SQLite database path.</param>
    public SqliteSourceRootStore(string dbPath) {
        if (string.IsNullOrWhiteSpace(dbPath)) {
            throw new ArgumentException("Database path cannot be empty.", nameof(dbPath));
        }

        _dbPath = Path.GetFullPath(dbPath.Trim());
        SqliteUsageTelemetrySchema.EnsureSchema(_db, _dbPath);
    }

    /// <inheritdoc />
    public void Upsert(SourceRootRecord root) {
        if (root is null) {
            throw new ArgumentNullException(nameof(root));
        }

        lock (_gate) {
            _db.ExecuteNonQuery(
                _dbPath,
                @"
INSERT INTO ix_usage_source_roots (
  id,
  provider_id,
  source_kind,
  path,
  platform_hint,
  machine_label,
  account_hint,
  enabled
)
VALUES (
  @id,
  @provider_id,
  @source_kind,
  @path,
  @platform_hint,
  @machine_label,
  @account_hint,
  @enabled
)
ON CONFLICT(id) DO UPDATE SET
  provider_id = excluded.provider_id,
  source_kind = excluded.source_kind,
  path = excluded.path,
  platform_hint = excluded.platform_hint,
  machine_label = excluded.machine_label,
  account_hint = excluded.account_hint,
  enabled = excluded.enabled;",
                parameters: new Dictionary<string, object?> {
                    ["@id"] = root.Id,
                    ["@provider_id"] = root.ProviderId,
                    ["@source_kind"] = root.SourceKind.ToString(),
                    ["@path"] = root.Path,
                    ["@platform_hint"] = SqliteUsageTelemetrySchema.Normalize(root.PlatformHint),
                    ["@machine_label"] = SqliteUsageTelemetrySchema.Normalize(root.MachineLabel),
                    ["@account_hint"] = SqliteUsageTelemetrySchema.Normalize(root.AccountHint),
                    ["@enabled"] = root.Enabled ? 1 : 0
                });
        }
    }

    /// <inheritdoc />
    public bool TryGet(string id, out SourceRootRecord root) {
        if (string.IsNullOrWhiteSpace(id)) {
            root = null!;
            return false;
        }

        lock (_gate) {
            var table = SqliteUsageTelemetrySchema.QueryAsTable(_db.Query(
                _dbPath,
                "SELECT id, provider_id, source_kind, path, platform_hint, machine_label, account_hint, enabled FROM ix_usage_source_roots WHERE id = @id LIMIT 1;",
                parameters: new Dictionary<string, object?> {
                    ["@id"] = id.Trim()
                }));

            if (table is null || table.Rows.Count == 0) {
                root = null!;
                return false;
            }

            root = ReadSourceRoot(table.Rows[0]);
            return true;
        }
    }

    /// <inheritdoc />
    public IReadOnlyList<SourceRootRecord> GetAll() {
        lock (_gate) {
            var table = SqliteUsageTelemetrySchema.QueryAsTable(_db.Query(
                _dbPath,
                "SELECT id, provider_id, source_kind, path, platform_hint, machine_label, account_hint, enabled FROM ix_usage_source_roots ORDER BY provider_id, path, id;"));
            if (table is null || table.Rows.Count == 0) {
                return Array.Empty<SourceRootRecord>();
            }

            var roots = new List<SourceRootRecord>(table.Rows.Count);
            foreach (DataRow row in table.Rows) {
                roots.Add(ReadSourceRoot(row));
            }
            return roots;
        }
    }

    /// <summary>
    /// Releases database resources held by the store.
    /// </summary>
    public void Dispose() {
        _db.Dispose();
    }

    private static SourceRootRecord ReadSourceRoot(DataRow row) {
        var sourceKindText = row["source_kind"]?.ToString();
        if (!Enum.TryParse(sourceKindText, ignoreCase: true, out UsageSourceKind sourceKind)) {
            sourceKind = UsageSourceKind.LocalLogs;
        }

        var root = new SourceRootRecord(
            row["id"]?.ToString() ?? string.Empty,
            row["provider_id"]?.ToString() ?? string.Empty,
            sourceKind,
            row["path"]?.ToString() ?? string.Empty) {
            PlatformHint = SqliteUsageTelemetrySchema.ReadOptionalString(row, "platform_hint"),
            MachineLabel = SqliteUsageTelemetrySchema.ReadOptionalString(row, "machine_label"),
            AccountHint = SqliteUsageTelemetrySchema.ReadOptionalString(row, "account_hint"),
            Enabled = SqliteUsageTelemetrySchema.ReadBoolean(row, "enabled")
        };
        return root;
    }
}

/// <summary>
/// SQLite-backed store for manual usage-account bindings.
/// </summary>
public sealed class SqliteUsageAccountBindingStore : IUsageAccountBindingStore, IDisposable {
    private readonly object _gate = new();
    private readonly SQLite _db = new();
    private readonly string _dbPath;

    /// <summary>
    /// Initializes a new SQLite-backed account-binding store.
    /// </summary>
    public SqliteUsageAccountBindingStore(string dbPath) {
        if (string.IsNullOrWhiteSpace(dbPath)) {
            throw new ArgumentException("Database path cannot be empty.", nameof(dbPath));
        }

        _dbPath = Path.GetFullPath(dbPath.Trim());
        SqliteUsageTelemetrySchema.EnsureSchema(_db, _dbPath);
    }

    /// <inheritdoc />
    public void Upsert(UsageAccountBindingRecord binding) {
        if (binding is null) {
            throw new ArgumentNullException(nameof(binding));
        }

        lock (_gate) {
            _db.ExecuteNonQuery(
                _dbPath,
                @"
INSERT INTO ix_usage_account_bindings (
  id,
  provider_id,
  source_root_id,
  match_provider_account_id,
  match_account_label,
  provider_account_id,
  account_label,
  person_label,
  enabled
)
VALUES (
  @id,
  @provider_id,
  @source_root_id,
  @match_provider_account_id,
  @match_account_label,
  @provider_account_id,
  @account_label,
  @person_label,
  @enabled
)
ON CONFLICT(id) DO UPDATE SET
  provider_id = excluded.provider_id,
  source_root_id = excluded.source_root_id,
  match_provider_account_id = excluded.match_provider_account_id,
  match_account_label = excluded.match_account_label,
  provider_account_id = excluded.provider_account_id,
  account_label = excluded.account_label,
  person_label = excluded.person_label,
  enabled = excluded.enabled;",
                parameters: new Dictionary<string, object?> {
                    ["@id"] = binding.Id,
                    ["@provider_id"] = binding.ProviderId,
                    ["@source_root_id"] = SqliteUsageTelemetrySchema.Normalize(binding.SourceRootId),
                    ["@match_provider_account_id"] = SqliteUsageTelemetrySchema.Normalize(binding.MatchProviderAccountId),
                    ["@match_account_label"] = SqliteUsageTelemetrySchema.Normalize(binding.MatchAccountLabel),
                    ["@provider_account_id"] = SqliteUsageTelemetrySchema.Normalize(binding.ProviderAccountId),
                    ["@account_label"] = SqliteUsageTelemetrySchema.Normalize(binding.AccountLabel),
                    ["@person_label"] = SqliteUsageTelemetrySchema.Normalize(binding.PersonLabel),
                    ["@enabled"] = binding.Enabled ? 1 : 0
                });
        }
    }

    /// <inheritdoc />
    public bool TryGet(string id, out UsageAccountBindingRecord binding) {
        if (string.IsNullOrWhiteSpace(id)) {
            binding = null!;
            return false;
        }

        lock (_gate) {
            var table = SqliteUsageTelemetrySchema.QueryAsTable(_db.Query(
                _dbPath,
                @"
SELECT
  id,
  provider_id,
  source_root_id,
  match_provider_account_id,
  match_account_label,
  provider_account_id,
  account_label,
  person_label,
  enabled
FROM ix_usage_account_bindings
WHERE id = @id
LIMIT 1;",
                parameters: new Dictionary<string, object?> {
                    ["@id"] = id.Trim()
                }));

            if (table is null || table.Rows.Count == 0) {
                binding = null!;
                return false;
            }

            binding = ReadUsageAccountBinding(table.Rows[0]);
            return true;
        }
    }

    /// <inheritdoc />
    public IReadOnlyList<UsageAccountBindingRecord> GetAll() {
        lock (_gate) {
            var table = SqliteUsageTelemetrySchema.QueryAsTable(_db.Query(
                _dbPath,
                @"
SELECT
  id,
  provider_id,
  source_root_id,
  match_provider_account_id,
  match_account_label,
  provider_account_id,
  account_label,
  person_label,
  enabled
FROM ix_usage_account_bindings
ORDER BY provider_id, id;"));

            if (table is null || table.Rows.Count == 0) {
                return Array.Empty<UsageAccountBindingRecord>();
            }

            var bindings = new List<UsageAccountBindingRecord>(table.Rows.Count);
            foreach (DataRow row in table.Rows) {
                bindings.Add(ReadUsageAccountBinding(row));
            }

            return bindings;
        }
    }

    /// <summary>
    /// Releases database resources held by the store.
    /// </summary>
    public void Dispose() {
        _db.Dispose();
    }

    private static UsageAccountBindingRecord ReadUsageAccountBinding(DataRow row) {
        var binding = new UsageAccountBindingRecord(
            row["id"]?.ToString() ?? string.Empty,
            row["provider_id"]?.ToString() ?? string.Empty) {
            SourceRootId = SqliteUsageTelemetrySchema.ReadOptionalString(row, "source_root_id"),
            MatchProviderAccountId = SqliteUsageTelemetrySchema.ReadOptionalString(row, "match_provider_account_id"),
            MatchAccountLabel = SqliteUsageTelemetrySchema.ReadOptionalString(row, "match_account_label"),
            ProviderAccountId = SqliteUsageTelemetrySchema.ReadOptionalString(row, "provider_account_id"),
            AccountLabel = SqliteUsageTelemetrySchema.ReadOptionalString(row, "account_label"),
            PersonLabel = SqliteUsageTelemetrySchema.ReadOptionalString(row, "person_label"),
            Enabled = SqliteUsageTelemetrySchema.ReadBoolean(row, "enabled")
        };
        return binding;
    }
}

/// <summary>
/// SQLite-backed raw-artifact store for incremental imports.
/// </summary>
public sealed class SqliteRawArtifactStore : IRawArtifactStore, IDisposable {
    private readonly object _gate = new();
    private readonly SQLite _db = new();
    private readonly string _dbPath;

    /// <summary>
    /// Initializes a new SQLite-backed raw-artifact store.
    /// </summary>
    public SqliteRawArtifactStore(string dbPath) {
        if (string.IsNullOrWhiteSpace(dbPath)) {
            throw new ArgumentException("Database path cannot be empty.", nameof(dbPath));
        }

        _dbPath = Path.GetFullPath(dbPath.Trim());
        SqliteUsageTelemetrySchema.EnsureSchema(_db, _dbPath);
    }

    /// <inheritdoc />
    public void Upsert(RawArtifactDescriptor artifact) {
        if (artifact is null) {
            throw new ArgumentNullException(nameof(artifact));
        }

        lock (_gate) {
            _db.ExecuteNonQuery(
                _dbPath,
                @"
INSERT INTO ix_usage_raw_artifacts (
  source_root_id,
  adapter_id,
  path,
  fingerprint,
  parser_version,
  size_bytes,
  last_write_utc,
  imported_at_utc,
  parsed_bytes,
  state_json
)
VALUES (
  @source_root_id,
  @adapter_id,
  @path,
  @fingerprint,
  @parser_version,
  @size_bytes,
  @last_write_utc,
  @imported_at_utc,
  @parsed_bytes,
  @state_json
)
ON CONFLICT(source_root_id, adapter_id, path) DO UPDATE SET
  fingerprint = excluded.fingerprint,
  parser_version = excluded.parser_version,
  size_bytes = excluded.size_bytes,
  last_write_utc = excluded.last_write_utc,
  imported_at_utc = excluded.imported_at_utc,
  parsed_bytes = excluded.parsed_bytes,
  state_json = excluded.state_json;",
                parameters: new Dictionary<string, object?> {
                    ["@source_root_id"] = artifact.SourceRootId,
                    ["@adapter_id"] = artifact.AdapterId,
                    ["@path"] = artifact.Path,
                    ["@fingerprint"] = artifact.Fingerprint,
                    ["@parser_version"] = SqliteUsageTelemetrySchema.Normalize(artifact.ParserVersion),
                    ["@size_bytes"] = artifact.SizeBytes,
                    ["@last_write_utc"] = artifact.LastWriteTimeUtc?.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture),
                    ["@imported_at_utc"] = artifact.ImportedAtUtc.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture),
                    ["@parsed_bytes"] = artifact.ParsedBytes,
                    ["@state_json"] = SqliteUsageTelemetrySchema.Normalize(artifact.StateJson)
                });
        }
    }

    /// <inheritdoc />
    public bool TryGet(string sourceRootId, string adapterId, string path, out RawArtifactDescriptor artifact) {
        if (string.IsNullOrWhiteSpace(sourceRootId) ||
            string.IsNullOrWhiteSpace(adapterId) ||
            string.IsNullOrWhiteSpace(path)) {
            artifact = null!;
            return false;
        }

        lock (_gate) {
            var table = SqliteUsageTelemetrySchema.QueryAsTable(_db.Query(
                _dbPath,
                @"
SELECT
  source_root_id,
  adapter_id,
  path,
  fingerprint,
  parser_version,
  size_bytes,
  last_write_utc,
  imported_at_utc,
  parsed_bytes,
  state_json
FROM ix_usage_raw_artifacts
WHERE source_root_id = @source_root_id
  AND adapter_id = @adapter_id
  AND path = @path
LIMIT 1;",
                parameters: new Dictionary<string, object?> {
                    ["@source_root_id"] = sourceRootId.Trim(),
                    ["@adapter_id"] = adapterId.Trim(),
                    ["@path"] = UsageTelemetryIdentity.NormalizePath(path)
                }));

            if (table is null || table.Rows.Count == 0) {
                artifact = null!;
                return false;
            }

            artifact = ReadRawArtifact(table.Rows[0]);
            return true;
        }
    }

    /// <inheritdoc />
    public IReadOnlyList<RawArtifactDescriptor> GetAll() {
        lock (_gate) {
            var table = SqliteUsageTelemetrySchema.QueryAsTable(_db.Query(
                _dbPath,
                @"
SELECT
  source_root_id,
  adapter_id,
  path,
  fingerprint,
  parser_version,
  size_bytes,
  last_write_utc,
  imported_at_utc,
  parsed_bytes,
  state_json
FROM ix_usage_raw_artifacts
ORDER BY source_root_id, adapter_id, path;"));

            if (table is null || table.Rows.Count == 0) {
                return Array.Empty<RawArtifactDescriptor>();
            }

            var artifacts = new List<RawArtifactDescriptor>(table.Rows.Count);
            foreach (DataRow row in table.Rows) {
                artifacts.Add(ReadRawArtifact(row));
            }

            return artifacts;
        }
    }

    /// <inheritdoc />
    public IReadOnlyDictionary<string, RawArtifactDescriptor> GetBySourceRootAdapter(string sourceRootId, string adapterId) {
        if (string.IsNullOrWhiteSpace(sourceRootId) || string.IsNullOrWhiteSpace(adapterId)) {
            return new Dictionary<string, RawArtifactDescriptor>(StringComparer.OrdinalIgnoreCase);
        }

        lock (_gate) {
            var table = SqliteUsageTelemetrySchema.QueryAsTable(_db.Query(
                _dbPath,
                @"
SELECT
  source_root_id,
  adapter_id,
  path,
  fingerprint,
  parser_version,
  size_bytes,
  last_write_utc,
  imported_at_utc,
  parsed_bytes,
  state_json
FROM ix_usage_raw_artifacts
WHERE source_root_id = @source_root_id
  AND adapter_id = @adapter_id
ORDER BY imported_at_utc DESC, last_write_utc DESC, path DESC;",
                parameters: new Dictionary<string, object?> {
                    ["@source_root_id"] = sourceRootId.Trim(),
                    ["@adapter_id"] = adapterId.Trim()
                }));

            if (table is null || table.Rows.Count == 0) {
                return new Dictionary<string, RawArtifactDescriptor>(StringComparer.OrdinalIgnoreCase);
            }

            var artifacts = new Dictionary<string, RawArtifactDescriptor>(StringComparer.OrdinalIgnoreCase);
            foreach (DataRow row in table.Rows) {
                var artifact = ReadRawArtifact(row);
                artifacts[UsageTelemetryIdentity.NormalizePath(artifact.Path)] = artifact;
            }

            return artifacts;
        }
    }

    /// <summary>
    /// Returns the most recently imported raw artifacts.
    /// </summary>
    /// <param name="limit">Maximum artifacts to return.</param>
    /// <returns>Recent raw artifacts ordered from newest to oldest.</returns>
    public IReadOnlyList<RawArtifactDescriptor> GetRecent(int limit) {
        if (limit <= 0) {
            return Array.Empty<RawArtifactDescriptor>();
        }

        lock (_gate) {
            var table = SqliteUsageTelemetrySchema.QueryAsTable(_db.Query(
                _dbPath,
                @"
SELECT
  source_root_id,
  adapter_id,
  path,
  fingerprint,
  parser_version,
  size_bytes,
  last_write_utc,
  imported_at_utc,
  parsed_bytes,
  state_json
FROM ix_usage_raw_artifacts
WHERE state_json IS NOT NULL
  AND state_json <> ''
ORDER BY imported_at_utc DESC, last_write_utc DESC, path DESC
LIMIT @limit;",
                parameters: new Dictionary<string, object?> {
                    ["@limit"] = limit
                }));

            if (table is null || table.Rows.Count == 0) {
                return Array.Empty<RawArtifactDescriptor>();
            }

            var artifacts = new List<RawArtifactDescriptor>(table.Rows.Count);
            foreach (DataRow row in table.Rows) {
                artifacts.Add(ReadRawArtifact(row));
            }

            return artifacts;
        }
    }

    /// <summary>
    /// Returns the most recently imported raw artifacts per source root and adapter.
    /// </summary>
    /// <param name="limitPerSourceRoot">Maximum artifacts to return for each source root/adapter pair.</param>
    /// <returns>Recent raw artifacts ordered by source root and newest import first.</returns>
    public IReadOnlyList<RawArtifactDescriptor> GetRecentPerSourceRoot(int limitPerSourceRoot) {
        if (limitPerSourceRoot <= 0) {
            return Array.Empty<RawArtifactDescriptor>();
        }

        lock (_gate) {
            var table = SqliteUsageTelemetrySchema.QueryAsTable(_db.Query(
                _dbPath,
                @"
WITH ranked AS (
    SELECT
      source_root_id,
      adapter_id,
      path,
      fingerprint,
      parser_version,
      size_bytes,
      last_write_utc,
      imported_at_utc,
      parsed_bytes,
      state_json,
      ROW_NUMBER() OVER (
        PARTITION BY source_root_id, adapter_id
        ORDER BY imported_at_utc DESC, last_write_utc DESC, path DESC
      ) AS artifact_rank
    FROM ix_usage_raw_artifacts
    WHERE state_json IS NOT NULL
      AND state_json <> ''
)
SELECT
  source_root_id,
  adapter_id,
  path,
  fingerprint,
  parser_version,
  size_bytes,
  last_write_utc,
  imported_at_utc,
  parsed_bytes,
  state_json
FROM ranked
WHERE artifact_rank <= @limitPerSourceRoot
ORDER BY source_root_id, adapter_id, imported_at_utc DESC, last_write_utc DESC, path DESC;",
                parameters: new Dictionary<string, object?> {
                    ["@limitPerSourceRoot"] = limitPerSourceRoot
                }));

            if (table is null || table.Rows.Count == 0) {
                return Array.Empty<RawArtifactDescriptor>();
            }

            var artifacts = new List<RawArtifactDescriptor>(table.Rows.Count);
            foreach (DataRow row in table.Rows) {
                artifacts.Add(ReadRawArtifact(row));
            }

            return artifacts;
        }
    }

    /// <summary>
    /// Releases database resources held by the store.
    /// </summary>
    public void Dispose() {
        _db.Dispose();
    }

    private static RawArtifactDescriptor ReadRawArtifact(DataRow row) {
        var artifact = new RawArtifactDescriptor(
            row["source_root_id"]?.ToString() ?? string.Empty,
            row["adapter_id"]?.ToString() ?? string.Empty,
            row["path"]?.ToString() ?? string.Empty,
            row["fingerprint"]?.ToString() ?? string.Empty) {
            ParserVersion = SqliteUsageTelemetrySchema.ReadOptionalString(row, "parser_version"),
            SizeBytes = SqliteUsageTelemetrySchema.ReadNullableInt64(row, "size_bytes"),
            LastWriteTimeUtc = SqliteUsageTelemetrySchema.ReadNullableDateTimeOffset(row, "last_write_utc"),
            ImportedAtUtc = SqliteUsageTelemetrySchema.ReadNullableDateTimeOffset(row, "imported_at_utc") ?? DateTimeOffset.MinValue,
            ParsedBytes = SqliteUsageTelemetrySchema.ReadNullableInt64(row, "parsed_bytes"),
            StateJson = SqliteUsageTelemetrySchema.ReadOptionalString(row, "state_json")
        };
        return artifact;
    }
}

/// <summary>
/// SQLite-backed usage-event store with dedupe-aware merge behavior.
/// </summary>
public sealed class SqliteUsageEventStore : IUsageEventStore, IDisposable {
    private readonly object _gate = new();
    private readonly SQLite _db = new();
    private readonly string _dbPath;
    private readonly Dictionary<string, UsageEventRecord> _canonicalEvents = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string> _dedupeKeyIndex = new(StringComparer.Ordinal);

    /// <summary>
    /// Initializes a new SQLite-backed usage-event store.
    /// </summary>
    /// <param name="dbPath">SQLite database path.</param>
    public SqliteUsageEventStore(string dbPath) {
        if (string.IsNullOrWhiteSpace(dbPath)) {
            throw new ArgumentException("Database path cannot be empty.", nameof(dbPath));
        }

        _dbPath = Path.GetFullPath(dbPath.Trim());
        SqliteUsageTelemetrySchema.EnsureSchema(_db, _dbPath);
        PrimeCache();
    }

    /// <inheritdoc />
    public UsageEventUpsertResult Upsert(UsageEventRecord record) {
        if (record is null) {
            throw new ArgumentNullException(nameof(record));
        }

        lock (_gate) {
            _db.BeginTransaction(_dbPath);
            try {
                var result = UpsertCore(record);
                _db.Commit();
                return result;
            } catch {
                try {
                    _db.Rollback();
                } catch {
                    // Ignore rollback failures.
                }
                throw;
            }
        }
    }

    /// <inheritdoc />
    public UsageEventBatchUpsertResult UpsertRange(IReadOnlyList<UsageEventRecord> records) {
        if (records is null) {
            throw new ArgumentNullException(nameof(records));
        }

        lock (_gate) {
            _db.BeginTransaction(_dbPath);
            try {
                var result = new UsageEventBatchUpsertResult();
                for (var i = 0; i < records.Count; i++) {
                    var upsert = UpsertCore(records[i]);
                    if (upsert.Inserted) {
                        result.Inserted++;
                    }
                    if (upsert.Updated) {
                        result.Updated++;
                    }
                }

                _db.Commit();
                return result;
            } catch {
                try {
                    _db.Rollback();
                } catch {
                    // Ignore rollback failures.
                }
                throw;
            }
        }
    }

    private bool TryFindExistingEvent(UsageEventRecord record, out UsageEventRecord existing) {
        if (_canonicalEvents.TryGetValue(record.EventId, out existing!)) {
            return true;
        }

        var incomingKeys = record.GetDeduplicationKeys();
        for (var i = 0; i < incomingKeys.Count; i++) {
            if (_dedupeKeyIndex.TryGetValue(incomingKeys[i], out var canonicalIdFromCache) &&
                _canonicalEvents.TryGetValue(canonicalIdFromCache, out existing!)) {
                return true;
            }
        }

        if (TryGetInternal(record.EventId, out existing!)) {
            TrackCanonicalEvent(existing, existing.GetDeduplicationKeys());
            return true;
        }

        var canonicalId = ResolveCanonicalEventId(record);
        if (!string.Equals(canonicalId, record.EventId, StringComparison.Ordinal) &&
            TryGetInternal(canonicalId, out existing!)) {
            TrackCanonicalEvent(existing, existing.GetDeduplicationKeys());
            return true;
        }

        var incomingKeySet = new HashSet<string>(incomingKeys, StringComparer.Ordinal);
        if (incomingKeySet.Count == 0) {
            existing = null!;
            return false;
        }

        foreach (var candidate in GetAllInternal()) {
            var candidateKeys = candidate.GetDeduplicationKeys();
            for (var i = 0; i < candidateKeys.Count; i++) {
                if (incomingKeySet.Contains(candidateKeys[i])) {
                    existing = candidate;
                    TrackCanonicalEvent(existing, candidateKeys);
                    return true;
                }
            }
        }

        existing = null!;
        return false;
    }

    /// <inheritdoc />
    public bool TryGet(string eventId, out UsageEventRecord record) {
        if (string.IsNullOrWhiteSpace(eventId)) {
            record = null!;
            return false;
        }

        lock (_gate) {
            return TryGetInternal(eventId.Trim(), out record!);
        }
    }

    /// <inheritdoc />
    public IReadOnlyList<UsageEventRecord> GetAll() {
        lock (_gate) {
            return GetAllInternal();
        }
    }

    /// <summary>
    /// Releases database resources held by the store.
    /// </summary>
    public void Dispose() {
        _db.Dispose();
    }

    private string ResolveCanonicalEventId(UsageEventRecord record) {
        foreach (var key in record.GetDeduplicationKeys()) {
            var table = SqliteUsageTelemetrySchema.QueryAsTable(_db.Query(
                _dbPath,
                "SELECT canonical_event_id FROM ix_usage_event_keys WHERE dedupe_key = @dedupe_key LIMIT 1;",
                parameters: new Dictionary<string, object?> {
                    ["@dedupe_key"] = key
                }));
            if (table is not null && table.Rows.Count > 0) {
                var existingId = table.Rows[0][0]?.ToString();
                if (!string.IsNullOrWhiteSpace(existingId)) {
                    return existingId!;
                }
            }
        }

        if (!string.IsNullOrWhiteSpace(record.ProviderAccountId) &&
            !string.IsNullOrWhiteSpace(record.SessionId) &&
            !string.IsNullOrWhiteSpace(record.TurnId)) {
            var canonicalId = FindCanonicalEventId(
                @"
SELECT canonical_event_id
FROM ix_usage_events
WHERE provider_id = @provider_id
  AND provider_account_id = @provider_account_id
  AND session_id = @session_id
  AND turn_id = @turn_id
LIMIT 1;",
                new Dictionary<string, object?> {
                    ["@provider_id"] = record.ProviderId,
                    ["@provider_account_id"] = record.ProviderAccountId,
                    ["@session_id"] = record.SessionId,
                    ["@turn_id"] = record.TurnId
                });
            if (!string.IsNullOrWhiteSpace(canonicalId)) {
                return canonicalId!;
            }
        }

        if (!string.IsNullOrWhiteSpace(record.ResponseId)) {
            var canonicalId = FindCanonicalEventId(
                @"
SELECT canonical_event_id
FROM ix_usage_events
WHERE provider_id = @provider_id
  AND response_id = @response_id
LIMIT 1;",
                new Dictionary<string, object?> {
                    ["@provider_id"] = record.ProviderId,
                    ["@response_id"] = record.ResponseId
                });
            if (!string.IsNullOrWhiteSpace(canonicalId)) {
                return canonicalId!;
            }
        }

        if (!string.IsNullOrWhiteSpace(record.RawHash)) {
            var canonicalId = FindCanonicalEventId(
                @"
SELECT canonical_event_id
FROM ix_usage_events
WHERE provider_id = @provider_id
  AND raw_hash = @raw_hash
LIMIT 1;",
                new Dictionary<string, object?> {
                    ["@provider_id"] = record.ProviderId,
                    ["@raw_hash"] = record.RawHash
                });
            if (!string.IsNullOrWhiteSpace(canonicalId)) {
                return canonicalId!;
            }
        }

        if (TryGetInternal(record.EventId, out var existing)) {
            return existing.EventId;
        }

        return record.EventId;
    }

    private UsageEventUpsertResult UpsertCore(UsageEventRecord record) {
        var inserted = false;
        var updated = false;
        UsageEventRecord canonical;

        if (TryFindExistingEvent(record, out canonical!)) {
            updated = MergeInto(canonical, record);
            PersistEvent(canonical);
        } else {
            canonical = Clone(record);
            PersistEvent(canonical);
            inserted = true;
        }

        RegisterDedupeKeys(canonical.EventId, canonical.GetDeduplicationKeys());
        RegisterDedupeKeys(canonical.EventId, record.GetDeduplicationKeys());
        TrackCanonicalEvent(canonical, canonical.GetDeduplicationKeys());
        TrackCanonicalEvent(canonical, record.GetDeduplicationKeys());

        return new UsageEventUpsertResult {
            CanonicalEventId = canonical.EventId,
            Inserted = inserted,
            Updated = !inserted && updated
        };
    }

    private string? FindCanonicalEventId(string sql, Dictionary<string, object?> parameters) {
        var table = SqliteUsageTelemetrySchema.QueryAsTable(_db.Query(_dbPath, sql, parameters: parameters));
        if (table is null || table.Rows.Count == 0) {
            return null;
        }

        var existingId = table.Rows[0][0]?.ToString();
        return string.IsNullOrWhiteSpace(existingId) ? null : existingId;
    }

    private void PrimeCache() {
        foreach (var record in GetAllInternal()) {
            TrackCanonicalEvent(record, record.GetDeduplicationKeys());
        }
    }

    private void TrackCanonicalEvent(UsageEventRecord record, IReadOnlyList<string> keys) {
        _canonicalEvents[record.EventId] = record;
        for (var i = 0; i < keys.Count; i++) {
            var key = keys[i];
            if (!string.IsNullOrWhiteSpace(key)) {
                _dedupeKeyIndex[key] = record.EventId;
            }
        }
    }

    private IReadOnlyList<UsageEventRecord> GetAllInternal() {
        var table = SqliteUsageTelemetrySchema.QueryAsTable(_db.Query(
            _dbPath,
            @"
SELECT
  canonical_event_id,
  event_id,
  provider_id,
  adapter_id,
  source_root_id,
  provider_account_id,
  account_label,
  person_label,
  machine_id,
  session_id,
  thread_id,
  conversation_title,
  workspace_path,
  repository_name,
  turn_id,
  response_id,
  timestamp_utc,
  model,
  surface,
  input_tokens,
  cached_input_tokens,
  output_tokens,
  reasoning_tokens,
  total_tokens,
  compact_count,
  duration_ms,
  cost_usd,
  truth_level,
  raw_hash
FROM ix_usage_events
ORDER BY timestamp_utc, provider_id, canonical_event_id;"));

        if (table is null || table.Rows.Count == 0) {
            return Array.Empty<UsageEventRecord>();
        }

        var list = new List<UsageEventRecord>(table.Rows.Count);
        foreach (DataRow row in table.Rows) {
            list.Add(ReadUsageEvent(row));
        }

        return list;
    }

    private bool TryGetInternal(string canonicalEventId, out UsageEventRecord record) {
        var table = SqliteUsageTelemetrySchema.QueryAsTable(_db.Query(
            _dbPath,
            @"
SELECT
  canonical_event_id,
  event_id,
  provider_id,
  adapter_id,
  source_root_id,
  provider_account_id,
  account_label,
  person_label,
  machine_id,
  session_id,
  thread_id,
  conversation_title,
  workspace_path,
  repository_name,
  turn_id,
  response_id,
  timestamp_utc,
  model,
  surface,
  input_tokens,
  cached_input_tokens,
  output_tokens,
  reasoning_tokens,
  total_tokens,
  compact_count,
  duration_ms,
  cost_usd,
  truth_level,
  raw_hash
FROM ix_usage_events
WHERE canonical_event_id = @canonical_event_id
LIMIT 1;",
            parameters: new Dictionary<string, object?> {
                ["@canonical_event_id"] = canonicalEventId
            }));

        if (table is null || table.Rows.Count == 0) {
            record = null!;
            return false;
        }

        record = ReadUsageEvent(table.Rows[0]);
        return true;
    }

    private void PersistEvent(UsageEventRecord record) {
        _db.ExecuteNonQuery(
            _dbPath,
            @"
INSERT INTO ix_usage_events (
  canonical_event_id,
  event_id,
  provider_id,
  adapter_id,
  source_root_id,
  provider_account_id,
  account_label,
  person_label,
  machine_id,
  session_id,
  thread_id,
  conversation_title,
  workspace_path,
  repository_name,
  turn_id,
  response_id,
  timestamp_utc,
  model,
  surface,
  input_tokens,
  cached_input_tokens,
  output_tokens,
  reasoning_tokens,
  total_tokens,
  compact_count,
  duration_ms,
  cost_usd,
  truth_level,
  raw_hash
)
VALUES (
  @canonical_event_id,
  @event_id,
  @provider_id,
  @adapter_id,
  @source_root_id,
  @provider_account_id,
  @account_label,
  @person_label,
  @machine_id,
  @session_id,
  @thread_id,
  @conversation_title,
  @workspace_path,
  @repository_name,
  @turn_id,
  @response_id,
  @timestamp_utc,
  @model,
  @surface,
  @input_tokens,
  @cached_input_tokens,
  @output_tokens,
  @reasoning_tokens,
  @total_tokens,
  @compact_count,
  @duration_ms,
  @cost_usd,
  @truth_level,
  @raw_hash
)
ON CONFLICT(canonical_event_id) DO UPDATE SET
  event_id = excluded.event_id,
  provider_id = excluded.provider_id,
  adapter_id = excluded.adapter_id,
  source_root_id = excluded.source_root_id,
  provider_account_id = excluded.provider_account_id,
  account_label = excluded.account_label,
  person_label = excluded.person_label,
  machine_id = excluded.machine_id,
  session_id = excluded.session_id,
  thread_id = excluded.thread_id,
  conversation_title = excluded.conversation_title,
  workspace_path = excluded.workspace_path,
  repository_name = excluded.repository_name,
  turn_id = excluded.turn_id,
  response_id = excluded.response_id,
  timestamp_utc = excluded.timestamp_utc,
  model = excluded.model,
  surface = excluded.surface,
  input_tokens = excluded.input_tokens,
  cached_input_tokens = excluded.cached_input_tokens,
  output_tokens = excluded.output_tokens,
  reasoning_tokens = excluded.reasoning_tokens,
  total_tokens = excluded.total_tokens,
  compact_count = excluded.compact_count,
  duration_ms = excluded.duration_ms,
  cost_usd = excluded.cost_usd,
  truth_level = excluded.truth_level,
  raw_hash = excluded.raw_hash;",
            useTransaction: _db.IsInTransaction,
            parameters: new Dictionary<string, object?> {
                ["@canonical_event_id"] = record.EventId,
                ["@event_id"] = record.EventId,
                ["@provider_id"] = record.ProviderId,
                ["@adapter_id"] = record.AdapterId,
                ["@source_root_id"] = record.SourceRootId,
                ["@provider_account_id"] = SqliteUsageTelemetrySchema.Normalize(record.ProviderAccountId),
                ["@account_label"] = SqliteUsageTelemetrySchema.Normalize(record.AccountLabel),
                ["@person_label"] = SqliteUsageTelemetrySchema.Normalize(record.PersonLabel),
                ["@machine_id"] = SqliteUsageTelemetrySchema.Normalize(record.MachineId),
                ["@session_id"] = SqliteUsageTelemetrySchema.Normalize(record.SessionId),
                ["@thread_id"] = SqliteUsageTelemetrySchema.Normalize(record.ThreadId),
                ["@conversation_title"] = SqliteUsageTelemetrySchema.Normalize(record.ConversationTitle),
                ["@workspace_path"] = SqliteUsageTelemetrySchema.Normalize(record.WorkspacePath),
                ["@repository_name"] = SqliteUsageTelemetrySchema.Normalize(record.RepositoryName),
                ["@turn_id"] = SqliteUsageTelemetrySchema.Normalize(record.TurnId),
                ["@response_id"] = SqliteUsageTelemetrySchema.Normalize(record.ResponseId),
                ["@timestamp_utc"] = record.TimestampUtc.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture),
                ["@model"] = SqliteUsageTelemetrySchema.Normalize(record.Model),
                ["@surface"] = SqliteUsageTelemetrySchema.Normalize(record.Surface),
                ["@input_tokens"] = record.InputTokens,
                ["@cached_input_tokens"] = record.CachedInputTokens,
                ["@output_tokens"] = record.OutputTokens,
                ["@reasoning_tokens"] = record.ReasoningTokens,
                ["@total_tokens"] = record.TotalTokens,
                ["@compact_count"] = record.CompactCount,
                ["@duration_ms"] = record.DurationMs,
                ["@cost_usd"] = record.CostUsd,
                ["@truth_level"] = record.TruthLevel.ToString(),
                ["@raw_hash"] = SqliteUsageTelemetrySchema.Normalize(record.RawHash)
            });
    }

    private void RegisterDedupeKeys(string canonicalEventId, IReadOnlyList<string> keys) {
        for (var i = 0; i < keys.Count; i++) {
            var key = keys[i];
            if (string.IsNullOrWhiteSpace(key)) {
                continue;
            }

            _db.ExecuteNonQuery(
                _dbPath,
                @"
INSERT INTO ix_usage_event_keys (dedupe_key, canonical_event_id)
VALUES (@dedupe_key, @canonical_event_id)
ON CONFLICT(dedupe_key) DO UPDATE SET
  canonical_event_id = excluded.canonical_event_id;",
                useTransaction: _db.IsInTransaction,
                parameters: new Dictionary<string, object?> {
                    ["@dedupe_key"] = key.Trim(),
                    ["@canonical_event_id"] = canonicalEventId
                });
        }
    }

    private static UsageEventRecord ReadUsageEvent(DataRow row) {
        var timestampText = row["timestamp_utc"]?.ToString();
        if (!DateTimeOffset.TryParse(timestampText, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var timestampUtc)) {
            timestampUtc = DateTimeOffset.MinValue;
        }

        var truthLevelText = row["truth_level"]?.ToString();
        if (!Enum.TryParse(truthLevelText, ignoreCase: true, out UsageTruthLevel truthLevel)) {
            truthLevel = UsageTruthLevel.Unknown;
        }

        var record = new UsageEventRecord(
            row["canonical_event_id"]?.ToString() ?? string.Empty,
            row["provider_id"]?.ToString() ?? string.Empty,
            row["adapter_id"]?.ToString() ?? string.Empty,
            row["source_root_id"]?.ToString() ?? string.Empty,
            timestampUtc) {
            ProviderAccountId = SqliteUsageTelemetrySchema.ReadOptionalString(row, "provider_account_id"),
            AccountLabel = SqliteUsageTelemetrySchema.ReadOptionalString(row, "account_label"),
            PersonLabel = SqliteUsageTelemetrySchema.ReadOptionalString(row, "person_label"),
            MachineId = SqliteUsageTelemetrySchema.ReadOptionalString(row, "machine_id"),
            SessionId = SqliteUsageTelemetrySchema.ReadOptionalString(row, "session_id"),
            ThreadId = SqliteUsageTelemetrySchema.ReadOptionalString(row, "thread_id"),
            ConversationTitle = SqliteUsageTelemetrySchema.ReadOptionalString(row, "conversation_title"),
            WorkspacePath = SqliteUsageTelemetrySchema.ReadOptionalString(row, "workspace_path"),
            RepositoryName = SqliteUsageTelemetrySchema.ReadOptionalString(row, "repository_name"),
            TurnId = SqliteUsageTelemetrySchema.ReadOptionalString(row, "turn_id"),
            ResponseId = SqliteUsageTelemetrySchema.ReadOptionalString(row, "response_id"),
            Model = SqliteUsageTelemetrySchema.ReadOptionalString(row, "model"),
            Surface = SqliteUsageTelemetrySchema.ReadOptionalString(row, "surface"),
            InputTokens = SqliteUsageTelemetrySchema.ReadNullableInt64(row, "input_tokens"),
            CachedInputTokens = SqliteUsageTelemetrySchema.ReadNullableInt64(row, "cached_input_tokens"),
            OutputTokens = SqliteUsageTelemetrySchema.ReadNullableInt64(row, "output_tokens"),
            ReasoningTokens = SqliteUsageTelemetrySchema.ReadNullableInt64(row, "reasoning_tokens"),
            TotalTokens = SqliteUsageTelemetrySchema.ReadNullableInt64(row, "total_tokens"),
            CompactCount = ReadNullableInt32(row, "compact_count"),
            DurationMs = SqliteUsageTelemetrySchema.ReadNullableInt64(row, "duration_ms"),
            CostUsd = SqliteUsageTelemetrySchema.ReadNullableDecimal(row, "cost_usd"),
            TruthLevel = truthLevel,
            RawHash = SqliteUsageTelemetrySchema.ReadOptionalString(row, "raw_hash")
        };
        return record;
    }

    private static UsageEventRecord Clone(UsageEventRecord record) {
        return new UsageEventRecord(
            record.EventId,
            record.ProviderId,
            record.AdapterId,
            record.SourceRootId,
            record.TimestampUtc) {
            ProviderAccountId = record.ProviderAccountId,
            AccountLabel = record.AccountLabel,
            PersonLabel = record.PersonLabel,
            MachineId = record.MachineId,
            SessionId = record.SessionId,
            ThreadId = record.ThreadId,
            ConversationTitle = record.ConversationTitle,
            WorkspacePath = record.WorkspacePath,
            RepositoryName = record.RepositoryName,
            TurnId = record.TurnId,
            ResponseId = record.ResponseId,
            Model = record.Model,
            Surface = record.Surface,
            InputTokens = record.InputTokens,
            CachedInputTokens = record.CachedInputTokens,
            OutputTokens = record.OutputTokens,
            ReasoningTokens = record.ReasoningTokens,
            TotalTokens = record.TotalTokens,
            CompactCount = record.CompactCount,
            DurationMs = record.DurationMs,
            CostUsd = record.CostUsd,
            TruthLevel = record.TruthLevel,
            RawHash = record.RawHash
        };
    }

    private static bool MergeInto(UsageEventRecord existing, UsageEventRecord incoming) {
        var updated = false;
        updated |= MergePreferredString(existing.ProviderAccountId, incoming.ProviderAccountId, value => existing.ProviderAccountId = value);
        updated |= MergePreferredString(existing.AccountLabel, incoming.AccountLabel, value => existing.AccountLabel = value);
        updated |= MergePreferredString(existing.PersonLabel, incoming.PersonLabel, value => existing.PersonLabel = value);
        updated |= MergeString(existing.MachineId, incoming.MachineId, value => existing.MachineId = value);
        updated |= MergeString(existing.SessionId, incoming.SessionId, value => existing.SessionId = value);
        updated |= MergeString(existing.ThreadId, incoming.ThreadId, value => existing.ThreadId = value);
        updated |= MergeString(existing.ConversationTitle, incoming.ConversationTitle, value => existing.ConversationTitle = value);
        updated |= MergeString(existing.WorkspacePath, incoming.WorkspacePath, value => existing.WorkspacePath = value);
        updated |= MergeString(existing.RepositoryName, incoming.RepositoryName, value => existing.RepositoryName = value);
        updated |= MergeString(existing.TurnId, incoming.TurnId, value => existing.TurnId = value);
        updated |= MergeString(existing.ResponseId, incoming.ResponseId, value => existing.ResponseId = value);
        updated |= MergeString(existing.Model, incoming.Model, value => existing.Model = value);
        updated |= MergeString(existing.Surface, incoming.Surface, value => existing.Surface = value);
        updated |= MergeNullableInt64(existing.InputTokens, incoming.InputTokens, value => existing.InputTokens = value);
        updated |= MergeNullableInt64(existing.CachedInputTokens, incoming.CachedInputTokens, value => existing.CachedInputTokens = value);
        updated |= MergeNullableInt64(existing.OutputTokens, incoming.OutputTokens, value => existing.OutputTokens = value);
        updated |= MergeNullableInt64(existing.ReasoningTokens, incoming.ReasoningTokens, value => existing.ReasoningTokens = value);
        updated |= MergeNullableInt64(existing.TotalTokens, incoming.TotalTokens, value => existing.TotalTokens = value);
        updated |= MergeNullableInt32(existing.CompactCount, incoming.CompactCount, value => existing.CompactCount = value);
        updated |= MergeNullableInt64(existing.DurationMs, incoming.DurationMs, value => existing.DurationMs = value);
        updated |= MergeNullableDecimal(existing.CostUsd, incoming.CostUsd, value => existing.CostUsd = value);
        updated |= MergeTruthLevel(existing.TruthLevel, incoming.TruthLevel, value => existing.TruthLevel = value);
        updated |= MergeString(existing.RawHash, incoming.RawHash, value => existing.RawHash = value);
        return updated;
    }

    private static bool MergeString(string? target, string? incoming, Action<string?> apply) {
        if (!string.IsNullOrWhiteSpace(target) || string.IsNullOrWhiteSpace(incoming)) {
            return false;
        }

        apply(incoming);
        return true;
    }

    private static bool MergePreferredString(string? target, string? incoming, Action<string?> apply) {
        if (string.IsNullOrWhiteSpace(incoming)) {
            return false;
        }
        if (string.Equals(target, incoming, StringComparison.OrdinalIgnoreCase)) {
            return false;
        }

        apply(incoming);
        return true;
    }

    private static bool MergeNullableInt64(long? target, long? incoming, Action<long?> apply) {
        if (target.HasValue || !incoming.HasValue) {
            return false;
        }

        apply(incoming);
        return true;
    }

    private static bool MergeNullableInt32(int? target, int? incoming, Action<int?> apply) {
        if (target.HasValue || !incoming.HasValue) {
            return false;
        }

        apply(incoming);
        return true;
    }

    private static int? ReadNullableInt32(DataRow row, string columnName) {
        var value = SqliteUsageTelemetrySchema.ReadNullableInt64(row, columnName);
        if (!value.HasValue) {
            return null;
        }

        return value.Value > int.MaxValue ? int.MaxValue : (int)value.Value;
    }

    private static bool MergeNullableDecimal(decimal? target, decimal? incoming, Action<decimal?> apply) {
        if (target.HasValue || !incoming.HasValue) {
            return false;
        }

        apply(incoming);
        return true;
    }

    private static bool MergeTruthLevel(UsageTruthLevel target, UsageTruthLevel incoming, Action<UsageTruthLevel> apply) {
        if ((int)incoming <= (int)target) {
            return false;
        }

        apply(incoming);
        return true;
    }
}

internal static class SqliteUsageTelemetrySchema {
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
CREATE TABLE IF NOT EXISTS ix_usage_source_roots (
  id TEXT PRIMARY KEY,
  provider_id TEXT NOT NULL,
  source_kind TEXT NOT NULL,
  path TEXT NOT NULL,
  platform_hint TEXT NULL,
  machine_label TEXT NULL,
  account_hint TEXT NULL,
  enabled INTEGER NOT NULL
);

CREATE TABLE IF NOT EXISTS ix_usage_raw_artifacts (
  source_root_id TEXT NOT NULL,
  adapter_id TEXT NOT NULL,
  path TEXT NOT NULL,
  fingerprint TEXT NOT NULL,
  parser_version TEXT NULL,
  size_bytes INTEGER NULL,
  last_write_utc TEXT NULL,
  imported_at_utc TEXT NOT NULL,
  parsed_bytes INTEGER NULL,
  state_json TEXT NULL,
  PRIMARY KEY(source_root_id, adapter_id, path)
);

CREATE TABLE IF NOT EXISTS ix_usage_events (
  canonical_event_id TEXT PRIMARY KEY,
  event_id TEXT NOT NULL,
  provider_id TEXT NOT NULL,
  adapter_id TEXT NOT NULL,
  source_root_id TEXT NOT NULL,
  provider_account_id TEXT NULL,
  account_label TEXT NULL,
  person_label TEXT NULL,
  machine_id TEXT NULL,
  session_id TEXT NULL,
  thread_id TEXT NULL,
  conversation_title TEXT NULL,
  workspace_path TEXT NULL,
  repository_name TEXT NULL,
  turn_id TEXT NULL,
  response_id TEXT NULL,
  timestamp_utc TEXT NOT NULL,
  model TEXT NULL,
  surface TEXT NULL,
  input_tokens INTEGER NULL,
  cached_input_tokens INTEGER NULL,
  output_tokens INTEGER NULL,
  reasoning_tokens INTEGER NULL,
  total_tokens INTEGER NULL,
  compact_count INTEGER NULL,
  duration_ms INTEGER NULL,
  cost_usd REAL NULL,
  truth_level TEXT NOT NULL,
  raw_hash TEXT NULL
);

CREATE TABLE IF NOT EXISTS ix_usage_event_keys (
  dedupe_key TEXT PRIMARY KEY,
  canonical_event_id TEXT NOT NULL
);

CREATE TABLE IF NOT EXISTS ix_usage_account_bindings (
  id TEXT PRIMARY KEY,
  provider_id TEXT NOT NULL,
  source_root_id TEXT NULL,
  match_provider_account_id TEXT NULL,
  match_account_label TEXT NULL,
  provider_account_id TEXT NULL,
  account_label TEXT NULL,
  person_label TEXT NULL,
  enabled INTEGER NOT NULL
);

CREATE INDEX IF NOT EXISTS ix_usage_source_roots_provider_path ON ix_usage_source_roots(provider_id, path);
CREATE INDEX IF NOT EXISTS ix_usage_raw_artifacts_root_adapter ON ix_usage_raw_artifacts(source_root_id, adapter_id);
CREATE INDEX IF NOT EXISTS ix_usage_events_timestamp ON ix_usage_events(timestamp_utc);
CREATE INDEX IF NOT EXISTS ix_usage_events_provider_account ON ix_usage_events(provider_id, provider_account_id);
CREATE INDEX IF NOT EXISTS ix_usage_events_session ON ix_usage_events(provider_id, session_id);
CREATE INDEX IF NOT EXISTS ix_usage_events_response ON ix_usage_events(provider_id, response_id);");
        try {
            db.ExecuteNonQuery(fullPath, "ALTER TABLE ix_usage_events ADD COLUMN person_label TEXT NULL;");
        } catch {
            // Ignore when the column already exists.
        }
        try {
            db.ExecuteNonQuery(fullPath, "ALTER TABLE ix_usage_events ADD COLUMN compact_count INTEGER NULL;");
        } catch {
            // Ignore when the column already exists.
        }
        try {
            db.ExecuteNonQuery(fullPath, "ALTER TABLE ix_usage_events ADD COLUMN conversation_title TEXT NULL;");
        } catch {
            // Ignore when the column already exists.
        }
        try {
            db.ExecuteNonQuery(fullPath, "ALTER TABLE ix_usage_events ADD COLUMN workspace_path TEXT NULL;");
        } catch {
            // Ignore when the column already exists.
        }
        try {
            db.ExecuteNonQuery(fullPath, "ALTER TABLE ix_usage_events ADD COLUMN repository_name TEXT NULL;");
        } catch {
            // Ignore when the column already exists.
        }
        try {
            db.ExecuteNonQuery(fullPath, "ALTER TABLE ix_usage_raw_artifacts ADD COLUMN parsed_bytes INTEGER NULL;");
        } catch {
            // Ignore when the column already exists.
        }
        try {
            db.ExecuteNonQuery(fullPath, "ALTER TABLE ix_usage_raw_artifacts ADD COLUMN state_json TEXT NULL;");
        } catch {
            // Ignore when the column already exists.
        }
    }

    public static DataTable? QueryAsTable(object? queryResult) {
        if (queryResult is DataTable table) {
            return table;
        }

        if (queryResult is DataSet dataSet && dataSet.Tables.Count > 0) {
            return dataSet.Tables[0];
        }

        return null;
    }

    public static string? ReadOptionalString(DataRow row, string columnName) {
        var value = row[columnName];
        if (value is DBNull || value is null) {
            return null;
        }

        var text = value.ToString();
        return string.IsNullOrWhiteSpace(text) ? null : text.Trim();
    }

    public static bool ReadBoolean(DataRow row, string columnName) {
        var value = row[columnName];
        if (value is DBNull || value is null) {
            return false;
        }

        if (value is bool boolValue) {
            return boolValue;
        }

        if (value is int intValue) {
            return intValue != 0;
        }

        if (value is long longValue) {
            return longValue != 0;
        }

        var text = value.ToString();
        if (bool.TryParse(text, out var parsedBool)) {
            return parsedBool;
        }
        if (long.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsedLong)) {
            return parsedLong != 0;
        }

        return false;
    }

    public static long? ReadNullableInt64(DataRow row, string columnName) {
        var value = row[columnName];
        if (value is DBNull || value is null) {
            return null;
        }

        if (value is long longValue) {
            return longValue;
        }
        if (value is int intValue) {
            return intValue;
        }
        if (value is short shortValue) {
            return shortValue;
        }
        if (value is decimal decimalValue) {
            return decimal.ToInt64(decimalValue);
        }
        if (value is double doubleValue) {
            return (long)Math.Round(doubleValue);
        }

        var text = value.ToString();
        if (long.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed)) {
            return parsed;
        }
        if (double.TryParse(text, NumberStyles.Float | NumberStyles.AllowThousands, CultureInfo.InvariantCulture, out var parsedDouble)) {
            return (long)Math.Round(parsedDouble);
        }

        return null;
    }

    public static decimal? ReadNullableDecimal(DataRow row, string columnName) {
        var value = row[columnName];
        if (value is DBNull || value is null) {
            return null;
        }

        if (value is decimal decimalValue) {
            return decimalValue;
        }
        if (value is double doubleValue) {
            return Convert.ToDecimal(doubleValue, CultureInfo.InvariantCulture);
        }
        if (value is float floatValue) {
            return Convert.ToDecimal(floatValue, CultureInfo.InvariantCulture);
        }

        var text = value.ToString();
        if (decimal.TryParse(text, NumberStyles.Float | NumberStyles.AllowThousands, CultureInfo.InvariantCulture, out var parsed)) {
            return parsed;
        }

        return null;
    }

    public static DateTimeOffset? ReadNullableDateTimeOffset(DataRow row, string columnName) {
        var value = row[columnName];
        if (value is DBNull || value is null) {
            return null;
        }

        var text = value.ToString();
        if (DateTimeOffset.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var parsed)) {
            return parsed.ToUniversalTime();
        }

        return null;
    }

    public static object? Normalize(string? value) {
        var trimmed = (value ?? string.Empty).Trim();
        return trimmed.Length == 0 ? null : trimmed;
    }
}
#endif
