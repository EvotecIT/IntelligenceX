using System;
using System.Collections.Generic;
using System.Data;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using DBAClientX;
using IntelligenceX.OpenAI;
using IntelligenceX.OpenAI.Chat;
using IntelligenceX.OpenAI.CompatibleHttp;
using IntelligenceX.Tools;

namespace IntelligenceX.Chat.Profiles;

/// <summary>
/// SQLite-backed profile store (file-based).
/// </summary>
internal sealed partial class SqliteServiceProfileStore : IServiceProfileStore, IDisposable {
    private const string DefaultWriteGovernanceMode = "enforced";
    private const string DefaultWriteAuditSinkMode = "none";
    private const string DefaultAuthenticationRuntimePreset = "default";

    private const string ProfileTable = "ix_service_profiles";
    private const string AllowedRootsTable = "ix_service_profile_allowed_roots";
    private const string BuiltInToolAssemblyNamesTable = "ix_service_profile_built_in_tool_assembly_names";
    private const string PluginPathsTable = "ix_service_profile_plugin_paths";
    private const string DisabledPackIdsTable = "ix_service_profile_disabled_pack_ids";
    private const string EnabledPackIdsTable = "ix_service_profile_enabled_pack_ids";

    private readonly string _dbPath;
    private readonly SQLite _db = new();

    public SqliteServiceProfileStore(string dbPath) {
        if (string.IsNullOrWhiteSpace(dbPath)) throw new ArgumentException("Database path cannot be empty.", nameof(dbPath));
        _dbPath = dbPath;

        var dir = Path.GetDirectoryName(_dbPath);
        if (!string.IsNullOrWhiteSpace(dir) && !Directory.Exists(dir)) {
            Directory.CreateDirectory(dir);
        }

        EnsureSchema();
    }

