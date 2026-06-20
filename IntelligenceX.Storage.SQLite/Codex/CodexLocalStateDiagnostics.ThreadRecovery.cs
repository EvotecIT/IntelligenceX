#if !NETSTANDARD2_0
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;

namespace IntelligenceX.Codex;

/// <summary>
/// Result of a backup-first Codex thread archive-state refresh.
/// </summary>
public sealed class CodexLocalStateThreadRecoveryResult {
    /// <summary>Resolved Codex home directory.</summary>
    public string CodexHome { get; init; } = string.Empty;
    /// <summary>Resolved Codex SQLite state database path.</summary>
    public string StateDatabasePath { get; init; } = string.Empty;
    /// <summary>Thread id requested for recovery.</summary>
    public string ThreadId { get; init; } = string.Empty;
    /// <summary>Whether the state database existed when recovery was requested.</summary>
    public bool DatabaseExists { get; init; }
    /// <summary>Whether the threads table has archive-state columns IX can refresh.</summary>
    public bool ArchiveColumnsAvailable { get; init; }
    /// <summary>Whether the requested thread row was found.</summary>
    public bool ThreadFound { get; init; }
    /// <summary>Whether the thread was archived before the refresh.</summary>
    public bool WasArchived { get; init; }
    /// <summary>Whether the thread was archived after the refresh.</summary>
    public bool FinalArchived { get; init; }
    /// <summary>Directory containing the SQLite backup created before mutation.</summary>
    public string BackupDirectory { get; init; } = string.Empty;
    /// <summary>Path to the SQLite backup created before mutation.</summary>
    public string BackupDatabasePath { get; init; } = string.Empty;
    /// <summary>Directory containing automation backups for this thread, when any were found.</summary>
    public string AutomationBackupDirectory { get; init; } = string.Empty;
    /// <summary>Automation ids that targeted this thread before recovery.</summary>
    public IReadOnlyList<string> AutomationIds { get; init; } = [];
    /// <summary>Number of automations targeting this thread before recovery.</summary>
    public int AutomationCountBefore { get; init; }
    /// <summary>Number of automations targeting this thread after recovery and restore checks.</summary>
    public int AutomationCountAfter { get; init; }
    /// <summary>Number of automation definitions restored from backup after recovery.</summary>
    public int AutomationRestoredCount { get; init; }
}

public sealed partial class CodexLocalStateDiagnosticsService {
    private sealed class ThreadAutomationBackup {
        public string AutomationId { get; init; } = string.Empty;
        public string SourceDirectory { get; init; } = string.Empty;
        public string BackupDirectory { get; init; } = string.Empty;
    }

    /// <summary>
    /// Backs up the Codex SQLite state database, toggles one thread through an
    /// archived state, then restores its original final active/archived state.
    /// </summary>
    /// <param name="threadId">Codex thread id to refresh.</param>
    /// <param name="codexHome">Optional Codex home override. Defaults to CODEX_HOME or ~/.codex.</param>
    /// <param name="backupRoot">Optional backup root override. Defaults to Documents\Codex\codex-backups.</param>
    /// <param name="cancellationToken">Cancellation token for the recovery operation.</param>
    /// <returns>A summary of the backed-up thread refresh.</returns>
    public Task<CodexLocalStateThreadRecoveryResult> RecoverThreadArchiveStateAsync(
        string threadId,
        string? codexHome = null,
        string? backupRoot = null,
        CancellationToken cancellationToken = default) {
        return Task.Run(
            () => RecoverThreadArchiveState(threadId, codexHome, backupRoot, cancellationToken),
            cancellationToken);
    }

