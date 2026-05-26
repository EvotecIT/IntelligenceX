#if !NETSTANDARD2_0
using System;
using System.Collections.Generic;
using System.Data;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using DBAClientX;
using Microsoft.Data.Sqlite;

namespace IntelligenceX.Codex;

/// <summary>
/// High-level health state for local Codex Desktop/CLI metadata.
/// </summary>
public enum CodexLocalStateHealthStatus {
    /// <summary>No scan has completed yet.</summary>
    Unknown,
    /// <summary>The inspected state looks healthy.</summary>
    Healthy,
    /// <summary>The inspected state has non-fatal findings.</summary>
    Warning,
    /// <summary>The inspected state has a health or accessibility error.</summary>
    Error
}

/// <summary>
/// Describes a concrete local Codex state finding.
/// </summary>
/// <param name="Key">Stable finding key.</param>
/// <param name="Severity">Finding severity.</param>
/// <param name="Message">User-facing summary.</param>
/// <param name="Count">Optional count associated with the finding.</param>
public sealed record CodexLocalStateFinding(
    string Key,
    CodexLocalStateHealthStatus Severity,
    string Message,
    int Count = 0);

/// <summary>
/// Snapshot of local Codex state health.
/// </summary>
public sealed class CodexLocalStateDiagnostics {
    /// <summary>Resolved Codex home directory.</summary>
    public string CodexHome { get; init; } = string.Empty;
    /// <summary>Resolved Codex SQLite state database path.</summary>
    public string StateDatabasePath { get; init; } = string.Empty;
    /// <summary>UTC time when the snapshot was collected.</summary>
    public DateTimeOffset ScannedAtUtc { get; init; } = DateTimeOffset.UtcNow;
    /// <summary>Overall local state health.</summary>
    public CodexLocalStateHealthStatus Status { get; init; } = CodexLocalStateHealthStatus.Unknown;
    /// <summary>Compact user-facing health text.</summary>
    public string StatusText { get; init; } = "Not scanned";
    /// <summary>Compact user-facing detail text.</summary>
    public string DetailText { get; init; } = string.Empty;
    /// <summary>Number of Windows extended-path values found in path-bearing SQLite columns.</summary>
    public int ExtendedPathCount { get; init; }
    /// <summary>Number of Windows extended-path values found in config metadata.</summary>
    public int ConfigExtendedPathCount { get; init; }
    /// <summary>Number of active threads with oversized display metadata.</summary>
    public int OversizedThreadMetadataCount { get; init; }
    /// <summary>Number of active threads found in the Codex state database.</summary>
    public int ActiveThreadCount { get; init; }
    /// <summary>Main SQLite state database file size in bytes.</summary>
    public long StateDatabaseBytes { get; init; }
    /// <summary>Total SQLite state size including WAL and shared-memory files.</summary>
    public long StateDatabaseTotalBytes { get; init; }
    /// <summary>SQLite engine version reported by the inspected database.</summary>
    public string? SQLiteVersion { get; init; }
    /// <summary>Result of PRAGMA integrity_check.</summary>
    public string? IntegrityCheck { get; init; }
    /// <summary>Result of PRAGMA quick_check.</summary>
    public string? QuickCheck { get; init; }
    /// <summary>Whether the state database file exists.</summary>
    public bool DatabaseExists { get; init; }
    /// <summary>Whether the state database could be opened read-only.</summary>
    public bool CanConnect { get; init; }
    /// <summary>Detailed local state findings.</summary>
    public IReadOnlyList<CodexLocalStateFinding> Findings { get; init; } = [];
}

/// <summary>
/// Reads local Codex Desktop/CLI state in a read-only way and summarizes issues
/// that can make resume/navigation brittle.
/// </summary>
public sealed class CodexLocalStateDiagnosticsService {
    private const int DefaultTitleLimit = 120;
    private const int DefaultPreviewLimit = 240;
    private const string ExtendedPathPrefix = @"\\?\";
    private static readonly string[] PathColumnHints = ["path", "cwd", "file", "folder", "dir", "root", "source", "workspace"];

