using IntelligenceX.Ledger.Context;
using IntelligenceX.Ledger.Contracts;
using IntelligenceX.Ledger.Coordination;
using IntelligenceX.Ledger.Hosting;
using IntelligenceX.Ledger.Storage;
using System.Text.Json;
using Xunit;

namespace IntelligenceX.Ledger.Tests;

public sealed class WorkViewRelationshipsTests : IDisposable {
    private static readonly DateTimeOffset Now = new(2026, 11, 20, 12, 0, 0, TimeSpan.Zero);
    private readonly string _root = LedgerTestPaths.NewDirectory("work-relationships");

    [Theory]
    [InlineData("claude", "failed")]
    [InlineData("codex", "succeeded")]
    public async Task AskStateOwnsTheHelperVerdictAndFinishedHelpersStayOutAfterArchival(string provider, string terminal) {
        using var store = new LedgerStore(Path.Combine(_root, "ledger.sqlite"), new(new()));
        await store.InitializeAsync();
        using var coordination = new CoordinationStore(Path.Combine(_root, "coordination.sqlite"),
            new(1, [new("mac", 2, 1, 4, 4096, ["shell"], AllowedCreateProviders: [provider])]));
        await coordination.InitializeAsync();
        var requester = Session("requester-item", "pc", "claude", new() { Title = "Format workbook" });
        var helper = Session("helper-item", "mac", provider, new() {
            TurnOpen = true, Outcome = "incomplete", LastActivityAtUtc = Now.AddHours(-2),
            // The native parent can arrive before the ask association.
            StartedByProvider = "claude", StartedBySessionId = requester.Session!.SourceSessionId,
            StartedVia = provider == "codex" ? "codex-parent-thread" : "claude-parent-session"
        });
        var reused = Session("reused-item", "mac", provider, new() {
            TurnOpen = true, Outcome = "incomplete", LastActivityAtUtc = Now.AddHours(-2)
        });
        await store.IngestAsync([requester, helper, reused], Now);

        var intent = new CoordinationAsk("ask-test", "Private instructions\nMore private instructions", "sample", "main",
            new string('b', 40), "claude", requester.Session.SourceSessionId!.ToString()!, MachineId: "mac", Provider: provider) {
            DisplayTitle = provider == "codex" ? "Check the chart export" : null
        };
        var caller = new CoordinationPrincipal("caller", "client", "unused", ["sample"], "pc");
        var queued = await coordination.AskAsync(intent, caller, Now.AddHours(-2), default);
        var request = queued.Request;
        var binding = new CoordinationSessionBinding(request.Id, 1, "grant", "helper", "mac", provider,
            helper.Session!.SourceSessionId!.ToString()!, "workspace", request.Checkpoint);
        var started = queued with { State = "started", Generation = 1,
            Machine = "mac", Assignee = binding.WorkerId, GrantId = binding.GrantId, BootId = "boot",
            Stages = new() { ["started"] = Now.AddHours(-2) } };
        await coordination.ExecuteAsync((_, state) => {
            state.Work[request.Id] = started;
            state.Bindings[request.Id] = binding;
            var reuse = request with { Id = "reused-test", ReuseSession = true };
            state.Work[reuse.Id] = started with { Request = reuse };
            state.Bindings[reuse.Id] = binding with { RequestId = reuse.Id, SessionId = reused.Session!.SourceSessionId!.ToString()! };
            return true;
        }, Now);
        Assert.Equal(0, await store.RecordLinksAsync([SessionLineageSource.Link(started, binding)!]));

        var runtime = new LedgerRuntime(store, Settings(), coordination);
        var context = new ProjectContextService(null);
        var running = await runtime.WorkAsync(context, Now, default);
        var row = Assert.Single(running.Items, row => row.Item.Id == helper.ItemId);
        Assert.Equal(("working", "active"), (row.Item.Verdict, row.Item.ReasonCode));
        Assert.Equal(provider == "codex" ? "Helper for Format workbook: Check the chart export" : "Helper for Format workbook", row.Label);
        Assert.Equal(new WorkHelperView(request.Id, "started", "claude", requester.Session.SourceSessionId!.ToString()!,
            requester.ItemId, "Format workbook"), row.Helper);
        var unrelated = Assert.Single(running.Items, row => row.Item.Id == reused.ItemId);
        Assert.Null(unrelated.Helper);
        Assert.Equal("no-progress", unrelated.Item.ReasonCode);

        var limitedAt = Now.AddSeconds(11);
        await store.IngestAsync([Next(helper, limitedAt, helper.Session with { UsageLimit = true })], limitedAt);
        var limited = Assert.Single((await runtime.WorkAsync(context, limitedAt, default)).Items, row => row.Item.Id == helper.ItemId);
        Assert.Equal(("needs-you", "usage-limit", "started"), (limited.Item.Verdict, limited.Item.ReasonCode, limited.Helper!.State));

        var asked = limitedAt.AddSeconds(11);
        await store.IngestAsync([Next(helper, asked, helper.Session)], asked);
        var host = new CoordinationPrincipal("mac-host", "host", "unused", ["sample"], "mac");
        await coordination.ExecuteAsync((engine, _) => engine.RaiseQuestion(request.Id,
            new(started.Generation, binding.GrantId, started.BootId!, "Which format?"), host), asked);
        var waiting = Assert.Single((await runtime.WorkAsync(context, asked, default)).Items, row => row.Item.Id == helper.ItemId);
        Assert.Equal(("waiting", "question", "question"), (waiting.Item.Verdict, waiting.Item.ReasonCode, waiting.Helper!.State));

        var firstResume = asked.AddSeconds(11);
        await ResumeAsync(1, firstResume);
        var firstWorking = Assert.Single((await runtime.WorkAsync(context, firstResume, default)).Items, row => row.Item.Id == helper.ItemId);
        var secondQuestion = firstResume.AddSeconds(11);
        await coordination.ExecuteAsync((engine, _) => engine.RaiseQuestion(request.Id,
            new(started.Generation, binding.GrantId, started.BootId!, "Which sheet?"), host), secondQuestion);
        var secondResume = secondQuestion.AddSeconds(11);
        await ResumeAsync(2, secondResume);
        var secondWorking = Assert.Single((await runtime.WorkAsync(context, secondResume, default)).Items, row => row.Item.Id == helper.ItemId);
        Assert.Equal(("working", secondResume), (secondWorking.Item.Verdict, secondWorking.Item.SinceUtc));
        Assert.NotEqual(firstWorking.Item.EpisodeId, secondWorking.Item.EpisodeId);

        var finished = secondResume.AddSeconds(11);
        await coordination.ExecuteAsync((_, state) => {
            state.Work[request.Id] = started with { State = terminal, Stages = new() { ["terminal"] = finished, ["accepted"] = finished } };
            state.Work.Remove("reused-test");
            state.Bindings.Remove("reused-test");
            return true;
        }, finished);
        Assert.DoesNotContain((await runtime.WorkAsync(context, finished, default, includeOpen: true)).Items, row => row.Item.Id == helper.ItemId);

        var archived = finished.AddDays(40);
        await coordination.ExecuteAsync((engine, _) => { engine.Schedule(); return true; }, archived);
        Assert.NotNull(await coordination.ArchivedAsync(request.Id));
        Assert.DoesNotContain((await runtime.WorkAsync(context, archived, default)).Items, row => row.Item.Id == helper.ItemId);

        if (terminal == "succeeded") {
            var ended = archived.AddSeconds(11);
            await store.IngestAsync([Next(helper, ended, helper.Session with {
                TurnOpen = false, Outcome = "completed", Archived = false, ArchiveObservedAtUtc = ended,
                LastActivityAtUtc = ended, TurnEndedAtUtc = ended
            })], ended);
            var open = await runtime.WorkAsync(context, ended, default, includeOpen: true);
            var completed = Assert.Single(open.Items, row => row.Item.Id == helper.ItemId);
            Assert.True(completed.Open);
            Assert.Equal(("done", "complete", terminal), (completed.Item.Verdict, completed.Item.ReasonCode, completed.Helper!.State));
            Assert.Equal("Helper for Format workbook: Check the chart export", completed.Label);
        }

        async Task ResumeAsync(int question, DateTimeOffset at) {
            await coordination.ExecuteAsync((engine, _) => {
                engine.Answer(request.Id, question, new("Use the first option."), caller);
                engine.RecordQuestionDelivery(request.Id, question, new("answer", "delivery-" + question, "sending"), host);
                return true;
            }, at.AddSeconds(-1));
            await coordination.ExecuteAsync((engine, _) => engine.RecordQuestionDelivery(request.Id, question,
                new("answer", "delivery-" + question, "submitted", "answer-observed"), host), at);
        }
    }

