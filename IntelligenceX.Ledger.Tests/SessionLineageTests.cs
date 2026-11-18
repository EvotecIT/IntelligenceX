using System.Text.Json;
using IntelligenceX.Ledger.Contracts;
using IntelligenceX.Ledger.Hosting;
using IntelligenceX.Ledger.Reporting;
using IntelligenceX.Ledger.Storage;
using IntelligenceX.Telemetry.Agents;
using Xunit;

namespace IntelligenceX.Ledger.Tests;

/// <summary>
/// A person's Claude session starts a side session, which starts another, which asks two Codex helpers on another
/// machine; one helper runs a Codex subagent. The tree, its rollup, takeover and trail are read from that shape.
/// </summary>
public sealed class SessionLineageTests : IDisposable {
    private static readonly DateTimeOffset Now = new(2026, 11, 18, 12, 0, 0, TimeSpan.Zero);
    private readonly string _directory = LedgerTestPaths.NewDirectory("lineage");
    private readonly Guid _epoch = Guid.NewGuid();
    private long _sequence;
    private static readonly Guid Root = Guid.Parse("00000000-0000-4000-8000-00000000000a");
    private static readonly Guid Side = Guid.Parse("00000000-0000-4000-8000-00000000000b");
    private static readonly Guid Nested = Guid.Parse("00000000-0000-4000-8000-00000000000c");
    private static readonly Guid Stuck = Guid.Parse("00000000-0000-4000-8000-00000000000d");
    private static readonly Guid Helper = Guid.Parse("00000000-0000-4000-8000-00000000000e");
    private static readonly Guid Subagent = Guid.Parse("00000000-0000-4000-8000-00000000000f");

    public void Dispose() { if (Directory.Exists(_directory)) Directory.Delete(_directory, true); }

    private LedgerEvent Session(string machine, string provider, Guid id, SessionFact fact) => new() {
        EventId = Guid.NewGuid(), Epoch = _epoch, Sequence = ++_sequence, MachineId = machine, ReporterId = "reporter-" + machine,
        ItemId = LedgerScope.SessionItemId(machine, provider, id.ToString()), Provider = provider, Source = "watcher", ObservedAtUtc = Now,
        Session = fact with { SourceSessionId = id }
    };

    private LedgerEvent Done(string machine, string provider, Guid id) => Session(machine, provider, id, new()) with {
        Session = null, Work = new() { ActiveClaim = false, ClaimsAtUtc = Now, PullRequestsKnown = true, PullRequestsAtUtc = Now }
    };

    private static RepositoryObservation Clean(string branch) =>
        new("evotecit/intelligencex", branch, "D:/Dev/Worktrees/" + branch, null, [], null, false, "master", "observed", true, Dirty: false, Unpushed: false,
            Commit: new string('a', 40));

    private static SessionFact Working(Guid? parent = null, string? via = null, string origin = "agent") => new() {
        Origin = origin, TurnOpen = true, TurnStartedAtUtc = Now.AddMinutes(-5), LastActivityAtUtc = Now, CreatedAtUtc = Now.AddHours(-3),
        StartedBySessionId = parent, StartedByProvider = parent is null ? null : "claude", StartedVia = via
    };

    private static SessionFact Finished(DateTimeOffset at, string branch, Guid? parent = null, string? provider = null, string? via = null) => new() {
        Origin = "agent", TurnOpen = false, Outcome = "completed", TurnStartedAtUtc = at.AddMinutes(-10), TurnEndedAtUtc = at,
        LastActivityAtUtc = at, CreatedAtUtc = at.AddMinutes(-20), Repository = Clean(branch),
        StartedBySessionId = parent, StartedByProvider = provider, StartedVia = via
    };

    private async Task<LedgerStore> SeedAsync() {
        var store = new LedgerStore(Path.Combine(_directory, "ledger.sqlite"), new(new()));
        await store.InitializeAsync();
        await store.IngestAsync([
            Session("win-pc", "claude", Root, Working(origin: "human")),
            Session("win-pc", "claude", Side, Finished(Now.AddHours(-2), "side", Root, "claude", "desktop-side-session")),
            Done("win-pc", "claude", Side),
            Session("win-pc", "claude", Nested, Working(Side, "desktop-side-session")),
            Session("mac", "codex", Stuck, new() { Origin = "agent", TurnOpen = false, Outcome = "interrupted", TurnStartedAtUtc = Now.AddHours(-2),
                TurnEndedAtUtc = Now.AddHours(-1), LastActivityAtUtc = Now.AddHours(-1), CreatedAtUtc = Now.AddHours(-2),
                Repository = Clean("feature/stuck") }),
            Session("mac", "codex", Helper, Finished(Now.AddMinutes(-90), "helper")),
            Done("mac", "codex", Helper),
            Session("mac", "codex", Subagent, Finished(Now.AddMinutes(-80), "subagent", Helper, "codex", "codex-parent-thread")),
            Done("mac", "codex", Subagent)
        ], Now);
        // The stuck helper's link was recorded when it was bound; the other is still only live coordination state.
        Assert.Equal(1, await store.RecordLinksAsync([new("claude", Nested.ToString(), "codex", Stuck.ToString(), "ix-ask", Now.AddHours(-2), "ask-1")]));
        return store;
    }

