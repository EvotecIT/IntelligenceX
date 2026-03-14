using System;
using System.Collections.Generic;
using System.Data;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using DBAClientX;
using IntelligenceX.Chat.Profiles;
using Xunit;

namespace IntelligenceX.Chat.Tests;

public sealed class SqliteServiceProfileStoreRuntimePolicyTests {
    [Fact]
    public async Task UpsertAndGet_RuntimePolicyFields_RoundTripAndNormalizePaths() {
        var dbPath = CreateTempDbPath();
        try {
            using var store = new SqliteServiceProfileStore(dbPath);
            var profile = new ServiceProfile {
                WriteGovernanceMode = "yolo",
                RequireWriteGovernanceRuntime = false,
                RequireWriteAuditSinkForWriteOperations = true,
                WriteAuditSinkMode = "sqlite",
                WriteAuditSinkPath = " C:/temp/write-audit.db ",
                AuthenticationRuntimePreset = "strict",
                RequireExplicitRoutingMetadata = true,
                RequireAuthenticationRuntime = true,
                RunAsProfilePath = " C:/profiles/run-as.json ",
                AuthenticationProfilePath = " C:/profiles/auth.json "
            };

            await store.UpsertAsync("runtime-policy", profile, CancellationToken.None);
            var loaded = await store.GetAsync("runtime-policy", CancellationToken.None);

            Assert.NotNull(loaded);
            Assert.Equal("yolo", loaded!.WriteGovernanceMode);
            Assert.False(loaded.RequireWriteGovernanceRuntime);
            Assert.True(loaded.RequireWriteAuditSinkForWriteOperations);
            Assert.Equal("sqlite", loaded.WriteAuditSinkMode);
            Assert.Equal("C:/temp/write-audit.db", loaded.WriteAuditSinkPath);
            Assert.Equal("strict", loaded.AuthenticationRuntimePreset);
            Assert.True(loaded.RequireExplicitRoutingMetadata);
            Assert.True(loaded.RequireAuthenticationRuntime);
            Assert.Equal("C:/profiles/run-as.json", loaded.RunAsProfilePath);
            Assert.Equal("C:/profiles/auth.json", loaded.AuthenticationProfilePath);
        } finally {
            TempPathTestHelper.TryDeleteFile(dbPath);
        }
    }

    [Fact]
    public async Task UpsertAndGet_RuntimePolicyFields_InvalidTokens_FallBackToDefaults() {
        var dbPath = CreateTempDbPath();
        try {
            using var store = new SqliteServiceProfileStore(dbPath);
            var profile = new ServiceProfile {
                WriteGovernanceMode = "invalid",
                WriteAuditSinkMode = "invalid",
                AuthenticationRuntimePreset = "invalid",
                WriteAuditSinkPath = "   ",
                RunAsProfilePath = "   ",
                AuthenticationProfilePath = "   "
            };

            await store.UpsertAsync("runtime-policy-defaults", profile, CancellationToken.None);
            var loaded = await store.GetAsync("runtime-policy-defaults", CancellationToken.None);

            Assert.NotNull(loaded);
            Assert.Equal("enforced", loaded!.WriteGovernanceMode);
            Assert.Equal("none", loaded.WriteAuditSinkMode);
            Assert.Equal("default", loaded.AuthenticationRuntimePreset);
            Assert.True(loaded.RequireExplicitRoutingMetadata);
            Assert.Null(loaded.WriteAuditSinkPath);
            Assert.Null(loaded.RunAsProfilePath);
            Assert.Null(loaded.AuthenticationProfilePath);
        } finally {
            TempPathTestHelper.TryDeleteFile(dbPath);
        }
    }

    [Fact]
    public async Task UpsertAndGet_PackToggleLists_RoundTripWithDedupedOrder() {
        var dbPath = CreateTempDbPath();
        try {
            using var store = new SqliteServiceProfileStore(dbPath);
            var profile = new ServiceProfile {
                DisabledPackIds = new() { " powershell ", "custom_pack", "PowerShell" },
                EnabledPackIds = new() { "dnsclientx", " custom_pack ", "DNSClientX" }
            };

            await store.UpsertAsync("pack-toggles", profile, CancellationToken.None);
            var loaded = await store.GetAsync("pack-toggles", CancellationToken.None);

            Assert.NotNull(loaded);
            Assert.Equal(new[] { "powershell", "custom_pack" }, loaded!.DisabledPackIds);
            Assert.Equal(new[] { "dnsclientx", "custom_pack" }, loaded.EnabledPackIds);
        } finally {
            TempPathTestHelper.TryDeleteFile(dbPath);
        }
    }