    private void EnsureSchema() {
        // Breaking change is acceptable: if we detect the legacy JSON schema, drop and recreate.
        if (HasLegacyJsonSchema()) {
            _db.ExecuteNonQuery(_dbPath, $@"
DROP TABLE IF EXISTS {AllowedRootsTable};
DROP TABLE IF EXISTS {BuiltInToolAssemblyNamesTable};
DROP TABLE IF EXISTS {PluginPathsTable};
DROP TABLE IF EXISTS {DisabledPackIdsTable};
DROP TABLE IF EXISTS {EnabledPackIdsTable};
DROP TABLE IF EXISTS {ProfileTable};");
        }

        _db.ExecuteNonQuery(_dbPath, $@"
CREATE TABLE IF NOT EXISTS {ProfileTable} (
  name TEXT PRIMARY KEY,
  model TEXT NOT NULL,
  transport_kind TEXT NOT NULL,
  openai_base_url TEXT NULL,
  openai_auth_mode TEXT NOT NULL DEFAULT 'bearer',
  openai_api_key BLOB NULL,
  openai_basic_username TEXT NULL,
  openai_basic_password BLOB NULL,
  openai_account_id TEXT NULL,
  openai_streaming INTEGER NOT NULL,
  openai_allow_insecure_http INTEGER NOT NULL,
  openai_allow_insecure_http_non_loopback INTEGER NOT NULL,
  reasoning_effort TEXT NULL,
  reasoning_summary TEXT NULL,
  text_verbosity TEXT NULL,
  temperature REAL NULL,
  max_tool_rounds INTEGER NOT NULL,
  parallel_tools INTEGER NOT NULL,
  allow_mutating_parallel_tool_calls INTEGER NOT NULL DEFAULT 0,
  turn_timeout_seconds INTEGER NOT NULL,
  tool_timeout_seconds INTEGER NOT NULL,
  instructions_file TEXT NULL,
  max_table_rows INTEGER NOT NULL,
  max_sample INTEGER NOT NULL,
  redact INTEGER NOT NULL,
  ad_domain_controller TEXT NULL,
  ad_default_search_base_dn TEXT NULL,
  ad_max_results INTEGER NOT NULL,
  powershell_allow_write INTEGER NOT NULL,
  enable_built_in_pack_loading INTEGER NOT NULL DEFAULT 1,
  use_default_built_in_tool_assembly_names INTEGER NOT NULL DEFAULT 1,
  enable_default_plugin_paths INTEGER NOT NULL,
  write_governance_mode TEXT NOT NULL DEFAULT 'enforced',
  require_write_governance_runtime INTEGER NOT NULL DEFAULT 1,
  require_write_audit_sink INTEGER NOT NULL DEFAULT 0,
  require_explicit_routing_metadata INTEGER NOT NULL DEFAULT 1,
  write_audit_sink_mode TEXT NOT NULL DEFAULT 'none',
  write_audit_sink_path TEXT NULL,
  authentication_runtime_preset TEXT NOT NULL DEFAULT 'default',
  require_authentication_runtime INTEGER NOT NULL DEFAULT 0,
  run_as_profile_path TEXT NULL,
  authentication_profile_path TEXT NULL,
  updated_utc TEXT NOT NULL
);

CREATE TABLE IF NOT EXISTS {AllowedRootsTable} (
  profile_name TEXT NOT NULL,
  ord INTEGER NOT NULL,
  path TEXT NOT NULL,
  PRIMARY KEY (profile_name, ord),
  UNIQUE (profile_name, path)
);

CREATE TABLE IF NOT EXISTS {BuiltInToolAssemblyNamesTable} (
  profile_name TEXT NOT NULL,
  ord INTEGER NOT NULL,
  path TEXT NOT NULL,
  PRIMARY KEY (profile_name, ord),
  UNIQUE (profile_name, path)
);

CREATE TABLE IF NOT EXISTS {PluginPathsTable} (
  profile_name TEXT NOT NULL,
  ord INTEGER NOT NULL,
  path TEXT NOT NULL,
  PRIMARY KEY (profile_name, ord),
  UNIQUE (profile_name, path)
);

CREATE TABLE IF NOT EXISTS {DisabledPackIdsTable} (
  profile_name TEXT NOT NULL,
  ord INTEGER NOT NULL,
  path TEXT NOT NULL,
  PRIMARY KEY (profile_name, ord),
  UNIQUE (profile_name, path)
);

CREATE TABLE IF NOT EXISTS {EnabledPackIdsTable} (
  profile_name TEXT NOT NULL,
  ord INTEGER NOT NULL,
  path TEXT NOT NULL,
  PRIMARY KEY (profile_name, ord),
  UNIQUE (profile_name, path)
);

CREATE INDEX IF NOT EXISTS ix_service_profiles_updated_utc ON {ProfileTable}(updated_utc);
CREATE INDEX IF NOT EXISTS ix_service_profiles_transport_kind ON {ProfileTable}(transport_kind);
");

        var knownProfileColumns = TryGetTableColumns(ProfileTable);
        EnsureColumnExists(ProfileTable, knownProfileColumns, "write_governance_mode", "TEXT NOT NULL DEFAULT 'enforced'");
        EnsureColumnExists(ProfileTable, knownProfileColumns, "require_write_governance_runtime", "INTEGER NOT NULL DEFAULT 1");
        EnsureColumnExists(ProfileTable, knownProfileColumns, "require_write_audit_sink", "INTEGER NOT NULL DEFAULT 0");
        EnsureColumnExists(ProfileTable, knownProfileColumns, "require_explicit_routing_metadata", "INTEGER NOT NULL DEFAULT 1");
        EnsureColumnExists(ProfileTable, knownProfileColumns, "write_audit_sink_mode", "TEXT NOT NULL DEFAULT 'none'");
        EnsureColumnExists(ProfileTable, knownProfileColumns, "write_audit_sink_path", "TEXT NULL");
        EnsureColumnExists(ProfileTable, knownProfileColumns, "openai_account_id", "TEXT NULL");
        EnsureColumnExists(ProfileTable, knownProfileColumns, "openai_auth_mode", "TEXT NOT NULL DEFAULT 'bearer'");
        EnsureColumnExists(ProfileTable, knownProfileColumns, "openai_basic_username", "TEXT NULL");
        EnsureColumnExists(ProfileTable, knownProfileColumns, "openai_basic_password", "BLOB NULL");
        EnsureColumnExists(ProfileTable, knownProfileColumns, "allow_mutating_parallel_tool_calls", "INTEGER NOT NULL DEFAULT 0");
        EnsureColumnExists(ProfileTable, knownProfileColumns, "enable_built_in_pack_loading", "INTEGER NOT NULL DEFAULT 1");
        EnsureColumnExists(ProfileTable, knownProfileColumns, "use_default_built_in_tool_assembly_names", "INTEGER NOT NULL DEFAULT 1");
        EnsureColumnExists(ProfileTable, knownProfileColumns, "authentication_runtime_preset", "TEXT NOT NULL DEFAULT 'default'");
        EnsureColumnExists(ProfileTable, knownProfileColumns, "require_authentication_runtime", "INTEGER NOT NULL DEFAULT 0");
        EnsureColumnExists(ProfileTable, knownProfileColumns, "run_as_profile_path", "TEXT NULL");
        EnsureColumnExists(ProfileTable, knownProfileColumns, "authentication_profile_path", "TEXT NULL");

        var refreshedProfileColumns = TryGetTableColumns(ProfileTable);
        if (HasDeprecatedPackToggleColumns(refreshedProfileColumns)) {
            MigrateProfileTableDroppingDeprecatedPackToggleColumns(refreshedProfileColumns);
        }
    }

    private bool HasLegacyJsonSchema() {
        try {
            var dt = QueryAsTable(_db.Query(_dbPath, $"PRAGMA table_info('{ProfileTable}')"));
            if (dt is null || dt.Rows.Count == 0) {
                return false;
            }

            foreach (DataRow row in dt.Rows) {
                var name = row["name"]?.ToString();
                if (string.Equals(name, "json", StringComparison.OrdinalIgnoreCase)) {
                    return true;
                }
            }
            return false;
        } catch {
            return false;
        }
    }

    private HashSet<string> TryGetTableColumns(string tableName) {
        var columns = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (string.IsNullOrWhiteSpace(tableName)) {
            return columns;
        }

        try {
            var schemaProbe = _db.Query(_dbPath, $"SELECT * FROM {tableName} LIMIT 1");
            if (schemaProbe is DataTable dt && dt.Columns.Count > 0) {
                foreach (DataColumn col in dt.Columns) {
                    var name = col.ColumnName;
                    if (!string.IsNullOrWhiteSpace(name)) {
                        columns.Add(name);
                    }
                }
            }
        } catch {
            // Ignore and fallback to PRAGMA probe below.
        }

        if (columns.Count > 0) {
            return columns;
        }

        try {
            var pragma = _db.Query(_dbPath, $"PRAGMA table_info('{tableName}')");
            if (pragma is DataTable dt && dt.Rows.Count > 0) {
                foreach (DataRow row in dt.Rows) {
                    var name = row["name"]?.ToString();
                    if (!string.IsNullOrWhiteSpace(name)) {
                        columns.Add(name);
                    }
                }
            }
        } catch {
            // Best-effort metadata probe only.
        }

        if (columns.Count > 0) {
            return columns;
        }

        try {
            var createTableSql = QueryAsTable(_db.Query(
                _dbPath,
                "SELECT sql FROM sqlite_master WHERE type = 'table' AND name = @name LIMIT 1;",
                parameters: new Dictionary<string, object?> { ["@name"] = tableName }));
            var createSql = createTableSql?.Rows.Count > 0
                ? createTableSql.Rows[0][0]?.ToString()
                : null;
            if (!string.IsNullOrWhiteSpace(createSql)) {
                foreach (var parsed in ParseColumnNamesFromCreateSql(createSql)) {
                    columns.Add(parsed);
                }
            }
        } catch {
            // Best-effort metadata probe only.
        }

        return columns;
    }

    private static IReadOnlyList<string> ParseColumnNamesFromCreateSql(string createTableSql) {
        var sql = (createTableSql ?? string.Empty).Trim();
        if (sql.Length == 0) {
            return Array.Empty<string>();
        }

        var openIndex = sql.IndexOf('(');
        var closeIndex = sql.LastIndexOf(')');
        if (openIndex < 0 || closeIndex <= openIndex) {
            return Array.Empty<string>();
        }

        var body = sql.Substring(openIndex + 1, closeIndex - openIndex - 1);
        var parts = new List<string>();
        var depth = 0;
        var segmentStart = 0;
        for (var i = 0; i < body.Length; i++) {
            var ch = body[i];
            if (ch == '(') {
                depth++;
                continue;
            }

            if (ch == ')') {
                depth = Math.Max(0, depth - 1);
                continue;
            }

            if (ch != ',' || depth != 0) {
                continue;
            }

            parts.Add(body.Substring(segmentStart, i - segmentStart));
            segmentStart = i + 1;
        }
        if (segmentStart < body.Length) {
            parts.Add(body.Substring(segmentStart));
        }

        var columns = new List<string>(parts.Count);
        for (var i = 0; i < parts.Count; i++) {
            var part = (parts[i] ?? string.Empty).Trim();
            if (part.Length == 0) {
                continue;
            }

            if (part.StartsWith("PRIMARY KEY", StringComparison.OrdinalIgnoreCase)
                || part.StartsWith("CONSTRAINT", StringComparison.OrdinalIgnoreCase)
                || part.StartsWith("UNIQUE", StringComparison.OrdinalIgnoreCase)
                || part.StartsWith("CHECK", StringComparison.OrdinalIgnoreCase)
                || part.StartsWith("FOREIGN KEY", StringComparison.OrdinalIgnoreCase)) {
                continue;
            }

            var token = ExtractLeadingSqlIdentifier(part);
            if (!string.IsNullOrWhiteSpace(token)) {
                columns.Add(token);
            }
        }

        return columns;
    }

    private static string ExtractLeadingSqlIdentifier(string segment) {
        var value = (segment ?? string.Empty).Trim();
        if (value.Length == 0) {
            return string.Empty;
        }

        if (value[0] == '"' || value[0] == '`' || value[0] == '[') {
            var close = value[0] == '[' ? ']' : value[0];
            var closeIndex = value.IndexOf(close, 1);
            if (closeIndex > 1) {
                return value.Substring(1, closeIndex - 1).Trim();
            }
        }

        var end = 0;
        while (end < value.Length && !char.IsWhiteSpace(value[end])) {
            end++;
        }

        var token = value.Substring(0, end).Trim();
        return token.Trim('"', '`', '[', ']');
    }

    private void EnsureColumnExists(string tableName, HashSet<string> knownColumns, string columnName, string columnDefinition) {
        if (string.IsNullOrWhiteSpace(tableName) || string.IsNullOrWhiteSpace(columnName) || string.IsNullOrWhiteSpace(columnDefinition)) {
            return;
        }

        if (knownColumns.Contains(columnName)) {
            return;
        }

        try {
            _db.ExecuteNonQuery(_dbPath, $"ALTER TABLE {tableName} ADD COLUMN {columnName} {columnDefinition};");
            knownColumns.Add(columnName);
        } catch {
            // Best-effort schema evolution; keep startup resilient.
        }
    }

    private static bool HasDeprecatedPackToggleColumns(IEnumerable<string> columns) {
        foreach (var column in columns) {
            if (IsDeprecatedPackToggleColumn(column)) {
                return true;
            }
        }

        return false;
    }

    private static bool IsDeprecatedPackToggleColumn(string? columnName) {
        var normalized = (columnName ?? string.Empty).Trim();
        if (normalized.Length == 0) {
            return false;
        }

        if (!normalized.StartsWith("enable_", StringComparison.OrdinalIgnoreCase)
            || !normalized.EndsWith("_pack", StringComparison.OrdinalIgnoreCase)) {
            return false;
        }

        return !string.Equals(normalized, "enable_default_plugin_paths", StringComparison.OrdinalIgnoreCase);
    }

    private static List<string> ResolveDeprecatedPackToggleColumns(IEnumerable<string> columns) {
        var result = new List<string>();
        foreach (var column in columns) {
            if (!IsDeprecatedPackToggleColumn(column)) {
                continue;
            }

            result.Add(column);
        }

        return result;
    }

    private void MigrateProfileTableDroppingDeprecatedPackToggleColumns(IEnumerable<string>? knownColumns = null) {
        const string migratedTable = "ix_service_profiles_v2";
        const string currentColumnList = "name, model, transport_kind, openai_base_url, openai_auth_mode, openai_api_key, openai_basic_username, openai_basic_password, openai_account_id, openai_streaming, openai_allow_insecure_http, openai_allow_insecure_http_non_loopback, reasoning_effort, reasoning_summary, text_verbosity, temperature, max_tool_rounds, parallel_tools, allow_mutating_parallel_tool_calls, turn_timeout_seconds, tool_timeout_seconds, instructions_file, max_table_rows, max_sample, redact, ad_domain_controller, ad_default_search_base_dn, ad_max_results, powershell_allow_write, enable_built_in_pack_loading, use_default_built_in_tool_assembly_names, enable_default_plugin_paths, write_governance_mode, require_write_governance_runtime, require_write_audit_sink, require_explicit_routing_metadata, write_audit_sink_mode, write_audit_sink_path, authentication_runtime_preset, require_authentication_runtime, run_as_profile_path, authentication_profile_path, updated_utc";
        var legacyToggleColumns = ResolveDeprecatedPackToggleColumns(knownColumns ?? TryGetTableColumns(ProfileTable));

        _db.BeginTransaction(_dbPath);
        try {
            _db.ExecuteNonQuery(_dbPath, $"DROP TABLE IF EXISTS {migratedTable};", useTransaction: true);
            _db.ExecuteNonQuery(_dbPath, $@"
CREATE TABLE {migratedTable} (
  name TEXT PRIMARY KEY,
  model TEXT NOT NULL,
  transport_kind TEXT NOT NULL,
  openai_base_url TEXT NULL,
  openai_auth_mode TEXT NOT NULL DEFAULT 'bearer',
  openai_api_key BLOB NULL,
  openai_basic_username TEXT NULL,
  openai_basic_password BLOB NULL,
  openai_account_id TEXT NULL,
  openai_streaming INTEGER NOT NULL,
  openai_allow_insecure_http INTEGER NOT NULL,
  openai_allow_insecure_http_non_loopback INTEGER NOT NULL,
  reasoning_effort TEXT NULL,
  reasoning_summary TEXT NULL,
  text_verbosity TEXT NULL,
  temperature REAL NULL,
  max_tool_rounds INTEGER NOT NULL,
  parallel_tools INTEGER NOT NULL,
  allow_mutating_parallel_tool_calls INTEGER NOT NULL DEFAULT 0,
  turn_timeout_seconds INTEGER NOT NULL,
  tool_timeout_seconds INTEGER NOT NULL,
  instructions_file TEXT NULL,
  max_table_rows INTEGER NOT NULL,
  max_sample INTEGER NOT NULL,
  redact INTEGER NOT NULL,
  ad_domain_controller TEXT NULL,
  ad_default_search_base_dn TEXT NULL,
  ad_max_results INTEGER NOT NULL,
  powershell_allow_write INTEGER NOT NULL,
  enable_built_in_pack_loading INTEGER NOT NULL DEFAULT 1,
  use_default_built_in_tool_assembly_names INTEGER NOT NULL DEFAULT 1,
  enable_default_plugin_paths INTEGER NOT NULL,
  write_governance_mode TEXT NOT NULL DEFAULT 'enforced',
  require_write_governance_runtime INTEGER NOT NULL DEFAULT 1,
  require_write_audit_sink INTEGER NOT NULL DEFAULT 0,
  require_explicit_routing_metadata INTEGER NOT NULL DEFAULT 1,
  write_audit_sink_mode TEXT NOT NULL DEFAULT 'none',
  write_audit_sink_path TEXT NULL,
  authentication_runtime_preset TEXT NOT NULL DEFAULT 'default',
  require_authentication_runtime INTEGER NOT NULL DEFAULT 0,
  run_as_profile_path TEXT NULL,
  authentication_profile_path TEXT NULL,
  updated_utc TEXT NOT NULL
);", useTransaction: true);
            _db.ExecuteNonQuery(_dbPath, $@"
INSERT INTO {migratedTable} ({currentColumnList})
SELECT {currentColumnList}
FROM {ProfileTable};", useTransaction: true);
            MigrateLegacyPackToggleOverrides(legacyToggleColumns, useTransaction: true);
            _db.ExecuteNonQuery(_dbPath, $"DROP TABLE {ProfileTable};", useTransaction: true);
            _db.ExecuteNonQuery(_dbPath, $"ALTER TABLE {migratedTable} RENAME TO {ProfileTable};", useTransaction: true);
            _db.ExecuteNonQuery(_dbPath, $"CREATE INDEX IF NOT EXISTS ix_service_profiles_updated_utc ON {ProfileTable}(updated_utc);", useTransaction: true);
            _db.ExecuteNonQuery(_dbPath, $"CREATE INDEX IF NOT EXISTS ix_service_profiles_transport_kind ON {ProfileTable}(transport_kind);", useTransaction: true);
            _db.Commit();
        } catch {
            try {
                _db.Rollback();
            } catch {
                // Ignore rollback failures.
            }

            throw;
        }
    }

    private void MigrateLegacyPackToggleOverrides(IReadOnlyList<string> legacyToggleColumns, bool useTransaction = false) {
        if (legacyToggleColumns is null || legacyToggleColumns.Count == 0) {
            return;
        }

        for (var i = 0; i < legacyToggleColumns.Count; i++) {
            var column = (legacyToggleColumns[i] ?? string.Empty).Trim();
            if (column.Length == 0 || !IsSafeSqlIdentifier(column)) {
                continue;
            }

            if (!TryResolveDeprecatedPackIdFromColumn(column, out var packId)) {
                continue;
            }

            var parameters = new Dictionary<string, object?> {
                ["@pack_id"] = packId
            };

            _db.ExecuteNonQuery(
                _dbPath,
                $@"
DELETE FROM {DisabledPackIdsTable}
WHERE path = @pack_id
  AND profile_name IN (
      SELECT name
      FROM {ProfileTable}
      WHERE COALESCE(CAST({column} AS INTEGER), 0) <> 0
  );",
                useTransaction: useTransaction,
                parameters: parameters);
            _db.ExecuteNonQuery(
                _dbPath,
                $@"
INSERT OR IGNORE INTO {EnabledPackIdsTable} (profile_name, ord, path)
SELECT p.name,
       COALESCE((SELECT MAX(e.ord) + 1 FROM {EnabledPackIdsTable} e WHERE e.profile_name = p.name), 0),
       @pack_id
FROM {ProfileTable} p
WHERE COALESCE(CAST(p.{column} AS INTEGER), 0) <> 0;",
                useTransaction: useTransaction,
                parameters: parameters);

            _db.ExecuteNonQuery(
                _dbPath,
                $@"
DELETE FROM {EnabledPackIdsTable}
WHERE path = @pack_id
  AND profile_name IN (
      SELECT name
      FROM {ProfileTable}
      WHERE COALESCE(CAST({column} AS INTEGER), 0) = 0
  );",
                useTransaction: useTransaction,
                parameters: parameters);
            _db.ExecuteNonQuery(
                _dbPath,
                $@"
INSERT OR IGNORE INTO {DisabledPackIdsTable} (profile_name, ord, path)
SELECT p.name,
       COALESCE((SELECT MAX(d.ord) + 1 FROM {DisabledPackIdsTable} d WHERE d.profile_name = p.name), 0),
       @pack_id
FROM {ProfileTable} p
WHERE COALESCE(CAST(p.{column} AS INTEGER), 0) = 0;",
                useTransaction: useTransaction,
                parameters: parameters);
        }
    }

    private static bool TryResolveDeprecatedPackIdFromColumn(string? columnName, out string packId) {
        packId = string.Empty;
        var normalized = (columnName ?? string.Empty).Trim();
        if (!IsDeprecatedPackToggleColumn(normalized)) {
            return false;
        }

        const string prefix = "enable_";
        const string suffix = "_pack";
        if (!normalized.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
            || !normalized.EndsWith(suffix, StringComparison.OrdinalIgnoreCase)
            || normalized.Length <= prefix.Length + suffix.Length) {
            return false;
        }

        var tokenLength = normalized.Length - prefix.Length - suffix.Length;
        var token = normalized.Substring(prefix.Length, tokenLength);
        var normalizedPackId = ToolSelectionMetadata.NormalizePackId(token);
        if (normalizedPackId.Length == 0) {
            return false;
        }

        packId = normalizedPackId;
        return true;
    }

    private static bool IsSafeSqlIdentifier(string value) {
        for (var i = 0; i < value.Length; i++) {
            var ch = value[i];
            if (ch == '_' || char.IsLetterOrDigit(ch)) {
                continue;
            }

            return false;
        }

        return value.Length > 0;
    }

    public Task<ServiceProfile?> GetAsync(string name, CancellationToken cancellationToken) {
        cancellationToken.ThrowIfCancellationRequested();
        if (string.IsNullOrWhiteSpace(name)) {
            return Task.FromResult<ServiceProfile?>(null);
        }

        var trimmed = name.Trim();
        var row = QueryAsTable(_db.Query(
            _dbPath,
            $@"
SELECT *
FROM {ProfileTable}
WHERE name = @name
LIMIT 1;",
            parameters: new Dictionary<string, object?> { ["@name"] = trimmed }));

        if (row is null || row.Rows.Count == 0) {
            return Task.FromResult<ServiceProfile?>(null);
        }

        var r = row.Rows[0];
        var transportText = ReadString(r, "transport_kind") ?? "native";
        if (!TryParseTransport(transportText, out var transport)) {
            throw new InvalidOperationException($"Profile '{trimmed}' has an invalid transport_kind '{transportText}'.");
        }
        var authModeText = ReadString(r, "openai_auth_mode") ?? "bearer";
        if (!TryParseCompatibleAuthMode(authModeText, out var authMode)) {
            throw new InvalidOperationException($"Profile '{trimmed}' has an invalid openai_auth_mode '{authModeText}'.");
        }

        string? apiKey = null;
        var apiKeyBytes = ReadBytes(r, "openai_api_key");
        if (apiKeyBytes is { Length: > 0 }) {
            try {
                apiKey = DpapiSecretProtector.UnprotectString(apiKeyBytes);
            } catch (Exception ex) {
                throw new InvalidOperationException($"Profile '{trimmed}' contains a protected API key that could not be decrypted.", ex);
            }
        }
        string? basicPassword = null;
        var basicPasswordBytes = ReadBytes(r, "openai_basic_password");
        if (basicPasswordBytes is { Length: > 0 }) {
            try {
                basicPassword = DpapiSecretProtector.UnprotectString(basicPasswordBytes);
            } catch (Exception ex) {
                throw new InvalidOperationException($"Profile '{trimmed}' contains a protected basic password that could not be decrypted.", ex);
            }
        }

        var profile = new ServiceProfile {
            Model = ReadString(r, "model") ?? OpenAIModelCatalog.DefaultModel,
            OpenAITransport = transport,
            OpenAIBaseUrl = ReadString(r, "openai_base_url"),
            OpenAIAuthMode = authMode,
            OpenAIApiKey = apiKey,
            OpenAIBasicUsername = ReadString(r, "openai_basic_username"),
            OpenAIBasicPassword = basicPassword,
            OpenAIAccountId = ReadString(r, "openai_account_id"),
            OpenAIStreaming = ReadBool(r, "openai_streaming", defaultValue: true),
            OpenAIAllowInsecureHttp = ReadBool(r, "openai_allow_insecure_http", defaultValue: false),
            OpenAIAllowInsecureHttpNonLoopback = ReadBool(r, "openai_allow_insecure_http_non_loopback", defaultValue: false),
            ReasoningEffort = ChatEnumParser.ParseReasoningEffort(ReadString(r, "reasoning_effort")),
            ReasoningSummary = ChatEnumParser.ParseReasoningSummary(ReadString(r, "reasoning_summary")),
            TextVerbosity = ChatEnumParser.ParseTextVerbosity(ReadString(r, "text_verbosity")),
            Temperature = ReadDouble(r, "temperature"),
            MaxToolRounds = ReadInt(r, "max_tool_rounds", defaultValue: 24),
            ParallelTools = ReadBool(r, "parallel_tools", defaultValue: true),
            AllowMutatingParallelToolCalls = ReadBool(r, "allow_mutating_parallel_tool_calls", defaultValue: false),
            TurnTimeoutSeconds = ReadInt(r, "turn_timeout_seconds", defaultValue: 0),
            ToolTimeoutSeconds = ReadInt(r, "tool_timeout_seconds", defaultValue: 0),
            InstructionsFile = ReadString(r, "instructions_file"),
            MaxTableRows = ReadInt(r, "max_table_rows", defaultValue: 0),
            MaxSample = ReadInt(r, "max_sample", defaultValue: 0),
            Redact = ReadBool(r, "redact", defaultValue: false),
            AdDomainController = ReadString(r, "ad_domain_controller"),
            AdDefaultSearchBaseDn = ReadString(r, "ad_default_search_base_dn"),
            AdMaxResults = ReadInt(r, "ad_max_results", defaultValue: 1000),
            PowerShellAllowWrite = ReadBool(r, "powershell_allow_write", defaultValue: false),
            EnableBuiltInPackLoading = ReadBool(r, "enable_built_in_pack_loading", defaultValue: true),
            UseDefaultBuiltInToolAssemblyNames = ReadBool(r, "use_default_built_in_tool_assembly_names", defaultValue: true),
            EnableDefaultPluginPaths = ReadBool(r, "enable_default_plugin_paths", defaultValue: true),
            WriteGovernanceMode = NormalizeWriteGovernanceMode(ReadString(r, "write_governance_mode")),
            RequireWriteGovernanceRuntime = ReadBool(r, "require_write_governance_runtime", defaultValue: true),
            RequireWriteAuditSinkForWriteOperations = ReadBool(r, "require_write_audit_sink", defaultValue: false),
            RequireExplicitRoutingMetadata = ReadBool(r, "require_explicit_routing_metadata", defaultValue: true),
            WriteAuditSinkMode = NormalizeWriteAuditSinkMode(ReadString(r, "write_audit_sink_mode")),
            WriteAuditSinkPath = NormalizeOptionalPath(ReadString(r, "write_audit_sink_path")),
            AuthenticationRuntimePreset = NormalizeAuthenticationRuntimePreset(ReadString(r, "authentication_runtime_preset")),
            RequireAuthenticationRuntime = ReadBool(r, "require_authentication_runtime", defaultValue: false),
            RunAsProfilePath = NormalizeOptionalPath(ReadString(r, "run_as_profile_path")),
            AuthenticationProfilePath = NormalizeOptionalPath(ReadString(r, "authentication_profile_path"))
        };

        profile.AllowedRoots = ReadOrderedList(trimmed, AllowedRootsTable);
        profile.BuiltInToolAssemblyNames = ReadOrderedList(trimmed, BuiltInToolAssemblyNamesTable);
        var storedPluginPaths = ReadOrderedList(trimmed, PluginPathsTable);
        profile.PluginPaths = ServiceProfilePluginPathPolicy.NormalizeStoredPluginPaths(storedPluginPaths);
        profile.DisabledPackIds = ReadOrderedList(trimmed, DisabledPackIdsTable);
        profile.EnabledPackIds = ReadOrderedList(trimmed, EnabledPackIdsTable);

        if (!SequenceEqualOrdinalIgnoreCase(storedPluginPaths, profile.PluginPaths)) {
            ReplaceOrderedList(trimmed, PluginPathsTable, profile.PluginPaths);
        }

        return Task.FromResult<ServiceProfile?>(profile);
    }

    public Task UpsertAsync(string name, ServiceProfile profile, CancellationToken cancellationToken) {
        cancellationToken.ThrowIfCancellationRequested();
        if (string.IsNullOrWhiteSpace(name)) throw new ArgumentException("Profile name cannot be empty.", nameof(name));
        if (profile == null) throw new ArgumentNullException(nameof(profile));

        var trimmed = name.Trim();
        var now = DateTime.UtcNow.ToString("O");
        var transportKind = SerializeTransport(profile.OpenAITransport);
        var authMode = SerializeCompatibleAuthMode(profile.OpenAIAuthMode);
        var apiKeyBytes = string.IsNullOrWhiteSpace(profile.OpenAIApiKey) ? null : DpapiSecretProtector.ProtectString(profile.OpenAIApiKey!.Trim());
        var basicPasswordBytes = string.IsNullOrWhiteSpace(profile.OpenAIBasicPassword)
            ? null
            : DpapiSecretProtector.ProtectString(profile.OpenAIBasicPassword!.Trim());
        var extraRequiredInsertColumns = ResolveRequiredInsertBackfillColumns();
        var normalizedPluginPaths = ServiceProfilePluginPathPolicy.NormalizeStoredPluginPaths(profile.PluginPaths);

        var extraInsertColumnsSql = string.Empty;
        var extraInsertValuesSql = string.Empty;
        if (extraRequiredInsertColumns.Count > 0) {
            var names = string.Join(", ", extraRequiredInsertColumns);
            var parameterNames = new List<string>(extraRequiredInsertColumns.Count);
            for (var i = 0; i < extraRequiredInsertColumns.Count; i++) {
                parameterNames.Add("@" + extraRequiredInsertColumns[i]);
            }

            extraInsertColumnsSql = names + ",\n  ";
            extraInsertValuesSql = string.Join(", ", parameterNames) + ",\n  ";
        }

        // Keep writes atomic and deterministic: overwrite the scalar row and replace list rows.
        _db.BeginTransaction(_dbPath);
        try {
            _db.ExecuteNonQuery(
                _dbPath,
                $@"
INSERT INTO {ProfileTable} (
  name, model, transport_kind, openai_base_url, openai_auth_mode, openai_api_key, openai_basic_username, openai_basic_password, openai_account_id, openai_streaming,
  openai_allow_insecure_http, openai_allow_insecure_http_non_loopback,
  reasoning_effort, reasoning_summary, text_verbosity, temperature,
  max_tool_rounds, parallel_tools, allow_mutating_parallel_tool_calls, turn_timeout_seconds, tool_timeout_seconds,
  instructions_file, max_table_rows, max_sample, redact,
  ad_domain_controller, ad_default_search_base_dn, ad_max_results,
  powershell_allow_write,
  enable_built_in_pack_loading,
  use_default_built_in_tool_assembly_names,
  enable_default_plugin_paths,
  write_governance_mode, require_write_governance_runtime, require_write_audit_sink, require_explicit_routing_metadata,
  write_audit_sink_mode, write_audit_sink_path,
  authentication_runtime_preset, require_authentication_runtime, run_as_profile_path, authentication_profile_path,
  {extraInsertColumnsSql}
  updated_utc
)
VALUES (
  @name, @model, @transport_kind, @openai_base_url, @openai_auth_mode, @openai_api_key, @openai_basic_username, @openai_basic_password, @openai_account_id, @openai_streaming,
  @openai_allow_insecure_http, @openai_allow_insecure_http_non_loopback,
  @reasoning_effort, @reasoning_summary, @text_verbosity, @temperature,
  @max_tool_rounds, @parallel_tools, @allow_mutating_parallel_tool_calls, @turn_timeout_seconds, @tool_timeout_seconds,
  @instructions_file, @max_table_rows, @max_sample, @redact,
  @ad_domain_controller, @ad_default_search_base_dn, @ad_max_results,
  @powershell_allow_write,
  @enable_built_in_pack_loading,
  @use_default_built_in_tool_assembly_names,
  @enable_default_plugin_paths,
  @write_governance_mode, @require_write_governance_runtime, @require_write_audit_sink, @require_explicit_routing_metadata,
  @write_audit_sink_mode, @write_audit_sink_path,
  @authentication_runtime_preset, @require_authentication_runtime, @run_as_profile_path, @authentication_profile_path,
  {extraInsertValuesSql}
  @updated_utc
)
ON CONFLICT(name) DO UPDATE SET
  model = excluded.model,
  transport_kind = excluded.transport_kind,
  openai_base_url = excluded.openai_base_url,
  openai_auth_mode = excluded.openai_auth_mode,
  openai_api_key = excluded.openai_api_key,
  openai_basic_username = excluded.openai_basic_username,
  openai_basic_password = excluded.openai_basic_password,
  openai_account_id = excluded.openai_account_id,
  openai_streaming = excluded.openai_streaming,
  openai_allow_insecure_http = excluded.openai_allow_insecure_http,
  openai_allow_insecure_http_non_loopback = excluded.openai_allow_insecure_http_non_loopback,
  reasoning_effort = excluded.reasoning_effort,
  reasoning_summary = excluded.reasoning_summary,
  text_verbosity = excluded.text_verbosity,
  temperature = excluded.temperature,
  max_tool_rounds = excluded.max_tool_rounds,
  parallel_tools = excluded.parallel_tools,
  allow_mutating_parallel_tool_calls = excluded.allow_mutating_parallel_tool_calls,
  turn_timeout_seconds = excluded.turn_timeout_seconds,
  tool_timeout_seconds = excluded.tool_timeout_seconds,
  instructions_file = excluded.instructions_file,
  max_table_rows = excluded.max_table_rows,
  max_sample = excluded.max_sample,
  redact = excluded.redact,
  ad_domain_controller = excluded.ad_domain_controller,
  ad_default_search_base_dn = excluded.ad_default_search_base_dn,
  ad_max_results = excluded.ad_max_results,
  powershell_allow_write = excluded.powershell_allow_write,
  enable_built_in_pack_loading = excluded.enable_built_in_pack_loading,
  use_default_built_in_tool_assembly_names = excluded.use_default_built_in_tool_assembly_names,
  enable_default_plugin_paths = excluded.enable_default_plugin_paths,
  write_governance_mode = excluded.write_governance_mode,
  require_write_governance_runtime = excluded.require_write_governance_runtime,
  require_write_audit_sink = excluded.require_write_audit_sink,
  require_explicit_routing_metadata = excluded.require_explicit_routing_metadata,
  write_audit_sink_mode = excluded.write_audit_sink_mode,
  write_audit_sink_path = excluded.write_audit_sink_path,
  authentication_runtime_preset = excluded.authentication_runtime_preset,
  require_authentication_runtime = excluded.require_authentication_runtime,
  run_as_profile_path = excluded.run_as_profile_path,
  authentication_profile_path = excluded.authentication_profile_path,
  updated_utc = excluded.updated_utc;",
                useTransaction: true,
                parameters: BuildUpsertParameters(
                    trimmed: trimmed,
                    profile: profile,
                    transportKind: transportKind,
                    authMode: authMode,
                    apiKeyBytes: apiKeyBytes,
                    basicPasswordBytes: basicPasswordBytes,
                    now: now,
                    extraRequiredInsertColumns: extraRequiredInsertColumns));

            ReplaceOrderedList(trimmed, AllowedRootsTable, profile.AllowedRoots, useTransaction: true);
            ReplaceOrderedList(trimmed, BuiltInToolAssemblyNamesTable, profile.BuiltInToolAssemblyNames, useTransaction: true);
            ReplaceOrderedList(trimmed, PluginPathsTable, normalizedPluginPaths, useTransaction: true);
            ReplaceOrderedList(trimmed, DisabledPackIdsTable, profile.DisabledPackIds, useTransaction: true);
            ReplaceOrderedList(trimmed, EnabledPackIdsTable, profile.EnabledPackIds, useTransaction: true);

            _db.Commit();
        } catch {
            try {
                _db.Rollback();
            } catch {
                // Ignore.
            }
            throw;
        }

        return Task.CompletedTask;
    }

    private Dictionary<string, object?> BuildUpsertParameters(
        string trimmed,
        ServiceProfile profile,
        string transportKind,
        string authMode,
        byte[]? apiKeyBytes,
        byte[]? basicPasswordBytes,
        string now,
        IReadOnlyList<string> extraRequiredInsertColumns) {
        var parameters = new Dictionary<string, object?> {
            ["@name"] = trimmed,
            ["@model"] = string.IsNullOrWhiteSpace(profile.Model) ? OpenAIModelCatalog.DefaultModel : profile.Model.Trim(),
            ["@transport_kind"] = transportKind,
            ["@openai_base_url"] = string.IsNullOrWhiteSpace(profile.OpenAIBaseUrl) ? null : profile.OpenAIBaseUrl.Trim(),
            ["@openai_auth_mode"] = authMode,
            ["@openai_api_key"] = apiKeyBytes,
            ["@openai_basic_username"] = string.IsNullOrWhiteSpace(profile.OpenAIBasicUsername) ? null : profile.OpenAIBasicUsername.Trim(),
            ["@openai_basic_password"] = basicPasswordBytes,
            ["@openai_account_id"] = string.IsNullOrWhiteSpace(profile.OpenAIAccountId) ? null : profile.OpenAIAccountId.Trim(),
            ["@openai_streaming"] = profile.OpenAIStreaming ? 1 : 0,
            ["@openai_allow_insecure_http"] = profile.OpenAIAllowInsecureHttp ? 1 : 0,
            ["@openai_allow_insecure_http_non_loopback"] = profile.OpenAIAllowInsecureHttpNonLoopback ? 1 : 0,
            // Store stable lowercase tokens; parsing uses ChatEnumParser which tolerates hyphens/underscores.
            ["@reasoning_effort"] = profile.ReasoningEffort.HasValue ? profile.ReasoningEffort.Value.ToString().ToLowerInvariant() : null,
            ["@reasoning_summary"] = profile.ReasoningSummary.HasValue ? profile.ReasoningSummary.Value.ToString().ToLowerInvariant() : null,
            ["@text_verbosity"] = profile.TextVerbosity.HasValue ? profile.TextVerbosity.Value.ToString().ToLowerInvariant() : null,
            ["@temperature"] = profile.Temperature,
            ["@max_tool_rounds"] = profile.MaxToolRounds,
            ["@parallel_tools"] = profile.ParallelTools ? 1 : 0,
            ["@allow_mutating_parallel_tool_calls"] = profile.AllowMutatingParallelToolCalls ? 1 : 0,
            ["@turn_timeout_seconds"] = profile.TurnTimeoutSeconds,
            ["@tool_timeout_seconds"] = profile.ToolTimeoutSeconds,
            ["@instructions_file"] = string.IsNullOrWhiteSpace(profile.InstructionsFile) ? null : profile.InstructionsFile.Trim(),
            ["@max_table_rows"] = profile.MaxTableRows,
            ["@max_sample"] = profile.MaxSample,
            ["@redact"] = profile.Redact ? 1 : 0,
            ["@ad_domain_controller"] = string.IsNullOrWhiteSpace(profile.AdDomainController) ? null : profile.AdDomainController.Trim(),
            ["@ad_default_search_base_dn"] = string.IsNullOrWhiteSpace(profile.AdDefaultSearchBaseDn) ? null : profile.AdDefaultSearchBaseDn.Trim(),
            ["@ad_max_results"] = profile.AdMaxResults,
            ["@powershell_allow_write"] = profile.PowerShellAllowWrite ? 1 : 0,
            ["@enable_built_in_pack_loading"] = profile.EnableBuiltInPackLoading ? 1 : 0,
            ["@use_default_built_in_tool_assembly_names"] = profile.UseDefaultBuiltInToolAssemblyNames ? 1 : 0,
            ["@enable_default_plugin_paths"] = profile.EnableDefaultPluginPaths ? 1 : 0,
            ["@write_governance_mode"] = NormalizeWriteGovernanceMode(profile.WriteGovernanceMode),
            ["@require_write_governance_runtime"] = profile.RequireWriteGovernanceRuntime ? 1 : 0,
            ["@require_write_audit_sink"] = profile.RequireWriteAuditSinkForWriteOperations ? 1 : 0,
            ["@require_explicit_routing_metadata"] = profile.RequireExplicitRoutingMetadata ? 1 : 0,
            ["@write_audit_sink_mode"] = NormalizeWriteAuditSinkMode(profile.WriteAuditSinkMode),
            ["@write_audit_sink_path"] = NormalizeOptionalPath(profile.WriteAuditSinkPath),
            ["@authentication_runtime_preset"] = NormalizeAuthenticationRuntimePreset(profile.AuthenticationRuntimePreset),
            ["@require_authentication_runtime"] = profile.RequireAuthenticationRuntime ? 1 : 0,
            ["@run_as_profile_path"] = NormalizeOptionalPath(profile.RunAsProfilePath),
            ["@authentication_profile_path"] = NormalizeOptionalPath(profile.AuthenticationProfilePath),
            ["@updated_utc"] = now
        };

        for (var i = 0; i < extraRequiredInsertColumns.Count; i++) {
            var column = (extraRequiredInsertColumns[i] ?? string.Empty).Trim();
            if (column.Length == 0) {
                continue;
            }

            parameters["@" + column] = 0;
        }

        return parameters;
    }

    private List<string> ResolveRequiredInsertBackfillColumns() {
        const string currentInsertColumnsCsv = "name, model, transport_kind, openai_base_url, openai_auth_mode, openai_api_key, openai_basic_username, openai_basic_password, openai_account_id, openai_streaming, openai_allow_insecure_http, openai_allow_insecure_http_non_loopback, reasoning_effort, reasoning_summary, text_verbosity, temperature, max_tool_rounds, parallel_tools, allow_mutating_parallel_tool_calls, turn_timeout_seconds, tool_timeout_seconds, instructions_file, max_table_rows, max_sample, redact, ad_domain_controller, ad_default_search_base_dn, ad_max_results, powershell_allow_write, enable_built_in_pack_loading, use_default_built_in_tool_assembly_names, enable_default_plugin_paths, write_governance_mode, require_write_governance_runtime, require_write_audit_sink, require_explicit_routing_metadata, write_audit_sink_mode, write_audit_sink_path, authentication_runtime_preset, require_authentication_runtime, run_as_profile_path, authentication_profile_path, updated_utc";

        var knownColumns = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var split = currentInsertColumnsCsv.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        for (var i = 0; i < split.Length; i++) {
            knownColumns.Add(split[i]);
        }

        var required = new List<string>();
        var existingColumns = TryGetTableColumns(ProfileTable);
        foreach (var column in existingColumns) {
            if (knownColumns.Contains(column)) {
                continue;
            }

            required.Add(column);
        }

        return required;
    }

    public Task<IReadOnlyList<string>> ListNamesAsync(CancellationToken cancellationToken) {
        cancellationToken.ThrowIfCancellationRequested();

        var list = new List<string>();
        var dt = QueryAsTable(_db.Query(_dbPath, $"SELECT name FROM {ProfileTable} ORDER BY name"));
        if (dt is not null) {
            foreach (DataRow row in dt.Rows) {
                var v = row[0]?.ToString();
                if (!string.IsNullOrWhiteSpace(v)) {
                    list.Add(v!);
                }
            }
        }
        return Task.FromResult<IReadOnlyList<string>>(list);
    }

    private List<string> ReadOrderedList(string profileName, string tableName) {
        var list = new List<string>();
        var result = QueryAsTable(_db.Query(
            _dbPath,
            $"SELECT path FROM {tableName} WHERE profile_name = @name ORDER BY ord",
            parameters: new Dictionary<string, object?> { ["@name"] = profileName }));

        if (result is null || result.Rows.Count == 0) {
            return list;
        }

        foreach (DataRow row in result.Rows) {
            var path = row[0]?.ToString();
            if (!string.IsNullOrWhiteSpace(path)) {
                list.Add(path!.Trim());
            }
        }
        return list;
    }

    private void ReplaceOrderedList(string profileName, string tableName, List<string>? values, bool useTransaction = false) {
        _db.ExecuteNonQuery(_dbPath, $"DELETE FROM {tableName} WHERE profile_name = @name",
            useTransaction: useTransaction,
            parameters: new Dictionary<string, object?> { ["@name"] = profileName });

        if (values is null || values.Count == 0) {
            return;
        }

        var ord = 0;
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var value in values) {
            var normalized = (value ?? string.Empty).Trim();
            if (normalized.Length == 0 || !seen.Add(normalized)) {
                continue;
            }

            _db.ExecuteNonQuery(
                _dbPath,
                $"INSERT INTO {tableName} (profile_name, ord, path) VALUES (@name, @ord, @path)",
                useTransaction: useTransaction,
                parameters: new Dictionary<string, object?> {
                    ["@name"] = profileName,
                    ["@ord"] = ord++,
                    ["@path"] = normalized
                });
        }
    }

}