    private static readonly SessionLink[] Live = [new("claude", Nested.ToString().ToUpperInvariant(), "codex", Helper.ToString(), "ix-ask", Now.AddMinutes(-100), "ask-2")];

    [Fact]
    public async Task TreeClimbsToTheRootAndRollsUpTheMostUrgentVerdict() {
        using var store = await SeedAsync();
        var tree = await store.SessionTreeAsync(Subagent.ToString(), Now, Live);

        Assert.Equal("found", tree.State);
        Assert.Equal(Root.ToString(), tree.Root);
        Assert.Equal(new[] { Root, Side, Nested, Stuck, Helper, Subagent }.Select(id => id.ToString()), tree.Nodes.Select(node => node.Id));
        Assert.Equal(new[] { 0, 1, 2, 3, 3, 4 }, tree.Nodes.Select(node => node.Depth));
        var nodes = tree.Nodes.ToDictionary(node => node.Id);
        Assert.Equal(("desktop-side-session", Side.ToString()), (nodes[Nested.ToString()].StartedVia, nodes[Nested.ToString()].StartedBy));
        Assert.Equal(("ix-ask", "mac"), (nodes[Helper.ToString()].StartedVia, nodes[Helper.ToString()].Machine));
        Assert.Equal("codex-parent-thread", nodes[Subagent.ToString()].StartedVia);

        var stuck = nodes[Stuck.ToString()];
        Assert.Equal(("stuck", "take-over", "stuck:interrupted"), (stuck.Verdict, stuck.Suggestion, stuck.SuggestionReason));
        Assert.Equal("stuck", tree.SubtreeVerdict);
        Assert.Equal("stuck", nodes[Side.ToString()].SubtreeVerdict);
        Assert.Equal(("done", "done"), (nodes[Helper.ToString()].Verdict, nodes[Helper.ToString()].SubtreeVerdict));

        // The finished helper and its subagent can go; a finished parent of live work and the live work stay.
        Assert.Equal(new[] { Helper, Subagent }.Select(id => id.ToString()), tree.Archive!.Select(entry => entry.Id));
        var held = tree.ArchiveHeld!.ToDictionary(entry => entry.Id, entry => entry.Reason);
        Assert.Equal("subtree-still-live", held[Side.ToString()]);
        Assert.Equal("not-finished:working", held[Nested.ToString()]);
        Assert.Equal("not-finished:stuck", held[Stuck.ToString()]);
        Assert.DoesNotContain(Root.ToString(), held.Keys);
    }

    [Fact]
    public async Task FirstParentStaysWhenALaterObservationNamesAnother() {
        using var store = await SeedAsync();
        await store.IngestAsync([Session("win-pc", "claude", Nested, Working(Root, "claude-parent-session") with { LastActivityAtUtc = Now.AddSeconds(5) })], Now.AddSeconds(5));
        // An existing child keeps its parent; a non-session id and a self-link are dropped.
        Assert.Equal(0, await store.RecordLinksAsync([new("codex", Helper.ToString(), "codex", Stuck.ToString(), "ix-ask", Now, "ask-3"),
            new("claude", "not-a-session", "codex", Helper.ToString(), "ix-ask", Now), new("claude", Root.ToString(), "claude", Root.ToString(), "ix-ask", Now)]));

        var nested = (await store.SessionTreeAsync(Nested.ToString(), Now.AddSeconds(5))).Nodes.Single(node => node.Id == Nested.ToString());
        Assert.Equal((Side.ToString(), "desktop-side-session"), (nested.StartedBy, nested.StartedVia));
        var stuck = (await store.SessionTreeAsync(Stuck.ToString(), Now.AddSeconds(5))).Nodes.Single(node => node.Id == Stuck.ToString());
        Assert.Equal(Nested.ToString(), stuck.StartedBy);
    }