    [Fact]
    public async Task UpsertAndGet_BuiltInAssemblyDiscoverySettings_RoundTripWithDedupedOrder() {
        var dbPath = CreateTempDbPath();
        try {
            using var store = new SqliteServiceProfileStore(dbPath);
            var profile = new ServiceProfile {
                UseDefaultBuiltInToolAssemblyNames = false,
                BuiltInToolAssemblyNames = new() {
                    " IntelligenceX.Tools.System ",
                    "IntelligenceX.Tools.EventLog",
                    "intelligencex.tools.system"
                }
            };

            await store.UpsertAsync("built-in-assemblies", profile, CancellationToken.None);
            var loaded = await store.GetAsync("built-in-assemblies", CancellationToken.None);

            Assert.NotNull(loaded);
            Assert.False(loaded!.UseDefaultBuiltInToolAssemblyNames);
            Assert.Equal(new[] { "IntelligenceX.Tools.System", "IntelligenceX.Tools.EventLog" }, loaded.BuiltInToolAssemblyNames);
        } finally {
            TempPathTestHelper.TryDeleteFile(dbPath);
        }
    }

    [Fact]
    public async Task UpsertAndGet_PluginPaths_StripsAppManagedBundleLocations() {
        var dbPath = CreateTempDbPath();
        var customPluginPath = TempPathTestHelper.CreateTempDirectoryPath("ix-custom-plugin-root");
        Directory.CreateDirectory(customPluginPath);
        try {
            using var store = new SqliteServiceProfileStore(dbPath);
            var profile = new ServiceProfile {
                PluginPaths = new() {
                    @"C:\Support\GitHub\IntelligenceX\artifacts\Releases\20260224-093226\portable\IntelligenceX.Chat-20260224-093226-win-x64\plugins",
                    @"C:\Users\przemyslaw.klys.EVOTEC\AppData\Local\Temp\IntelligenceX.Chat\service-runtime\v1-demo\plugins",
                    @"C:\Users\przemyslaw.klys.EVOTEC\AppData\Local\IntelligenceX.Chat\plugin-cache\zip-v2-deadbeef\plugins",
                    customPluginPath
                }
            };

            await store.UpsertAsync("plugin-paths", profile, CancellationToken.None);
            var loaded = await store.GetAsync("plugin-paths", CancellationToken.None);

            Assert.NotNull(loaded);
            Assert.Equal(new[] { customPluginPath }, loaded!.PluginPaths);
        } finally {
            if (Directory.Exists(customPluginPath)) {
                Directory.Delete(customPluginPath, recursive: true);
            }

            TempPathTestHelper.TryDeleteFile(dbPath);
        }
    }

    [Fact]
    public async Task UpsertAndGet_PluginPaths_StripsAppManagedReleaseAndRuntimeRoots() {
        var dbPath = CreateTempDbPath();
        try {
            var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            var managedPluginCacheRoot = Path.Combine(localAppData, "IntelligenceX.Chat", "plugin-cache");
            var managedServiceRuntimeRoot = Path.Combine(Path.GetTempPath(), "IntelligenceX.Chat", "service-runtime");
            using var store = new SqliteServiceProfileStore(dbPath);
            var profile = new ServiceProfile {
                PluginPaths = new() {
                    @"C:\Support\GitHub\IntelligenceX\artifacts\Releases\20260224-093226\portable\IntelligenceX.Chat-20260224-093226-win-x64\plugins",
                    managedServiceRuntimeRoot,
                    @"C:\Users\przemyslaw.klys.EVOTEC\AppData\Local\Temp\IntelligenceX.Chat\service-runtime\v1-abc123\plugins",
                    managedPluginCacheRoot,
                    Path.Combine(managedPluginCacheRoot, "zip-v2-abc123"),
                    @"C:\Custom\Plugins",
                    @" C:\Custom\Plugins ",
                    @"D:\Shared\plugin-cache\plugins"
                }
            };

            await store.UpsertAsync("plugin-paths", profile, CancellationToken.None);
            var loaded = await store.GetAsync("plugin-paths", CancellationToken.None);

            Assert.NotNull(loaded);
            Assert.Equal(new[] { @"C:\Custom\Plugins", @"D:\Shared\plugin-cache\plugins" }, loaded!.PluginPaths);

            var db = new SQLite();
            var persisted = QueryAsTable(db.Query(
                dbPath,
                "SELECT path FROM ix_service_profile_plugin_paths WHERE profile_name = @name ORDER BY ord;",
                parameters: new Dictionary<string, object?> { ["@name"] = "plugin-paths" }));

            Assert.NotNull(persisted);
            Assert.Equal(2, persisted!.Rows.Count);
            Assert.Equal(@"C:\Custom\Plugins", persisted.Rows[0]["path"]?.ToString());
            Assert.Equal(@"D:\Shared\plugin-cache\plugins", persisted.Rows[1]["path"]?.ToString());
        } finally {
            TempPathTestHelper.TryDeleteFile(dbPath);
        }
    }