    [Fact]
    public async Task NativeChildrenUseTheParentsCatalogLabelWithoutLosingTheirOwnWork() {
        using var store = new LedgerStore(Path.Combine(_root, "ledger.sqlite"), new(new()));
        await store.InitializeAsync();
        var parent = Session("catalog-parent", "pc", "claude", new());
        var side = Session("desktop-side", "pc", "claude", new() {
            Title = " ", TurnOpen = true, LastActivityAtUtc = Now,
            StartedByProvider = parent.Provider, StartedBySessionId = parent.Session!.SourceSessionId, StartedVia = "desktop-side-session"
        });
        var child = Session("codex-child", "pc", "codex", side.Session! with { SourceSessionId = null, StartedVia = "claude-parent-session" });
        var unnamed = Session("unnamed-session", "pc", "claude", new() { TurnOpen = true, LastActivityAtUtc = Now });
        await store.IngestAsync([parent, side, child, unnamed], Now);
        await store.IngestAsync([Next(side, Now.AddSeconds(1), side.Session! with {
            StartedByProvider = null, StartedBySessionId = null, StartedVia = null
        })], Now.AddSeconds(1));
        var path = Path.Combine(_root, "projects.json");
        await File.WriteAllTextAsync(path, JsonSerializer.Serialize(new ProjectCatalog {
            ObservedAtUtc = Now, WorkLabels = [new(parent.ItemId, "Export charts")]
        }, LedgerJson.Options));

        var view = await new LedgerRuntime(store, Settings()).WorkAsync(new ProjectContextService(path), Now, default);

        Assert.Equal(3, view.Counts["working"]);
        foreach (var observation in new[] { side, child }) {
            var row = Assert.Single(view.Items, row => row.Item.Id == observation.ItemId);
            Assert.Equal("Export charts", row.Label);
            Assert.Equal(new WorkStartedByView("claude", parent.Session.SourceSessionId!.ToString()!, parent.ItemId,
                "Export charts", observation.Session!.StartedVia!), row.StartedBy);
            Assert.Null(row.Helper);
        }
        Assert.Equal("claude session unnamed-se", Assert.Single(view.Items, row => row.Item.Id == unnamed.ItemId).Label);
    }

    private LedgerSettings Settings() => new() { DataDirectory = _root, ReadToken = new('r', 32), WriteToken = new('w', 32) };

    private static LedgerEvent Session(string id, string machine, string provider, SessionFact session) => new() {
        EventId = Guid.NewGuid(), Epoch = Guid.NewGuid(), Sequence = 1, ItemId = id, MachineId = machine,
        ReporterId = "reporter-" + machine, Provider = provider, Source = "watcher", ObservedAtUtc = Now,
        Session = session with { SourceSessionId = Guid.NewGuid() }
    };

    private static LedgerEvent Next(LedgerEvent original, DateTimeOffset now, SessionFact session) =>
        original with { EventId = Guid.NewGuid(), Epoch = Guid.NewGuid(), ObservedAtUtc = now, Session = session };

    public void Dispose() { if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true); }
}