    [Fact]
    public async Task TakeoverMovesReservationsAndRecordsBothSides() {
        using var store = await SeedAsync();
        var branch = await store.AcquireLeaseAsync(new("branch:evotecit/intelligencex:feature/stuck", "codex:" + Stuck, "mac", "fix", 600), Now.AddMinutes(-1));
        var resource = await store.AcquireLeaseAsync(new("resource:fixture", Stuck.ToString(), "mac", "fixture", 3600), Now.AddMinutes(-1));
        Assert.True(branch.Ok && resource.Ok);
        SessionAskStep[] asks = [new("ask-9", "codex", Stuck.ToString(), null, null, "queued", Now.AddHours(-1), []),
            new("ask-8", "codex", Stuck.ToString(), "claude", Root.ToString(), "succeeded", Now.AddHours(-2), [], "result-8")];
        using var sql = new DBAClientX.SQLite();
        async Task<long> Ordinal(string key) {
            await using var db = await sql.OpenSessionAsync(Path.Combine(_directory, "ledger.sqlite"));
            return Convert.ToInt64(await db.ExecuteScalarAsync("SELECT ordinal FROM reservations WHERE key=@key",
                new Dictionary<string, object?> { ["key"] = key }));
        }
        var ordinal = await Ordinal("resource:fixture");

        Assert.Equal("by-not-live", (await store.TakeOverSessionAsync(new(Stuck.ToString(), Helper.ToString()), Now)).State);
        var early = await store.TakeOverSessionAsync(new(Nested.ToString(), Root.ToString()), Now);
        Assert.Equal(("not-eligible", Now.AddMinutes(30)), (early.State, early.EligibleAtUtc));

        var reply = await store.TakeOverSessionAsync(new(Stuck.ToString(), Nested.ToString(), "helper crashed"), Now, Live, asks);
        Assert.True(reply.Ok);
        Assert.Equal(("taken-over", "stuck", "feature/stuck"), (reply.State, reply.Trigger, reply.Branch));
        // A finished ask isn't work to continue.
        Assert.Equal(new[] { "ask-9" }, reply.OpenAsks);
        Assert.Equal(ordinal, await Ordinal("resource:fixture"));
        await using (var db = await sql.OpenSessionAsync(Path.Combine(_directory, "ledger.sqlite"))) {
            // The stopped holder's interval ends where the new holder's starts, so budgets count the time once.
            var history = await db.QueryAsListAsync("SELECT started,ended FROM lease_history WHERE key='resource:fixture' ORDER BY started",
                row => (row.GetString(0), row.GetString(1)));
            Assert.Equal(2, history.Count);
            Assert.Equal(history[0].Item2, history[1].Item1);
            Assert.Equal(Now.ToString("O"), history[1].Item1);
        }
        Assert.Equal(new string('a', 40), reply.Commit);
        Assert.Equal(2, reply.Leases!.Length);
        Assert.All(reply.Leases, lease => Assert.Equal("claude:" + Nested, lease.Holder));
        Assert.Equal(new[] { "codex:" + Stuck, Stuck.ToString() }.Order(), reply.Leases.Select(lease => lease.PreviousHolder!).Order());
        var moved = Assert.Single((await store.WhoAsync("branch:evotecit/intelligencex:feature/stuck", false, Now)).Leases);
        Assert.Equal(("claude:" + Nested, branch.Lease!.ExpiresAtUtc), (moved.Holder, moved.ExpiresAtUtc));
        // Each reservation keeps its own expiry; the new holder renews it as usual.
        Assert.Equal(resource.Lease!.ExpiresAtUtc, Assert.Single((await store.WhoAsync("resource:fixture", false, Now)).Leases).ExpiresAtUtc);

        var again = await store.TakeOverSessionAsync(new(Stuck.ToString(), Nested.ToString()), Now.AddMinutes(1));
        Assert.Equal(("already-taken-over", 2), (again.State, again.Leases!.Length));
        var other = await store.TakeOverSessionAsync(new(Stuck.ToString(), Root.ToString()), Now.AddMinutes(1));
        Assert.Equal(("taken-over-by-other", Nested.ToString()), (other.State, other.By));

        var nodes = (await store.SessionTreeAsync(Root.ToString(), Now, Live)).Nodes.ToDictionary(node => node.Id);
        Assert.Equal(Nested.ToString(), nodes[Stuck.ToString()].TakenOverBy);
        Assert.Null(nodes[Stuck.ToString()].Suggestion);
        Assert.Equal(Stuck.ToString(), nodes[Nested.ToString()].TookOverFrom);
        Assert.Contains("branch:evotecit/intelligencex:feature/stuck", nodes[Nested.ToString()].Leases!);
        Assert.Null(nodes[Stuck.ToString()].Leases);

        var trail = await store.SessionTrailAsync(Helper.ToString(), Now, Live, asks.Append(
            new("ask-2", "claude", Nested.ToString(), "codex", Helper.ToString(), "completed", Now.AddMinutes(-100),
                new() { ["bound"] = Now.AddMinutes(-99), ["completed"] = Now.AddMinutes(-90) }, "result-2")));
        Assert.Equal(Root.ToString(), trail.Root);
        Assert.Contains(trail.Entries, entry => entry is { Kind: "took-over" } && entry.Session == Nested.ToString() && entry.Detail!.Contains(Stuck.ToString()));
        Assert.Contains(trail.Entries, entry => entry is { Kind: "lease-ended" } && entry.Session == Stuck.ToString());
        Assert.Contains(trail.Entries, entry => entry is { Kind: "ask-completed", Detail: "ask-2 result-2" });
        Assert.Contains(trail.Entries, entry => entry.Kind == "started" && entry.Session == Subagent.ToString() && entry.Detail == "by " + Helper + " via codex-parent-thread");
        Assert.Equal(trail.Entries.Where(entry => entry.AtUtc is not null).Select(entry => entry.AtUtc).Order(), trail.Entries.Where(entry => entry.AtUtc is not null).Select(entry => entry.AtUtc));
    }

