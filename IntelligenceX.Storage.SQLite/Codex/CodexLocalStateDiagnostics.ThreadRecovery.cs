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
}

public sealed partial class CodexLocalStateDiagnosticsService {
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
            return new CodexLocalStateThreadRecoveryResult {
                CodexHome = home,
                StateDatabasePath = stateDb,
                ThreadId = normalizedThreadId,
                DatabaseExists = true,
                ArchiveColumnsAvailable = false,
                BackupDirectory = backupDirectory,
                BackupDatabasePath = backupDb
            };
        }

        var wasArchived = ReadThreadArchivedState(connection, normalizedThreadId, columnNames, cancellationToken);
        if (wasArchived is null) {
            return new CodexLocalStateThreadRecoveryResult {
                CodexHome = home,
                StateDatabasePath = stateDb,
                ThreadId = normalizedThreadId,
                DatabaseExists = true,
                ArchiveColumnsAvailable = true,
                ThreadFound = false,
                BackupDirectory = backupDirectory,
                BackupDatabasePath = backupDb
            };
        }

        using var transaction = connection.BeginTransaction();
        SetThreadArchivedState(connection, transaction, normalizedThreadId, columnNames, archived: true);
        SetThreadArchivedState(connection, transaction, normalizedThreadId, columnNames, archived: wasArchived.Value);
        transaction.Commit();

        return new CodexLocalStateThreadRecoveryResult {
            CodexHome = home,
            StateDatabasePath = stateDb,
            ThreadId = normalizedThreadId,
            DatabaseExists = true,
            ArchiveColumnsAvailable = true,
            ThreadFound = true,
            WasArchived = wasArchived.Value,
            FinalArchived = wasArchived.Value,
            BackupDirectory = backupDirectory,
            BackupDatabasePath = backupDb
        };
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
