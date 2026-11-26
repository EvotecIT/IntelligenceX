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
    // agent's local store (even when the session's tools started before it) and the request tools find it there; asks for
    // another machine, or retries of an id the Ledger may hold, are never sent there. Once the Ledger answers, the finished
    // request is imported under its own id, once. A follow-up goes to the store holding the ask it continues. A stopped
    // store that still holds a request keeps its file, and a retry it can't answer for never goes to the Ledger.
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
                    WorkspaceRoot = Path.Combine(root, "work"), Capabilities = ["reservation"], LocalCoordinationPort = FreePort(),
                    LocalFinishedRetention = TimeSpan.FromSeconds(1)
                };
                Directory.CreateDirectory(Path.GetDirectoryName(settings.CoordinationConfig)!);
                LocalCoordination.Cache(settings, Policy, ["sample"]);
                var route = LocalRoute.Load(settings.CoordinationConfig);
                Assert.Null(route.Current());
                await using var local = await LocalCoordination.StartAsync(settings, token);
                Assert.NotNull(local);
                using var down = LedgerReadClient.ForCoordination(new($"http://127.0.0.1:{FreePort()}/", new string('t', 32)));
                var asks = new AskTools(down, route);
                var requests = new CoordinationTools(down, route);

                var image = Path.Combine(root, "screen.png");
                File.WriteAllBytes(image, [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 1, 2, 3]);
                var work = await AskAsync(asks, id: null, machine: null, images: [image]);
                Assert.Equal("local", work.Route);
                Assert.Equal("unreachable", Outcome(await Assert.ThrowsAsync<McpException>(() => AskAsync(asks, null, "other-machine"))));
                Assert.Equal("unreachable", Outcome(await Assert.ThrowsAsync<McpException>(() => AskAsync(asks, "ask-maybe-in-the-ledger", null))));
                Assert.Equal(work.Request.Id, (await AskAsync(asks, work.Request.Id, null, images: [image])).Request.Id);

                Assert.Equal("cancelled", (await requests.Cancel(work.Request.Id, new(0, "no-longer-needed"), apply: true)).State);
                // With the Ledger answering (here: refusing this token), a follow-up of the local ask still goes to the local
                // store, which judges it; the Ledger would leave the outcome uncertain.
                using var refusing = LedgerReadClient.ForCoordination(new(endpoint, new string('x', 32)));
                var followUp = await Assert.ThrowsAsync<McpException>(() => AskAsync(new AskTools(refusing, route), null, null, continues: work.Request.Id));
                Assert.Equal("rejected", Outcome(followUp));
                await Task.Delay(TimeSpan.FromSeconds(1.5), token);
                var pending = await AskAsync(asks, null, null); // a later command moves the finished request to the local archive

                using var ledger = LedgerReadClient.ForCoordination(new(endpoint, hostToken));
                // What the agent caches for its local store: this machine only.
                Assert.Equal(["synthetic-machine"], (await ledger.HostPolicyAsync(token)).Machines.Select(m => m.Id));
                Assert.Equal(1, await local!.ExportAsync(ledger, token));
                Assert.Equal(0, await local.ExportAsync(ledger, token));
                using var sessions = LedgerReadClient.ForCoordination(new(endpoint, sessionsToken));
                var imported = await sessions.RequestAsync(work.Request.Id, token);
                Assert.Equal("cancelled", imported.Work.State);
                // Its title and image came with it: a retry naming them matches the Ledger's copy, and the image reads there.
                Assert.Equal(work.Request.Id, (await AskAsync(new AskTools(sessions), work.Request.Id, "synthetic-machine", images: [image])).Request.Id);
                var imageId = Assert.Single(imported.Work.Request.Helper!.Images!);
                Assert.True(File.Exists(await sessions.DownloadImageAsync(imageId, Path.Combine(root, "downloaded"), token)));
                Assert.Empty((await sessions.CoordinationStatusAsync("sample", token)).Items);

                // New projects mean new principal scopes, and a policy without this machine closes the store: either way
                // the agent restarts it.
                Assert.False(local.Stale(settings));
                LocalCoordination.Cache(settings, Policy, ["sample", "another"]);
                Assert.True(local.Stale(settings));
                LocalCoordination.Cache(settings, Policy with { Machines = Policy.Machines.Where(m => m.Id != "synthetic-machine").ToArray() }, ["sample"]);
                Assert.True(local.Stale(settings));

                // The store stops while it still holds a request: its file stays, the retry is reported unreachable and
                // the Ledger never sees it.
                await local.DisposeAsync();
                Assert.True(File.Exists(Path.Combine(Path.GetDirectoryName(settings.CoordinationConfig)!, LocalCoordination.ClientFileName)));
                var stopped = new AskTools(sessions, LocalRoute.Load(settings.CoordinationConfig));
                Assert.Equal("unreachable", Outcome(await Assert.ThrowsAsync<McpException>(() => AskAsync(stopped, pending.Request.Id, null))));
                Assert.Equal(HttpStatusCode.NotFound, (await Assert.ThrowsAsync<HttpRequestException>(() =>
                    sessions.RequestAsync(pending.Request.Id, token))).StatusCode);
            } finally {
                if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
            }
        });
    }

    // Protects the Ledger from local work replacing or rerunning its own: an identical import is answered again, while
    // an id the Ledger holds live or archived with different content, or one from another machine or for a project the
    // host may not use, is refused.
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
        // Each machine field is checked: one naming this host can't hide another naming a different machine.
        var mixed = Finished("ask-local-4", "other-machine");
        Assert.Equal("import-machine-conflict", Code(() => new CoordinationEngine(state, CoordinationLifecycleTests.Now)
            .Import(mixed with { Work = mixed.Work with { Machine = "synthetic-machine" } }, host, null)));
        var foreign = Finished("ask-local-3", "synthetic-machine");
        Assert.Equal("project-not-authorized", Code(() => new CoordinationEngine(state, CoordinationLifecycleTests.Now)
            .Import(foreign with { Work = foreign.Work with { Request = foreign.Work.Request with {
                Checkpoint = foreign.Work.Request.Checkpoint with { Project = "other" } } } }, host, null)));
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

    private static Task<CoordinationWork> AskAsync(AskTools tools, string? id, string? machine, string? continues = null,
        string[]? images = null) =>
        tools.Ask("probe", machine: machine, provider: "codex", id: id, project: "sample", branch: "main",
            commit: new string('c', 40), sessionId: "session-1", sessionProvider: "claude", continues: continues,
            displayTitle: images is null ? null : "Local probe", images: images);

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
