using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace IntelligenceX.Ledger.Agents;

/// <summary>Machine-local settings for the agent service (its own agent.json). Limits and routing come from the Ledger
/// policy at run time; these settings only say where things are on this machine.</summary>
public sealed record AgentSettings {
    public required string MachineId { get; init; }
    /// <summary>The machine reporter's configuration, whose identity and credentials carry the sign-in items.</summary>
    public required string ReporterConfig { get; init; }
    /// <summary>Coordination client settings (endpoint and token) for this machine's host principal.</summary>
    public required string CoordinationConfig { get; init; }
    /// <summary>Private local state: run records and the boot identity.</summary>
    public required string StateDirectory { get; init; }
    /// <summary>Parent directory for helper workspaces, one empty directory per request.</summary>
    public required string WorkspaceRoot { get; init; }
    /// <summary>Capabilities this host attests, matching the machine's policy entry.</summary>
    public required string[] Capabilities { get; init; }
    public string CodexExecutable { get; init; } = "codex";
    /// <summary>The Codex app's control socket. Null uses CODEX_HOME or ~/.codex.</summary>
    public string? CodexControlSocket { get; init; }
    public string ClaudeExecutable { get; init; } = "claude";
    /// <summary>Permission mode for Claude helpers. Nobody is there to approve a tool call, so a helper that should build,
    /// test and push needs bypassPermissions, the same unattended access Codex helpers run with (approval policy never).
    /// acceptEdits limits a helper to file edits; plan makes it read-only.</summary>
    public string ClaudeHelperPermissionMode { get; init; } = "bypassPermissions";
    /// <summary>Seconds between Ledger sweeps. Sweeps are plain HTTP reads; no model is involved.</summary>
    public double SweepSeconds { get; init; } = 3;
    /// <summary>Loopback port of the local coordination store that keeps asks between sessions on this machine working
    /// while the Ledger can't be reached. 0 turns it off.</summary>
    public int LocalCoordinationPort { get; init; } = 7349;
    /// <summary>How long a finished request stays live in the local store. A follow-up ask (<c>continues</c>) needs the
    /// earlier request live, so this covers follow-ups during an outage; the request is imported once it is archived.</summary>
    internal TimeSpan LocalFinishedRetention { get; init; } = TimeSpan.FromHours(1);
    /// <summary>Where the session hooks keep the boot identity and Claude permission modes: the Ledger instance's state
    /// folder. The local store's agent instance keeps its own run records but reads these from there.</summary>
    internal string? HookStateDirectory { get; init; }
    internal string HookState => HookStateDirectory ?? StateDirectory;
    /// <summary>Folders holding this machine's checkouts, for the local store's bootstrap policy. Null reads the reporter's
    /// <c>repositoryRoots</c>, which the installer maintains.</summary>
    internal string[]? RepositoryRoots { get; init; }
}

/// <summary>A stable identity for the current OS boot, kept across service restarts within that boot.</summary>
internal static class BootIdentity {
    // Kept beside the run records so the inbox hook can attest the session with the same identity.
    public static string Get(string stateDirectory, string machineId) {
        var boot = BootTime();
        var file = Path.Combine(stateDirectory, "boot.json");
        try {
            if (File.Exists(file) && JsonSerializer.Deserialize<Stored>(File.ReadAllText(file)) is { } stored &&
                Math.Abs((stored.BootTimeUtc - boot).TotalSeconds) <= 120) return stored.BootId;
        } catch (JsonException) { }
        var id = "boot-" + Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(machineId + "|" + boot.ToString("O"))))[..40];
        LocalFiles.WriteAtomic(file, JsonSerializer.Serialize(new Stored(id, boot)));
        return id;
    }

    /// <summary>Linux reads the kernel's boot id, macOS kern.boottime; Windows derives it from the tick count, which
    /// includes sleep, so it stays put within one boot (the stored value absorbs clock adjustments).</summary>
    private static DateTimeOffset BootTime() {
        if (OperatingSystem.IsMacOS() && MacBootTime() is { } mac) return mac;
        return DateTimeOffset.UtcNow - TimeSpan.FromMilliseconds(Environment.TickCount64);
    }

    private static DateTimeOffset? MacBootTime() {
        var size = (nint)16;
        var value = new long[2];
        var handle = GCHandle.Alloc(value, GCHandleType.Pinned);
        try {
            return sysctlbyname("kern.boottime", handle.AddrOfPinnedObject(), ref size, IntPtr.Zero, 0) == 0
                ? DateTimeOffset.FromUnixTimeSeconds(value[0]) : null;
        } finally { handle.Free(); }
    }

    [DllImport("libc", SetLastError = true)]
    private static extern int sysctlbyname(string name, IntPtr oldp, ref nint oldlenp, IntPtr newp, nint newlen);

    private sealed record Stored(string BootId, DateTimeOffset BootTimeUtc);
}

