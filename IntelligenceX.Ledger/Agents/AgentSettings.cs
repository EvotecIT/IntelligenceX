using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace IntelligenceX.Ledger.Agents;

/// <summary>Machine-local settings for the agent service inside the reporter. Limits and routing come from the Ledger
/// policy at run time; these settings only say where things are on this machine.</summary>
public sealed record AgentSettings {
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
    public static void WriteAtomic(string path, string content) {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        File.WriteAllText(temporary, content);
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
}
