using System;
using System.Collections.Generic;
using System.Data.Common;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using DBAClientX;
using IntelligenceX.Telemetry.Agents;
using IntelligenceX.Telemetry.Usage;

namespace IntelligenceX.Codex;

/// <summary>Reads Codex Desktop's thread metadata through DbaClientX in read-only mode.</summary>
/// <remarks>Does not select messages, instructions, tool arguments or credentials. The core log collector does not require SQLite.</remarks>
public static class CodexAgentSessionMetadataReader {
    /// <summary>Reads archive flags, workspace, branch and timestamps from state_5.sqlite.</summary>
    /// <param name="root">An existing Codex telemetry root.</param>
    /// <param name="cancellationToken">Cancels database operations.</param>
    public static async Task<IReadOnlyList<AgentSessionState>> ReadAsync(SourceRootRecord root,
        CancellationToken cancellationToken = default) {
        if (root is null) throw new ArgumentNullException(nameof(root));
        if (root.ProviderId != "codex") throw new ArgumentException("A Codex source root is required.", nameof(root));
        var database = Path.Combine(root.Path, "state_5.sqlite");
        if (!File.Exists(database)) return Array.Empty<AgentSessionState>();
        // This optional dashboard source must not stall refreshes by retrying a busy provider database.
        using var sqlite = new SQLite { CommandTimeout = 1, BusyTimeoutMs = 1000, MaxRetryAttempts = 1 };
        var columns = await sqlite.QueryReadOnlyAsListAsync(database, "PRAGMA table_info(threads);",
            static row => row.GetString(1), cancellationToken: cancellationToken).ConfigureAwait(false);
        var names = new HashSet<string>(columns, StringComparer.OrdinalIgnoreCase);
        if (!names.Contains("id")) throw new InvalidDataException("Codex thread metadata schema is unavailable.");
        // threads.title can be initialized from the first prompt. It has no explicit-name provenance.
        var fields = new[] { "id", "cwd", "git_branch", "git_origin_url", "created_at", "updated_at", "archived", "created_at_ms", "updated_at_ms" };
        var query = "SELECT " + string.Join(", ", fields.Select(field => names.Contains(field) ? "\"" + field + "\"" : "NULL")) + " FROM threads;";
        var machine = root.MachineLabel ?? (root.SourceKind == UsageSourceKind.LocalLogs ? Environment.MachineName : null);
        return await sqlite.QueryReadOnlyAsListAsync(database, query, row => {
            var archived = !row.IsDBNull(6) && Convert.ToInt64(row.GetValue(6), CultureInfo.InvariantCulture) != 0;
            return new AgentSessionState {
                Provider = "codex", Id = ReadText(row, 0) ?? string.Empty, SourceRootId = root.Id,
                ObservationSources = AgentObservationSources.DesktopMetadata,
                WorkingDirectory = ReadText(row, 1), Branch = ReadText(row, 2), Machine = machine,
                Repository = AgentRepositoryIdentity.FromRemote(ReadText(row, 3)),
                StartedAtUtc = ReadTime(row, 7, milliseconds: true) ?? ReadTime(row, 4),
                LastActivityAtUtc = ReadTime(row, 8, milliseconds: true) ?? ReadTime(row, 5),
                IsArchived = archived, State = archived ? AgentWorkState.Finished : AgentWorkState.Idle
            };
        }, cancellationToken: cancellationToken).ConfigureAwait(false);
    }

    private static string? ReadText(DbDataReader row, int ordinal) => row.IsDBNull(ordinal)
        ? null : Convert.ToString(row.GetValue(ordinal), CultureInfo.InvariantCulture);

    private static DateTimeOffset? ReadTime(DbDataReader row, int ordinal, bool milliseconds = false) {
        if (row.IsDBNull(ordinal) || !long.TryParse(ReadText(row, ordinal), NumberStyles.Integer, CultureInfo.InvariantCulture, out var value)) return null;
        try { return milliseconds ? DateTimeOffset.FromUnixTimeMilliseconds(value) : DateTimeOffset.FromUnixTimeSeconds(value); }
        catch (ArgumentOutOfRangeException) { return null; }
    }
}