internal static class LocalFiles {
    /// <summary>Replaces a file in one step. On macOS and Linux the replacement keeps the original's permissions, and a
    /// new file is readable by its owner only, since these files hold tokens and private state.</summary>
    public static void WriteAtomic(string path, string content) {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        var options = new FileStreamOptions { Mode = FileMode.CreateNew, Access = FileAccess.Write, Share = FileShare.None };
        // Created owner-only, so no other user can read the content while it is written; then given the original's mode.
        if (!OperatingSystem.IsWindows()) options.UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite;
        using (var stream = new FileStream(temporary, options)) {
            stream.Write(Encoding.UTF8.GetBytes(content));
            stream.Flush(flushToDisk: true);
        }
        if (!OperatingSystem.IsWindows() && File.Exists(path)) File.SetUnixFileMode(temporary, File.GetUnixFileMode(path));
        File.Move(temporary, path, overwrite: true);
    }

    /// <summary>Reads a file another thread may be replacing at that moment (on Windows the replace briefly denies access).</summary>
    public static string? ReadShared(string path) {
        for (var attempt = 0; ; attempt++) {
            try { return File.Exists(path) ? File.ReadAllText(path) : null; }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException && attempt < 5) { Thread.Sleep(50); }
        }
    }

    public static string Hash(params object?[] parts) =>
        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(string.Join("|", parts))));
}

/// <summary>Durable local progress for one helper run, so a restart follows the same thread instead of starting another.</summary>
internal sealed record RunRecord {
    public required string RequestId { get; init; }
    public required long Generation { get; init; }
    public required string GrantId { get; init; }
    public required string Workspace { get; init; }
    public string? ThreadId { get; init; }
    public bool Bound { get; init; }
    public bool Submitted { get; init; }
    public bool Started { get; init; }
    public string? TurnId { get; init; }
    /// <summary>turn, question or done.</summary>
    public string Phase { get; init; } = "turn";
    public int? QuestionNumber { get; init; }
    public int Resumes { get; init; }
    /// <summary>Turns that ended at the Codex account's usage limit. They are retried without using up a resume.</summary>
    public int LimitWaits { get; init; }
    /// <summary>turn/start calls the app server refused. The same input is sent again, a bounded number of times.</summary>
    public int Rejections { get; init; }
    /// <summary>When the next try after a usage limit is due; a restart before then waits for it.</summary>
    public DateTimeOffset? LimitRetryAtUtc { get; init; }
    public string? ResultRef { get; init; }
    /// <summary>The thread's turn count just before a turn/start whose outcome isn't known yet. A retry follows the turn
    /// that appeared after it instead of submitting the same input again.</summary>
    public int? KnownTurns { get; init; }
    /// <summary>An answer-side delivery begun (sending) but not yet recorded as submitted, with the prompt it delivers.</summary>
    public string? AnswerAttempt { get; init; }
    public string? AnswerPrompt { get; init; }
    /// <summary>Result content chosen before it is published, so a retry or reconciliation publishes the same content.</summary>
    public string? PendingResult { get; init; }
    public bool PendingSucceeded { get; init; }
    /// <summary>The pull request comment a visual review posted: "posting" while gh runs, then its URL. A restart that finds
    /// "posting" does not post again, since the first attempt may have gone through.</summary>
    public string? PullRequestComment { get; init; }
    /// <summary>A Claude helper's running process (id and start time), so a restarted service can stop one left behind.</summary>
    public int? ProcessId { get; init; }
    public DateTimeOffset? ProcessStartedUtc { get; init; }
    /// <summary>A Claude turn's outcome, saved as soon as the turn ends so a restart handles it without running the turn again.</summary>
    public TurnOutcome? LastTurn { get; init; }
    /// <summary>Set once the helper's retirement is verified; until then the record is what proves this service owns its
    /// thread and workspace, so it is kept however long the workspace waits for review.</summary>
    public bool Retired { get; init; }

    public static string FileFor(string stateDirectory, string requestId, long generation) =>
        Path.Combine(stateDirectory, "runs", LocalFiles.Hash(requestId, generation)[..32] + ".json");

    /// <summary>The run's record, or null. A record that can't be parsed (cut short by a power loss) is renamed aside
    /// and treated as missing, so one damaged file can't stop every sweep; the request is then reconciled.</summary>
    public static RunRecord? Load(string stateDirectory, string requestId, long generation) {
        var file = FileFor(stateDirectory, requestId, generation);
        if (LocalFiles.ReadShared(file) is not { } text) return null;
        try { return JsonSerializer.Deserialize<RunRecord>(text); }
        catch (JsonException) {
            // The marker keeps the grant listed as taken up, so a thread the record named is never requeued around.
            LocalFiles.WriteAtomic(file + QuarantineSuffix, JsonSerializer.Serialize(new QuarantinedRun(requestId, generation)));
            File.Move(file, file + ".unreadable-" + DateTimeOffset.UtcNow.ToUnixTimeSeconds(), overwrite: true);
            AgentService.Log("run-record-unreadable", requestId);
            return null;
        }
    }