    private readonly SQLite _sqlite = new() {
        ReturnType = ReturnType.DataTable,
        CommandTimeout = 10
    };

    /// <summary>
    /// Resolves the current user's Codex home directory, honoring CODEX_HOME.
    /// </summary>
    /// <returns>The resolved Codex home directory.</returns>
    public static string ResolveDefaultCodexHome() {
        var overrideHome = Environment.GetEnvironmentVariable("CODEX_HOME");
        if (!string.IsNullOrWhiteSpace(overrideHome)) {
            return Path.GetFullPath(Environment.ExpandEnvironmentVariables(overrideHome.Trim()));
        }

        var profile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        return Path.Combine(profile, ".codex");
    }

    /// <summary>
    /// Collects a read-only local Codex state diagnostics snapshot.
    /// </summary>
    /// <param name="codexHome">Optional Codex home override. Defaults to CODEX_HOME or ~/.codex.</param>
    /// <param name="cancellationToken">Cancellation token for SQLite diagnostics.</param>
    /// <returns>A local Codex state diagnostics snapshot.</returns>
    public async Task<CodexLocalStateDiagnostics> CollectAsync(
        string? codexHome = null,
        CancellationToken cancellationToken = default) {
        var requestedHome = string.IsNullOrWhiteSpace(codexHome) ? ResolveDefaultCodexHome() : codexHome!.Trim();
        var home = Path.GetFullPath(requestedHome);
        var stateDb = Path.Combine(home, "state_5.sqlite");
        var findings = new List<CodexLocalStateFinding>();
        var sqliteDiagnostics = await _sqlite.CollectDiagnosticsAsync(
            stateDb,
            cancellationToken,
            busyTimeoutMs: 1000).ConfigureAwait(false);

        var extendedPathCount = 0;
        var activeThreadCount = 0;
        var oversizedMetadataCount = 0;
        if (sqliteDiagnostics.CanConnect) {
            extendedPathCount = CountExtendedPathRows(stateDb, findings);
            var metadata = CountThreadMetadata(stateDb);
            activeThreadCount = metadata.ActiveThreads;
            oversizedMetadataCount = metadata.OversizedRows;
        }

        var configExtendedPathCount = CountConfigExtendedPaths(home);
        if (!sqliteDiagnostics.Exists) {
            findings.Add(new CodexLocalStateFinding(
                "state-db-missing",
                CodexLocalStateHealthStatus.Warning,
                "Codex state database was not found."));
        } else if (!sqliteDiagnostics.CanConnect) {
            findings.Add(new CodexLocalStateFinding(
                "state-db-unavailable",
                CodexLocalStateHealthStatus.Error,
                sqliteDiagnostics.ErrorMessage ?? "Codex state database could not be opened read-only."));
        }

        if (sqliteDiagnostics.Exists && sqliteDiagnostics.CanConnect && !sqliteDiagnostics.IsHealthy) {
            findings.Add(new CodexLocalStateFinding(
                "sqlite-integrity",
                CodexLocalStateHealthStatus.Error,
                "SQLite integrity checks are not healthy."));
        }

        if (configExtendedPathCount > 0) {
            findings.Add(new CodexLocalStateFinding(
                "config-extended-paths",
                CodexLocalStateHealthStatus.Warning,
                $"config.toml contains {configExtendedPathCount.ToString(CultureInfo.InvariantCulture)} Windows extended path value(s).",
                configExtendedPathCount));
        }

        if (oversizedMetadataCount > 0) {
            findings.Add(new CodexLocalStateFinding(
                "thread-metadata-bloat",
                CodexLocalStateHealthStatus.Warning,
                $"{oversizedMetadataCount.ToString(CultureInfo.InvariantCulture)} active thread(s) have oversized title or preview metadata.",
                oversizedMetadataCount));
        }

        var status = BuildStatus(findings, sqliteDiagnostics.Exists, sqliteDiagnostics.CanConnect);
        return new CodexLocalStateDiagnostics {
            CodexHome = home,
            StateDatabasePath = stateDb,
            ScannedAtUtc = DateTimeOffset.UtcNow,
            Status = status,
            StatusText = BuildStatusText(status, findings),
            DetailText = BuildDetailText(activeThreadCount, extendedPathCount, configExtendedPathCount, oversizedMetadataCount),
            ExtendedPathCount = extendedPathCount,
            ConfigExtendedPathCount = configExtendedPathCount,
            OversizedThreadMetadataCount = oversizedMetadataCount,
            ActiveThreadCount = activeThreadCount,
            StateDatabaseBytes = sqliteDiagnostics.DatabaseFileSizeBytes,
            StateDatabaseTotalBytes = sqliteDiagnostics.TotalFileSizeBytes,
            SQLiteVersion = sqliteDiagnostics.SQLiteVersion,
            IntegrityCheck = sqliteDiagnostics.IntegrityCheck,
            QuickCheck = sqliteDiagnostics.QuickCheck,
            DatabaseExists = sqliteDiagnostics.Exists,
            CanConnect = sqliteDiagnostics.CanConnect,
            Findings = findings
        };
    }

