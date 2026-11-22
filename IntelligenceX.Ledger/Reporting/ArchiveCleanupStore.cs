using System.Text.Json;
using IntelligenceX.Ledger.Agents;
using IntelligenceX.Ledger.Contracts;
using IntelligenceX.Telemetry.Agents;

namespace IntelligenceX.Ledger.Reporting;

internal sealed record ArchiveCleanupRegistration(Guid ThreadId, string Machine, string SourceRootId,
    DateTimeOffset ReadyAtUtc, string WorkingDirectory, string? Worktree, string[] Scratch);

internal sealed record ArchiveCleanupPath(string Path, string State);

internal sealed record ArchiveCleanupReceipt(string State, DateTimeOffset ObservedAtUtc, DateTimeOffset? ArchiveObservedAtUtc,
    ArchiveCleanupPath[] Paths);

/// <summary>Host-local, opt-in reconciliation for desktop cleanup. The reporter observes registered paths without
/// acquiring deletion or archive authority. The chat-to-worktree binding survives branch deletion.</summary>
internal sealed class ArchiveCleanupStore(ReporterSettings settings, Action<string, string>? writeReceipt = null) {
    private readonly string _directory = Path.Combine(Path.GetFullPath(settings.SpoolDirectory), "archive-cleanup");

    private readonly Action<string, string> _writeReceipt = writeReceipt ?? LocalFiles.WriteAtomic;
    private readonly Dictionary<(Guid Id, DateTimeOffset ReadyAtUtc), ArchiveCleanupReceipt> _pendingReceipts = new();

    private string RegistrationPath(Guid id) => Path.Combine(_directory, id.ToString("D") + ".ready.json");
    private string ReceiptPath(Guid id) => Path.Combine(_directory, id.ToString("D") + ".receipt.json");

    private static readonly StringComparison PathComparison = OperatingSystem.IsWindows()
        ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;

    internal (ArchiveCleanupRegistration? Registration, ArchiveCleanupReceipt? Receipt) Read(Guid id) =>
        (Read<ArchiveCleanupRegistration>(RegistrationPath(id)), Read<ArchiveCleanupReceipt>(ReceiptPath(id)));

    internal ArchiveCleanupRegistration Register(Guid id, AgentSessionState nativeSession, string? worktree,
        IEnumerable<string> scratch, DateTimeOffset now) {
        if (id == Guid.Empty || !Guid.TryParse(nativeSession.Id, out var nativeId) || id != nativeId ||
            nativeSession is not { Provider: "codex", ArchiveStateKnown: true, IsArchived: false, SourceRootId.Length: > 0 } ||
            !TryNormalize(nativeSession.WorkingDirectory, out var cwd))
            throw new InvalidDataException("Registration requires a matching, active local Codex chat with known archive state and working directory.");

        var scratchPaths = scratch.Select(ExistingDirectory).ToArray();
        if (worktree is not null) {
            worktree = ExistingDirectory(worktree);
            if (!string.Equals(worktree, cwd, PathComparison) || !File.Exists(Path.Combine(worktree, ".git")))
                throw new InvalidDataException("The worktree must be the chat's current linked Git checkout.");
        }
        string[] targets = worktree is null ? scratchPaths : [worktree, .. scratchPaths];
        if (targets.Length is < 1 or > 16 || targets.Any(path => !string.Equals(path, worktree, PathComparison) &&
            (Contains(path, cwd) || Contains(cwd, path) || File.Exists(Path.Combine(path, ".git")) || Directory.Exists(Path.Combine(path, ".git")))) ||
            targets.Any(path => targets.Count(other => Contains(path, other)) > 1))
            throw new InvalidDataException("Register 1–16 distinct, non-overlapping targets. Scratch must be outside the current checkout and cannot be a Git root.");

        var registration = new ArchiveCleanupRegistration(id, settings.MachineId, nativeSession.SourceRootId!, now, cwd, worktree, scratchPaths);
        // Publish the completed record atomically and refuse to replace another registration for this chat.
        var temporary = RegistrationPath(id) + "." + Guid.NewGuid().ToString("N") + ".pending";
        try {
            LocalFiles.WriteAtomic(temporary, JsonSerializer.Serialize(registration, LedgerJson.Options));
            File.Move(temporary, RegistrationPath(id));
        } finally {
            File.Delete(temporary);
        }
        return registration;
    }

    internal void Observe(IReadOnlyList<AgentSessionState> sessions, SessionArchiveCache archives, DateTimeOffset now) {
        if (!Directory.Exists(_directory)) return;
        Dictionary<(string Root, Guid Id), AgentSessionState?>? sessionsById = null;
        foreach (var file in Directory.EnumerateFiles(_directory, "*.ready.json")) {
            try {
                var registration = Read<ArchiveCleanupRegistration>(file);
                if (registration is null || registration.Scratch is null || registration.Machine != settings.MachineId ||
                    !string.Equals(file, RegistrationPath(registration.ThreadId), PathComparison)) continue;
                var key = (registration.ThreadId, registration.ReadyAtUtc);
                var receiptPath = ReceiptPath(registration.ThreadId);
                var retrying = _pendingReceipts.TryGetValue(key, out var prior);
                if (!retrying) prior = Read<ArchiveCleanupReceipt>(receiptPath);
                sessionsById ??= IndexSessions(sessions);
                sessionsById.TryGetValue((registration.SourceRootId, registration.ThreadId), out var session);
                var evidence = session is null ? null : archives.For(session);
                var receipt = Evaluate(registration, session, evidence, prior, now);
                if (retrying || prior is null || prior.State != receipt.State || prior.ArchiveObservedAtUtc != receipt.ArchiveObservedAtUtc ||
                    prior.Paths is null || !prior.Paths.SequenceEqual(receipt.Paths)) {
                    // Retain a known archive or reopen across a failed write, then reconcile with the next observation.
                    if (receipt.ArchiveObservedAtUtc is not null) _pendingReceipts[key] = receipt;
                    _writeReceipt(receiptPath, JsonSerializer.Serialize(receipt, LedgerJson.Options));
                    _pendingReceipts.Remove(key);
                }
            } catch (Exception e) when (e is IOException or UnauthorizedAccessException or JsonException) {
                Console.Error.WriteLine("archive-cleanup-record-unavailable");
            }
        }
    }

