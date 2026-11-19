using System.Security.Cryptography;
using System.Text;

namespace IntelligenceX.Ledger.Contracts;

/// <summary>The permission mode each Claude Code session on this machine last ran a prompt in, as its inbox hook saw it.
/// The Claude desktop app holds a SendMessage from a session in a different permission mode until its user approves it,
/// so the agent's sender runs in the recipient's recorded mode for its messages to start a turn. One small file per
/// session under the agent's state folder; only known mode names are written or read back.</summary>
public static class ClaudeSessionModes {
    private static readonly string[] Known = ["default", "acceptEdits", "plan", "bypassPermissions", "dontAsk", "auto"];

    private static string PathFor(string stateDirectory, string sessionId) => Path.Combine(stateDirectory, "claude-modes",
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(sessionId))).ToLowerInvariant()[..32] + ".mode");

    /// <summary>Records the session's current mode when it is a known one and differs from what is stored.</summary>
    public static void Record(string stateDirectory, string sessionId, string? mode) {
        if (mode is null || !Known.Contains(mode, StringComparer.Ordinal) || Read(stateDirectory, sessionId) == mode) return;
        var path = PathFor(stateDirectory, sessionId);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        File.WriteAllText(temporary, mode);
        File.Move(temporary, path, overwrite: true);
    }

    /// <summary>The session's recorded mode, or null when none (or an unknown value) is stored.</summary>
    public static string? Read(string stateDirectory, string sessionId) {
        try {
            var mode = File.ReadAllText(PathFor(stateDirectory, sessionId)).Trim();
            return Known.Contains(mode, StringComparer.Ordinal) ? mode : null;
        } catch (Exception error) when (error is IOException or UnauthorizedAccessException) { return null; }
    }
}