    [Fact]
    public async Task GetAsync_MigratesStoredPluginPaths_WhenLegacyAppManagedRootsExist() {
        var dbPath = CreateTempDbPath();
        try {
            using (var bootstrap = new SqliteServiceProfileStore(dbPath)) {
                // Creates current schema/list tables.
            }

            var db = new SQLite();
            db.ExecuteNonQuery(
                dbPath,
                """
INSERT INTO ix_service_profiles (
  name, model, transport_kind, openai_auth_mode, openai_streaming, openai_allow_insecure_http,
  openai_allow_insecure_http_non_loopback, max_tool_rounds, parallel_tools, allow_mutating_parallel_tool_calls,
  turn_timeout_seconds, tool_timeout_seconds, max_table_rows, max_sample, redact, ad_max_results,
  powershell_allow_write, enable_built_in_pack_loading, use_default_built_in_tool_assembly_names,
  enable_default_plugin_paths, write_governance_mode, require_write_governance_runtime, require_write_audit_sink,
  require_explicit_routing_metadata, write_audit_sink_mode, authentication_runtime_preset, require_authentication_runtime,
  updated_utc
) VALUES (
  @name, @model, @transport_kind, @openai_auth_mode, @openai_streaming, @openai_allow_insecure_http,
  @openai_allow_insecure_http_non_loopback, @max_tool_rounds, @parallel_tools, @allow_mutating_parallel_tool_calls,
  @turn_timeout_seconds, @tool_timeout_seconds, @max_table_rows, @max_sample, @redact, @ad_max_results,
  @powershell_allow_write, @enable_built_in_pack_loading, @use_default_built_in_tool_assembly_names,
  @enable_default_plugin_paths, @write_governance_mode, @require_write_governance_runtime, @require_write_audit_sink,
  @require_explicit_routing_metadata, @write_audit_sink_mode, @authentication_runtime_preset, @require_authentication_runtime,
  @updated_utc
);
""",
                parameters: new Dictionary<string, object?> {
                    ["@name"] = "legacy-plugin-paths",
                    ["@model"] = "legacy-model",
                    ["@transport_kind"] = "native",
                    ["@openai_auth_mode"] = "bearer",
                    ["@openai_streaming"] = 1,
                    ["@openai_allow_insecure_http"] = 0,
                    ["@openai_allow_insecure_http_non_loopback"] = 0,
                    ["@max_tool_rounds"] = 24,
                    ["@parallel_tools"] = 1,
                    ["@allow_mutating_parallel_tool_calls"] = 0,
                    ["@turn_timeout_seconds"] = 0,
                    ["@tool_timeout_seconds"] = 0,
                    ["@max_table_rows"] = 0,
                    ["@max_sample"] = 0,
                    ["@redact"] = 0,
                    ["@ad_max_results"] = 1000,
                    ["@powershell_allow_write"] = 0,
                    ["@enable_built_in_pack_loading"] = 1,
                    ["@use_default_built_in_tool_assembly_names"] = 1,
                    ["@enable_default_plugin_paths"] = 1,
                    ["@write_governance_mode"] = "enforced",
                    ["@require_write_governance_runtime"] = 1,
                    ["@require_write_audit_sink"] = 0,
                    ["@require_explicit_routing_metadata"] = 1,
                    ["@write_audit_sink_mode"] = "none",
                    ["@authentication_runtime_preset"] = "default",
                    ["@require_authentication_runtime"] = 0,
                    ["@updated_utc"] = DateTime.UtcNow.ToString("O")
                });
            db.ExecuteNonQuery(
                dbPath,
                """
INSERT INTO ix_service_profile_plugin_paths (profile_name, ord, path) VALUES
  (@name, 0, @release_path),
  (@name, 1, @custom_path);
""",
                parameters: new Dictionary<string, object?> {
                    ["@name"] = "legacy-plugin-paths",
                    ["@release_path"] = @"C:\Support\GitHub\IntelligenceX\artifacts\Releases\20260222-182223\portable\IntelligenceX.Chat-20260222-182223-win-x64\plugins",
                    ["@custom_path"] = @"D:\PluginDrop"
                });

            using var store = new SqliteServiceProfileStore(dbPath);
            var loaded = await store.GetAsync("legacy-plugin-paths", CancellationToken.None);

            Assert.NotNull(loaded);
            Assert.Equal(new[] { @"D:\PluginDrop" }, loaded!.PluginPaths);

            var persisted = QueryAsTable(db.Query(
                dbPath,
                "SELECT path FROM ix_service_profile_plugin_paths WHERE profile_name = @name ORDER BY ord;",
                parameters: new Dictionary<string, object?> { ["@name"] = "legacy-plugin-paths" }));

            Assert.NotNull(persisted);
            Assert.Single(persisted!.Rows.Cast<DataRow>());
            Assert.Equal(@"D:\PluginDrop", persisted.Rows[0]["path"]?.ToString());
        } finally {
            TempPathTestHelper.TryDeleteFile(dbPath);
        }
    }