    private static Dictionary<(string Root, Guid Id), AgentSessionState?> IndexSessions(IReadOnlyList<AgentSessionState> sessions) {
        var indexed = new Dictionary<(string Root, Guid Id), AgentSessionState?>();
        foreach (var session in sessions) {
            if (session.Provider != "codex" || session.SourceRootId is not { Length: > 0 } root ||
                !Guid.TryParse(session.Id, out var id)) continue;
            // More than one observation of this exact source makes the chat binding ambiguous.
            if (!indexed.TryAdd((root, id), session)) indexed[(root, id)] = null;
        }
        return indexed;
    }

    internal static ArchiveCleanupReceipt Evaluate(ArchiveCleanupRegistration registration, AgentSessionState? session,
        SessionArchiveCache.Observation? archive, ArchiveCleanupReceipt? prior, DateTimeOffset now) {
        if (prior?.State == "reopened") return prior;
        ArchiveCleanupReceipt Hold(string state, DateTimeOffset? archivedAt = null) =>
            new(state, now, archivedAt ?? prior?.ArchiveObservedAtUtc, []);
        bool MatchesWorkspace(string? path) => TryNormalize(path, out var cwd) &&
            string.Equals(cwd, registration.WorkingDirectory, PathComparison);
        if (session is null || archive is null) return Hold("archive-state-unavailable");
        if (!archive.Archived) return Hold(prior?.ArchiveObservedAtUtc is null ? "waiting-for-archive" : "reopened");
        if (archive.ReadStartedAtUtc <= registration.ReadyAtUtc) return Hold("waiting-for-new-archive");
        // Registration already observed the chat active. A later native read can confirm its next
        // archive even when the reporter missed the intervening active state.
        var archivedAt = prior?.ArchiveObservedAtUtc ?? archive.ReadStartedAtUtc;
        if (SessionOrigin.For(session).Origin != "human" || session.TurnOpen != false ||
            !MatchesWorkspace(session.WorkingDirectory) || !MatchesWorkspace(archive.WorkingDirectory)) return Hold("chat-binding-unconfirmed", archivedAt);

        string[] targets = registration.Worktree is { } worktree ? [worktree, .. registration.Scratch] : registration.Scratch;
        var paths = targets.Select(path => new ArchiveCleanupPath(path, PathState(path))).ToArray();
        return new(paths.All(path => path.State == "absent") ? "verified-absent" : "retained",
            now, archivedAt, paths);
    }

    private static T? Read<T>(string file) where T : class => LocalFiles.ReadShared(file) is { } content
        ? JsonSerializer.Deserialize<T>(content, LedgerJson.Options) : null;

    private static bool TryNormalize(string? path, out string fullPath) {
        fullPath = "";
        if (string.IsNullOrWhiteSpace(path)) return false;
        if (OperatingSystem.IsWindows()) {
            // Codex Desktop reports ordinary worktrees with the Win32 extended-length prefix.
            if (path.Length >= 7 && path.StartsWith(@"\\?\", StringComparison.Ordinal) &&
                char.IsAsciiLetter(path[4]) && path[5] == ':' && path[6] == '\\') path = path[4..];
            if (path.StartsWith(@"\\", StringComparison.Ordinal)) return false;
        }
        if (!Path.IsPathFullyQualified(path)) return false;
        try {
            fullPath = Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));
            var root = Path.GetPathRoot(fullPath);
            return (!OperatingSystem.IsWindows() || root is { Length: 3 }) &&
                !string.Equals(fullPath, root, PathComparison);
        } catch (Exception error) when (error is ArgumentException or NotSupportedException or IOException) {
            return false;
        }
    }

    private static bool Contains(string parent, string path) => string.Equals(parent, path, PathComparison) ||
        path.StartsWith(parent + Path.DirectorySeparatorChar, PathComparison);

    private static string ExistingDirectory(string path) {
        if (!TryNormalize(path, out var fullPath) || PathState(fullPath) != "directory-present")
            throw new InvalidDataException("Each target must be an existing, absolute directory with no link in its path.");
        return fullPath;
    }

    private static string PathState(string path) {
        if (!TryNormalize(path, out var fullPath) || !Directory.Exists(Path.GetPathRoot(fullPath))) return "unavailable";
        try {
            var ancestry = new Stack<string>();
            for (var part = fullPath; part is not null && !string.Equals(part, Path.GetPathRoot(part), PathComparison);
                part = Path.GetDirectoryName(part)) ancestry.Push(part);
            while (ancestry.TryPop(out var part)) {
                var attributes = File.GetAttributes(part);
                if ((attributes & FileAttributes.ReparsePoint) != 0) return "linked-path";
                if ((attributes & FileAttributes.Directory) == 0) return "unexpected-file";
            }
            return "directory-present";
        } catch (Exception e) when (e is FileNotFoundException or DirectoryNotFoundException) {
            return "absent";
        } catch (Exception e) when (e is IOException or UnauthorizedAccessException) {
            return "unavailable";
        }
    }
}
