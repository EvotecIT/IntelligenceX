using System;
using System.Collections.Generic;
using System.Data.Common;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using DBAClientX;
using IntelligenceX.Telemetry.Agents;
using IntelligenceX.Telemetry.Usage;

namespace IntelligenceX.Codex;

/// <summary>Reads Codex Desktop's thread metadata through DbaClientX in read-only mode.</summary>
/// <remarks>Does not select messages, instructions, tool arguments or credentials. The core log collector does not require SQLite.</remarks>
public static class CodexAgentSessionMetadataReader {
    /// <summary>Collects available roots independently and reports failures for their exact source identity.</summary>
    /// <param name="roots">Codex roots whose optional Desktop metadata should be read.</param>
    /// <param name="cancellationToken">Cancels the entire collection.</param>
    public static async Task<(IReadOnlyList<AgentSessionState> Sessions, IReadOnlyList<AgentSourceDiagnostic> Diagnostics)> ReadAvailableAsync(
        IEnumerable<SourceRootRecord> roots, CancellationToken cancellationToken = default) {
        if (roots is null) throw new ArgumentNullException(nameof(roots));
        var sessions = new List<AgentSessionState>();
        var diagnostics = new List<AgentSourceDiagnostic>();
        foreach (var root in roots) {
            cancellationToken.ThrowIfCancellationRequested();
            if (IsUnsupportedWslRoot(root)) {
                diagnostics.Add(new AgentSourceDiagnostic { Provider = "codex", SourceRootId = root.Id, Code = "unsupported-metadata-source" });
                continue;
            }
            try { sessions.AddRange(await ReadAsync(root, cancellationToken).ConfigureAwait(false)); }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
            catch (Exception) {
                diagnostics.Add(new AgentSourceDiagnostic { Provider = "codex", SourceRootId = root.Id, Code = "unreadable-metadata" });
            }
        }
        return (sessions, diagnostics);
    }

    /// <summary>Reads archive flags, workspace, branch and timestamps from state_5.sqlite.</summary>
    /// <param name="root">An existing Codex telemetry root.</param>
    /// <param name="cancellationToken">Cancels database operations.</param>
    /// <remarks>Windows cannot safely use Linux SQLite locking through a WSL UNC share. Those optional roots return no Desktop rows; ReadAvailableAsync reports the skipped source.</remarks>
    public static async Task<IReadOnlyList<AgentSessionState>> ReadAsync(SourceRootRecord root,
        CancellationToken cancellationToken = default) {
        if (root is null) throw new ArgumentNullException(nameof(root));
        if (root.ProviderId != "codex") throw new ArgumentException("A Codex source root is required.", nameof(root));
        if (IsUnsupportedWslRoot(root)) return Array.Empty<AgentSessionState>();
        var database = Path.Combine(root.Path, "state_5.sqlite");
        if (!File.Exists(database)) return Array.Empty<AgentSessionState>();
        // Read-only connections can still encounter a brief schema/checkpoint lock. Allow the
        // provider's normal five-second busy window without adding application-level retries.
        // Metadata runs independently of local log updates in AgentWorkWatcher.
        using var sqlite = new SQLite { CommandTimeout = 5, BusyTimeoutMs = 5000, MaxRetryAttempts = 1 };
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
                IsArchived = archived, ArchiveStateKnown = !row.IsDBNull(6),
                State = archived ? AgentWorkState.Finished : AgentWorkState.Idle
            };
        }, cancellationToken: cancellationToken).ConfigureAwait(false);
    }

    private static bool IsUnsupportedWslRoot(SourceRootRecord root) => RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
        && root.ProviderId == "codex" && (root.SourceKind == UsageSourceKind.LocalLogs
            && string.Equals(root.PlatformHint, "wsl", StringComparison.OrdinalIgnoreCase)
            || root.Path.StartsWith("\\\\wsl$\\", StringComparison.OrdinalIgnoreCase)
            || root.Path.StartsWith("\\\\wsl.localhost\\", StringComparison.OrdinalIgnoreCase));

    private static string? ReadText(DbDataReader row, int ordinal) => row.IsDBNull(ordinal)
        ? null : Convert.ToString(row.GetValue(ordinal), CultureInfo.InvariantCulture);

    private static DateTimeOffset? ReadTime(DbDataReader row, int ordinal, bool milliseconds = false) {
        if (row.IsDBNull(ordinal) || !long.TryParse(ReadText(row, ordinal), NumberStyles.Integer, CultureInfo.InvariantCulture, out var value)) return null;
        try { return milliseconds ? DateTimeOffset.FromUnixTimeMilliseconds(value) : DateTimeOffset.FromUnixTimeSeconds(value); }
        catch (ArgumentOutOfRangeException) { return null; }
    }
}
