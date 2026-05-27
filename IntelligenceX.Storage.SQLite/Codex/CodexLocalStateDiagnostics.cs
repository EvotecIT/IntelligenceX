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
/// Result of a backup-first Codex local state path repair.
/// </summary>
public sealed class CodexLocalStatePathRepairResult {
    /// <summary>Resolved Codex home directory.</summary>
    public string CodexHome { get; init; } = string.Empty;
    /// <summary>Resolved Codex SQLite state database path.</summary>
    public string StateDatabasePath { get; init; } = string.Empty;
    /// <summary>Whether the state database existed when repair was requested.</summary>
    public bool DatabaseExists { get; init; }
    /// <summary>Directory containing the SQLite backup created before mutation.</summary>
    public string BackupDirectory { get; init; } = string.Empty;
    /// <summary>Path to the SQLite backup created before mutation.</summary>
    public string BackupDatabasePath { get; init; } = string.Empty;
    /// <summary>Number of active thread path values inspected.</summary>
    public int ScannedPathValueCount { get; init; }
    /// <summary>Number of active thread path values rewritten.</summary>
    public int ChangedPathValueCount { get; init; }
    /// <summary>Number of active thread rows updated.</summary>
    public int ChangedThreadRowCount { get; init; }
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

    /// <summary>
    /// Backs up the Codex SQLite state database, then rewrites active thread path
    /// values from extended Windows paths to normal drive-rooted paths.
    /// </summary>
    /// <param name="codexHome">Optional Codex home override. Defaults to CODEX_HOME or ~/.codex.</param>
    /// <param name="backupRoot">Optional backup root override. Defaults to Documents\Codex\codex-backups.</param>
    /// <param name="cancellationToken">Cancellation token for the repair operation.</param>
    /// <returns>A summary of the backed-up repair.</returns>
    public Task<CodexLocalStatePathRepairResult> NormalizeActiveThreadPathsAsync(
        string? codexHome = null,
        string? backupRoot = null,
        CancellationToken cancellationToken = default) {
        return Task.Run(
            () => NormalizeActiveThreadPaths(codexHome, backupRoot, cancellationToken),
            cancellationToken);
    }

    private CodexLocalStatePathRepairResult NormalizeActiveThreadPaths(
        string? codexHome,
        string? backupRoot,
        CancellationToken cancellationToken) {
        var requestedHome = string.IsNullOrWhiteSpace(codexHome) ? ResolveDefaultCodexHome() : codexHome!.Trim();
        var home = Path.GetFullPath(requestedHome);
        var stateDb = Path.Combine(home, "state_5.sqlite");
        if (!File.Exists(stateDb)) {
            return new CodexLocalStatePathRepairResult {
                CodexHome = home,
                StateDatabasePath = stateDb,
                DatabaseExists = false
            };
        }

        cancellationToken.ThrowIfCancellationRequested();
        var backupDirectory = CreateBackupDirectory(backupRoot);
        var backupDb = Path.Combine(backupDirectory, "state_5.sqlite");

        var builder = new SqliteConnectionStringBuilder {
            DataSource = stateDb,
            Mode = SqliteOpenMode.ReadWrite
        };
        using var connection = new SqliteConnection(builder.ConnectionString);
        connection.Open();
        SetBusyTimeout(connection, 10000);
        BackupDatabase(connection, backupDb);

        var columns = GetTableColumns(stateDb, "threads");
        var columnNames = columns
            .Select(static column => column.Name)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var pathColumns = columns
            .Where(static column => IsTextColumn(column.Type) && IsPathColumn(column.Name))
            .Select(static column => column.Name)
            .ToArray();
        if (pathColumns.Length == 0) {
            return new CodexLocalStatePathRepairResult {
                CodexHome = home,
                StateDatabasePath = stateDb,
                DatabaseExists = true,
                BackupDirectory = backupDirectory,
                BackupDatabasePath = backupDb
            };
        }

        var activeExpr = BuildActiveThreadExpression(columnNames);
        var rows = ReadActiveThreadPathRows(connection, pathColumns, activeExpr, cancellationToken);
        var scannedValues = 0;
        var changedValues = 0;
        var changedRows = 0;

        using var transaction = connection.BeginTransaction();
        foreach (var row in rows) {
            cancellationToken.ThrowIfCancellationRequested();
            var updates = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var (column, value) in row.Values) {
                if (string.IsNullOrWhiteSpace(value)) {
                    continue;
                }

                scannedValues++;
                if (!TryNormalizeExtendedWindowsPath(value!, out var normalized)) {
                    continue;
                }

                updates[column] = normalized;
                changedValues++;
            }

            if (updates.Count == 0) {
                continue;
            }

            UpdateThreadPathRow(connection, transaction, row.RowId, updates);
            changedRows++;
        }

