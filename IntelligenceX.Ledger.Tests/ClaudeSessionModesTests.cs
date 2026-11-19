using IntelligenceX.Ledger.Contracts;
using Xunit;

namespace IntelligenceX.Ledger.Tests;

public sealed class ClaudeSessionModesTests : IDisposable {
    private readonly string _state = Path.Combine(Path.GetTempPath(), "ix-modes-" + Guid.NewGuid().ToString("N"));

    [Fact]
    public void TheHookRecordsEachSessionsModeAndTheSenderReadsOnlyKnownOnes() {
        Assert.Null(ClaudeSessionModes.Read(_state, "session-a"));
        ClaudeSessionModes.Record(_state, "session-a", "bypassPermissions");
        ClaudeSessionModes.Record(_state, "session-b", "default");
        Assert.Equal("bypassPermissions", ClaudeSessionModes.Read(_state, "session-a"));
        Assert.Equal("default", ClaudeSessionModes.Read(_state, "session-b"));

        // A mode switch replaces the stored one; an unknown or missing value changes nothing.
        ClaudeSessionModes.Record(_state, "session-a", "acceptEdits");
        ClaudeSessionModes.Record(_state, "session-a", "--dangerously-skip-permissions");
        ClaudeSessionModes.Record(_state, "session-a", null);
        Assert.Equal("acceptEdits", ClaudeSessionModes.Read(_state, "session-a"));

        // A tampered file is not passed on to the sender.
        var file = Assert.Single(Directory.GetFiles(Path.Combine(_state, "claude-modes"), "*.mode"), f => File.ReadAllText(f) == "acceptEdits");
        File.WriteAllText(file, "anything else");
        Assert.Null(ClaudeSessionModes.Read(_state, "session-a"));
    }

    public void Dispose() {
        if (Directory.Exists(_state)) Directory.Delete(_state, recursive: true);
    }
}