    private const string QuarantineSuffix = ".quarantined";
    private sealed record QuarantinedRun(string RequestId, long Generation);

    /// <summary>The grants of every run record not yet done: the grants this host has taken up. A record is saved before
    /// anything is created for its grant, so a grant missing here never got a thread, process or workspace. Null when a
    /// record can't be read: its grant is unknown, so the host can't say what it holds and must not report absence.</summary>
    public static List<(string RequestId, long Generation, string GrantId)>? Unfinished(string stateDirectory) {
        var grants = new List<(string, long, string)>();
        var folder = Path.Combine(stateDirectory, "runs");
        if (!Directory.Exists(folder)) return grants;
        foreach (var file in Directory.EnumerateFiles(folder, "*.json")) {
            if (file.EndsWith(".tokens.json", StringComparison.Ordinal)) continue;
            RunRecord? record;
            try { record = LocalFiles.ReadShared(file) is { } text ? JsonSerializer.Deserialize<RunRecord>(text) : null; }
            catch (Exception error) when (error is JsonException or IOException or UnauthorizedAccessException) {
                AgentService.Log("run-record-unreadable", Path.GetFileName(file));
                return null;
            }
            if (record is null) {
                // A file that vanished between listing and reading (pruned) holds nothing; anything else is unreadable.
                if (File.Exists(file)) { AgentService.Log("run-record-unreadable", Path.GetFileName(file)); return null; }
                continue;
            }
            if (record.Phase != "done") grants.Add((record.RequestId, record.Generation, record.GrantId));
        }
        // A set-aside record's grant is unknown, but its request and generation are: list them as taken up.
        foreach (var marker in Directory.EnumerateFiles(folder, "*.json" + QuarantineSuffix)) {
            QuarantinedRun? run;
            try { run = LocalFiles.ReadShared(marker) is { } text ? JsonSerializer.Deserialize<QuarantinedRun>(text) : null; }
            catch (Exception error) when (error is JsonException or IOException or UnauthorizedAccessException) { run = null; }
            if (run is null) {
                if (File.Exists(marker)) { AgentService.Log("run-record-unreadable", Path.GetFileName(marker)); return null; }
                continue;
            }
            if (!grants.Any(g => g.Item1 == run.RequestId && g.Item2 == run.Generation))
                grants.Add((run.RequestId, run.Generation, "quarantined"));
        }
        return grants;
    }

    /// <summary>Removes run records older than <paramref name="age"/> once the run is done and its helper retired (or it
    /// never had a thread), with their token files and set-aside records.</summary>
    public static void Prune(string stateDirectory, TimeSpan age, Func<string, bool> active) {
        var folder = Path.Combine(stateDirectory, "runs");
        if (!Directory.Exists(folder)) return;
        foreach (var file in Directory.EnumerateFiles(folder)) {
            try {
                if (DateTime.UtcNow - File.GetLastWriteTimeUtc(file) < age) continue;
                if (file.EndsWith(".json", StringComparison.Ordinal) && !file.EndsWith(".tokens.json", StringComparison.Ordinal)) {
                    var record = JsonSerializer.Deserialize<RunRecord>(File.ReadAllText(file));
                    if (record is null || record.Phase != "done" || record.ThreadId is not null && !record.Retired ||
                        active("run:" + record.RequestId + ":" + record.Generation)) continue;
                }
                // A token file or set-aside record goes only with (or after) its run record.
                else if (File.Exists(Path.Combine(folder, Path.GetFileName(file)[..32] + ".json"))) continue;
                File.Delete(file);
            } catch (Exception error) when (error is IOException or UnauthorizedAccessException or JsonException) { }
        }
    }

    public RunRecord Save(string stateDirectory) {
        LocalFiles.WriteAtomic(FileFor(stateDirectory, RequestId, Generation), JsonSerializer.Serialize(this));
        return this;
    }

    /// <summary>Tokens observed so far for the current turn, written as usage notifications arrive (a separate file, so the
    /// notification thread never races the run record), so a turn followed after a restart keeps what it already spent.</summary>
    public static string TokensFile(string stateDirectory, string requestId, long generation) =>
        Path.ChangeExtension(FileFor(stateDirectory, requestId, generation), ".tokens.json");

    public static TurnTokens LoadTokens(string stateDirectory, string requestId, long generation) {
        try {
            return LocalFiles.ReadShared(TokensFile(stateDirectory, requestId, generation)) is { } text
                ? JsonSerializer.Deserialize<TurnTokens>(text) ?? TurnTokens.None : TurnTokens.None;
        } catch (JsonException) { return TurnTokens.None; }
    }

    /// <summary>When this turn's usage was first sent, so a retry sends the identical entry and the Ledger keeps one.</summary>
    public string? UsageTurn { get; init; }
    public DateTimeOffset? UsageAtUtc { get; init; }
}
