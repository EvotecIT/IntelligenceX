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
/// Summarizes one local Codex storage area.
/// </summary>
/// <param name="Key">Stable area key.</param>
/// <param name="Label">User-facing area label.</param>
/// <param name="Path">Resolved filesystem path.</param>
/// <param name="FileCount">Number of files in the area.</param>
/// <param name="Bytes">Total byte size of files in the area.</param>
/// <param name="Recommendation">Short operational recommendation.</param>
public sealed record CodexLocalStateArea(
    string Key,
    string Label,
    string Path,
    int FileCount,
    long Bytes,
    string Recommendation);

/// <summary>
/// A Codex thread id observed in recent local failure logs that may benefit from archive-state refresh.
/// </summary>
/// <param name="ThreadId">Codex thread id found in a failure log entry.</param>
/// <param name="Title">Current local thread title, when available.</param>
/// <param name="FailureCount">Number of matching recent failure log entries.</param>
/// <param name="LastSeenUtc">Most recent matching failure timestamp.</param>
/// <param name="LastActivityUtc">Most recent local thread activity timestamp, when available.</param>
/// <param name="ThreadFound">Whether the thread id exists in local state.</param>
/// <param name="IsArchived">Whether the local thread is currently archived, when found.</param>
/// <param name="IsCurrent">Whether the failure is newer than the last known local thread activity.</param>
public sealed record CodexLocalStateBrokenThreadCandidate(
    string ThreadId,
    string Title,
    int FailureCount,
    DateTimeOffset LastSeenUtc,
    DateTimeOffset? LastActivityUtc,
    bool ThreadFound,
    bool IsArchived,
    bool IsCurrent);

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
    /// <summary>Number of recent broken-thread candidates found in local Codex logs.</summary>
    public int BrokenThreadCandidateCount { get; init; }
    /// <summary>Number of recent broken-thread candidates that are active and can be refreshed automatically.</summary>
    public int RecoverableBrokenThreadCandidateCount { get; init; }
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
    /// <summary>Detailed local Codex storage areas.</summary>
    public IReadOnlyList<CodexLocalStateArea> Areas { get; init; } = [];
    /// <summary>Recent thread ids found in Codex failure logs.</summary>
    public IReadOnlyList<CodexLocalStateBrokenThreadCandidate> BrokenThreadCandidates { get; init; } = [];
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
/// Result of a backup-first local Codex cleanup operation.
/// </summary>
public sealed class CodexLocalStateCleanupResult {
    /// <summary>Resolved Codex home directory.</summary>
    public string CodexHome { get; init; } = string.Empty;
    /// <summary>Directory containing cleanup archives created by this operation.</summary>
    public string ArchiveDirectory { get; init; } = string.Empty;
    /// <summary>Path repair result included in the cleanup.</summary>
    public CodexLocalStatePathRepairResult PathRepair { get; init; } = new();
    /// <summary>Number of log files moved to the archive.</summary>
    public int ArchivedLogFileCount { get; init; }
    /// <summary>Number of stale session files moved to the archive.</summary>
    public int ArchivedSessionFileCount { get; init; }
    /// <summary>Total log bytes moved to the archive.</summary>
    public long ArchivedLogBytes { get; init; }
    /// <summary>Total stale session bytes moved to the archive.</summary>
    public long ArchivedSessionBytes { get; init; }
}

/// <summary>
/// Reads local Codex Desktop/CLI state in a read-only way and summarizes issues
/// that can make resume/navigation brittle.
/// </summary>
public sealed partial class CodexLocalStateDiagnosticsService {
    private const int DefaultTitleLimit = 120;
    private const int DefaultPreviewLimit = 240;
    private const long LargeLogsWarningBytes = 256L * 1024L * 1024L;
    private const long LargeSessionsWarningBytes = 1024L * 1024L * 1024L;
    private const long LargeArchivedSessionsWarningBytes = 10L * 1024L * 1024L * 1024L;
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
        IReadOnlyList<CodexLocalStateBrokenThreadCandidate> brokenThreadCandidates = [];
        if (sqliteDiagnostics.CanConnect) {
            extendedPathCount = CountExtendedPathRows(stateDb, findings);
            var metadata = CountThreadMetadata(stateDb);
            activeThreadCount = metadata.ActiveThreads;
            oversizedMetadataCount = metadata.OversizedRows;
            brokenThreadCandidates = CollectBrokenThreadCandidates(home, stateDb, cancellationToken);
        }