    private CodexLocalStateThreadRecoveryResult RecoverThreadArchiveState(
        string threadId,
        string? codexHome,
        string? backupRoot,
        CancellationToken cancellationToken) {
        var normalizedThreadId = threadId?.Trim() ?? string.Empty;
        if (!Guid.TryParse(normalizedThreadId, out _)) {
            throw new ArgumentException("Thread id must be a UUID.", nameof(threadId));
        }

        var requestedHome = string.IsNullOrWhiteSpace(codexHome) ? ResolveDefaultCodexHome() : codexHome!.Trim();
        var home = Path.GetFullPath(requestedHome);
        var stateDb = Path.Combine(home, "state_5.sqlite");
        if (!File.Exists(stateDb)) {
            return new CodexLocalStateThreadRecoveryResult {
                CodexHome = home,
                StateDatabasePath = stateDb,
                ThreadId = normalizedThreadId,
                DatabaseExists = false
            };
        }

        cancellationToken.ThrowIfCancellationRequested();
        var backupDirectory = CreateBackupDirectory(backupRoot);
        var backupDb = Path.Combine(backupDirectory, "state_5.sqlite");
        var automationBackups = BackupThreadAutomations(home, normalizedThreadId, backupDirectory);

        var builder = new SqliteConnectionStringBuilder {
            DataSource = stateDb,
            Mode = SqliteOpenMode.ReadWrite
        };
        using var connection = new SqliteConnection(builder.ConnectionString);
        connection.Open();
        SetBusyTimeout(connection, 10000);
        BackupDatabase(connection, backupDb);

        var columnNames = GetTableColumns(stateDb, "threads")
            .Select(static column => column.Name)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var hasArchiveColumns = columnNames.Contains("archived") || columnNames.Contains("archived_at");
        if (!columnNames.Contains("id") || !hasArchiveColumns) {
            return CreateThreadRecoveryResult(
                home,
                stateDb,
                normalizedThreadId,
                databaseExists: true,
                archiveColumnsAvailable: false,
                backupDirectory: backupDirectory,
                backupDb: backupDb,
                automationBackups: automationBackups);
        }

        var wasArchived = ReadThreadArchivedState(connection, normalizedThreadId, columnNames, cancellationToken);
        if (wasArchived is null) {
            return CreateThreadRecoveryResult(
                home,
                stateDb,
                normalizedThreadId,
                databaseExists: true,
                archiveColumnsAvailable: true,
                threadFound: false,
                backupDirectory: backupDirectory,
                backupDb: backupDb,
                automationBackups: automationBackups);
        }

        using var transaction = connection.BeginTransaction();
        SetThreadArchivedState(connection, transaction, normalizedThreadId, columnNames, archived: true);
        SetThreadArchivedState(connection, transaction, normalizedThreadId, columnNames, archived: wasArchived.Value);
        transaction.Commit();
        var restoredAutomations = RestoreMissingThreadAutomations(automationBackups);

        return CreateThreadRecoveryResult(
            home,
            stateDb,
            normalizedThreadId,
            databaseExists: true,
            archiveColumnsAvailable: true,
            threadFound: true,
            wasArchived: wasArchived.Value,
            finalArchived: wasArchived.Value,
            backupDirectory: backupDirectory,
            backupDb: backupDb,
            automationBackups: automationBackups,
            automationRestoredCount: restoredAutomations);
    }

    private static CodexLocalStateThreadRecoveryResult CreateThreadRecoveryResult(
        string home,
        string stateDb,
        string threadId,
        bool databaseExists,
        bool archiveColumnsAvailable = false,
        bool threadFound = false,
        bool wasArchived = false,
        bool finalArchived = false,
        string backupDirectory = "",
        string backupDb = "",
        IReadOnlyList<ThreadAutomationBackup>? automationBackups = null,
        int automationRestoredCount = 0) {
        var backups = automationBackups ?? [];
        return new CodexLocalStateThreadRecoveryResult {
            CodexHome = home,
            StateDatabasePath = stateDb,
            ThreadId = threadId,
            DatabaseExists = databaseExists,
            ArchiveColumnsAvailable = archiveColumnsAvailable,
            ThreadFound = threadFound,
            WasArchived = wasArchived,
            FinalArchived = finalArchived,
            BackupDirectory = backupDirectory,
            BackupDatabasePath = backupDb,
            AutomationBackupDirectory = backups.Count > 0
                ? Path.Combine(backupDirectory, "automations")
                : string.Empty,
            AutomationIds = backups.Select(static item => item.AutomationId).ToArray(),
            AutomationCountBefore = backups.Count,
            AutomationCountAfter = CountThreadAutomations(home, threadId),
            AutomationRestoredCount = automationRestoredCount
        };
    }

    private static IReadOnlyList<ThreadAutomationBackup> BackupThreadAutomations(
        string codexHome,
        string threadId,
        string backupDirectory) {
        var automationsRoot = Path.Combine(codexHome, "automations");
        if (!Directory.Exists(automationsRoot)) {
            return [];
        }

        var backupRoot = Path.Combine(backupDirectory, "automations");
        var backups = new List<ThreadAutomationBackup>();
        foreach (var automationDirectory in Directory.EnumerateDirectories(automationsRoot)) {
            var definitionPath = Path.Combine(automationDirectory, "automation.toml");
            if (!File.Exists(definitionPath) || !AutomationTargetsThread(definitionPath, threadId)) {
                continue;
            }

            var automationId = Path.GetFileName(automationDirectory);
            var destination = Path.Combine(backupRoot, automationId);
            CopyDirectory(automationDirectory, destination, overwrite: true);
            backups.Add(new ThreadAutomationBackup {
                AutomationId = automationId,
                SourceDirectory = automationDirectory,
                BackupDirectory = destination
            });
        }

        return backups;
    }

    private static int RestoreMissingThreadAutomations(IReadOnlyList<ThreadAutomationBackup> backups) {
        var restored = 0;
        foreach (var backup in backups) {
            var sourceDefinition = Path.Combine(backup.SourceDirectory, "automation.toml");
            if (File.Exists(sourceDefinition)) {
                continue;
            }

            CopyDirectory(backup.BackupDirectory, backup.SourceDirectory, overwrite: true);
            restored++;
        }

        return restored;
    }

