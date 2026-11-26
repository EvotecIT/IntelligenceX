using System.Net;
using System.Net.Sockets;
using System.Text.Json;
using IntelligenceX.Ledger.Contracts;
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
        Assert.Equal(("OfficeIMO", "main", new string('b', 40)), AskTools.Source("evotecit/officeimo", "main", new('b', 40), null, Checkout));
        Assert.Throws<McpException>(() => AskTools.Source(null, null, null, null, () => (null, null, null)));
    }

    // Protects the fallback decision when the Ledger host is off: a connection that never opened must say so, with the
    // id to retry and the configured fallback, instead of "uncertain", which forbids using another route.
    [Fact]
    public async Task AnAskThatCannotConnectIsReportedUnreachableWithTheConfiguredFallback() {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        var config = Path.Combine(Path.GetTempPath(), "ix-ask-" + Guid.NewGuid().ToString("N") + ".json");
        await File.WriteAllTextAsync(config, JsonSerializer.Serialize(new {
            endpoint = $"http://127.0.0.1:{port}/", token = new string('t', 32), fallback = "Use the local bridge."
        }));
        try {
            using var client = LedgerReadClient.ForCoordination(CoordinationClientSettings.Load(config));

            var error = await Assert.ThrowsAsync<McpException>(() => AskAsync(new AskTools(client), null));

            using var outcome = JsonDocument.Parse(error.Message);
            Assert.Equal("unreachable", outcome.RootElement.GetProperty("outcome").GetString());
            Assert.False(outcome.RootElement.GetProperty("sent").GetBoolean());
            Assert.Equal("connection-refused", outcome.RootElement.GetProperty("reason").GetString());
            Assert.StartsWith("ask-", outcome.RootElement.GetProperty("id").GetString());
            Assert.Equal("Use the local bridge.", outcome.RootElement.GetProperty("fallback").GetString());

            // A retry may follow an earlier attempt that did arrive, so it must not be offered the fallback.
            var retry = await Assert.ThrowsAsync<McpException>(() => AskAsync(new AskTools(client), "ask-unreachable-1"));
            using var retried = JsonDocument.Parse(retry.Message);
            Assert.Equal("ask-unreachable-1", retried.RootElement.GetProperty("id").GetString());
            Assert.False(retried.RootElement.TryGetProperty("fallback", out _));
            // So may an answer: the Ledger takes an identical one again, but the same answer sent another way would not be.
            var answer = await Assert.ThrowsAsync<McpException>(() => new AskTools(client).Answer("ask-unreachable-1", 1, "yes"));
            using var answered = JsonDocument.Parse(answer.Message);
            Assert.Equal("unreachable", answered.RootElement.GetProperty("outcome").GetString());
            Assert.False(answered.RootElement.TryGetProperty("fallback", out _));
        } finally { File.Delete(config); }
    }

    // The other side of that decision: once the request was written, a dropped connection may still have been taken by
    // the service, so it stays "uncertain" and the caller retries with the id rather than sending the work elsewhere.
    [Fact]
    public async Task AnAskWhoseConnectionDropsAfterSendingStaysUncertain() {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var server = Task.Run(async () => {
            using var socket = await listener.AcceptTcpClientAsync(deadline.Token);
            await using var stream = socket.GetStream();
            var buffer = new byte[65536];
            var request = "";
            while (!request.Contains("\"task\"", StringComparison.Ordinal)) {
                var count = await stream.ReadAsync(buffer, deadline.Token);
                if (count == 0) break;
                request += System.Text.Encoding.UTF8.GetString(buffer, 0, count);
            }
            socket.Client.LingerState = new LingerOption(true, 0);
            return request;
        });
        using var client = LedgerReadClient.ForCoordination(new($"http://127.0.0.1:{port}/", new string('t', 32)));

        var error = await Assert.ThrowsAsync<McpException>(() => AskAsync(new AskTools(client), "ask-dropped-1"));

        Assert.Contains("POST /api/coordination/v1/ask ", await server);
        using var outcome = JsonDocument.Parse(error.Message);
        Assert.Equal("uncertain", outcome.RootElement.GetProperty("outcome").GetString());
        Assert.Equal("ask-dropped-1", outcome.RootElement.GetProperty("id").GetString());
    }

    private static Task<IntelligenceX.Ledger.Contracts.CoordinationWork> AskAsync(AskTools tools, string? id) =>
        tools.Ask("probe", machine: "mac", provider: "codex", id: id, project: "OfficeIMO", branch: "master",
            commit: new string('c', 40), sessionId: "session-1", sessionProvider: "claude");
}
