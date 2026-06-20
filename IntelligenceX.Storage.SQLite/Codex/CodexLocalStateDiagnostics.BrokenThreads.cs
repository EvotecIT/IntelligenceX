#if !NETSTANDARD2_0
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using Microsoft.Data.Sqlite;

namespace IntelligenceX.Codex;

public sealed partial class CodexLocalStateDiagnosticsService {
    private const int BrokenThreadLogLookbackHours = 48;
    private static readonly string[] BrokenThreadLogPrefixes = [
        "failed to queue mcp refresh for thread ",
        "failed to start turn",
        "failed to update thread settings",
        "error creating task",
        "error submitting message",
        "failed to submit message"
    ];

    private static readonly Regex ThreadReferenceRegex = new(
        @"(?:thread|conversation)(?:\s+id)?\s*[:=]?\s*([0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12})",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

    private static IReadOnlyList<CodexLocalStateBrokenThreadCandidate> CollectBrokenThreadCandidates(
        string codexHome,
        string stateDb,
        CancellationToken cancellationToken) {
        var logsDb = Path.Combine(codexHome, "logs_2.sqlite");
        if (!File.Exists(logsDb)) {
            return [];
        }

        IReadOnlyList<(string ThreadId, int FailureCount, long LastSeen)> loggedCandidates;
        try {
            loggedCandidates = ReadBrokenThreadLogCandidates(logsDb, cancellationToken);
        } catch (SqliteException) {
            return [];
        } catch (IOException) {
            return [];
        } catch (UnauthorizedAccessException) {
            return [];
        }

        if (loggedCandidates.Count == 0) {
            return [];
        }

        var threadStateById = ReadThreadArchiveStates(stateDb, loggedCandidates.Select(static item => item.ThreadId), cancellationToken);
        return loggedCandidates
            .Select(item => {
                var found = threadStateById.TryGetValue(item.ThreadId, out var state);
                return new CodexLocalStateBrokenThreadCandidate(
                    item.ThreadId,
                    found ? state.Title : string.Empty,
                    item.FailureCount,
                    DateTimeOffset.FromUnixTimeSeconds(item.LastSeen),
                    found && state.LastActivity > 0 ? DateTimeOffset.FromUnixTimeSeconds(state.LastActivity) : null,
                    found,
                    found && state.IsArchived,
                    !found || state.LastActivity <= 0 || item.LastSeen >= state.LastActivity);
            })
            .OrderByDescending(static item => item.LastSeenUtc)
            .ThenByDescending(static item => item.FailureCount)
            .ToArray();
    }

    private static IReadOnlyList<(string ThreadId, int FailureCount, long LastSeen)> ReadBrokenThreadLogCandidates(
        string logsDb,
        CancellationToken cancellationToken) {
        var since = DateTimeOffset.UtcNow.AddHours(-BrokenThreadLogLookbackHours).ToUnixTimeSeconds();
        var builder = new SqliteConnectionStringBuilder {
            DataSource = logsDb,
            Mode = SqliteOpenMode.ReadOnly
        };
        using var connection = new SqliteConnection(builder.ConnectionString);
        connection.Open();
        SetBusyTimeout(connection, 1000);
        using var command = connection.CreateCommand();
        var filters = string.Join(
            " OR ",
            BrokenThreadLogPrefixes.Select((_, index) => $"lower(ltrim(coalesce(feedback_log_body,''), @trimCharacters)) LIKE @pattern{index.ToString(CultureInfo.InvariantCulture)}"));
        command.CommandText = $"""
                              SELECT ts, thread_id, feedback_log_body
                              FROM logs
                              WHERE ts >= @since
                                AND ({filters})
                              ORDER BY ts DESC;
                              """;
        command.Parameters.AddWithValue("@since", since);
        command.Parameters.AddWithValue("@trimCharacters", " \t\r\n");
        for (var i = 0; i < BrokenThreadLogPrefixes.Length; i++) {
            command.Parameters.AddWithValue("@pattern" + i.ToString(CultureInfo.InvariantCulture), BrokenThreadLogPrefixes[i] + "%");
        }

        var candidates = new Dictionary<string, (int Count, long LastSeen)>(StringComparer.OrdinalIgnoreCase);
        using var reader = command.ExecuteReader();
        while (reader.Read()) {
            cancellationToken.ThrowIfCancellationRequested();
            var ts = reader.IsDBNull(0) ? 0L : Convert.ToInt64(reader.GetValue(0), CultureInfo.InvariantCulture);
            var explicitThreadId = reader.IsDBNull(1) ? string.Empty : reader.GetString(1);
            var body = reader.IsDBNull(2) ? string.Empty : reader.GetString(2);
            if (!IsBrokenThreadFailureLog(body)) {
                continue;
            }

            foreach (var threadId in ExtractThreadIds(explicitThreadId, body)) {
                if (!candidates.TryGetValue(threadId, out var current)) {
                    candidates[threadId] = (1, ts);
                    continue;
                }

                candidates[threadId] = (current.Count + 1, Math.Max(current.LastSeen, ts));
            }
        }

        return candidates
            .Select(static pair => (pair.Key, pair.Value.Count, pair.Value.LastSeen))
            .OrderByDescending(static item => item.LastSeen)
            .ThenByDescending(static item => item.Count)
            .ToArray();
    }

    private static bool IsBrokenThreadFailureLog(string body) {
        var text = body.TrimStart().ToLowerInvariant();
        return BrokenThreadLogPrefixes.Any(prefix => text.StartsWith(prefix, StringComparison.Ordinal));
    }

    private static IEnumerable<string> ExtractThreadIds(string explicitThreadId, string body) {
        if (Guid.TryParse(explicitThreadId, out _)) {
            yield return explicitThreadId;
        }

        foreach (Match match in ThreadReferenceRegex.Matches(body)) {
            var value = match.Groups[1].Value;
            if (Guid.TryParse(value, out _)) {
                yield return value;
            }
        }
    }

    private static IReadOnlyDictionary<string, (string Title, bool IsArchived, long LastActivity)> ReadThreadArchiveStates(
        string stateDb,
        IEnumerable<string> threadIds,
        CancellationToken cancellationToken) {
        var ids = threadIds.Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        if (ids.Length == 0 || !File.Exists(stateDb)) {
            return new Dictionary<string, (string Title, bool IsArchived, long LastActivity)>(StringComparer.OrdinalIgnoreCase);
        }

        var columns = GetTableColumns(stateDb, "threads")
            .Select(static column => column.Name)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (!columns.Contains("id")) {
            return new Dictionary<string, (string Title, bool IsArchived, long LastActivity)>(StringComparer.OrdinalIgnoreCase);
        }

        var titleExpr = columns.Contains("title") ? QuoteIdentifier("title") : "''";
        var archivedExpr = columns.Contains("archived") ? QuoteIdentifier("archived") : "NULL";
        var archivedAtExpr = columns.Contains("archived_at") ? QuoteIdentifier("archived_at") : "NULL";
        var updatedAtExpr = columns.Contains("updated_at") ? QuoteIdentifier("updated_at") : "NULL";
        var recencyAtExpr = columns.Contains("recency_at") ? QuoteIdentifier("recency_at") : "NULL";
        var builder = new SqliteConnectionStringBuilder {
            DataSource = stateDb,
            Mode = SqliteOpenMode.ReadOnly
        };
        using var connection = new SqliteConnection(builder.ConnectionString);
        connection.Open();
        SetBusyTimeout(connection, 1000);

        var result = new Dictionary<string, (string Title, bool IsArchived, long LastActivity)>(StringComparer.OrdinalIgnoreCase);
        foreach (var threadId in ids) {
            cancellationToken.ThrowIfCancellationRequested();
            using var command = connection.CreateCommand();
            command.CommandText = $"SELECT {titleExpr}, {archivedExpr}, {archivedAtExpr}, {updatedAtExpr}, {recencyAtExpr} FROM threads WHERE {QuoteIdentifier("id")} = @threadId LIMIT 1;";
            command.Parameters.AddWithValue("@threadId", threadId);
            using var reader = command.ExecuteReader();
            if (!reader.Read()) {
                continue;
            }

            var title = reader.IsDBNull(0) ? string.Empty : reader.GetString(0);
            var archived = reader.IsDBNull(1) ? null : reader.GetValue(1);
            var archivedAt = reader.IsDBNull(2) ? null : reader.GetValue(2);
            var updatedAt = reader.IsDBNull(3) ? 0L : Convert.ToInt64(reader.GetValue(3), CultureInfo.InvariantCulture);
            var recencyAt = reader.IsDBNull(4) ? 0L : Convert.ToInt64(reader.GetValue(4), CultureInfo.InvariantCulture);
            result[threadId] = (title, IsTruthySqliteValue(archived) || HasSqliteValue(archivedAt), Math.Max(updatedAt, recencyAt));
        }

        return result;
    }
}
#endif
