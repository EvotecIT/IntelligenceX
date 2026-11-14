using IntelligenceX.Ledger.Contracts;
using IntelligenceX.Ledger.Reporting;
using IntelligenceX.Ledger.Storage;
using IntelligenceX.Telemetry.Agents;
using IntelligenceX.Telemetry.Usage;
using System.Text.Json;
using Xunit;

namespace IntelligenceX.Ledger.Tests;

public sealed class SessionCleanupTests : IDisposable {
    private readonly string _root = LedgerTestPaths.NewDirectory("session-cleanup");
    private static readonly DateTimeOffset Now = new(2030, 1, 1, 0, 0, 0, TimeSpan.Zero);

    [Theory]
    [InlineData("codex", 0)]
    [InlineData("codex", 8192)]
    [InlineData("claude", 0)]
    [InlineData("claude", 8192)]
    public void NativeCreationEvidenceAndTurnCountReachTheReporterThroughSmallAndProjectedLogs(string provider, int padding) {
        Directory.CreateDirectory(_root);
        var id = "10000000-0000-4000-8000-000000000001";
        var lines = new List<object>();
        if (provider == "codex") {
            lines.Add(new { timestamp = Now, type = "session_meta", payload = new { id,
                source = new { subagent = new { thread_spawn = new { parent_thread_id = "synthetic-parent" } } },
                padding = new string('x', padding) } });
            lines.Add(new { timestamp = Now, type = "event_msg", payload = new { type = "task_started", turn_id = "first" } });
            lines.Add(new { timestamp = Now, type = "event_msg", payload = new { type = "task_completed", turn_id = "first" } });
            lines.Add(new { timestamp = Now, type = "event_msg", payload = new { type = "task_started", turn_id = "second" } });
        } else {
            foreach (var turn in new[] { "first", "second" }) lines.Add(new { timestamp = Now, type = "user", sessionId = id,
                parentSessionId = "synthetic-parent", uuid = turn, padding = new string('x', padding),
                message = new { content = new[] { new { type = "text", text = "Synthetic request" } } } });
        }
        File.WriteAllLines(Path.Combine(_root, "rollout-synthetic.jsonl"), lines.Select(line => JsonSerializer.Serialize(line)));
        var observed = Assert.Single(LocalAgentWorkStateService.ReadLocal(
            [new SourceRootRecord("synthetic-root", provider, UsageSourceKind.LocalLogs, _root)], nowUtc: Now,
            options: new AgentWorkOptions { ActiveWithin = null }).Sessions);
        var settings = new ReporterSettings { Endpoint = "http://127.0.0.1:1", Token = new('t', 32),
            MachineId = "synthetic", ReporterId = "reporter", SpoolDirectory = _root };
        var fact = LocalReporter.SessionEvidence(settings, observed, null);
        Assert.Equal("agent", fact.Origin);
        Assert.Equal(2, fact.TurnCount);
    }