    private int CountExtendedPathRows(string stateDb, List<CodexLocalStateFinding> findings) {
        var total = 0;
        foreach (var (table, column) in EnumeratePathTextColumns(stateDb)) {
            var count = Convert.ToInt32(_sqlite.ExecuteScalar(
                stateDb,
                $"SELECT COUNT(*) FROM {QuoteIdentifier(table)} WHERE instr({QuoteIdentifier(column)}, @prefix) > 0;",
                new Dictionary<string, object?> {
                    ["@prefix"] = ExtendedPathPrefix
                }), CultureInfo.InvariantCulture);
            if (count <= 0) {
                continue;
            }

            total += count;
            findings.Add(new CodexLocalStateFinding(
                $"extended-paths:{table}.{column}",
                CodexLocalStateHealthStatus.Warning,
                $"{table}.{column} contains {count.ToString(CultureInfo.InvariantCulture)} Windows extended path value(s).",
                count));
        }

        return total;
    }

    private IReadOnlyList<(string Table, string Column)> EnumeratePathTextColumns(string stateDb) {
        var tables = ToRows(_sqlite.Query(
            stateDb,
            "SELECT name FROM sqlite_master WHERE type='table' AND name NOT LIKE 'sqlite_%' ORDER BY name;"));
        var columns = new List<(string Table, string Column)>();
        foreach (var row in tables) {
            var table = Convert.ToString(row["name"], CultureInfo.InvariantCulture);
            if (string.IsNullOrWhiteSpace(table)) {
                continue;
            }

            columns.AddRange(GetTableColumns(stateDb, table)
                .Where(static column => IsTextColumn(column.Type) && IsPathColumn(column.Name))
                .Select(column => (table, column.Name)));
        }

        return columns;
    }

    private (int ActiveThreads, int OversizedRows) CountThreadMetadata(string stateDb) {
        var columns = GetTableColumns(stateDb, "threads")
            .Select(static column => column.Name)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (!columns.Contains("title")) {
            return (0, 0);
        }

        var activeExpr = columns.Contains("archived")
            ? "COALESCE(archived,0)=0"
            : columns.Contains("archived_at")
                ? "archived_at IS NULL"
                : "1=1";
        var previewExpr = columns.Contains("first_user_message")
            ? $"length(first_user_message) > {DefaultPreviewLimit.ToString(CultureInfo.InvariantCulture)}"
            : "0";
        var row = ToRows(_sqlite.Query(
            stateDb,
            $"""
             SELECT
               COUNT(*) AS active_count,
               COALESCE(SUM(CASE WHEN length(title) > {DefaultTitleLimit.ToString(CultureInfo.InvariantCulture)} OR {previewExpr} THEN 1 ELSE 0 END), 0) AS oversized_count
             FROM threads
             WHERE {activeExpr};
             """)).FirstOrDefault();
        if (row is null) {
            return (0, 0);
        }

        return (
            Convert.ToInt32(row["active_count"], CultureInfo.InvariantCulture),
            Convert.ToInt32(row["oversized_count"], CultureInfo.InvariantCulture));
    }

