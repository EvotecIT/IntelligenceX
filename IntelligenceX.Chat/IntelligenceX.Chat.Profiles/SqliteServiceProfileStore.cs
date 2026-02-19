using System;
using System.Collections.Generic;
using System.Data;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using DBAClientX;
using IntelligenceX.OpenAI;
using IntelligenceX.OpenAI.Chat;

namespace IntelligenceX.Chat.Profiles;

/// <summary>
/// SQLite-backed profile store (file-based).
/// </summary>
internal sealed class SqliteServiceProfileStore : IServiceProfileStore, IDisposable {
    private const string ProfileTable = "ix_service_profiles";
    private const string AllowedRootsTable = "ix_service_profile_allowed_roots";
    private const string PluginPathsTable = "ix_service_profile_plugin_paths";

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
DROP TABLE IF EXISTS {PluginPathsTable};
DROP TABLE IF EXISTS {ProfileTable};");
        }

        _db.ExecuteNonQuery(_dbPath, $@"
CREATE TABLE IF NOT EXISTS {ProfileTable} (
  name TEXT PRIMARY KEY,
  model TEXT NOT NULL,
  transport_kind TEXT NOT NULL,
  openai_base_url TEXT NULL,
  openai_api_key BLOB NULL,
  openai_streaming INTEGER NOT NULL,
  openai_allow_insecure_http INTEGER NOT NULL,
  openai_allow_insecure_http_non_loopback INTEGER NOT NULL,
  reasoning_effort TEXT NULL,
  reasoning_summary TEXT NULL,
  text_verbosity TEXT NULL,
  temperature REAL NULL,
  max_tool_rounds INTEGER NOT NULL,
  parallel_tools INTEGER NOT NULL,
  turn_timeout_seconds INTEGER NOT NULL,
  tool_timeout_seconds INTEGER NOT NULL,
  instructions_file TEXT NULL,
  max_table_rows INTEGER NOT NULL,
  max_sample INTEGER NOT NULL,
  redact INTEGER NOT NULL,
  ad_domain_controller TEXT NULL,
  ad_default_search_base_dn TEXT NULL,
  ad_max_results INTEGER NOT NULL,
  enable_powershell_pack INTEGER NOT NULL,
  powershell_allow_write INTEGER NOT NULL,
  enable_testimox_pack INTEGER NOT NULL,
  enable_officeimo_pack INTEGER NOT NULL DEFAULT 1,
  enable_default_plugin_paths INTEGER NOT NULL,
  write_governance_mode TEXT NOT NULL DEFAULT 'enforced',
  require_write_governance_runtime INTEGER NOT NULL DEFAULT 1,
  require_write_audit_sink INTEGER NOT NULL DEFAULT 0,
  write_audit_sink_mode TEXT NOT NULL DEFAULT 'none',
  write_audit_sink_path TEXT NULL,
  authentication_runtime_preset TEXT NOT NULL DEFAULT 'default',
  require_authentication_runtime INTEGER NOT NULL DEFAULT 0,
  run_as_profile_path TEXT NULL,
  updated_utc TEXT NOT NULL
);

CREATE TABLE IF NOT EXISTS {AllowedRootsTable} (
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

CREATE INDEX IF NOT EXISTS ix_service_profiles_updated_utc ON {ProfileTable}(updated_utc);
CREATE INDEX IF NOT EXISTS ix_service_profiles_transport_kind ON {ProfileTable}(transport_kind);
");

        EnsureColumnExists(ProfileTable, "enable_officeimo_pack", "INTEGER NOT NULL DEFAULT 1");
        EnsureColumnExists(ProfileTable, "write_governance_mode", "TEXT NOT NULL DEFAULT 'enforced'");
        EnsureColumnExists(ProfileTable, "require_write_governance_runtime", "INTEGER NOT NULL DEFAULT 1");
        EnsureColumnExists(ProfileTable, "require_write_audit_sink", "INTEGER NOT NULL DEFAULT 0");
        EnsureColumnExists(ProfileTable, "write_audit_sink_mode", "TEXT NOT NULL DEFAULT 'none'");
        EnsureColumnExists(ProfileTable, "write_audit_sink_path", "TEXT NULL");
        EnsureColumnExists(ProfileTable, "authentication_runtime_preset", "TEXT NOT NULL DEFAULT 'default'");
        EnsureColumnExists(ProfileTable, "require_authentication_runtime", "INTEGER NOT NULL DEFAULT 0");
        EnsureColumnExists(ProfileTable, "run_as_profile_path", "TEXT NULL");
    }

    private bool HasLegacyJsonSchema() {
        try {
            var result = _db.Query(_dbPath, $"PRAGMA table_info('{ProfileTable}')");
            if (result is not DataTable dt || dt.Rows.Count == 0) {
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

    private void EnsureColumnExists(string tableName, string columnName, string columnDefinition) {
        if (string.IsNullOrWhiteSpace(tableName) || string.IsNullOrWhiteSpace(columnName) || string.IsNullOrWhiteSpace(columnDefinition)) {
            return;
        }

        try {
            var result = _db.Query(_dbPath, $"PRAGMA table_info('{tableName}')");
            if (result is not DataTable dt || dt.Rows.Count == 0) {
                return;
            }

            foreach (DataRow row in dt.Rows) {
                var name = row["name"]?.ToString();
                if (string.Equals(name, columnName, StringComparison.OrdinalIgnoreCase)) {
                    return;
                }
            }

            _db.ExecuteNonQuery(_dbPath, $"ALTER TABLE {tableName} ADD COLUMN {columnName} {columnDefinition};");
        } catch {
            // Best-effort schema evolution; keep startup resilient.
        }
    }

    public Task<ServiceProfile?> GetAsync(string name, CancellationToken cancellationToken) {
        cancellationToken.ThrowIfCancellationRequested();
        if (string.IsNullOrWhiteSpace(name)) {
            return Task.FromResult<ServiceProfile?>(null);
        }

        var trimmed = name.Trim();
        var row = _db.Query(
            _dbPath,
            $@"
SELECT
  model,
  transport_kind,
  openai_base_url,
  openai_api_key,
  openai_streaming,
  openai_allow_insecure_http,
  openai_allow_insecure_http_non_loopback,
  reasoning_effort,
  reasoning_summary,
  text_verbosity,
  temperature,
  max_tool_rounds,
  parallel_tools,
  turn_timeout_seconds,
  tool_timeout_seconds,
  instructions_file,
  max_table_rows,
  max_sample,
  redact,
  ad_domain_controller,
  ad_default_search_base_dn,
  ad_max_results,
  enable_powershell_pack,
  powershell_allow_write,
  enable_testimox_pack,
  enable_officeimo_pack,
  enable_default_plugin_paths,
  write_governance_mode,
  require_write_governance_runtime,
  require_write_audit_sink,
  write_audit_sink_mode,
  write_audit_sink_path,
  authentication_runtime_preset,
  require_authentication_runtime,
  run_as_profile_path
FROM {ProfileTable}
WHERE name = @name
LIMIT 1;",
            parameters: new Dictionary<string, object?> { ["@name"] = trimmed }) as DataTable;

        if (row is null || row.Rows.Count == 0) {
            return Task.FromResult<ServiceProfile?>(null);
        }

        var r = row.Rows[0];
        var transportText = ReadString(r, "transport_kind") ?? "native";
        if (!TryParseTransport(transportText, out var transport)) {
            throw new InvalidOperationException($"Profile '{trimmed}' has an invalid transport_kind '{transportText}'.");
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

        var profile = new ServiceProfile {
            Model = ReadString(r, "model") ?? "gpt-5.3-codex",
            OpenAITransport = transport,
            OpenAIBaseUrl = ReadString(r, "openai_base_url"),
            OpenAIApiKey = apiKey,
            OpenAIStreaming = ReadBool(r, "openai_streaming", defaultValue: true),
            OpenAIAllowInsecureHttp = ReadBool(r, "openai_allow_insecure_http", defaultValue: false),
            OpenAIAllowInsecureHttpNonLoopback = ReadBool(r, "openai_allow_insecure_http_non_loopback", defaultValue: false),
            ReasoningEffort = ChatEnumParser.ParseReasoningEffort(ReadString(r, "reasoning_effort")),
            ReasoningSummary = ChatEnumParser.ParseReasoningSummary(ReadString(r, "reasoning_summary")),
            TextVerbosity = ChatEnumParser.ParseTextVerbosity(ReadString(r, "text_verbosity")),
            Temperature = ReadDouble(r, "temperature"),
            MaxToolRounds = ReadInt(r, "max_tool_rounds", defaultValue: 24),
            ParallelTools = ReadBool(r, "parallel_tools", defaultValue: true),
            TurnTimeoutSeconds = ReadInt(r, "turn_timeout_seconds", defaultValue: 0),
            ToolTimeoutSeconds = ReadInt(r, "tool_timeout_seconds", defaultValue: 0),
            InstructionsFile = ReadString(r, "instructions_file"),
            MaxTableRows = ReadInt(r, "max_table_rows", defaultValue: 0),
            MaxSample = ReadInt(r, "max_sample", defaultValue: 0),
            Redact = ReadBool(r, "redact", defaultValue: false),
            AdDomainController = ReadString(r, "ad_domain_controller"),
            AdDefaultSearchBaseDn = ReadString(r, "ad_default_search_base_dn"),
            AdMaxResults = ReadInt(r, "ad_max_results", defaultValue: 1000),
            EnablePowerShellPack = ReadBool(r, "enable_powershell_pack", defaultValue: false),
            PowerShellAllowWrite = ReadBool(r, "powershell_allow_write", defaultValue: false),
            EnableTestimoXPack = ReadBool(r, "enable_testimox_pack", defaultValue: true),
            EnableOfficeImoPack = ReadBool(r, "enable_officeimo_pack", defaultValue: true),
            EnableDefaultPluginPaths = ReadBool(r, "enable_default_plugin_paths", defaultValue: true),
            WriteGovernanceMode = ReadString(r, "write_governance_mode") ?? "enforced",
            RequireWriteGovernanceRuntime = ReadBool(r, "require_write_governance_runtime", defaultValue: true),
            RequireWriteAuditSinkForWriteOperations = ReadBool(r, "require_write_audit_sink", defaultValue: false),
            WriteAuditSinkMode = ReadString(r, "write_audit_sink_mode") ?? "none",
            WriteAuditSinkPath = ReadString(r, "write_audit_sink_path"),
            AuthenticationRuntimePreset = ReadString(r, "authentication_runtime_preset") ?? "default",
            RequireAuthenticationRuntime = ReadBool(r, "require_authentication_runtime", defaultValue: false),
            RunAsProfilePath = ReadString(r, "run_as_profile_path")
        };

        profile.AllowedRoots = ReadOrderedList(trimmed, AllowedRootsTable);
        profile.PluginPaths = ReadOrderedList(trimmed, PluginPathsTable);

        return Task.FromResult<ServiceProfile?>(profile);
    }

    public Task UpsertAsync(string name, ServiceProfile profile, CancellationToken cancellationToken) {
        cancellationToken.ThrowIfCancellationRequested();
        if (string.IsNullOrWhiteSpace(name)) throw new ArgumentException("Profile name cannot be empty.", nameof(name));
        if (profile == null) throw new ArgumentNullException(nameof(profile));

        var trimmed = name.Trim();
        var now = DateTime.UtcNow.ToString("O");
        var transportKind = SerializeTransport(profile.OpenAITransport);
        var apiKeyBytes = string.IsNullOrWhiteSpace(profile.OpenAIApiKey) ? null : DpapiSecretProtector.ProtectString(profile.OpenAIApiKey!.Trim());

        // Keep writes atomic and deterministic: overwrite the scalar row and replace list rows.
        _db.ExecuteNonQuery(_dbPath, "BEGIN IMMEDIATE TRANSACTION;");
        try {
            _db.ExecuteNonQuery(
                _dbPath,
                $@"
INSERT INTO {ProfileTable} (
  name, model, transport_kind, openai_base_url, openai_api_key, openai_streaming,
  openai_allow_insecure_http, openai_allow_insecure_http_non_loopback,
  reasoning_effort, reasoning_summary, text_verbosity, temperature,
  max_tool_rounds, parallel_tools, turn_timeout_seconds, tool_timeout_seconds,
  instructions_file, max_table_rows, max_sample, redact,
  ad_domain_controller, ad_default_search_base_dn, ad_max_results,
  enable_powershell_pack, powershell_allow_write, enable_testimox_pack, enable_officeimo_pack,
  enable_default_plugin_paths,
  write_governance_mode, require_write_governance_runtime, require_write_audit_sink,
  write_audit_sink_mode, write_audit_sink_path,
  authentication_runtime_preset, require_authentication_runtime, run_as_profile_path,
  updated_utc
)
VALUES (
  @name, @model, @transport_kind, @openai_base_url, @openai_api_key, @openai_streaming,
  @openai_allow_insecure_http, @openai_allow_insecure_http_non_loopback,
  @reasoning_effort, @reasoning_summary, @text_verbosity, @temperature,
  @max_tool_rounds, @parallel_tools, @turn_timeout_seconds, @tool_timeout_seconds,
  @instructions_file, @max_table_rows, @max_sample, @redact,
  @ad_domain_controller, @ad_default_search_base_dn, @ad_max_results,
  @enable_powershell_pack, @powershell_allow_write, @enable_testimox_pack, @enable_officeimo_pack,
  @enable_default_plugin_paths,
  @write_governance_mode, @require_write_governance_runtime, @require_write_audit_sink,
  @write_audit_sink_mode, @write_audit_sink_path,
  @authentication_runtime_preset, @require_authentication_runtime, @run_as_profile_path,
  @updated_utc
)
ON CONFLICT(name) DO UPDATE SET
  model = excluded.model,
  transport_kind = excluded.transport_kind,
  openai_base_url = excluded.openai_base_url,
  openai_api_key = excluded.openai_api_key,
  openai_streaming = excluded.openai_streaming,
  openai_allow_insecure_http = excluded.openai_allow_insecure_http,
  openai_allow_insecure_http_non_loopback = excluded.openai_allow_insecure_http_non_loopback,
  reasoning_effort = excluded.reasoning_effort,
  reasoning_summary = excluded.reasoning_summary,
  text_verbosity = excluded.text_verbosity,
  temperature = excluded.temperature,
  max_tool_rounds = excluded.max_tool_rounds,
  parallel_tools = excluded.parallel_tools,
  turn_timeout_seconds = excluded.turn_timeout_seconds,
  tool_timeout_seconds = excluded.tool_timeout_seconds,
  instructions_file = excluded.instructions_file,
  max_table_rows = excluded.max_table_rows,
  max_sample = excluded.max_sample,
  redact = excluded.redact,
  ad_domain_controller = excluded.ad_domain_controller,
  ad_default_search_base_dn = excluded.ad_default_search_base_dn,
  ad_max_results = excluded.ad_max_results,
  enable_powershell_pack = excluded.enable_powershell_pack,
  powershell_allow_write = excluded.powershell_allow_write,
  enable_testimox_pack = excluded.enable_testimox_pack,
  enable_officeimo_pack = excluded.enable_officeimo_pack,
  enable_default_plugin_paths = excluded.enable_default_plugin_paths,
  write_governance_mode = excluded.write_governance_mode,
  require_write_governance_runtime = excluded.require_write_governance_runtime,
  require_write_audit_sink = excluded.require_write_audit_sink,
  write_audit_sink_mode = excluded.write_audit_sink_mode,
  write_audit_sink_path = excluded.write_audit_sink_path,
  authentication_runtime_preset = excluded.authentication_runtime_preset,
  require_authentication_runtime = excluded.require_authentication_runtime,
  run_as_profile_path = excluded.run_as_profile_path,
  updated_utc = excluded.updated_utc;",
                parameters: new Dictionary<string, object?> {
                    ["@name"] = trimmed,
                    ["@model"] = string.IsNullOrWhiteSpace(profile.Model) ? "gpt-5.3-codex" : profile.Model.Trim(),
                    ["@transport_kind"] = transportKind,
                    ["@openai_base_url"] = string.IsNullOrWhiteSpace(profile.OpenAIBaseUrl) ? null : profile.OpenAIBaseUrl.Trim(),
                    ["@openai_api_key"] = apiKeyBytes,
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
                    ["@turn_timeout_seconds"] = profile.TurnTimeoutSeconds,
                    ["@tool_timeout_seconds"] = profile.ToolTimeoutSeconds,
                    ["@instructions_file"] = string.IsNullOrWhiteSpace(profile.InstructionsFile) ? null : profile.InstructionsFile.Trim(),
                    ["@max_table_rows"] = profile.MaxTableRows,
                    ["@max_sample"] = profile.MaxSample,
                    ["@redact"] = profile.Redact ? 1 : 0,
                    ["@ad_domain_controller"] = string.IsNullOrWhiteSpace(profile.AdDomainController) ? null : profile.AdDomainController.Trim(),
                    ["@ad_default_search_base_dn"] = string.IsNullOrWhiteSpace(profile.AdDefaultSearchBaseDn) ? null : profile.AdDefaultSearchBaseDn.Trim(),
                    ["@ad_max_results"] = profile.AdMaxResults,
                    ["@enable_powershell_pack"] = profile.EnablePowerShellPack ? 1 : 0,
                    ["@powershell_allow_write"] = profile.PowerShellAllowWrite ? 1 : 0,
                    ["@enable_testimox_pack"] = profile.EnableTestimoXPack ? 1 : 0,
                    ["@enable_officeimo_pack"] = profile.EnableOfficeImoPack ? 1 : 0,
                    ["@enable_default_plugin_paths"] = profile.EnableDefaultPluginPaths ? 1 : 0,
                    ["@write_governance_mode"] = string.IsNullOrWhiteSpace(profile.WriteGovernanceMode) ? "enforced" : profile.WriteGovernanceMode.Trim(),
                    ["@require_write_governance_runtime"] = profile.RequireWriteGovernanceRuntime ? 1 : 0,
                    ["@require_write_audit_sink"] = profile.RequireWriteAuditSinkForWriteOperations ? 1 : 0,
                    ["@write_audit_sink_mode"] = string.IsNullOrWhiteSpace(profile.WriteAuditSinkMode) ? "none" : profile.WriteAuditSinkMode.Trim(),
                    ["@write_audit_sink_path"] = string.IsNullOrWhiteSpace(profile.WriteAuditSinkPath) ? null : profile.WriteAuditSinkPath.Trim(),
                    ["@authentication_runtime_preset"] = string.IsNullOrWhiteSpace(profile.AuthenticationRuntimePreset) ? "default" : profile.AuthenticationRuntimePreset.Trim(),
                    ["@require_authentication_runtime"] = profile.RequireAuthenticationRuntime ? 1 : 0,
                    ["@run_as_profile_path"] = string.IsNullOrWhiteSpace(profile.RunAsProfilePath) ? null : profile.RunAsProfilePath.Trim(),
                    ["@updated_utc"] = now
                });

            ReplaceOrderedList(trimmed, AllowedRootsTable, profile.AllowedRoots);
            ReplaceOrderedList(trimmed, PluginPathsTable, profile.PluginPaths);

            _db.ExecuteNonQuery(_dbPath, "COMMIT;");
        } catch {
            try {
                _db.ExecuteNonQuery(_dbPath, "ROLLBACK;");
            } catch {
                // Ignore.
            }
            throw;
        }

        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<string>> ListNamesAsync(CancellationToken cancellationToken) {
        cancellationToken.ThrowIfCancellationRequested();

        var list = new List<string>();
        var result = _db.Query(_dbPath, $"SELECT name FROM {ProfileTable} ORDER BY name");
        if (result is DataTable dt) {
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
        var result = _db.Query(
            _dbPath,
            $"SELECT path FROM {tableName} WHERE profile_name = @name ORDER BY ord",
            parameters: new Dictionary<string, object?> { ["@name"] = profileName }) as DataTable;

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

    private void ReplaceOrderedList(string profileName, string tableName, List<string>? values) {
        _db.ExecuteNonQuery(_dbPath, $"DELETE FROM {tableName} WHERE profile_name = @name",
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
                parameters: new Dictionary<string, object?> {
                    ["@name"] = profileName,
                    ["@ord"] = ord++,
                    ["@path"] = normalized
                });
        }
    }

    private static string SerializeTransport(OpenAITransportKind kind) {
        return kind switch {
            OpenAITransportKind.Native => "native",
            OpenAITransportKind.AppServer => "appserver",
            OpenAITransportKind.CompatibleHttp => "compatible-http",
            OpenAITransportKind.CopilotCli => "copilot-cli",
            _ => "native"
        };
    }

    private static bool TryParseTransport(string? value, out OpenAITransportKind kind) {
        kind = OpenAITransportKind.Native;
        if (string.IsNullOrWhiteSpace(value)) {
            return false;
        }
        switch (value.Trim().ToLowerInvariant()) {
            case "native":
                kind = OpenAITransportKind.Native;
                return true;
            case "appserver":
            case "app-server":
            case "codex":
                kind = OpenAITransportKind.AppServer;
                return true;
            case "compatible-http":
            case "compatiblehttp":
            case "http":
            case "local":
            case "ollama":
            case "lmstudio":
            case "lm-studio":
                kind = OpenAITransportKind.CompatibleHttp;
                return true;
            case "copilot":
            case "copilot-cli":
            case "github-copilot":
            case "githubcopilot":
                kind = OpenAITransportKind.CopilotCli;
                return true;
            default:
                return false;
        }
    }

    private static string? ReadString(DataRow row, string col) {
        if (!row.Table.Columns.Contains(col)) {
            return null;
        }
        var value = row[col];
        if (value is null || value == DBNull.Value) {
            return null;
        }
        var text = value.ToString();
        return string.IsNullOrWhiteSpace(text) ? null : text;
    }

    private static byte[]? ReadBytes(DataRow row, string col) {
        if (!row.Table.Columns.Contains(col)) {
            return null;
        }
        var value = row[col];
        if (value is null || value == DBNull.Value) {
            return null;
        }
        if (value is byte[] bytes) {
            return bytes;
        }
        return null;
    }

    private static bool ReadBool(DataRow row, string col, bool defaultValue) {
        if (!row.Table.Columns.Contains(col)) {
            return defaultValue;
        }
        var value = row[col];
        if (value is null || value == DBNull.Value) {
            return defaultValue;
        }
        if (value is bool b) {
            return b;
        }
        if (value is long l) {
            return l != 0;
        }
        if (value is int i) {
            return i != 0;
        }
        if (int.TryParse(value.ToString(), out var parsed)) {
            return parsed != 0;
        }
        return defaultValue;
    }

    private static int ReadInt(DataRow row, string col, int defaultValue) {
        if (!row.Table.Columns.Contains(col)) {
            return defaultValue;
        }
        var value = row[col];
        if (value is null || value == DBNull.Value) {
            return defaultValue;
        }
        if (value is int i) {
            return i;
        }
        if (value is long l) {
            return (int)l;
        }
        return int.TryParse(value.ToString(), out var parsed) ? parsed : defaultValue;
    }

    private static double? ReadDouble(DataRow row, string col) {
        if (!row.Table.Columns.Contains(col)) {
            return null;
        }
        var value = row[col];
        if (value is null || value == DBNull.Value) {
            return null;
        }
        if (value is double d) {
            return d;
        }
        if (value is float f) {
            return f;
        }
        if (value is long l) {
            return l;
        }
        return double.TryParse(value.ToString(), out var parsed) ? parsed : null;
    }

    public void Dispose() {
        _db.Dispose();
    }
}