    [Fact]
    public async Task EnsureSchema_MigratesLegacyPackToggleColumns_ToPackIdOverrideLists() {
        var dbPath = CreateTempDbPath();
        try {
            SeedLegacyProfileRowWithPackToggles(dbPath, profileName: "legacy-profile");

            using var store = new SqliteServiceProfileStore(dbPath);
            var loaded = await store.GetAsync("legacy-profile", CancellationToken.None);

            Assert.NotNull(loaded);
            Assert.Contains("testimox", loaded!.DisabledPackIds);
            Assert.Contains("officeimo", loaded.EnabledPackIds);
            Assert.Contains("powershell", loaded.EnabledPackIds);
        } finally {
            TempPathTestHelper.TryDeleteFile(dbPath);
        }
    }

    [Fact]
    public async Task Upsert_BackfillsUnknownRequiredNonPackColumns() {
        var dbPath = CreateTempDbPath();
        try {
            using (var bootstrap = new SqliteServiceProfileStore(dbPath)) {
                // Creates baseline schema/list tables.
            }

            RecreateProfilesTableWithUnknownRequiredColumn(dbPath);

            using var store = new SqliteServiceProfileStore(dbPath);
            await store.UpsertAsync("required-backfill", new ServiceProfile(), CancellationToken.None);

            var db = new SQLite();
            var row = QueryAsTable(db.Query(
                dbPath,
                "SELECT custom_required_non_pack FROM ix_service_profiles WHERE name = @name LIMIT 1;",
                parameters: new Dictionary<string, object?> { ["@name"] = "required-backfill" }));

            Assert.NotNull(row);
            Assert.Single(row!.Rows.Cast<DataRow>());
            Assert.Equal(0, Convert.ToInt32(row.Rows[0]["custom_required_non_pack"]));
        } finally {
            TempPathTestHelper.TryDeleteFile(dbPath);
        }
    }

    private static string CreateTempDbPath() {
        return TempPathTestHelper.CreateTempFilePath("ix-chat-profiles", ".db");
    }

