using IntelligenceX.Ledger.Contracts;
using IntelligenceX.Ledger.Reporting;
using IntelligenceX.Ledger.Storage;
using Xunit;

namespace IntelligenceX.Ledger.Tests;

public sealed class LedgerPersistenceTests : IDisposable {
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "ix-ledger-tests", Guid.NewGuid().ToString("N"));
    private static readonly DateTimeOffset Now = new(2026, 1, 12, 12, 0, 0, TimeSpan.Zero);
    private static readonly TaskScope Scope = new("synthetic-task", 1);
    private static LedgerEvent Event(long sequence = 1) => new() { EventId = Guid.NewGuid(), Epoch = Guid.NewGuid(), Sequence = sequence,
        MachineId = "synthetic-machine", ReporterId = "synthetic-reporter", ItemId = "synthetic-item", Provider = "codex",
        Source = "evidence", ObservedAtUtc = Now, Delivery = new("synthetic-delivery", "submitted", Now) };

    [Theory]
    [InlineData(false, "completed")]
    [InlineData(true, "completed")]
    [InlineData(true, "incomplete")]
    public async Task TerminalRereadCanCorrectEarlierStartWithoutClosingALaterTurn(bool terminalCoversCurrentActivity, string outcome) {
        var path = Path.Combine(_directory, "session-order.sqlite");
        var partial = Event() with { Source = "watcher", Delivery = null, Session = new() {
            TurnId = "synthetic-partial-turn", TurnOpen = true, Outcome = "unknown",
            TurnStartedAtUtc = Now.AddMinutes(-10), LastActivityAtUtc = Now.AddMinutes(-5)
        } };
        using (var initial = new LedgerStore(path, new(new()))) {
            await initial.InitializeAsync();
            await initial.IngestAsync([partial], Now);
        }
        var ended = terminalCoversCurrentActivity ? Now.AddMinutes(-1) : Now.AddMinutes(-20);
        var terminal = partial with { EventId = Guid.NewGuid(), Sequence = 2, ObservedAtUtc = Now.AddMinutes(1),
            Session = new() { TurnId = "synthetic-reread-turn", TurnOpen = false, Outcome = outcome,
                TurnStartedAtUtc = Now.AddDays(-1), TurnEndedAtUtc = ended, LastActivityAtUtc = ended } };
        using var restarted = new LedgerStore(path, new(new()));
        await restarted.InitializeAsync();
        await restarted.IngestAsync([terminal], Now.AddMinutes(1));
        var item = Assert.Single(await restarted.EvaluateAllAsync(Now.AddMinutes(1)));
        Assert.Equal(terminalCoversCurrentActivity ? terminal.Session.TurnId : partial.Session.TurnId, item.Session!.TurnId);
        Assert.Equal(!terminalCoversCurrentActivity, item.Session.TurnOpen);
        Assert.Equal(terminalCoversCurrentActivity ? terminal.EventId : partial.EventId, item.SessionEvidence!.EventId);
        Assert.Equal(2, (await restarted.HistoryAsync(partial.ItemId, 0, 100, default)).Length);
    }

    [Fact]
    public async Task MatchedInputResolutionRetiresPersistedChildWithoutCompletingParent() {
        var path = Path.Combine(_directory, "resolved-input.sqlite");
        var session = new IntelligenceX.Telemetry.Agents.AgentSessionState {
            Id = Guid.NewGuid().ToString(), TurnId = "synthetic-turn", TurnOpen = true,
            PendingInputs = [new() { Id = "synthetic-question", TurnId = "synthetic-turn", AnswerIsUnconfirmed = true }]
        };
        var pending = Assert.Single(LocalReporter.InputFacts(session));
        var settings = new ReporterSettings { Endpoint = "http://127.0.0.1/", Token = new('w', 32),
            MachineId = "synthetic-machine", ReporterId = "synthetic-reporter", SpoolDirectory = _directory };
        Assert.True(LocalReporter.SessionEvidence(settings, session, null).InputUnconfirmed);
        var successor = session with { TurnId = "synthetic-successor", TurnStartedAtUtc = Now };
        Assert.False(LocalReporter.SessionEvidence(settings, successor, null).InputUnconfirmed);
        Assert.Equal(pending.Id, Assert.Single(LocalReporter.InputFacts(successor)).Id);
        var parentId = LocalReporter.ItemId("synthetic-machine", "codex", session.Id);
        var childId = LedgerScope.InputItemId(parentId, pending.Id);
        var parent = Event() with { ItemId = parentId, Delivery = null, Session = new() {
            TurnOpen = true, TurnId = pending.Fact.TurnId, TurnStartedAtUtc = Now.AddMinutes(-1), LastActivityAtUtc = Now
        }};
        var child = Event(2) with { ItemId = childId, ParentItemId = parentId, ItemKind = "input", Delivery = null, Session = pending.Fact };
        using (var first = new LedgerStore(path, new(new()))) {
            await first.InitializeAsync();
            Assert.Equal(2, (await first.IngestAsync([parent, child], Now)).Length);
            var before = (await first.EvaluateAllAsync(Now)).Single(row => row.Id == childId);
            Assert.Equal("input-status", Assert.Single(before.Current!.MissingFacts!));
        }
        // Missing/filtered observations must not resolve an input. Only a matched answer carries a child update.
        var missing = session with { PendingInputs = [] };
        Assert.Empty(LocalReporter.InputFacts(missing));
        var answered = missing with { ResolvedInputs = [new() { Id = "synthetic-question", TurnId = "synthetic-turn" }] };
        var resolution = Assert.Single(LocalReporter.InputFacts(answered));
        Assert.Equal(pending.Id, resolution.Id);
        using var restarted = new LedgerStore(path, new(new()));
        await restarted.InitializeAsync();
        var update = Event(3) with { ItemId = childId, ParentItemId = parentId, ItemKind = "input", Delivery = null,
            ObservedAtUtc = Now.AddMinutes(1), Session = resolution.Fact };
        Assert.Single(await restarted.IngestAsync([update], Now.AddMinutes(1)));
        var rows = await restarted.EvaluateAllAsync(Now.AddMinutes(1));
        Assert.Equal("working", rows.Single(row => row.Id == parentId).Current!.Value);
        var done = rows.Single(row => row.Id == childId).Current!;
        Assert.Equal("done", done.Value);
        Assert.Equal("complete", done.ReasonCode);
        Assert.Empty(done.MissingFacts!);
        var next = session with { PendingInputs = [session.PendingInputs[0] with { RequestedAtUtc = Now.AddMinutes(2) }] };
        var reopened = update with { EventId = Guid.NewGuid(), Sequence = 4, ObservedAtUtc = Now.AddMinutes(2),
            Session = Assert.Single(LocalReporter.InputFacts(next)).Fact };
        Assert.Single(await restarted.IngestAsync([reopened], Now.AddMinutes(2)));
        var pendingAgain = (await restarted.EvaluateAllAsync(Now.AddMinutes(2))).Single(row => row.Id == childId).Current!;
        Assert.Equal("stuck", pendingAgain.Value);
        Assert.Equal("input-status", Assert.Single(pendingAgain.MissingFacts!));
    }

    [Fact]
    public async Task ReceiptsAreIdempotentAcrossRestartAndDoNotInventStages() {
        var observation = Event();
        var path = Path.Combine(_directory, "ledger.sqlite");
        using (var first = new LedgerStore(path, new(new()))) {
            await first.InitializeAsync();
            Assert.Equal([observation.EventId], await first.IngestAsync([observation], Now));
        }
        using var restarted = new LedgerStore(path, new(new()));
        await restarted.InitializeAsync();
        Assert.Equal([observation.EventId], await restarted.IngestAsync([observation], Now.AddMinutes(1)));
        var row = Assert.Single(await restarted.EvaluateAllAsync(Now.AddMinutes(11)));
        var stages = row.Deliveries["synthetic-delivery"].Stages;
        Assert.Equal(Now, stages["submitted"].AtUtc);
        Assert.Equal("unknown", stages["requested"].Knowledge);
        Assert.Equal("unknown", stages["admitted"].Knowledge);
        Assert.Equal("unknown", stages["started"].Knowledge);
        Assert.Equal("unknown", stages["accepted"].Knowledge);
        Assert.Equal("delivery-uncertain", row.Current!.ReasonCode);
        Assert.Single(await restarted.HistoryAsync(row.Id, 0, 100, default));
    }

    [Fact]
    public async Task DailyReportPreservesCorrectnessMarksAndObservedBoundsAcrossRestart() {
        var path = Path.Combine(_directory, "ledger.sqlite");
        var day = DateOnly.FromDateTime(Now.UtcDateTime);
        using (var store = new LedgerStore(path, new(new()))) {
            await store.InitializeAsync();
            await store.IngestAsync([Event()], Now);
            await store.IngestAsync([Event(2) with { Delivery = null, ReviewMark = new(1, "wrong") }], Now.AddMinutes(1));
        }
        using var restarted = new LedgerStore(path, new(new()));
        await restarted.InitializeAsync();
        using var report = System.Text.Json.JsonDocument.Parse(System.Text.Json.JsonSerializer.Serialize(
            await restarted.ReportAsync(day, Now.AddHours(1), default), LedgerJson.Options));
        var root = report.RootElement;
        Assert.Equal("observed-only", root.GetProperty("coverage").GetString());
        Assert.Equal(Now, root.GetProperty("observationCoverage").GetProperty("firstReceiptAtUtc").GetDateTimeOffset());
        Assert.Equal(Now.AddMinutes(1), root.GetProperty("observationCoverage").GetProperty("latestReceiptAtUtc").GetDateTimeOffset());
        Assert.Equal("wrong", Assert.Single(root.GetProperty("transitions").EnumerateArray()).GetProperty("mark").GetString());
        Assert.Equal("synthetic-item", Assert.Single(root.GetProperty("unresolvedUnknowns").EnumerateArray()).GetProperty("id").GetString());
    }

    [Fact]
    public async Task ConflictingBatchRollsBackHistoryAndCurrentRows() {
        using var store = new LedgerStore(Path.Combine(_directory, "ledger.sqlite"), new(new()));
        await store.InitializeAsync();
        var first = Event();
        await store.IngestAsync([first], Now);
        await Assert.ThrowsAsync<EventConflictException>(() => store.IngestAsync([
            Event(2) with { ItemId = "another-item" }, first with { ObservedAtUtc = Now.AddSeconds(1) }
        ], Now));
        Assert.Single(await store.EvaluateAllAsync(Now));
        Assert.Empty(await store.HistoryAsync("another-item", 0, 100, default));
    }

    [Fact]
    public void BusyOutboxAdmissionDefersObservationWithoutLosingRetainedEvents() {
        using var spool = new ReporterSpool(_directory, 1024 * 1024);
        var retained = Event();
        var deferred = Event(2);
        spool.Append(retained);
        using (var admission = new FileStream(Path.Combine(_directory, "admission.lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None)) {
            Assert.False(spool.TryAppend(deferred));
            Assert.Equal(retained.EventId, Assert.Single(spool.Pending()).EventId);
        }
        Assert.True(spool.TryAppend(deferred));
        Assert.Equal(2, spool.Pending().Length);
    }

    [Fact]
    public void OutboxAcknowledgementRespectsTheAdmissionLeaseAndRetainsEventsUntilReleased() {
        using var spool = new ReporterSpool(_directory, 1024 * 1024);
        var retained = Event();
        spool.Append(retained);
        using (var admission = new FileStream(Path.Combine(_directory, "admission.lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None)) {
            Assert.ThrowsAny<IOException>(() => spool.Acknowledge([retained.EventId]));
            Assert.Equal(retained.EventId, Assert.Single(spool.Pending()).EventId);
        }
        spool.Acknowledge([retained.EventId]);
        Assert.Empty(spool.Pending());
    }

    [Fact]
    public void OutboxSurvivesRestartAndRetainsUnacknowledgedEventsAtCapacity() {
        var observation = Event();
        using (var spool = new ReporterSpool(_directory, 2048)) spool.Append(observation);
        var retained = Assert.Single(Directory.EnumerateFiles(_directory, "*.json"));
        var legacy = System.Text.Json.Nodes.JsonNode.Parse(File.ReadAllText(retained))!;
        legacy["delivery"]!.AsObject().Remove("causeCode");
        File.WriteAllText(retained, legacy.ToJsonString());
        using var restarted = new ReporterSpool(_directory, 2048);
        Assert.Equal(observation, Assert.Single(restarted.Pending()));
        restarted.Append(observation);
        Assert.ThrowsAny<IOException>(() => restarted.Append(Event(2) with { Coverage = [new string('x', 4096)], Delivery = null }));
        Assert.Equal(observation.EventId, Assert.Single(restarted.Pending()).EventId);
        restarted.Acknowledge([Guid.NewGuid()]);
        Assert.Single(restarted.Pending());
        restarted.Acknowledge([observation.EventId]);
        Assert.Empty(restarted.Pending());
        Assert.True(restarted.TryAppend(Event(3)));
        Assert.Single(restarted.Pending());
    }

    [Fact]
    public void SnapshotAdmissionResumesPastAcknowledgedPrefixAfterImporterRestart() {
        Directory.CreateDirectory(_directory);
        var facts = new[] { Event(), Event(2), Event(3) };
        var file = Path.Combine(_directory, "evidence.snapshot");
        File.WriteAllText(file, System.Text.Json.JsonSerializer.Serialize(facts, LedgerJson.Options));
        var capacity = facts.Max(e => System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(e, LedgerJson.Options).Length);
        var outbox = Path.Combine(_directory, "outbox");
        using var spool = new ReporterSpool(outbox, capacity);
        for (var index = 0; index < facts.Length; index++) {
            var importer = new EvidenceSnapshotImporter(file, outbox);
            Assert.Equal(index == facts.Length - 1, importer.Import(spool));
            Assert.Equal(facts[index].EventId, Assert.Single(spool.Pending()).EventId);
            spool.Acknowledge([facts[index].EventId]);
        }
        Assert.True(new EvidenceSnapshotImporter(file, outbox).Import(spool));
        Assert.Empty(spool.Pending());
    }

    [Fact]
    public async Task FreshCombinedEnvelopeCannotRenewStalePullRequestCheck() {
        using var store = new LedgerStore(Path.Combine(_directory, "ledger.sqlite"), new(new()));
        await store.InitializeAsync();
        var session = Event() with { Delivery = null, Session = new() { TurnOpen = false, Outcome = "completed", TurnEndedAtUtc = Now } };
        var work = Event(2) with { Delivery = null, Work = new() { ActiveClaim = true, ClaimsAtUtc = Now,
            PullRequestsKnown = true, PullRequestsAtUtc = Now } };
        await store.IngestAsync([session, work], Now);
        await store.IngestAsync([work with { EventId = Guid.NewGuid(), Sequence = 3, ObservedAtUtc = Now.AddMinutes(15),
            Work = work.Work! with { ActiveClaim = false, ClaimsAtUtc = Now.AddMinutes(15) } }], Now.AddMinutes(15));
        var row = Assert.Single(await store.EvaluateAllAsync(Now.AddMinutes(15)));
        Assert.Equal("unknown", row.Current!.ReasonCode);
        Assert.Contains("pull-requests", row.Current.MissingFacts!);
        Assert.Null(row.Work!.PullRequestsKnown);
        Assert.False(row.Work.ActiveClaim);
    }

    [Fact]
    public async Task UnavailableEvidenceDoesNotResetReviewWaitBeforeSamePullRequestReturns() {
        using var store = new LedgerStore(Path.Combine(_directory, "ledger.sqlite"), new(new()));
        await store.InitializeAsync();
        var session = Event() with { Delivery = null, Session = new() { TurnOpen = false, Outcome = "completed",
            TurnEndedAtUtc = Now, RepositoryBranchKey = "synthetic-branch" } };
        var local = Event(2) with { Delivery = null, Work = new() { OwnedPullRequests = ["synthetic-pr"] } };
        var global = Event(3) with { Delivery = null, ItemId = "project-evidence", GlobalWork = new(Now, [], Now,
            new() { ["synthetic-branch"] = ["synthetic-pr"] }) };
        await store.IngestAsync([session, local, global], Now);
        var initial = Assert.Single(await store.EvaluateAllAsync(Now));
        var overdue = Now.AddHours(24);
        Assert.Null(initial.ImplicitReviewWait!.DueAtUtc);
        Assert.True(initial.ImplicitReviewWait.Inferred);
        Assert.Equal(overdue, initial.ImplicitReviewWait.ReviewAfterUtc);
        var expired = Assert.Single(await store.EvaluateAllAsync(Now.AddMinutes(11)));
        Assert.Null(expired.Work!.PullRequestsKnown);
        Assert.Equal(overdue, expired.ImplicitReviewWait!.ReviewAfterUtc);
        await store.IngestAsync([global with { EventId = Guid.NewGuid(), Sequence = 4, ObservedAtUtc = Now.AddHours(23),
            GlobalWork = global.GlobalWork! with { PullRequestsAtUtc = Now.AddHours(23) } }], Now.AddHours(23));
        var returned = Assert.Single(await store.EvaluateAllAsync(Now.AddHours(25)));
        Assert.Equal(overdue, returned.ImplicitReviewWait!.ReviewAfterUtc);
        Assert.Equal("wait-review", returned.Current!.ReasonCode);
        await store.IngestAsync([global with { EventId = Guid.NewGuid(), Sequence = 5, ObservedAtUtc = Now.AddHours(26),
            GlobalWork = new(Now.AddHours(26), [], Now.AddHours(26), [], [new("synthetic-pr", "closed", Now.AddHours(26))]) }], Now.AddHours(26));
        Assert.Null(Assert.Single(await store.EvaluateAllAsync(Now.AddHours(26))).ImplicitReviewWait);
    }

    [Theory]
    [InlineData("started", 0)]
    [InlineData("submitted", null)]
    [InlineData("started", 1)]
    public async Task HistoricalOrEqualTimeProgressCannotClearInferredHoldButNewStartCan(string stage, int? minute) {
        using var store = new LedgerStore(Path.Combine(_directory, "ledger.sqlite"), new(new()));
        await store.InitializeAsync();
        var start = Event() with { Delivery = new("synthetic-delivery", "started", Now) };
        await store.IngestAsync([start, Event(2) with { ObservedAtUtc = Now.AddMinutes(1),
            Delivery = new("synthetic-delivery", "uncertain", null) }], Now.AddMinutes(1));
        await store.IngestAsync([start with { EventId = Guid.NewGuid(), Sequence = 3, ObservedAtUtc = Now.AddMinutes(2),
            Delivery = new("synthetic-delivery", stage, minute.HasValue ? Now.AddMinutes(minute.Value) : null) }], Now.AddMinutes(2));
        var held = Assert.Single(await store.EvaluateAllAsync(Now.AddMinutes(2)));
        Assert.Equal("wake-held", held.Current!.ReasonCode);
        Assert.Equal(Now.AddMinutes(1), held.Current.SinceUtc);
        await store.IngestAsync([start with { EventId = Guid.NewGuid(), Sequence = 4, ObservedAtUtc = Now.AddMinutes(3),
            Delivery = new("synthetic-delivery", "started", Now.AddMinutes(3)) }], Now.AddMinutes(3));
        Assert.Equal("started", Assert.Single(await store.EvaluateAllAsync(Now.AddMinutes(3))).Deliveries["synthetic-delivery"].LastStage);
    }

    [Fact]
    public async Task ExplicitSameStageTimeEnrichesUnknownWithoutReplacingFirstReceiptOrInventingOtherStages() {
        using var store = new LedgerStore(Path.Combine(_directory, "ledger.sqlite"), new(new()));
        await store.InitializeAsync();
        var first = Event() with { Delivery = new("synthetic-delivery", "submitted", null) };
        var timed = Event(2) with { Delivery = new("synthetic-delivery", "submitted", Now.AddMinutes(-11)) };
        await store.IngestAsync([first], Now);
        await store.IngestAsync([timed], Now.AddMinutes(1));
        var row = Assert.Single(await store.EvaluateAllAsync(Now.AddMinutes(1)));
        Assert.Equal("delivery-uncertain", row.Current!.ReasonCode);
        Assert.Equal(Now.AddMinutes(-11), row.Current.SinceUtc);
        Assert.Equal(Now.AddMinutes(-11), row.Deliveries["synthetic-delivery"].LastStageAtUtc);
        Assert.False(row.Deliveries["synthetic-delivery"].LastStageTimeIsInferred);
        var stages = row.Deliveries["synthetic-delivery"].Stages;
        Assert.Equal(first.EventId, stages["submitted"].EventId);
        Assert.Equal(timed.EventId, stages["submitted"].TimeEvidence!.EventId);
        Assert.Equal("unknown", stages["admitted"].Knowledge);
        Assert.Equal("unknown", stages["requested"].Knowledge);
        Assert.Equal(2, (await store.HistoryAsync(row.Id, 0, 100, default)).Length);
        await store.IngestAsync([Event(3) with { Delivery = new("synthetic-delivery", "started", Now.AddMinutes(-10)) }], Now.AddMinutes(2));
        var advanced = Assert.Single(await store.EvaluateAllAsync(Now.AddMinutes(2))).Deliveries["synthetic-delivery"];
        Assert.Equal("started", advanced.LastStage);
        Assert.Equal(Now.AddMinutes(-10), advanced.LastStageAtUtc);
        Assert.Equal("unknown", advanced.Stages["terminal"].Knowledge);
        Assert.Equal("unknown", advanced.Stages["accepted"].Knowledge);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task IndependentWorkDimensionsSurviveWatcherRefreshUntilExplicitResolution(bool controls) {
        using var store = new LedgerStore(Path.Combine(_directory, "ledger.sqlite"), new(new()));
        await store.InitializeAsync();
        var session = Event() with { Delivery = null, Session = new() { Scope = Scope, TurnOpen = false, Outcome = "completed", TurnEndedAtUtc = Now, AppOwned = true } };
        var watcher = Event(2) with { Delivery = null, Source = "watcher", ObservedAtUtc = Now.AddMinutes(1), Work = new() {
            ActiveClaim = false, ClaimsAtUtc = Now.AddMinutes(1), PullRequestsKnown = true, PullRequestsAtUtc = Now.AddMinutes(1) } };
        await store.IngestAsync([session, watcher], Now.AddMinutes(1));
        var wait = new WaitFact("ci", "synthetic-condition", Now, Now.AddHours(2));
        var external = Event(3) with { Delivery = null, Work = new() { Scope = Scope, Wait = wait,
            GoalHeld = controls ? true : null, GoalHeldAtUtc = controls ? Now : null,
            QueuedInputs = controls ? 2 : null, Stalled = controls ? true : null, NeedsApp = controls ? true : null } };
        // Older evidence from an independent source is not discarded by a newer watcher envelope.
        await store.IngestAsync([external], Now.AddMinutes(2));
        await store.IngestAsync([watcher with { EventId = Guid.NewGuid(), Sequence = 4, ObservedAtUtc = Now.AddMinutes(3),
            Work = watcher.Work! with { ClaimsAtUtc = Now.AddMinutes(3), PullRequestsAtUtc = Now.AddMinutes(3) } }], Now.AddMinutes(3));
        var row = Assert.Single(await store.EvaluateAllAsync(Now.AddMinutes(3)));
        Assert.Equal(wait, row.Work!.Wait);
        Assert.Equal(controls ? "goal-held" : "waiting-ci", row.Current!.ReasonCode);
        if (controls) { Assert.True(row.Work.GoalHeld); Assert.True(row.Work.NeedsApp); Assert.True(row.Work.Stalled); Assert.Equal(2, row.Work.QueuedInputs); }
        await store.IngestAsync([Event(5) with { Delivery = null, ObservedAtUtc = Now.AddMinutes(4), Work = new() {
            Scope = Scope, Wait = wait with { Resolved = true }, GoalHeld = false, NeedsApp = false, Stalled = false, QueuedInputs = 0 } }], Now.AddMinutes(4));
        row = Assert.Single(await store.EvaluateAllAsync(Now.AddMinutes(4)));
        Assert.Null(row.Work!.Wait);
        Assert.Equal("unknown", row.Current!.ReasonCode);
        Assert.Contains("pull-requests", row.Current.MissingFacts!);
        await store.IngestAsync([Event(6) with { Delivery = null, ObservedAtUtc = Now.AddMinutes(5), Work = new() {
            Scope = Scope, ActiveClaim = false, ClaimsAtUtc = Now.AddMinutes(5),
            PullRequestsKnown = true, PullRequestsAtUtc = Now.AddMinutes(5) } }], Now.AddMinutes(5));
        row = Assert.Single(await store.EvaluateAllAsync(Now.AddMinutes(5)));
        Assert.Equal("idle", row.Current!.ReasonCode);
    }

    [Theory]
    [InlineData("question")]
    [InlineData("usage-limit")]
    [InlineData("goal-held")]
    [InlineData("needs-app")]
    [InlineData("queue-behind-stall")]
    [InlineData("active")]
    [InlineData("quiet")]
    public async Task TimestampFreeConditionSinceUsesConditionObservationInsteadOfOldItemEnrollment(string condition) {
        using var store = new LedgerStore(Path.Combine(_directory, "ledger.sqlite"), new(new()));
        await store.InitializeAsync();
        var original = Event() with { Delivery = null, ObservedAtUtc = Now.AddDays(-1), Session = new() { Scope = Scope, AppOwned = true } };
        await store.IngestAsync([original], Now.AddDays(-1));
        var state = original with { EventId = Guid.NewGuid(), Sequence = 2, ObservedAtUtc = Now, Session = new() {
            Scope = Scope, AppOwned = true, Question = condition == "question", InputId = condition == "question" ? "synthetic-input" : null, UsageLimit = condition == "usage-limit",
            TurnOpen = condition is "active" or "quiet" ? true : null,
            LastActivityAtUtc = condition == "active" ? Now : condition == "quiet" ? Now.AddMinutes(-11) : null } };
        await store.IngestAsync([state, Event(3) with { Delivery = null, Work = new() {
            Scope = Scope, GoalHeld = condition == "goal-held" ? true : null, NeedsApp = condition == "needs-app" ? true : null,
            QueuedInputs = condition == "goal-held" ? 1 : condition == "queue-behind-stall" ? 2 : null } }], Now);
        var row = Assert.Single(await store.EvaluateAllAsync(Now.AddMinutes(1)));
        Assert.Equal(condition, row.Current!.ReasonCode);
        Assert.Equal(Now, row.Current.SinceUtc);
        Assert.Equal("first-observation", row.Current.SinceBasis);
    }

    [Fact]
    public async Task UnavailableOwnershipDoesNotHideAppSteeringAndWatcherDoesNotEraseAffirmativeOwnership() {
        using var store = new LedgerStore(Path.Combine(_directory, "ledger.sqlite"), new(new()));
        await store.InitializeAsync();
        var session = Event() with { Delivery = null, Session = new() { TurnOpen = true, TurnStartedAtUtc = Now, LastActivityAtUtc = Now } };
        await store.IngestAsync([session, Event(2) with { Delivery = null, Work = new() { NeedsApp = true, NeedsAppAtUtc = Now } }], Now);
        var unknown = Assert.Single(await store.EvaluateAllAsync(Now));
        Assert.Equal("unknown", unknown.Current!.ReasonCode);
        Assert.Contains("app-ownership", unknown.Current.MissingFacts!);
        await store.IngestAsync([session with { EventId = Guid.NewGuid(), Sequence = 4, Source = "watcher", ObservedAtUtc = Now.AddMinutes(2) }], Now.AddMinutes(2));
        var owner = session with { EventId = Guid.NewGuid(), Sequence = 3, ObservedAtUtc = Now.AddMinutes(1), Session = session.Session! with { AppOwned = true } };
        await store.IngestAsync([owner], Now.AddMinutes(2));
        var needs = Assert.Single(await store.EvaluateAllAsync(Now.AddMinutes(2)));
        Assert.Equal("needs-app", needs.Current!.ReasonCode);
        Assert.Equal(owner.EventId, needs.OwnershipEvidence!.EventId);
    }

    [Fact]
    public async Task FullOutboxDrainsBeforePostingLatestHealthAfterConnectivityReturns() {
        var retained = Event();
        var heartbeat = Event(2) with { Delivery = null, Heartbeat = true };
        var capacity = System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(retained, LedgerJson.Options).Length;
        using var spool = new ReporterSpool(_directory, capacity);
        spool.Append(retained);
        Assert.False(spool.TryAppend(heartbeat));
        using var handler = new ReceiptHandler();
        using var client = new HttpClient(handler) { BaseAddress = new("http://127.0.0.1/") };
        await LocalReporter.DeliverAsync(client, spool, heartbeat, default);
        Assert.Equal(new[] { retained.EventId }, handler.Received);
        Assert.Equal(heartbeat.ObservedAtUtc, handler.Health!.ObservedAtUtc);
        Assert.Empty(spool.Pending());
    }

    private sealed class ReceiptHandler : HttpMessageHandler {
        internal List<Guid> Received { get; } = [];
        internal ReporterHealth? Health { get; private set; }
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) {
            if (request.RequestUri!.AbsolutePath.EndsWith("/reporters/health", StringComparison.Ordinal)) {
                Health = System.Text.Json.JsonSerializer.Deserialize<ReporterHealth>(await request.Content!.ReadAsStringAsync(cancellationToken), LedgerJson.Options);
                Assert.NotEmpty(Received);
                return new(System.Net.HttpStatusCode.OK) { Content = new StringContent("{}") };
            }
            var events = System.Text.Json.JsonSerializer.Deserialize<LedgerEvent[]>(await request.Content!.ReadAsStringAsync(cancellationToken), LedgerJson.Options)!;
            Received.AddRange(events.Select(e => e.EventId));
            return new(System.Net.HttpStatusCode.OK) { Content = new StringContent(System.Text.Json.JsonSerializer.Serialize(
                new { acknowledgedEventIds = events.Select(e => e.EventId).ToArray() })) };
        }
    }

    [Fact]
    public void InterruptedAtomicWriteRecoversCompleteEventAndDiscardsOnlyIncompleteRemnant() {
        Directory.CreateDirectory(_directory);
        var observation = Event();
        var bytes = System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(observation, LedgerJson.Options);
        var file = Path.Combine(_directory, $"{observation.Sequence:D20}-{observation.EventId:N}.json.tmp");
        File.WriteAllBytes(file, bytes);
        File.WriteAllText(Path.Combine(_directory, $"{2:D20}-{Guid.NewGuid():N}.json.tmp"), "{\"eventId\":");
        using var spool = new ReporterSpool(_directory, bytes.Length);
        Assert.Equal(observation, Assert.Single(spool.Pending()));
        Assert.Empty(Directory.EnumerateFiles(_directory, "*.tmp"));
        Assert.False(spool.TryAppend(Event(3)));
        spool.Acknowledge([observation.EventId]);
        Assert.True(spool.TryAppend(Event(4)));
    }

    [Fact]
    public async Task UnknownRequestTimeDoesNotMaskKnownCurrentWakeDispositionInSameSnapshot() {
        using var store = new LedgerStore(Path.Combine(_directory, "ledger.sqlite"), new(new()));
        await store.InitializeAsync();
        await store.IngestAsync([Event() with { Delivery = new("synthetic-delivery", "requested", null) },
            Event(2) with { Delivery = new("synthetic-delivery", "uncertain", Now.AddDays(-1), CauseCode: "synthetic-error") }], Now);
        var row = Assert.Single(await store.EvaluateAllAsync(Now));
        Assert.Equal("wake-held", row.Current!.ReasonCode);
        Assert.Equal(Now.AddDays(-1), row.Current.SinceUtc);
        Assert.Equal("synthetic-error", row.Deliveries["synthetic-delivery"].Stages["uncertain"].CauseCode);
        Assert.Null(row.Deliveries["synthetic-delivery"].Stages["requested"].AtUtc);
        await store.IngestAsync([Event(3) with { ObservedAtUtc = Now.AddMinutes(1), Delivery = new("synthetic-delivery", "requested", null) }], Now.AddMinutes(1));
        Assert.Equal("wake-held", Assert.Single(await store.EvaluateAllAsync(Now.AddMinutes(1))).Current!.ReasonCode);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task PartialGlobalSourcesRetainIndependentFreshEvidenceAndMissingAssociationRemainsUnknown(bool hasAssociation) {
        using var store = new LedgerStore(Path.Combine(_directory, "ledger.sqlite"), new(new()));
        await store.InitializeAsync();
        var session = Event() with { Delivery = null, Session = new() { TurnOpen = false, Outcome = "completed",
            TurnEndedAtUtc = Now, RepositoryBranchKey = hasAssociation ? "synthetic-branch" : null } };
        var global = Event(2) with { Delivery = null, ItemId = "project-evidence", GlobalWork = new(Now, [], Now, []) };
        await store.IngestAsync([session, global], Now);
        await store.EvaluateAllAsync(Now);
        await store.IngestAsync([global with { EventId = Guid.NewGuid(), Sequence = 3, ObservedAtUtc = Now.AddMinutes(1),
            GlobalWork = new(Now.AddMinutes(1), [], null, []) }], Now.AddMinutes(1));
        var row = Assert.Single(await store.EvaluateAllAsync(Now.AddMinutes(1)));
        Assert.Equal(hasAssociation ? "idle" : "unknown", row.Current!.ReasonCode);
        Assert.Equal(global.EventId, row.PullRequestEvidence!.EventId);
        if (!hasAssociation) Assert.Contains("pull-requests", row.Current.MissingFacts!);
        var local = Event(4) with { Delivery = null, ObservedAtUtc = Now.AddMinutes(2), Work = new() {
            ActiveClaim = true, ClaimsAtUtc = Now.AddMinutes(2), PullRequestsKnown = true, PullRequestsAtUtc = Now.AddMinutes(2) } };
        await store.IngestAsync([local, global with { EventId = Guid.NewGuid(), Sequence = 5, ObservedAtUtc = Now.AddMinutes(3),
            GlobalWork = new(null, [], Now.AddMinutes(3), []) }], Now.AddMinutes(3));
        row = Assert.Single(await store.EvaluateAllAsync(Now.AddMinutes(3)));
        Assert.True(row.Work!.ActiveClaim);
        Assert.Equal(local.EventId, row.ClaimEvidence!.EventId);
    }

    [Fact]
    public async Task RepeatedWorkEvidenceDoesNotExtendInferredReviewTime() {
        using var store = new LedgerStore(Path.Combine(_directory, "ledger.sqlite"), new(new()));
        await store.InitializeAsync();
        var session = Event() with { Delivery = null, Session = new() { Outcome = "completed", TurnEndedAtUtc = Now } };
        var work = Event(2) with { Delivery = null, Work = new() { OpenPullRequests = ["synthetic-pr"], OwnedPullRequests = ["synthetic-pr"], PullRequestsKnown = true, PullRequestsAtUtc = Now } };
        await store.IngestAsync([session, work], Now);
        await store.IngestAsync([work with { EventId = Guid.NewGuid(), Sequence = 3, ObservedAtUtc = Now.AddHours(23) }], Now.AddHours(23));
        var row = Assert.Single(await store.EvaluateAllAsync(Now.AddHours(25)));
        Assert.Null(row.ImplicitReviewWait!.DueAtUtc);
        Assert.Equal(Now.AddHours(24), row.ImplicitReviewWait.ReviewAfterUtc);
        Assert.Equal("wait-review", row.Current!.ReasonCode);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task DelayedHistoricalObservationCannotAgeSilentLastKnownState(bool deliveryHistory) {
        using var store = new LedgerStore(Path.Combine(_directory, "ledger.sqlite"), new(new()));
        await store.InitializeAsync();
        var observation = Event() with { Delivery = null, Session = new() { TurnOpen = true,
            TurnStartedAtUtc = Now, LastActivityAtUtc = Now } };
        await store.IngestAsync([observation], Now);
        if (deliveryHistory) await store.IngestAsync([Event(2) with { Delivery = new("synthetic-delivery", "started", Now) }], Now);
        await store.EvaluateAllAsync(Now.AddMinutes(15), silentMachines: ["synthetic-machine"]);
        var delayed = deliveryHistory
            ? Event(3) with { ObservedAtUtc = Now.AddMinutes(-1), Delivery = new("synthetic-delivery", "requested", Now.AddMinutes(-1)) }
            : observation with { EventId = Guid.NewGuid(), Sequence = 2, ObservedAtUtc = Now.AddMinutes(-1) };
        await store.IngestAsync([delayed], Now.AddMinutes(40));
        var row = Assert.Single(await store.EvaluateAllAsync(Now.AddMinutes(40), silentMachines: ["synthetic-machine"]));
        Assert.Equal("active", row.Current!.ReasonCode); Assert.True(row.Current.LastKnown);
        Assert.Single(await store.DailyAsync(DateOnly.FromDateTime(Now.UtcDateTime), default));
    }

    [Fact]
    public async Task OlderHeartbeatCannotRegressMachineLiveness() {
        using var store = new LedgerStore(Path.Combine(_directory, "ledger.sqlite"), new(new()));
        await store.InitializeAsync();
        var heartbeat = Event() with { Delivery = null, Heartbeat = true, ObservedAtUtc = Now };
        await store.IngestAsync([heartbeat], Now);
        await store.IngestAsync([heartbeat with { EventId = Guid.NewGuid(), Sequence = 2, ObservedAtUtc = Now.AddMinutes(-20) }], Now.AddMinutes(1));
        Assert.Equal(Now, (await store.HeartbeatsAsync(default))[heartbeat.ReporterId]);
    }

    [Fact]
    public void ExplicitNullRuleSettingsProduceTheSettingsValidationError() {
        Directory.CreateDirectory(_directory);
        var path = Path.Combine(_directory, "config.json");
        var node = System.Text.Json.Nodes.JsonNode.Parse(System.Text.Json.JsonSerializer.Serialize(new Hosting.LedgerSettings {
            DataDirectory = _directory, ReadToken = new('r', 32), WriteToken = new('w', 32),
            ExpectedReporters = new() { ["synthetic-machine"] = ["synthetic-reporter"] }
        }, LedgerJson.Options))!;
        node["rules"] = null;
        File.WriteAllText(path, node.ToJsonString());
        Assert.Equal("Invalid ledger settings.", Assert.Throws<InvalidDataException>(() => Hosting.LedgerSettings.Load(path)).Message);
    }

    [Fact]
    public void DuplicateReporterIdentityAcrossMachinesIsRejected() {
        Directory.CreateDirectory(_directory);
        var path = Path.Combine(_directory, "config.json");
        File.WriteAllText(path, System.Text.Json.JsonSerializer.Serialize(new Hosting.LedgerSettings {
            DataDirectory = _directory, ReadToken = new('r', 32), WriteToken = new('w', 32),
            ExpectedReporters = new() { ["machine-one"] = ["same-reporter"], ["machine-two"] = ["same-reporter"] }
        }, LedgerJson.Options));
        Assert.Throws<InvalidDataException>(() => Hosting.LedgerSettings.Load(path));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CentralReadEvidenceSurvivesConsecutiveSnapshotsAndRestartBeforeRemoteItemEvaluation(bool itemLink) {
        var path = Path.Combine(_directory, "ledger.sqlite");
        var global = Event() with { Delivery = null, ItemId = "project-evidence", GlobalWork = new(Now, [], Now,
            itemLink ? [] : new() { ["synthetic-branch"] = ["synthetic-pr"] }, [new("synthetic-pr", "open", Now)],
            itemLink ? new() { ["remote-item"] = new(["synthetic-pr"], true, Now) } : null) };
        var remote = Event(2) with { Delivery = null, MachineId = "remote-machine", ItemId = "remote-item", Session = new() {
            TurnOpen = false, Outcome = "completed", TurnEndedAtUtc = Now.AddMinutes(-1), RepositoryBranchKey = "synthetic-branch" } };
        using (var initial = new LedgerStore(path, new(new()))) {
            await initial.InitializeAsync();
            var ownership = Event(7) with { Delivery = null, MachineId = "remote-machine", ItemId = "remote-item",
                Work = new() { OwnedPullRequests = ["synthetic-pr"] } };
            await initial.IngestAsync([remote, ownership, global, global with { EventId = Guid.NewGuid(), Sequence = 3, ObservedAtUtc = Now.AddMinutes(1),
                GlobalWork = new(Now.AddMinutes(1), [], Now.AddMinutes(1), [], null,
                    itemLink ? new() { ["remote-item"] = new([], true, Now.AddMinutes(1)) } : null) }], Now.AddMinutes(1));
        }
        using var store = new LedgerStore(path, new(new()));
        await store.InitializeAsync();
        var missing = Assert.Single(await store.EvaluateAllAsync(Now.AddMinutes(1)));
        Assert.Equal("waiting-review", missing.Current!.ReasonCode);
        Assert.Null(missing.Work!.PullRequestsKnown);
        Assert.Contains(missing.PullRequestProofEvidence, proof => proof.EventId == global.EventId);
        await store.IngestAsync([global with { EventId = Guid.NewGuid(), Sequence = 4, ObservedAtUtc = Now.AddMinutes(2),
            GlobalWork = new(Now.AddMinutes(2), [], Now.AddMinutes(2), [], [new("synthetic-pr", "closed", Now.AddMinutes(2))]) }], Now.AddMinutes(2));
        var done = Assert.Single(await store.EvaluateAllAsync(Now.AddMinutes(2)));
        Assert.Equal("cancelled", done.Current!.ReasonCode);
        Assert.Equal(Now.AddMinutes(-1), done.Current.SinceUtc);
        await store.IngestAsync([global with { EventId = Guid.NewGuid(), Sequence = 5, ObservedAtUtc = Now.AddMinutes(3),
            GlobalWork = new(Now.AddMinutes(3), [], Now.AddMinutes(3), new() { ["synthetic-branch"] = ["synthetic-pr"] }) }], Now.AddMinutes(3));
        Assert.Equal("waiting-review", Assert.Single(await store.EvaluateAllAsync(Now.AddMinutes(3))).Current!.ReasonCode);
        await store.IngestAsync([remote with { EventId = Guid.NewGuid(), Sequence = 6, ObservedAtUtc = Now.AddMinutes(4),
            Session = remote.Session! with { RepositoryBranchKey = "another-synthetic-branch" } }], Now.AddMinutes(4));
        var changed = Assert.Single(await store.EvaluateAllAsync(Now.AddMinutes(4)));
        Assert.Equal("waiting-review", changed.Current!.ReasonCode);
        Assert.Equal(Now.AddMinutes(3), changed.PositivePullRequests["synthetic-pr"].ObservedAtUtc);
    }

    [Fact]
    public async Task ExactItemLinkOutsideSearchScopeRequiresFreshClosureAndNewerReopenOverridesLocalSnapshot() {
        using var store = new LedgerStore(Path.Combine(_directory, "ledger.sqlite"), new(new()));
        await store.InitializeAsync();
        var session = Event() with { Delivery = null, Session = new() { TurnOpen = false, Outcome = "completed", TurnEndedAtUtc = Now } };
        var global = Event(2) with { Delivery = null, ItemId = "project-evidence", GlobalWork = new(Now, [], Now, [],
            [new("synthetic-pr", "closed", Now)], new() { [session.ItemId] = new(["synthetic-pr"], true, Now) }) };
        await store.IngestAsync([session, global], Now);
        var done = Assert.Single(await store.EvaluateAllAsync(Now));
        Assert.Equal("cancelled", done.Current!.ReasonCode);
        Assert.Contains(done.PullRequestProofEvidence, proof => proof.EventId == global.EventId);
        var aged = Assert.Single(await store.EvaluateAllAsync(Now.AddMinutes(11)));
        Assert.Null(aged.Work!.PullRequestsKnown);
        Assert.Equal("cancelled", aged.Current!.ReasonCode); // Terminal verdict stays sticky until affirmative new work.
        await store.IngestAsync([Event(3) with { Delivery = null, ObservedAtUtc = Now.AddMinutes(12), Work = new() {
            ActiveClaim = false, ClaimsAtUtc = Now.AddMinutes(12), PullRequestsKnown = true, PullRequestsAtUtc = Now.AddMinutes(12), HadPullRequests = true } },
            global with { EventId = Guid.NewGuid(), Sequence = 4, ObservedAtUtc = Now.AddMinutes(13), GlobalWork = new(null, [], null, [],
                [new("synthetic-pr", "open", Now.AddMinutes(13))]) }], Now.AddMinutes(13));
        Assert.Equal("waiting-review", Assert.Single(await store.EvaluateAllAsync(Now.AddMinutes(13))).Current!.ReasonCode);
    }

    [Fact]
    public async Task VersionOneReceiptsRemainIdempotentWhenOptionalFieldsAreAdded() {
        var path = Path.Combine(_directory, "ledger.sqlite");
        var observation = Event();
        using var store = new LedgerStore(path, new(new()));
        await store.InitializeAsync();
        await store.IngestAsync([observation], Now);
        // A retained pre-upgrade receipt has the same v1 meaning, without later optional fields.
        var legacy = System.Text.Json.Nodes.JsonNode.Parse(System.Text.Json.JsonSerializer.Serialize(observation, LedgerJson.Options))!;
        legacy.AsObject().Remove("globalWork"); legacy.AsObject().Remove("reviewMark");
        using var sqlite = new DBAClientX.SQLite();
        sqlite.ExecuteNonQuery(path, "UPDATE events SET json=@json WHERE id=@id", new Dictionary<string, object?> {
            ["json"] = legacy.ToJsonString(), ["id"] = observation.EventId.ToString() });
        Assert.Equal([observation.EventId], await store.IngestAsync([observation], Now.AddMinutes(1)));
        Assert.Single(await store.HistoryAsync(observation.ItemId, 0, 100, default));
    }

    [Fact]
    public async Task UnchangedUnknownPollingPreservesMatchSinceWithoutDatabaseWrites() {
        var path = Path.Combine(_directory, "ledger.sqlite");
        using var store = new LedgerStore(path, new(new()));
        await store.InitializeAsync();
        await store.IngestAsync([Event() with { Delivery = null }], Now);
        var first = Assert.Single(await store.EvaluateAllAsync(Now));
        Assert.Equal("unknown", first.Current!.ReasonCode);
        using var database = new DBAClientX.SQLite();
        await using var observer = await database.OpenSessionAsync(path);
        var before = Convert.ToInt64(await observer.ExecuteScalarAsync("PRAGMA data_version"));
        var later = Assert.Single(await store.EvaluateAllAsync(Now.AddMinutes(1)));
        var after = Convert.ToInt64(await observer.ExecuteScalarAsync("PRAGMA data_version"));
        Assert.Equal(before, after);
        Assert.Equal(first.Current.SinceUtc, Assert.Single(later.Current!.Matches).SinceUtc);
        Assert.Equal(Now.AddMinutes(1), later.Current.EvaluatedAtUtc);
    }

    public void Dispose() { if (Directory.Exists(_directory)) Directory.Delete(_directory, true); }
}
