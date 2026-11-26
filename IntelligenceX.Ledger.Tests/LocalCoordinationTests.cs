using System.Net;
using System.Net.Sockets;
using System.Text.Json;
using IntelligenceX.Ledger.Agents;
using IntelligenceX.Ledger.Contracts;
using IntelligenceX.Ledger.Coordination;
using IntelligenceX.Ledger.Mcp;
using ModelContextProtocol;
using Xunit;

namespace IntelligenceX.Ledger.Tests;

public sealed class LocalCoordinationTests {
    private static readonly CoordinationPolicy Policy = new(1, [
        new("synthetic-machine", 1, 1, 4, 4096, ["reservation"], AllowedCreateProviders: ["codex", "claude"]),
        new("other-machine", 1, 1, 4, 4096, ["reservation"], AllowedCreateProviders: ["codex", "claude"])]);

    // Protects the outage path end to end: while the Ledger is unreachable, a new ask for this machine is taken by the
    // agent's local store and the request tools find it there; asks for another machine, or retries of an id the Ledger
    // may hold, are never sent there. Once the Ledger answers, the finished request is imported under its own id, once.
    [Fact]
    public async Task AsksForThisMachineUseTheLocalStoreWhileTheLedgerIsDownAndAreImportedOnce() {
        var hostToken = new string('h', 32);
        var sessionsToken = new string('s', 32);
        await CoordinationHttpTests.WithService(new() {
            InitialPolicy = Policy,
            Principals = [
                new("synthetic-machine-host", "host", hostToken, ["sample"], "synthetic-machine"),
                new("synthetic-machine-sessions", "client", sessionsToken, ["sample"], "synthetic-machine")]
        }, async (endpoint, _, token) => {
            var root = LedgerTestPaths.NewDirectory("local-coordination");
            try {
                var settings = new AgentSettings {
                    MachineId = "synthetic-machine", ReporterConfig = Path.Combine(root, "reporter.json"),
                    CoordinationConfig = Path.Combine(root, "coordination", "host-client.json"), StateDirectory = Path.Combine(root, "state"),
                    WorkspaceRoot = Path.Combine(root, "work"), Capabilities = ["reservation"], LocalCoordinationPort = FreePort()
                };
                Directory.CreateDirectory(Path.GetDirectoryName(settings.CoordinationConfig)!);
                LocalCoordination.Cache(settings, Policy, ["sample"]);
                await using var local = await LocalCoordination.StartAsync(settings, token);
                Assert.NotNull(local);
                using var down = LedgerReadClient.ForCoordination(new($"http://127.0.0.1:{FreePort()}/", new string('t', 32)));
                var route = LocalRoute.Load(settings.CoordinationConfig);
                var asks = new AskTools(down, route);
                var requests = new CoordinationTools(down, route);

                var work = await AskAsync(asks, id: null, machine: null);
                Assert.Equal("local", work.Route);
                Assert.Equal("unreachable", Outcome(await Assert.ThrowsAsync<McpException>(() => AskAsync(asks, null, "other-machine"))));
                Assert.Equal("unreachable", Outcome(await Assert.ThrowsAsync<McpException>(() => AskAsync(asks, "ask-maybe-in-the-ledger", null))));
                Assert.Equal(work.Request.Id, (await AskAsync(asks, work.Request.Id, null)).Request.Id);

                Assert.Equal("cancelled", (await requests.Cancel(work.Request.Id, new(0, "no-longer-needed"), apply: true)).State);
                await Task.Delay(TimeSpan.FromSeconds(1.5), token);
                await AskAsync(asks, null, null); // a later command moves the finished request to the local archive

                using var ledger = LedgerReadClient.ForCoordination(new(endpoint, hostToken));
                // What the agent caches for its local store: this machine only.
                Assert.Equal(["synthetic-machine"], (await ledger.HostPolicyAsync(token)).Machines.Select(m => m.Id));
                Assert.Equal(1, await local!.ExportAsync(ledger, token));
                Assert.Equal(0, await local.ExportAsync(ledger, token));
                using var sessions = LedgerReadClient.ForCoordination(new(endpoint, sessionsToken));
                var imported = await sessions.RequestAsync(work.Request.Id, token);
                Assert.Equal("cancelled", imported.Work.State);
                Assert.Empty((await sessions.CoordinationStatusAsync("sample", token)).Items);
            } finally {
                if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
            }
        });
    }

    // Protects the Ledger from local work replacing or rerunning its own: an identical import is answered again, while
    // an id the Ledger holds live or archived with different content, or one from another machine, is refused.
    [Fact]
    public void AnImportIsRecordedOnceAndNeverReplacesLedgerWork() {
        var host = new CoordinationPrincipal("synthetic-machine-host", "host", "unused", ["sample"], "synthetic-machine");
        var entry = Finished("ask-local-1", "synthetic-machine");
        var state = new CoordinationState { Policy = Policy };
        var engine = new CoordinationEngine(state, CoordinationLifecycleTests.Now);

        Assert.True(engine.Import(entry, host, null).Acknowledged);
        Assert.Single(engine.Archived);
        Assert.True(new CoordinationEngine(state, CoordinationLifecycleTests.Now).Import(entry, host, entry).Acknowledged);
        Assert.Equal("import-conflict", Code(() => new CoordinationEngine(state, CoordinationLifecycleTests.Now)
            .Import(entry with { Work = entry.Work with { Reason = "changed" } }, host, entry)));
        Assert.Equal("import-machine-conflict", Code(() => new CoordinationEngine(state, CoordinationLifecycleTests.Now)
            .Import(Finished("ask-local-2", "other-machine"), host, null)));
        state.Work["ask-live"] = Finished("ask-live", "synthetic-machine").Work;
        Assert.Equal("import-conflict", Code(() => new CoordinationEngine(state, CoordinationLifecycleTests.Now)
            .Import(Finished("ask-live", "synthetic-machine"), host, null)));
    }

    private static CoordinationArchiveEntry Finished(string id, string machine) {
        var request = new CoordinationRequest(id, "ask-" + id, "help", "reservation", machine, null, new(AgentSlots: 1),
            new("sample", "main", new('a', 40), null, "checkpoint", "validate"), [], 1, "authorization");
        var work = new CoordinationWork(request, "synthetic-machine-sessions", 1, "cancelled", CoordinationLifecycleTests.Now,
            Machine: machine, Stages: new() { ["accepted"] = CoordinationLifecycleTests.Now });
        return new(id, CoordinationLifecycleTests.Now, work, null, null, null, null, [], [], null);
    }

    private static string Code(Action action) => Assert.Throws<CoordinationConflictException>(action).Message;

    private static Task<CoordinationWork> AskAsync(AskTools tools, string? id, string? machine) =>
        tools.Ask("probe", machine: machine, provider: "codex", id: id, project: "sample", branch: "main",
            commit: new string('c', 40), sessionId: "session-1", sessionProvider: "claude");

    private static string? Outcome(McpException error) {
        using var json = JsonDocument.Parse(error.Message);
        return json.RootElement.GetProperty("outcome").GetString();
    }

    private static int FreePort() {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }
}