        var areas = CollectAreas(home, stateDb, sqliteDiagnostics.TotalFileSizeBytes);
        AddAreaFindings(areas, findings);

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

        var recoverableBrokenThreadCandidateCount = brokenThreadCandidates.Count(static item => item.ThreadFound && !item.IsArchived && item.IsCurrent);

        if (brokenThreadCandidates.Count > 0) {
            findings.Add(new CodexLocalStateFinding(
                "broken-thread-candidates",
                CodexLocalStateHealthStatus.Warning,
                $"{brokenThreadCandidates.Count.ToString(CultureInfo.InvariantCulture)} recent Codex thread failure-log candidate(s) were found.",
                brokenThreadCandidates.Count));
        }

        var status = BuildStatus(findings, sqliteDiagnostics.Exists, sqliteDiagnostics.CanConnect);
        return new CodexLocalStateDiagnostics {
            CodexHome = home,
            StateDatabasePath = stateDb,
            ScannedAtUtc = DateTimeOffset.UtcNow,
            Status = status,
            StatusText = BuildStatusText(status, findings),
            DetailText = BuildDetailText(activeThreadCount, extendedPathCount, configExtendedPathCount, oversizedMetadataCount, brokenThreadCandidates.Count, recoverableBrokenThreadCandidateCount),
            ExtendedPathCount = extendedPathCount,
            ConfigExtendedPathCount = configExtendedPathCount,
            OversizedThreadMetadataCount = oversizedMetadataCount,
            ActiveThreadCount = activeThreadCount,
            BrokenThreadCandidateCount = brokenThreadCandidates.Count,
            RecoverableBrokenThreadCandidateCount = recoverableBrokenThreadCandidateCount,
            StateDatabaseBytes = sqliteDiagnostics.DatabaseFileSizeBytes,
            StateDatabaseTotalBytes = sqliteDiagnostics.TotalFileSizeBytes,
            SQLiteVersion = sqliteDiagnostics.SQLiteVersion,
            IntegrityCheck = sqliteDiagnostics.IntegrityCheck,
            QuickCheck = sqliteDiagnostics.QuickCheck,
            DatabaseExists = sqliteDiagnostics.Exists,
            CanConnect = sqliteDiagnostics.CanConnect,
            Areas = areas,
            BrokenThreadCandidates = brokenThreadCandidates,
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

    /// <summary>
    /// Runs safe backup-first cleanup: path normalization, stale session archiving,
    /// and old log rotation. Files are moved to archives, never permanently deleted.
    /// </summary>
    /// <param name="codexHome">Optional Codex home override. Defaults to CODEX_HOME or ~/.codex.</param>
    /// <param name="backupRoot">Optional backup root override. Defaults to Documents\Codex\codex-backups.</param>
    /// <param name="cancellationToken">Cancellation token for the cleanup operation.</param>
    /// <returns>A summary of archived files and repaired paths.</returns>
    public Task<CodexLocalStateCleanupResult> CleanUpAsync(
        string? codexHome = null,
        string? backupRoot = null,
        CancellationToken cancellationToken = default) {
        return Task.Run(
            () => CleanUp(codexHome, backupRoot, cancellationToken),
            cancellationToken);
    }

    private CodexLocalStateCleanupResult CleanUp(
        string? codexHome,
        string? backupRoot,
        CancellationToken cancellationToken) {
        var requestedHome = string.IsNullOrWhiteSpace(codexHome) ? ResolveDefaultCodexHome() : codexHome!.Trim();
        var home = Path.GetFullPath(requestedHome);
        var archiveRoot = CreateArchiveDirectory(home);
        Directory.CreateDirectory(archiveRoot);
        var pathRepair = NormalizeActiveThreadPaths(home, backupRoot, cancellationToken);
        var activeSessionPaths = ReadActiveSessionPaths(home);
        var staleSessionArchive = Path.Combine(archiveRoot, "sessions");
        var logArchive = Path.Combine(archiveRoot, "logs");
        var staleSessionCutoffUtc = DateTime.UtcNow.AddDays(-14);
        var logCutoffUtc = DateTime.UtcNow.AddHours(-2);

        var sessionMove = ArchiveFiles(
            Path.Combine(home, "sessions"),
            staleSessionArchive,
            file => IsSessionFile(file)
                    && file.LastWriteTimeUtc < staleSessionCutoffUtc
                    && !activeSessionPaths.Contains(file.FullName),
            cancellationToken);
        var logMove = ArchiveFiles(
            Path.Combine(home, "logs"),
            logArchive,
            file => IsLogFile(file) && file.LastWriteTimeUtc < logCutoffUtc,
            cancellationToken);
        var rootLogMove = ArchiveSingleFile(
            Path.Combine(home, "codex-tui.log"),
            Path.Combine(logArchive, "codex-tui.log"),
            file => file.LastWriteTimeUtc < logCutoffUtc,
            cancellationToken);

        return new CodexLocalStateCleanupResult {
            CodexHome = home,
            ArchiveDirectory = archiveRoot,
            PathRepair = pathRepair,
            ArchivedLogFileCount = logMove.Count + rootLogMove.Count,
            ArchivedLogBytes = logMove.Bytes + rootLogMove.Bytes,
            ArchivedSessionFileCount = sessionMove.Count,
            ArchivedSessionBytes = sessionMove.Bytes
        };
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
            var updates = new List<(string Column, string OriginalValue, string NormalizedValue)>();
            foreach (var (column, value) in row.Values) {
                if (string.IsNullOrWhiteSpace(value)) {
                    continue;
                }

                scannedValues++;
                if (!TryNormalizeExtendedWindowsPath(value!, out var normalized)) {
                    continue;
                }

                updates.Add((column, value!, normalized));
            }

            if (updates.Count == 0) {
                continue;
            }

            if (UpdateThreadPathRow(connection, transaction, row.RowId, updates, activeExpr)) {
                changedValues += updates.Count;
                changedRows++;
            }
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

    private static IReadOnlyList<CodexLocalStateArea> CollectAreas(string codexHome, string stateDb, long stateTotalBytes) {
        var sessions = ScanDirectory(Path.Combine(codexHome, "sessions"));
        var archivedSessions = ScanDirectory(Path.Combine(codexHome, "archived_sessions"));
        var logs = ScanLogs(codexHome);
        var backups = ScanDirectory(ResolveDefaultBackupRoot());
        return [
            new CodexLocalStateArea(
                "state-db",
                "SQLite state",
                stateDb,
                File.Exists(stateDb) ? 1 : 0,
                stateTotalBytes,
                stateTotalBytes > 0 ? "Keep; repair only with backup" : "No state database found"),
            new CodexLocalStateArea(
                "sessions",
                "Sessions",
                Path.Combine(codexHome, "sessions"),
                sessions.Count,
                sessions.Bytes,
                sessions.Bytes > LargeSessionsWarningBytes ? "Archive stale inactive sessions" : "No cleanup required"),
            new CodexLocalStateArea(
                "archived-sessions",
                "Archived sessions",
                Path.Combine(codexHome, "archived_sessions"),
                archivedSessions.Count,
                archivedSessions.Bytes,
                archivedSessions.Bytes > LargeArchivedSessionsWarningBytes ? "Review archive size before pruning" : "Keep archived copies"),
            new CodexLocalStateArea(
                "logs",
                "Logs",
                Path.Combine(codexHome, "logs"),
                logs.Count,
                logs.Bytes,
                logs.Bytes > LargeLogsWarningBytes ? "Archive old log files" : "Log size is modest"),
            new CodexLocalStateArea(
                "backups",
                "Backups",
                ResolveDefaultBackupRoot(),
                backups.Count,
                backups.Bytes,
                backups.Count > 0 ? "Keep for restore; prune manually when confident" : "No IX/Codex backups found")
        ];
    }

    private static void AddAreaFindings(
        IReadOnlyCollection<CodexLocalStateArea> areas,
        ICollection<CodexLocalStateFinding> findings) {
        var logs = areas.FirstOrDefault(static area => area.Key == "logs");
        if (logs is not null && logs.Bytes > LargeLogsWarningBytes) {
            findings.Add(new CodexLocalStateFinding(
                "large-logs",
                CodexLocalStateHealthStatus.Warning,
                $"Codex logs use {FormatBytesInvariant(logs.Bytes)} across {logs.FileCount.ToString(CultureInfo.InvariantCulture)} file(s).",
                logs.FileCount));
        }

        var sessions = areas.FirstOrDefault(static area => area.Key == "sessions");
        if (sessions is not null && sessions.Bytes > LargeSessionsWarningBytes) {
            findings.Add(new CodexLocalStateFinding(
                "large-sessions",
                CodexLocalStateHealthStatus.Warning,
                $"Active session files use {FormatBytesInvariant(sessions.Bytes)}.",
                sessions.FileCount));
        }

        var archivedSessions = areas.FirstOrDefault(static area => area.Key == "archived-sessions");
        if (archivedSessions is not null && archivedSessions.Bytes > LargeArchivedSessionsWarningBytes) {
            findings.Add(new CodexLocalStateFinding(
                "large-archived-sessions",
                CodexLocalStateHealthStatus.Warning,
                $"Archived sessions use {FormatBytesInvariant(archivedSessions.Bytes)}.",
                archivedSessions.FileCount));
        }
    }

    private static (int Count, long Bytes) ScanDirectory(string path) {
        if (!Directory.Exists(path)) {
            return (0, 0);
        }

        var count = 0;
        var bytes = 0L;
        foreach (var file in EnumerateFilesSafe(path)) {
            count++;
            bytes += SafeLength(file);
        }

        return (count, bytes);
    }

    private static (int Count, long Bytes) ScanLogs(string codexHome) {
        var files = new List<FileInfo>();
        var rootLog = Path.Combine(codexHome, "codex-tui.log");
        if (File.Exists(rootLog)) {
            files.Add(new FileInfo(rootLog));
        }

        var logsRoot = Path.Combine(codexHome, "logs");
        if (Directory.Exists(logsRoot)) {
            files.AddRange(EnumerateFilesSafe(logsRoot));
        }

        return (files.Count, files.Sum(SafeLength));
    }

    private static ISet<string> ReadActiveSessionPaths(string codexHome) {
        var stateDb = Path.Combine(codexHome, "state_5.sqlite");
        if (!File.Exists(stateDb)) {
            return new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        }

        try {
            var columns = GetTableColumns(stateDb, "threads");
            var columnNames = columns
                .Select(static column => column.Name)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            var pathColumns = columns
                .Where(static column => IsTextColumn(column.Type) && IsPathColumn(column.Name))
                .Select(static column => column.Name)
                .ToArray();
            if (pathColumns.Length == 0) {
                return new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            }

            var activeExpr = BuildActiveThreadExpression(columnNames);
            var selectedColumns = string.Join(", ", pathColumns.Select(QuoteIdentifier));
            var result = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var builder = new SqliteConnectionStringBuilder {
                DataSource = stateDb,
                Mode = SqliteOpenMode.ReadOnly
            };
            using var connection = new SqliteConnection(builder.ConnectionString);
            connection.Open();
            using var command = connection.CreateCommand();
            command.CommandText = $"SELECT {selectedColumns} FROM threads WHERE {activeExpr};";
            using var reader = command.ExecuteReader();
            var ordinals = pathColumns.Select(reader.GetOrdinal).ToArray();
            while (reader.Read()) {
                foreach (var ordinal in ordinals) {
                    if (!reader.IsDBNull(ordinal)
                        && TryResolveActiveSessionPath(reader.GetString(ordinal), out var activePath)) {
                        result.Add(activePath);
                    }
                }
            }

            return result;
        } catch {
            return new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        }
    }

    private static (int Count, long Bytes) ArchiveFiles(
        string sourceRoot,
        string archiveRoot,
        Func<FileInfo, bool> shouldArchive,
        CancellationToken cancellationToken) {
        if (!Directory.Exists(sourceRoot)) {
            return (0, 0);
        }

        var archived = 0;
        var bytes = 0L;
        var sourceFullPath = Path.GetFullPath(sourceRoot);
        if (IsReparsePoint(new DirectoryInfo(sourceFullPath))) {
            return (0, 0);
        }

        foreach (var file in EnumerateFilesSafe(sourceFullPath)) {
            cancellationToken.ThrowIfCancellationRequested();
            var fileFullPath = Path.GetFullPath(file.FullName);
            if (!IsDescendantPath(sourceFullPath, fileFullPath) || IsReparsePoint(file)) {
                continue;
            }

            if (!shouldArchive(file)) {
                continue;
            }

            var length = SafeLength(file);
            var relative = GetRelativePath(sourceFullPath, fileFullPath);
            var destination = Path.Combine(archiveRoot, relative);
            var destinationDirectory = Path.GetDirectoryName(destination);
            if (!string.IsNullOrWhiteSpace(destinationDirectory)) {
                Directory.CreateDirectory(destinationDirectory);
            }

            destination = EnsureUniquePath(destination);
            try {
                file.MoveTo(destination);
                archived++;
                bytes += length;
            } catch (IOException) {
                // Active Codex files can be locked. Leave them in place for the next pass.
            } catch (UnauthorizedAccessException) {
                // Keep cleanup best-effort and archive-only.
            }
        }

        return (archived, bytes);
    }

    private static (int Count, long Bytes) ArchiveSingleFile(
        string sourcePath,
        string destinationPath,
        Func<FileInfo, bool> shouldArchive,
        CancellationToken cancellationToken) {
        cancellationToken.ThrowIfCancellationRequested();
        var file = new FileInfo(sourcePath);
        if (!file.Exists || !shouldArchive(file)) {
            return (0, 0);
        }

        var destinationDirectory = Path.GetDirectoryName(destinationPath);
        if (!string.IsNullOrWhiteSpace(destinationDirectory)) {
            Directory.CreateDirectory(destinationDirectory);
        }

        var length = SafeLength(file);
        try {
            file.MoveTo(EnsureUniquePath(destinationPath));
            return (1, length);
        } catch (IOException) {
            return (0, 0);
        } catch (UnauthorizedAccessException) {
            return (0, 0);
        }
    }

    private static IEnumerable<FileInfo> EnumerateFilesSafe(string path) {
        var pending = new Stack<string>();
        var root = Path.GetFullPath(path);
        pending.Push(root);
        while (pending.Count > 0) {
            var current = pending.Pop();
            IEnumerable<string> directories;
            IEnumerable<string> files;
            try {
                directories = Directory.EnumerateDirectories(current).ToArray();
                files = Directory.EnumerateFiles(current).ToArray();
            } catch (IOException) {
                continue;
            } catch (UnauthorizedAccessException) {
                continue;
            }

            foreach (var directory in directories) {
                var directoryInfo = new DirectoryInfo(directory);
                if (IsReparsePoint(directoryInfo)) {
                    continue;
                }

                var directoryFullPath = Path.GetFullPath(directoryInfo.FullName);
                if (IsDescendantPath(root, directoryFullPath)) {
                    pending.Push(directoryFullPath);
                }
            }

            foreach (var file in files) {
                var fileInfo = new FileInfo(file);
                if (!IsReparsePoint(fileInfo) && IsDescendantPath(root, fileInfo.FullName)) {
                    yield return fileInfo;
                }
            }
        }
    }

    private static bool TryResolveActiveSessionPath(string? value, out string path) {
        path = string.Empty;
        if (string.IsNullOrWhiteSpace(value)) {
            return false;
        }

        var candidate = value!.Trim();
        if (TryNormalizeExtendedWindowsPath(candidate, out var normalized)) {
            candidate = normalized;
        }

        if (!Path.IsPathRooted(candidate)) {
            return false;
        }

        try {
            path = Path.GetFullPath(candidate);
            return true;
        } catch (ArgumentException) {
            return false;
        } catch (NotSupportedException) {
            return false;
        } catch (PathTooLongException) {
            return false;
        }
    }

    private static bool IsDescendantPath(string root, string path) {
        var normalizedRoot = AppendDirectorySeparator(Path.GetFullPath(root));
        var normalizedPath = Path.GetFullPath(path);
        return normalizedPath.StartsWith(normalizedRoot, StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsReparsePoint(FileSystemInfo info) {
        try {
            return info.Exists && (info.Attributes & FileAttributes.ReparsePoint) != 0;
        } catch (IOException) {
            return true;
        } catch (UnauthorizedAccessException) {
            return true;
        }
    }

    private static bool IsSessionFile(FileInfo file) {
        return string.Equals(file.Extension, ".jsonl", StringComparison.OrdinalIgnoreCase)
               || string.Equals(file.Extension, ".json", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsLogFile(FileInfo file) {
        return string.Equals(file.Extension, ".log", StringComparison.OrdinalIgnoreCase)
               || string.Equals(file.Extension, ".sqlite", StringComparison.OrdinalIgnoreCase);
    }

    private static long SafeLength(FileInfo file) {
        try {
            return file.Exists ? file.Length : 0L;
        } catch (IOException) {
            return 0L;
        } catch (UnauthorizedAccessException) {
            return 0L;
        }
    }

    private static string EnsureUniquePath(string path) {
        if (!File.Exists(path)) {
            return path;
        }

        var directory = Path.GetDirectoryName(path) ?? string.Empty;
        var name = Path.GetFileNameWithoutExtension(path);
        var extension = Path.GetExtension(path);
        for (var i = 1; i < 1000; i++) {
            var candidate = Path.Combine(directory, name + "-" + i.ToString(CultureInfo.InvariantCulture) + extension);
            if (!File.Exists(candidate)) {
                return candidate;
            }
        }

        return Path.Combine(directory, name + "-" + Guid.NewGuid().ToString("N") + extension);
    }

    private static string GetRelativePath(string basePath, string path) {
        var baseUri = new Uri(AppendDirectorySeparator(Path.GetFullPath(basePath)));
        var pathUri = new Uri(Path.GetFullPath(path));
        return Uri.UnescapeDataString(baseUri.MakeRelativeUri(pathUri).ToString())
            .Replace('/', Path.DirectorySeparatorChar);
    }

    private static string AppendDirectorySeparator(string path) {
        return path.EndsWith(Path.DirectorySeparatorChar.ToString(), StringComparison.Ordinal)
               || path.EndsWith(Path.AltDirectorySeparatorChar.ToString(), StringComparison.Ordinal)
            ? path
            : path + Path.DirectorySeparatorChar;
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

    private static bool UpdateThreadPathRow(
        SqliteConnection connection,
        SqliteTransaction transaction,
        long rowId,
        IReadOnlyList<(string Column, string OriginalValue, string NormalizedValue)> updates,
        string activeExpr) {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "UPDATE threads SET "
                              + string.Join(", ", updates.Select((item, index) => $"{QuoteIdentifier(item.Column)} = @p{index.ToString(CultureInfo.InvariantCulture)}"))
                              + " WHERE rowid = @rowid AND "
                              + activeExpr
                              + " AND "
                              + string.Join(" AND ", updates.Select((item, index) => $"{QuoteIdentifier(item.Column)} = @old{index.ToString(CultureInfo.InvariantCulture)}"))
                              + ";";
        var parameterIndex = 0;
        foreach (var item in updates) {
            command.Parameters.AddWithValue("@p" + parameterIndex.ToString(CultureInfo.InvariantCulture), item.NormalizedValue);
            command.Parameters.AddWithValue("@old" + parameterIndex.ToString(CultureInfo.InvariantCulture), item.OriginalValue);
            parameterIndex++;
        }

        command.Parameters.AddWithValue("@rowid", rowId);
        return command.ExecuteNonQuery() == 1;
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
        int oversizedMetadataCount,
        int brokenThreadCandidateCount,
        int recoverableBrokenThreadCandidateCount) {
        return string.Format(
            CultureInfo.InvariantCulture,
            "{0} active threads • {1} SQLite path findings • {2} config paths • {3} metadata warnings • {4} failure-log candidates ({5} active recoverable)",
            activeThreadCount,
            extendedPathCount,
            configExtendedPathCount,
            oversizedMetadataCount,
            brokenThreadCandidateCount,
            recoverableBrokenThreadCandidateCount);
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
        var root = string.IsNullOrWhiteSpace(backupRoot) ? ResolveDefaultBackupRoot() : backupRoot!;

        return Path.Combine(
            root!,
            "intelligencex-codex-hot-repair-"
            + DateTimeOffset.Now.ToString("yyyyMMdd-HHmmss-fff", CultureInfo.InvariantCulture)
            + "-"
            + Guid.NewGuid().ToString("N").Substring(0, 8));
    }

    private static string CreateArchiveDirectory(string codexHome) {
        return Path.Combine(
            codexHome,
            "archived_cleanup",
            "intelligencex-codex-cleanup-"
            + DateTimeOffset.Now.ToString("yyyyMMdd-HHmmss-fff", CultureInfo.InvariantCulture)
            + "-"
            + Guid.NewGuid().ToString("N").Substring(0, 8));
    }

    private static string ResolveDefaultBackupRoot() {
        var documents = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
        if (string.IsNullOrWhiteSpace(documents)) {
            documents = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Documents");
        }

        return Path.Combine(documents, "Codex", "codex-backups");
    }

    private static string FormatBytesInvariant(long bytes) {
        string[] suffixes = ["B", "KB", "MB", "GB", "TB"];
        var value = Math.Max(0, bytes);
        var suffixIndex = 0;
        var display = (double)value;
        while (display >= 1024d && suffixIndex < suffixes.Length - 1) {
            display /= 1024d;
            suffixIndex++;
        }

        return display >= 10d || suffixIndex == 0
            ? display.ToString("0", CultureInfo.InvariantCulture) + " " + suffixes[suffixIndex]
            : display.ToString("0.0", CultureInfo.InvariantCulture) + " " + suffixes[suffixIndex];
    }

    private static string QuoteIdentifier(string value) {
        return "\"" + value.Replace("\"", "\"\"") + "\"";
    }

    private static string QuoteString(string value) {
        return "'" + value.Replace("'", "''") + "'";
    }
}
#endif
