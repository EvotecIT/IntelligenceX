using IntelligenceX.Ledger.Mcp;
using ModelContextProtocol;
using Xunit;

namespace IntelligenceX.Ledger.Tests;

public sealed class AskToolsTests {
    [Fact]
    public void TheAskingSessionComesFromTheCallerOrTheEnvironment() {
        string? None(string _) => null;
        Assert.Equal(("codex", "01a1e9af-1b37"), AskTools.CurrentSession("01A1E9AF-1B37", null, None));
        Assert.Equal(("claude", "Session-1"), AskTools.CurrentSession("Session-1", "claude", None));
        Assert.Equal(("claude", "abc"), AskTools.CurrentSession("abc", null, name => name == "CLAUDECODE" ? "1" : null));
        Assert.Equal(("codex", "thread-1"), AskTools.CurrentSession(null, null, name => name == "CODEX_THREAD_ID" ? "THREAD-1" : null));
        Assert.Throws<McpException>(() => AskTools.CurrentSession(null, null, None));
        Assert.Throws<McpException>(() => AskTools.CurrentSession("x", "gemini", None));
    }
}