    private static void SeedLegacyProfileRowWithPackToggles(string dbPath, string profileName) {
        var db = new SQLite();
        db.ExecuteNonQuery(dbPath, """
CREATE TABLE IF NOT EXISTS ix_service_profiles (
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
  enable_testimox_pack INTEGER NOT NULL,
  powershell_allow_write INTEGER NOT NULL,
  enable_officeimo_pack INTEGER NOT NULL,
  enable_default_plugin_paths INTEGER NOT NULL,
  updated_utc TEXT NOT NULL
);
""");

        db.ExecuteNonQuery(
            dbPath,
            """
INSERT INTO ix_service_profiles (
  name, model, transport_kind, openai_base_url, openai_api_key,
  openai_streaming, openai_allow_insecure_http, openai_allow_insecure_http_non_loopback,
  reasoning_effort, reasoning_summary, text_verbosity, temperature,
  max_tool_rounds, parallel_tools, turn_timeout_seconds, tool_timeout_seconds,
  instructions_file, max_table_rows, max_sample, redact,
  ad_domain_controller, ad_default_search_base_dn, ad_max_results,
  enable_powershell_pack, enable_testimox_pack, powershell_allow_write, enable_officeimo_pack, enable_default_plugin_paths,
  updated_utc
) VALUES (
  @name, @model, @transport_kind, @openai_base_url, @openai_api_key,
  @openai_streaming, @openai_allow_insecure_http, @openai_allow_insecure_http_non_loopback,
  @reasoning_effort, @reasoning_summary, @text_verbosity, @temperature,
  @max_tool_rounds, @parallel_tools, @turn_timeout_seconds, @tool_timeout_seconds,
  @instructions_file, @max_table_rows, @max_sample, @redact,
  @ad_domain_controller, @ad_default_search_base_dn, @ad_max_results,
  @enable_powershell_pack, @enable_testimox_pack, @powershell_allow_write, @enable_officeimo_pack, @enable_default_plugin_paths,
  @updated_utc
);
""",
            parameters: new Dictionary<string, object?> {
                ["@name"] = profileName,
                ["@model"] = "legacy-model",
                ["@transport_kind"] = "native",
                ["@openai_base_url"] = null,
                ["@openai_api_key"] = null,
                ["@openai_streaming"] = 1,
                ["@openai_allow_insecure_http"] = 0,
                ["@openai_allow_insecure_http_non_loopback"] = 0,
                ["@reasoning_effort"] = null,
                ["@reasoning_summary"] = null,
                ["@text_verbosity"] = null,
                ["@temperature"] = null,
                ["@max_tool_rounds"] = 24,
                ["@parallel_tools"] = 1,
                ["@turn_timeout_seconds"] = 0,
                ["@tool_timeout_seconds"] = 0,
                ["@instructions_file"] = null,
                ["@max_table_rows"] = 0,
                ["@max_sample"] = 0,
                ["@redact"] = 0,
                ["@ad_domain_controller"] = null,
                ["@ad_default_search_base_dn"] = null,
                ["@ad_max_results"] = 1000,
                ["@enable_powershell_pack"] = 1,
                ["@enable_testimox_pack"] = 0,
                ["@powershell_allow_write"] = 0,
                ["@enable_officeimo_pack"] = 1,
                ["@enable_default_plugin_paths"] = 1,
                ["@updated_utc"] = DateTime.UtcNow.ToString("O")
            });
    }

    private static void RecreateProfilesTableWithUnknownRequiredColumn(string dbPath) {
        var db = new SQLite();
        db.ExecuteNonQuery(dbPath, "BEGIN IMMEDIATE TRANSACTION;");
        try {
            db.ExecuteNonQuery(dbPath, "DROP TABLE IF EXISTS ix_service_profiles;");
            db.ExecuteNonQuery(dbPath, """
CREATE TABLE ix_service_profiles (
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
  custom_required_non_pack INTEGER NOT NULL,
  updated_utc TEXT NOT NULL
);
""");
            db.ExecuteNonQuery(dbPath, "COMMIT;");
        } catch {
            try {
                db.ExecuteNonQuery(dbPath, "ROLLBACK;");
            } catch {
                // Ignore rollback failures.
            }
            throw;
        }
    }

    private static DataTable? QueryAsTable(object? queryResult) {
        if (queryResult is DataTable table) {
            return table;
        }

        if (queryResult is DataSet dataSet && dataSet.Tables.Count > 0) {
            return dataSet.Tables[0];
        }

        return null;
    }

}