        transaction.Commit();
        return new CodexLocalStatePathRepairResult {
            CodexHome = home,
            StateDatabasePath = stateDb,
            DatabaseExists = true,
            BackupDirectory = backupDirectory,
            BackupDatabasePath = backupDb,
            ScannedPathValueCount = scannedValues,
            ChangedPathValueCount = changedValues,
            ChangedThreadRowCount = changedRows
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

        var activeExpr = BuildActiveThreadExpression(columns);
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

    private static IReadOnlyList<(long RowId, IReadOnlyList<(string Column, string? Value)> Values)> ReadActiveThreadPathRows(
        SqliteConnection connection,
        IReadOnlyList<string> pathColumns,
        string activeExpr,
        CancellationToken cancellationToken) {
        var selectedColumns = string.Join(", ", pathColumns.Select(QuoteIdentifier));
        using var command = connection.CreateCommand();
        command.CommandText = $"SELECT rowid AS __ix_rowid, {selectedColumns} FROM threads WHERE {activeExpr};";
        using var reader = command.ExecuteReader();
        var rowIdOrdinal = reader.GetOrdinal("__ix_rowid");
        var columnOrdinals = pathColumns
            .Select(column => (Column: column, Ordinal: reader.GetOrdinal(column)))
            .ToArray();
        var rows = new List<(long RowId, IReadOnlyList<(string Column, string? Value)> Values)>();
        while (reader.Read()) {
            cancellationToken.ThrowIfCancellationRequested();
            var values = new List<(string Column, string? Value)>(columnOrdinals.Length);
            foreach (var (column, ordinal) in columnOrdinals) {
                values.Add((column, reader.IsDBNull(ordinal) ? null : reader.GetString(ordinal)));
            }

            rows.Add((reader.GetInt64(rowIdOrdinal), values));
        }

        return rows;
    }

    private static void UpdateThreadPathRow(
        SqliteConnection connection,
        SqliteTransaction transaction,
        long rowId,
        IReadOnlyDictionary<string, string> updates) {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "UPDATE threads SET "
                              + string.Join(", ", updates.Keys.Select((column, index) => $"{QuoteIdentifier(column)} = @p{index.ToString(CultureInfo.InvariantCulture)}"))
                              + " WHERE rowid = @rowid;";
        var parameterIndex = 0;
        foreach (var value in updates.Values) {
            command.Parameters.AddWithValue("@p" + parameterIndex.ToString(CultureInfo.InvariantCulture), value);
            parameterIndex++;
        }

        command.Parameters.AddWithValue("@rowid", rowId);
        command.ExecuteNonQuery();
    }

    private static void BackupDatabase(SqliteConnection sourceConnection, string backupDb) {
        var directory = Path.GetDirectoryName(backupDb);
        if (!string.IsNullOrWhiteSpace(directory)) {
            Directory.CreateDirectory(directory);
        }

        var builder = new SqliteConnectionStringBuilder {
            DataSource = backupDb,
            Mode = SqliteOpenMode.ReadWriteCreate
        };
        using var destination = new SqliteConnection(builder.ConnectionString);
        destination.Open();
        sourceConnection.BackupDatabase(destination);
    }

    private static void SetBusyTimeout(SqliteConnection connection, int milliseconds) {
        using var command = connection.CreateCommand();
        command.CommandText = "PRAGMA busy_timeout = " + milliseconds.ToString(CultureInfo.InvariantCulture) + ";";
        command.ExecuteNonQuery();
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

    private static string BuildActiveThreadExpression(ISet<string> columns) {
        return columns.Contains("archived")
            ? "COALESCE(archived,0)=0"
            : columns.Contains("archived_at")
                ? "archived_at IS NULL"
                : "1=1";
    }

    private static bool TryNormalizeExtendedWindowsPath(string value, out string normalized) {
        normalized = value;
        if (!value.StartsWith(ExtendedPathPrefix, StringComparison.Ordinal)) {
            return false;
        }

        var withoutPrefix = value.Substring(ExtendedPathPrefix.Length);
        if (!IsDriveRootedWindowsPath(withoutPrefix)) {
            return false;
        }

        normalized = withoutPrefix;
        return true;
    }

    private static bool IsDriveRootedWindowsPath(string value) {
        return value.Length >= 3
               && char.IsLetter(value[0])
               && value[1] == ':'
               && value[2] == '\\';
    }

    private static string CreateBackupDirectory(string? backupRoot) {
        var root = backupRoot;
        if (string.IsNullOrWhiteSpace(root)) {
            var documents = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
            if (string.IsNullOrWhiteSpace(documents)) {
                documents = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Documents");
            }

            root = Path.Combine(documents, "Codex", "codex-backups");
        }

        return Path.Combine(
            root!,
            "intelligencex-codex-hot-repair-" + DateTimeOffset.Now.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture));
    }

    private static string QuoteIdentifier(string value) {
        return "\"" + value.Replace("\"", "\"\"") + "\"";
    }

    private static string QuoteString(string value) {
        return "'" + value.Replace("'", "''") + "'";
    }
}
#endif