    [Theory]
    [InlineData("codex", "Auto: synthetic", null, null, "agent")]
    [InlineData("codex", "AUTO: synthetic", null, null, "agent")]
    [InlineData("codex", "Claude: synthetic", null, null, "agent")]
    [InlineData("claude", "Synthetic helper", "synthetic-parent", null, "agent")]
    [InlineData("codex", "Synthetic personal fork", "synthetic-parent", "desktop", "human")]
    [InlineData("codex", "Synthetic personal", null, "desktop", "human")]
    [InlineData("codex", "Synthetic helper", null, "subagent", "agent")]
    [InlineData("codex", "Synthetic CLI", null, "cli", "unknown")]
    public void CreationEvidenceProtectsInteractiveAndUnqualifiedSessions(string provider, string title, string? parent, string? source, string expected) {
        var origin = SessionOrigin.For(new AgentSessionState { Provider = provider, Title = title, ParentSessionId = parent, SessionSource = source });
        Assert.Equal(expected, origin.Origin);
        Assert.NotEmpty(origin.Evidence);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CleanupSeparatesPersonalSessionsAndHoldsActualUnfinishedResources(bool qualifiedHolder) {
        using var store = new LedgerStore(Path.Combine(_root, "ledger.sqlite"), new(new()));
        await store.InitializeAsync();
        var epoch = Guid.NewGuid();
        var ids = new[] { "human", "unknown", "ready", "dirty", "unpushed", "leased", "question", "young" };
        var observations = ids.Select((id, index) => new LedgerEvent {
            EventId = Guid.NewGuid(), Epoch = epoch, Sequence = index + 1, MachineId = "synthetic", ReporterId = "reporter",
            ItemId = id, Provider = "codex", Source = "watcher", ObservedAtUtc = Now,
            Session = new() { SourceSessionId = Guid.NewGuid(), Title = "Synthetic " + id, Origin = index < 2 ? id : "agent",
                TurnId = "turn", TurnOpen = false, Outcome = "completed", LastActivityAtUtc = id == "young" ? Now : Now.AddDays(-4),
                CreatedAtUtc = Now.AddDays(-4), TurnCount = 100,
                Repository = new("example/repo", "feature/synthetic", "synthetic-checkout", "Synthetic " + id, [], [], false, "origin/main", "observed",
                    Dirty: id == "dirty", Unpushed: id == "unpushed") }
        }).ToArray();
        await store.IngestAsync(observations, Now);
        await store.IngestAsync([observations[6] with { EventId = Guid.NewGuid(), Sequence = 9, ItemId = "pending-input", ItemKind = "input", ParentItemId = "question",
            Session = new() { TurnId = "turn", InputId = "input", Question = true, QuestionBlocking = false, QuestionAtUtc = Now.AddDays(-4) } }], Now);
        var holder = qualifiedHolder ? "codex:" + observations[5].Session!.SourceSessionId + ":owner-driven" : "leased";
        Assert.True((await store.AcquireLeaseAsync(new("branch:example/repo:feature", holder, qualifiedHolder ? "synthetic-other" : "synthetic", "edit", 600), Now)).Ok);
        Assert.Single((await store.SessionStatusAsync(observations[5].Session!.SourceSessionId.ToString()!, Now)).Leases);
        var plan = await store.CleanupPlanAsync(Now);
        Assert.Equal(observations[2].Session!.SourceSessionId.ToString(), Assert.Single(plan.Archivable).Id);
        Assert.Equal(new[] { "human", "unknown" }, plan.YoursIdle.Select(entry => entry.Origin).Order());
        Assert.Equal(new[] { "dirty-worktree", "live-lease-or-queue", "open-question", "unpushed-worktree" }, plan.Held.Select(entry => entry.Reason).Order());
        Assert.DoesNotContain(plan.Archivable, entry => entry.Title == "Synthetic young");
        Assert.Null((await store.SessionStatusAsync(observations[0].Session!.SourceSessionId.ToString()!, Now)).FreshAdvice);
        Assert.Null((await store.SessionStatusAsync(observations[1].Session!.SourceSessionId.ToString()!, Now)).FreshAdvice);
        Assert.Equal("fresh-session-recommended", (await store.SessionStatusAsync(observations[2].Session!.SourceSessionId.ToString()!, Now)).FreshAdvice);
        Assert.True((await store.SessionStatusAsync(observations[6].Session!.SourceSessionId.ToString()!, Now)).QuestionWaiting);
        await store.IngestAsync([observations[5] with { EventId = Guid.NewGuid(), Sequence = 10,
            Session = observations[5].Session! with { Archived = true, ArchiveObservedAtUtc = Now } }], Now);
        Assert.Empty((await store.WhoAsync("branch:example/repo:feature", false, Now)).Leases);
        Assert.DoesNotContain((await store.CleanupPlanAsync(Now)).Held, entry => entry.Title == "Synthetic leased");
    }

    public void Dispose() { if (Directory.Exists(_root)) Directory.Delete(_root, true); }
}