    private static IReadOnlyList<(string Name, string Type)> GetTableColumns(string stateDb, string table) {
        var columns = new List<(string Name, string Type)>();
        var builder = new SqliteConnectionStringBuilder {
            DataSource = stateDb,
            Mode = SqliteOpenMode.ReadOnly
        };
        using var connection = new SqliteConnection(builder.ConnectionString);
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = $"PRAGMA table_info({QuoteString(table)});";
        using var reader = command.ExecuteReader();
        var nameOrdinal = reader.GetOrdinal("name");
        var typeOrdinal = reader.GetOrdinal("type");
        while (reader.Read()) {
            var name = reader.IsDBNull(nameOrdinal) ? string.Empty : reader.GetString(nameOrdinal);
            if (string.IsNullOrWhiteSpace(name)) {
                continue;
            }

            var type = reader.IsDBNull(typeOrdinal) ? string.Empty : reader.GetString(typeOrdinal);
            columns.Add((name, type));
        }

        return columns;
    }

    private static int CountConfigExtendedPaths(string codexHome) {
        var configPath = Path.Combine(codexHome, "config.toml");
        if (!File.Exists(configPath)) {
            return 0;
        }

        var text = File.ReadAllText(configPath, Encoding.UTF8);
        var count = 0;
        var index = 0;
        while ((index = text.IndexOf(ExtendedPathPrefix, index, StringComparison.Ordinal)) >= 0) {
            count++;
            index += ExtendedPathPrefix.Length;
        }

        return count;
    }

    private static IReadOnlyList<DataRow> ToRows(object? value) {
        return value is DataTable table
            ? table.Rows.Cast<DataRow>().ToArray()
            : [];
    }

    private static CodexLocalStateHealthStatus BuildStatus(
        IReadOnlyCollection<CodexLocalStateFinding> findings,
        bool exists,
        bool canConnect) {
        if (findings.Any(static finding => finding.Severity == CodexLocalStateHealthStatus.Error)) {
            return CodexLocalStateHealthStatus.Error;
        }

        if (!exists || !canConnect || findings.Count > 0) {
            return CodexLocalStateHealthStatus.Warning;
        }

        return CodexLocalStateHealthStatus.Healthy;
    }

    private static string BuildStatusText(
        CodexLocalStateHealthStatus status,
        IReadOnlyCollection<CodexLocalStateFinding> findings) {
        return status switch {
            CodexLocalStateHealthStatus.Healthy => "Codex state healthy",
            CodexLocalStateHealthStatus.Error => "Codex state needs attention",
            CodexLocalStateHealthStatus.Warning => findings.Count == 1
                ? "Codex state has 1 finding"
                : $"Codex state has {findings.Count.ToString(CultureInfo.InvariantCulture)} findings",
            _ => "Codex state not scanned"
        };
    }

    private static string BuildDetailText(
        int activeThreadCount,
        int extendedPathCount,
        int configExtendedPathCount,
        int oversizedMetadataCount) {
        return string.Format(
            CultureInfo.InvariantCulture,
            "{0} active threads • {1} SQLite path findings • {2} config paths • {3} metadata warnings",
            activeThreadCount,
            extendedPathCount,
            configExtendedPathCount,
            oversizedMetadataCount);
    }

    private static bool IsTextColumn(string? columnType) {
        return string.IsNullOrWhiteSpace(columnType)
            || columnType!.IndexOf("TEXT", StringComparison.OrdinalIgnoreCase) >= 0;
    }

    private static bool IsPathColumn(string columnName) {
        return PathColumnHints.Any(hint => columnName.IndexOf(hint, StringComparison.OrdinalIgnoreCase) >= 0);
    }

    private static string QuoteIdentifier(string value) {
        return "\"" + value.Replace("\"", "\"\"") + "\"";
    }

    private static string QuoteString(string value) {
        return "'" + value.Replace("'", "''") + "'";
    }
}
#endif
