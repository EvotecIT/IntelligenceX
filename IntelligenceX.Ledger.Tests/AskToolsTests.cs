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

    private static readonly string Here = new('a', 40);
    private static (string?, string?, string?) Checkout() => ("OfficeIMO", "feature/x", Here);

    // Protects asks about another repository: this checkout's branch and commit must not be attached to it, and an
    // owner/name project (refused by the service as an identifier) is accepted as the repository name.
    [Fact]
    public void AnotherRepositoryDoesNotBorrowThisCheckoutsBranchOrCommit() {
        Assert.Equal(("CanopyX", AskTools.DefaultBranch, AskTools.Unpinned), AskTools.Source("EvotecIT/CanopyX", null, null, null, Checkout));
        Assert.Equal(("CanopyX", "main", AskTools.Unpinned), AskTools.Source("https://github.com/EvotecIT/CanopyX.git", "main", null, null, Checkout));
        Assert.Throws<McpException>(() => AskTools.Source("CanopyX", null, null, "review", Checkout));
        Assert.Throws<McpException>(() => AskTools.Source("bad name", null, null, null, Checkout));
    }

    [Fact]
    public void ThisCheckoutFillsWhatTheCallerLeftOut() {
        Assert.Equal(("OfficeIMO", "feature/x", Here), AskTools.Source(null, null, null, null, Checkout));
        Assert.Equal(("OfficeIMO", "main", Here), AskTools.Source("evotecit/officeimo", "main", null, null, Checkout));
        Assert.Throws<McpException>(() => AskTools.Source(null, null, null, null, () => (null, null, null)));
    }
}