    [Fact]
    public async Task AStoppedSuccessorIsTakenOverItself() {
        using var store = await SeedAsync();
        Assert.True((await store.TakeOverSessionAsync(new(Stuck.ToString(), Nested.ToString()), Now)).Ok);
        var later = Now.AddHours(2);
        await store.IngestAsync([Session("win-pc", "claude", Root, Working(origin: "human") with { LastActivityAtUtc = later, TurnStartedAtUtc = later })], later);
        var redirect = await store.TakeOverSessionAsync(new(Stuck.ToString(), Root.ToString()), later);
        Assert.Equal((false, "take-over-successor", Nested.ToString()), (redirect.Ok, redirect.State, redirect.By));
        Assert.Equal("taken-over", (await store.TakeOverSessionAsync(new(Nested.ToString(), Root.ToString()), later)).State);
    }

    [Fact]
    public async Task TruncatedSubtreesAreHeldAndUnreportedHelpersResolve() {
        using var store = await SeedAsync();
        var children = Enumerable.Range(0, LedgerStore.TreeNodeLimit).Select(i => new SessionLink("codex", Helper.ToString(), "codex",
            new Guid(i + 1, 0, 0, new byte[8]).ToString(), "ix-ask", Now.AddMinutes(-70)));
        Assert.Equal(LedgerStore.TreeNodeLimit, await store.RecordLinksAsync(children));

        var tree = await store.SessionTreeAsync(Root.ToString(), Now, Live);
        Assert.True(tree.Truncated);
        Assert.Equal(LedgerStore.TreeNodeLimit, tree.Nodes.Length);
        var helper = tree.Nodes.Single(node => node.Id == Helper.ToString());
        Assert.Equal("unknown", helper.SubtreeVerdict);
        Assert.Equal("subtree-truncated", tree.ArchiveHeld!.Single(entry => entry.Id == Helper.ToString()).Reason);
        Assert.DoesNotContain(tree.Archive!, entry => entry.Id == Helper.ToString());

        // A helper bound moments ago has a link but no report yet; its tree still resolves.
        var fresh = new Guid(1, 0, 0, new byte[8]).ToString();
        var fromChild = await store.SessionTreeAsync(fresh, Now, Live);
        Assert.Equal(("found", Root.ToString()), (fromChild.State, fromChild.Root));
    }

    [Fact]
    public async Task UnknownSessionsAndAmbiguousTitlesAreExplicit() {
        using var store = await SeedAsync();
        Assert.Equal("not-found", (await store.SessionTreeAsync(Guid.NewGuid().ToString(), Now)).State);
        Assert.Equal("not-found", (await store.TakeOverSessionAsync(new(Guid.NewGuid().ToString(), Nested.ToString()), Now)).State);
        Assert.Equal("same-session", (await store.TakeOverSessionAsync(new(Nested.ToString(), Nested.ToString()), Now)).State);
    }

