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
    /// <summary>Seconds between Ledger sweeps. Sweeps are plain HTTP reads; no model is involved.</summary>
    public double SweepSeconds { get; init; } = 3;
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
        File.WriteAllText(temporary, content);
        if (!OperatingSystem.IsWindows())
            File.SetUnixFileMode(temporary, File.Exists(path) ? File.GetUnixFileMode(path) : UnixFileMode.UserRead | UnixFileMode.UserWrite);
        File.Move(temporary, path, overwrite: true);
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

    public static string FileFor(string stateDirectory, string requestId, long generation) =>
        Path.Combine(stateDirectory, "runs", LocalFiles.Hash(requestId, generation)[..32] + ".json");

    public static RunRecord? Load(string stateDirectory, string requestId, long generation) {
        var file = FileFor(stateDirectory, requestId, generation);
        return File.Exists(file) ? JsonSerializer.Deserialize<RunRecord>(File.ReadAllText(file)) : null;
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
        var file = TokensFile(stateDirectory, requestId, generation);
        return File.Exists(file) ? JsonSerializer.Deserialize<TurnTokens>(File.ReadAllText(file)) ?? TurnTokens.None : TurnTokens.None;
    }
}