    private static int CountThreadAutomations(string codexHome, string threadId) {
        var automationsRoot = Path.Combine(codexHome, "automations");
        if (!Directory.Exists(automationsRoot)) {
            return 0;
        }

        return Directory.EnumerateDirectories(automationsRoot)
            .Select(static directory => Path.Combine(directory, "automation.toml"))
            .Count(path => File.Exists(path) && AutomationTargetsThread(path, threadId));
    }

    private static bool AutomationTargetsThread(string definitionPath, string threadId) {
        foreach (var line in File.ReadLines(definitionPath)) {
            var trimmed = line.Trim();
            if (!trimmed.StartsWith("target_thread_id", StringComparison.OrdinalIgnoreCase)) {
                continue;
            }

            var separator = trimmed.IndexOf('=');
            if (separator < 0) {
                continue;
            }

            var value = trimmed.Substring(separator + 1).Trim().Trim('"');
            if (string.Equals(value, threadId, StringComparison.OrdinalIgnoreCase)) {
                return true;
            }
        }

        return false;
    }

    private static void CopyDirectory(string sourceDirectory, string destinationDirectory, bool overwrite) {
        Directory.CreateDirectory(destinationDirectory);
        foreach (var file in Directory.EnumerateFiles(sourceDirectory)) {
            var destination = Path.Combine(destinationDirectory, Path.GetFileName(file));
            File.Copy(file, destination, overwrite);
        }

        foreach (var directory in Directory.EnumerateDirectories(sourceDirectory)) {
            var childDestination = Path.Combine(destinationDirectory, Path.GetFileName(directory));
            CopyDirectory(directory, childDestination, overwrite);
        }
    }

    private static bool? ReadThreadArchivedState(
        SqliteConnection connection,
        string threadId,
        ISet<string> columns,
        CancellationToken cancellationToken) {
        cancellationToken.ThrowIfCancellationRequested();
        using var command = connection.CreateCommand();
        var archivedExpression = columns.Contains("archived")
            ? QuoteIdentifier("archived")
            : "NULL";
        var archivedAtExpression = columns.Contains("archived_at")
            ? QuoteIdentifier("archived_at")
            : "NULL";
        command.CommandText = $"SELECT {archivedExpression} AS archived, {archivedAtExpression} AS archived_at FROM threads WHERE {QuoteIdentifier("id")} = @threadId LIMIT 1;";
        command.Parameters.AddWithValue("@threadId", threadId);
        using var reader = command.ExecuteReader();
        if (!reader.Read()) {
            return null;
        }

        var archivedValue = reader.IsDBNull(0) ? null : reader.GetValue(0);
        var archivedAtValue = reader.IsDBNull(1) ? null : reader.GetValue(1);
        return IsTruthySqliteValue(archivedValue) || HasSqliteValue(archivedAtValue);
    }

    private static void SetThreadArchivedState(
        SqliteConnection connection,
        SqliteTransaction transaction,
        string threadId,
        ISet<string> columns,
        bool archived) {
        var assignments = new List<string>();
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        if (columns.Contains("archived")) {
            assignments.Add($"{QuoteIdentifier("archived")} = @archived");
            command.Parameters.AddWithValue("@archived", archived ? 1 : 0);
        }

        if (columns.Contains("archived_at")) {
            assignments.Add($"{QuoteIdentifier("archived_at")} = @archivedAt");
            command.Parameters.AddWithValue("@archivedAt", archived ? DateTimeOffset.UtcNow.ToUnixTimeSeconds() : DBNull.Value);
        }

        command.CommandText = "UPDATE threads SET "
                              + string.Join(", ", assignments)
                              + $" WHERE {QuoteIdentifier("id")} = @threadId;";
        command.Parameters.AddWithValue("@threadId", threadId);
        command.ExecuteNonQuery();
    }

    private static bool IsTruthySqliteValue(object? value) {
        if (!HasSqliteValue(value)) {
            return false;
        }

        return value switch {
            bool boolean => boolean,
            byte number => number != 0,
            short number => number != 0,
            int number => number != 0,
            long number => number != 0,
            string text => IsTruthyString(text),
            _ => false
        };
    }

    private static bool HasSqliteValue(object? value) {
        return value is not null
               && value is not DBNull
               && !string.IsNullOrWhiteSpace(Convert.ToString(value, CultureInfo.InvariantCulture));
    }

    private static bool IsTruthyString(string value) {
        var text = value.Trim();
        return string.Equals(text, "1", StringComparison.OrdinalIgnoreCase)
               || string.Equals(text, "true", StringComparison.OrdinalIgnoreCase)
               || string.Equals(text, "yes", StringComparison.OrdinalIgnoreCase);
    }
}
#endif
