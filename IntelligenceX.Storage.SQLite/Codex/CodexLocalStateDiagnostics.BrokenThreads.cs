#if !NETSTANDARD2_0
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using DBAClientX;

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
        } catch (DbaClientXException) {
            return [];
        } catch (Exception ex) when (IsSqliteProviderException(ex)) {
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
        var filters = string.Join(
            " OR ",
            BrokenThreadLogPrefixes.Select((_, index) => $"lower(ltrim(coalesce(feedback_log_body,''), @trimCharacters)) LIKE @pattern{index.ToString(CultureInfo.InvariantCulture)}"));
        var parameters = new Dictionary<string, object?> {
            ["@since"] = since,
            ["@trimCharacters"] = " \t\r\n"
        };
        for (var i = 0; i < BrokenThreadLogPrefixes.Length; i++) {
            parameters["@pattern" + i.ToString(CultureInfo.InvariantCulture)] = BrokenThreadLogPrefixes[i] + "%";
        }

        using var sqlite = new SQLite {
            CommandTimeout = 10
        };
        using var session = sqlite.OpenSession(logsDb);
        var rows = session.QueryAsList(
            $"""
             SELECT ts, thread_id, feedback_log_body
             FROM logs
             WHERE ts >= @since
               AND ({filters})
             ORDER BY ts DESC;
             """,
            static row => (
                Ts: row.IsDBNull(0) ? 0L : Convert.ToInt64(row.GetValue(0), CultureInfo.InvariantCulture),
                ThreadId: row.IsDBNull(1) ? string.Empty : Convert.ToString(row.GetValue(1), CultureInfo.InvariantCulture) ?? string.Empty,
                Body: row.IsDBNull(2) ? string.Empty : Convert.ToString(row.GetValue(2), CultureInfo.InvariantCulture) ?? string.Empty),
            parameters);

        var candidates = new Dictionary<string, (int Count, long LastSeen)>(StringComparer.OrdinalIgnoreCase);
        foreach (var row in rows) {
            cancellationToken.ThrowIfCancellationRequested();
            if (!IsBrokenThreadFailureLog(row.Body)) {
                continue;
            }

            foreach (var threadId in ExtractThreadIds(row.ThreadId, row.Body)) {
                if (!candidates.TryGetValue(threadId, out var current)) {
                    candidates[threadId] = (1, row.Ts);
                    continue;
                }

                candidates[threadId] = (current.Count + 1, Math.Max(current.LastSeen, row.Ts));
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
        using var sqlite = new SQLite {
            CommandTimeout = 10
        };
        using var session = sqlite.OpenSession(stateDb);

        var result = new Dictionary<string, (string Title, bool IsArchived, long LastActivity)>(StringComparer.OrdinalIgnoreCase);
        foreach (var threadId in ids) {
            cancellationToken.ThrowIfCancellationRequested();
            var rows = session.QueryAsList(
                $"SELECT {titleExpr}, {archivedExpr}, {archivedAtExpr}, {updatedAtExpr}, {recencyAtExpr} FROM threads WHERE {QuoteIdentifier("id")} = @threadId LIMIT 1;",
                static row => (
                    Title: row.IsDBNull(0) ? string.Empty : Convert.ToString(row.GetValue(0), CultureInfo.InvariantCulture) ?? string.Empty,
                    Archived: row.IsDBNull(1) ? null : row.GetValue(1),
                    ArchivedAt: row.IsDBNull(2) ? null : row.GetValue(2),
                    UpdatedAt: row.IsDBNull(3) ? 0L : Convert.ToInt64(row.GetValue(3), CultureInfo.InvariantCulture),
                    RecencyAt: row.IsDBNull(4) ? 0L : Convert.ToInt64(row.GetValue(4), CultureInfo.InvariantCulture)),
                new Dictionary<string, object?> {
                    ["@threadId"] = threadId
                });
            if (rows.Count == 0) {
                continue;
            }

            var row = rows[0];
            result[threadId] = (row.Title, IsTruthySqliteValue(row.Archived) || HasSqliteValue(row.ArchivedAt), Math.Max(row.UpdatedAt, row.RecencyAt));
        }

        return result;
    }
}
#endif