    [Fact]
    public void DesktopSideSessionsResolveToTheirParentsCliSession() {
        var root = Path.Combine(_directory, "desktop");
        var folder = Path.Combine(root, Guid.NewGuid().ToString(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(folder);
        File.WriteAllText(Path.Combine(folder, "local_parent.json"), JsonSerializer.Serialize(new { sessionId = "local_parent", cliSessionId = Root.ToString() }));
        File.WriteAllText(Path.Combine(folder, "local_child.json"), JsonSerializer.Serialize(new {
            sessionId = "local_child", cliSessionId = Side.ToString(), spawnedFrom = new { sessionId = "local_parent", sideSession = true } }));
        File.WriteAllText(Path.Combine(folder, "local_card.json"), JsonSerializer.Serialize(new {
            sessionId = "local_card", cliSessionId = Stuck.ToString(), spawnedFrom = new { sessionId = "local_parent", taskId = "task_1" } }));
        File.WriteAllText(Path.Combine(folder, "local_orphan.json"), JsonSerializer.Serialize(new {
            sessionId = "local_orphan", cliSessionId = Nested.ToString(), spawnedFrom = new { sessionId = "local_gone" } }));
        var accounts = new ClaudeDesktopAccounts(root, Path.Combine(root, "missing.json"));
        Assert.Equal((Root.ToString(), true), accounts.Parent(Side.ToString()));
        Assert.Equal((Root.ToString(), false), accounts.Parent(Stuck.ToString()));
        Assert.Null(accounts.Parent(Root.ToString()));
        Assert.Null(accounts.Parent(Nested.ToString()));
    }

    [Fact]
    public void ReporterSendsNativeParentsAndAdmissionKeepsTheFieldsTogether() {
        var codex = new AgentSessionState { Provider = "codex", Id = Subagent.ToString(), ParentSessionId = Helper.ToString() };
        Assert.Equal((Helper, "codex", "codex-parent-thread"), SessionOrigin.Parent(codex));
        // A person's fork in the app has a parent as well, so the parent alone doesn't decide who started it.
        Assert.Equal("unknown", SessionOrigin.For(codex).Origin);
        Assert.Null(SessionOrigin.Parent(codex with { ParentSessionId = "agent-a1b2" }));
        var settings = new ReporterSettings { Endpoint = "http://127.0.0.1/", Token = new('w', 32), MachineId = "mac", ReporterId = "reporter-mac", SpoolDirectory = _directory };
        var fact = LocalReporter.SessionEvidence(settings, codex, null);
        Assert.Equal((Helper, "codex", "codex-parent-thread"), (fact.StartedBySessionId!.Value, fact.StartedByProvider!, fact.StartedVia!));

        var ledger = new LedgerSettings { DataDirectory = _directory, ReadToken = new('r', 32), WriteToken = new('w', 32),
            ExpectedReporters = new() { ["mac"] = ["reporter-mac"] } };
        var observation = Session("mac", "codex", Subagent, fact);
        Assert.True(EventAdmission.Valid(observation, ledger, Now));
        Assert.False(EventAdmission.Valid(observation with { Session = fact with { StartedVia = null } }, ledger, Now));
        Assert.False(EventAdmission.Valid(observation with { Session = fact with { StartedVia = "rumour" } }, ledger, Now));
        // An unset parent leaves no trace on the wire, so a service without lineage still accepts the reporter's events.
        Assert.DoesNotContain("startedBy", JsonSerializer.Serialize(fact with { StartedBySessionId = null, StartedByProvider = null, StartedVia = null }, LedgerJson.Options));
    }

    [Fact]
    public void AnAskLinksItsReturnSessionToTheBoundHelper() {
        var candidate = CoordinationLifecycleTests.Candidate;
        var request = new CoordinationRequest("ask-7", "goal", "help", "engine", "mac", null, new(), candidate, [], 1, "authorization",
            ReturnTo: new("requester", "win-pc", "claude", Nested.ToString(), candidate));
        var work = new CoordinationWork(request, "win-pc-sessions", 1, "completed", Now.AddMinutes(-30), ResultRef: "result-7",
            Stages: new() { ["bound"] = Now.AddMinutes(-29), ["completed"] = Now.AddMinutes(-5) });
        var binding = new CoordinationSessionBinding("ask-7", 1, "grant", "helper", "mac", "codex", Helper.ToString(), "workspace", candidate);

        Assert.Equal(new SessionLink("claude", Nested.ToString(), "codex", Helper.ToString(), "ix-ask", Now.AddMinutes(-29), "ask-7"),
            SessionLineageSource.Link(work, binding));
        var step = SessionLineageSource.Step(work, binding, null)!;
        Assert.Equal(("codex", Helper.ToString(), "result-7", 2), (step.HelperProvider, step.HelperSessionId, step.ResultRef, step.Stages.Count));
        // Ordinary submissions have no asking session to link.
        Assert.Null(SessionLineageSource.Link(work with { Request = request with { ReturnTo = null } }, binding));
    }
}
