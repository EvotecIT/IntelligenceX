using IntelligenceX.Ledger.Context;
using IntelligenceX.Ledger.Contracts;
using IntelligenceX.Ledger.Hosting;
using IntelligenceX.Ledger.Storage;
using Xunit;

namespace IntelligenceX.Ledger.Tests;

public sealed class EndedSessionTests {
    private static readonly DateTimeOffset Now = new(2026, 1, 12, 12, 0, 0, TimeSpan.Zero);

    [Theory]
    [InlineData("current")]
    [InlineData("resolved")]
    [InlineData("previous-generation")]
    public async Task CompletedRosterPreservesOnlyCurrentUnresolvedSafety(string scenario) {
        var root = LedgerTestPaths.NewDirectory("ended-safety");
        try {
            using var store = new LedgerStore(Path.Combine(root, "ledger.sqlite"), new(new()));
            await store.InitializeAsync();
            var scope = new TaskScope("synthetic-task", 2);
            var observation = Observation(new() { Scope = scope, TurnOpen = false, Outcome = "completed",
                TurnEndedAtUtc = Now, LastActivityAtUtc = Now }, new() { Scope = scope, ActiveClaim = false, PullRequestsKnown = true });
            await store.IngestAsync([observation, Heartbeat(observation, 2), observation with {
                EventId = Guid.NewGuid(), Sequence = 3, Session = null, Work = null,
                Safety = new(scenario == "previous-generation" ? scope with { Generation = 1 } : scope,
                    "synthetic-safety", "synthetic-case", "synthetic-policy", 1, "synthetic-evidence", Now,
                    Resolved: scenario == "resolved")
            }], Now);
            var view = await Runtime(store, root).WorkAsync(new ProjectContextService(null), Now, default);
            if (scenario == "current") {
                var row = Assert.Single(view.Items).Item;
                Assert.Equal("needs-you", row.Verdict);
                Assert.Equal("urgent-safety", row.ReasonCode);
            } else Assert.Empty(view.Items);
        } finally { if (Directory.Exists(root)) Directory.Delete(root, recursive: true); }
    }

    [Fact]
    public async Task CurrentWorkSeparatesFrozenImportHistoryFromFreshDeliveryHolds() {
        var root = LedgerTestPaths.NewDirectory("delivery-roster");
        try {
            using var store = new LedgerStore(Path.Combine(root, "ledger.sqlite"), new(new()));
            await store.InitializeAsync();
            var observation = Observation(new(), new() { ActiveClaim = false });
            var history = observation with { ItemId = "synthetic-old-delivery", ItemKind = "delivery", Session = null,
                Work = null, Source = "evidence", ObservedAtUtc = Now.AddDays(-2),
                Delivery = new("synthetic-old", "held", Now.AddDays(-2)) };
            var fresh = history with { EventId = Guid.NewGuid(), Sequence = 2, ItemId = "synthetic-fresh-delivery",
                ObservedAtUtc = Now, Delivery = new("synthetic-fresh", "held", Now) };
            await store.IngestAsync([history, fresh, Heartbeat(observation, 3)], Now);
            var view = await Runtime(store, root).WorkAsync(new ProjectContextService(null), Now, default);
            Assert.Equal("synthetic-fresh-delivery", Assert.Single(view.Items).Item.Id);
            Assert.Equal(1, view.Counts["stuck"]);
            Assert.Contains("imported-delivery-history-older-than-24h:1", view.Coverage);
            var retained = Assert.Single(await store.EvaluateAllAsync(Now), item => item.Id == history.ItemId);
            Assert.Equal("wake-held", retained.Current!.ReasonCode);
            Assert.Equal("known", retained.Deliveries["synthetic-old"].Stages["held"].Knowledge);
        } finally { if (Directory.Exists(root)) Directory.Delete(root, recursive: true); }
    }

    [Theory]
    [InlineData(true)]
    [InlineData(null)]
    public async Task NormalCompletionLeavesCurrentWorkUntilUnconfirmedInputReopensFollowUp(bool? pullRequestsKnown) {
        var root = LedgerTestPaths.NewDirectory("ended-session");
        try {
            using var store = new LedgerStore(Path.Combine(root, "ledger.sqlite"), new(new()));
            await store.InitializeAsync();
            var observation = Observation(new() {
                TurnId = "synthetic-turn", TurnOpen = true, Archived = false, TurnStartedAtUtc = Now.AddHours(-1), LastActivityAtUtc = Now
            }, new() { ActiveClaim = false, PullRequestsKnown = pullRequestsKnown });
            await store.IngestAsync([observation, Heartbeat(observation, 2)], Now);
            var runtime = Runtime(store, root);
            var context = new ProjectContextService(null);
            Assert.Equal("working", Assert.Single((await runtime.WorkAsync(context, Now, default)).Items).Item.Verdict);

            var ended = Now.AddMinutes(1);
            await store.IngestAsync([observation with {
                EventId = Guid.NewGuid(), Sequence = 3, ObservedAtUtc = ended,
                Session = observation.Session! with { TurnOpen = false, Outcome = "completed", TurnEndedAtUtc = ended, LastActivityAtUtc = ended }
            }], ended);
            runtime.InvalidateWork();

            var view = await runtime.WorkAsync(context, ended, default);
            Assert.Empty(view.Items);
            Assert.Empty(view.Counts);
            var retained = Assert.Single(await store.EvaluateAllAsync(ended));
            Assert.Equal(pullRequestsKnown == true ? "done" : "stuck", retained.Current!.Value);
            Assert.Equal(pullRequestsKnown == true ? "idle" : "unknown", retained.Current.ReasonCode);
            if (pullRequestsKnown != true) {
                Assert.NotNull(retained.Current.MissingFacts);
                Assert.Equal(["pull-requests"], retained.Current.MissingFacts!);
            }
            Assert.Equal(ended, retained.Current.SinceUtc);
            Assert.False(retained.Session!.Archived);
            var followUpAt = ended.AddMinutes(1);
            if (pullRequestsKnown == true) {
                var stalePrAt = ended.AddHours(2);
                await store.IngestAsync([
                    Heartbeat(observation, 4) with { ObservedAtUtc = stalePrAt },
                    observation with { EventId = Guid.NewGuid(), Sequence = 5, ObservedAtUtc = stalePrAt, Session = null,
                        Work = new() { ActiveClaim = false, ClaimsAtUtc = stalePrAt } }
                ], stalePrAt);
                runtime.InvalidateWork();
                Assert.Empty((await runtime.WorkAsync(context, stalePrAt, default)).Items);
                var historical = Assert.Single(await store.EvaluateAllAsync(stalePrAt));
                Assert.Equal("done", historical.Current!.Value);
                Assert.Null(historical.Work!.PullRequestsKnown);
                followUpAt = stalePrAt.AddMinutes(1);
            }
            await store.IngestAsync([observation with { EventId = Guid.NewGuid(), Sequence = 6, ObservedAtUtc = followUpAt,
                Session = retained.Session with { InputUnconfirmed = true } }], followUpAt);
            runtime.InvalidateWork();
            var followUp = Assert.Single((await runtime.WorkAsync(context, followUpAt, default)).Items).Item;
            Assert.Equal("stuck", followUp.Verdict);
            Assert.Equal("unknown", followUp.ReasonCode);
            Assert.Equal(["input-status"], followUp.MissingFacts);
        } finally { if (Directory.Exists(root)) Directory.Delete(root, recursive: true); }
    }

    [Theory]
    [InlineData("interrupted", "stuck", "interrupted")]
    [InlineData("failed", "stuck", "interrupted")]
    [InlineData("active-claim", "stuck", "unknown", false)]
    [InlineData("open-pr", "waiting", "waiting-review", false)]
    [InlineData("wait", "waiting", "waiting-ci")]
    [InlineData("question", "needs-you", "question")]
    [InlineData("usage-limit", "needs-you", "usage-limit")]
    [InlineData("unknown-turn", "stuck", "unknown")]
    [InlineData("archive-only", "stuck", "unknown", false)]
    [InlineData("unknown-outcome", "stuck", "unknown")]
    [InlineData("unknown-claim", "stuck", "unknown", false)]
    [InlineData("recorded-pr", "stuck", "unknown", false)]
    [InlineData("pending-delivery", "stuck", "unknown")]
    [InlineData("archived-claim", "stuck", "unknown", false)]
    [InlineData("queued-input", "stuck", "unknown")]
    [InlineData("unconfirmed-input", "stuck", "unknown")]
    [InlineData("reporter-silent", "done", "idle", false)]
    public async Task SessionRosterRetainsExplicitFollowUpAndHistoricalUnknownFacts(string scenario, string verdict, string reason, bool visible = true) {
        var root = LedgerTestPaths.NewDirectory("ended-session-hold");
        try {
            using var store = new LedgerStore(Path.Combine(root, "ledger.sqlite"), new(new()));
            await store.InitializeAsync();
            var session = new SessionFact {
                TurnId = "synthetic-turn", TurnOpen = false, Outcome = "completed", Archived = false,
                TurnStartedAtUtc = Now.AddHours(-2), TurnEndedAtUtc = Now.AddHours(-1), LastActivityAtUtc = Now.AddHours(-1)
            };
            var work = new WorkFact { ActiveClaim = false, PullRequestsKnown = true };
            switch (scenario) {
                case "interrupted":
                case "failed": session = session with { Outcome = scenario }; break;
                case "active-claim": work = work with { ActiveClaim = true }; break;
                case "open-pr": work = work with { OpenPullRequests = ["synthetic-pr"], OwnedPullRequests = ["synthetic-pr"] }; break;
                case "wait": work = work with { Wait = new("ci", "synthetic-check", Now.AddHours(-1), Now.AddHours(1)) }; break;
                case "question": session = session with { Question = true, InputId = "synthetic-question", QuestionAtUtc = Now.AddHours(-1) }; break;
                case "usage-limit": session = session with { UsageLimit = true }; break;
                case "unknown-turn": session = session with { TurnOpen = null, Outcome = "unknown" }; break;
                case "archive-only": session = session with { TurnId = null, TurnOpen = null, Outcome = "unknown", Archived = true,
                    TurnStartedAtUtc = null, TurnEndedAtUtc = null, LastActivityAtUtc = null, ArchiveObservedAtUtc = Now }; break;
                case "unknown-outcome": session = session with { Outcome = "unknown" }; break;
                case "unknown-claim": work = work with { ActiveClaim = null }; break;
                case "recorded-pr": work = work with { PullRequestsKnown = null, HadPullRequests = true }; break;
                case "pending-delivery": work = work with { PullRequestsKnown = null }; break;
                case "archived-claim":
                    work = work with { ActiveClaim = true };
                    session = session with { Archived = true, ArchiveObservedAtUtc = Now };
                    break;
                case "queued-input": work = work with { QueuedInputs = 1, OldestQueuedAtUtc = Now.AddMinutes(-5) }; break;
                case "unconfirmed-input": session = session with { InputUnconfirmed = true }; break;
            }
            // A claim keeps the archive-only observation visible without supplying terminal evidence.
            if (scenario == "archive-only") work = work with { ActiveClaim = true };
            var observation = Observation(session, work);
            await store.IngestAsync(scenario == "reporter-silent" ? [observation] : [observation, Heartbeat(observation, 2)], Now);
            if (scenario == "pending-delivery") await store.IngestAsync([observation with {
                EventId = Guid.NewGuid(), Sequence = 3, Session = null, Work = null,
                Delivery = new("synthetic-delivery", "requested", Now)
            }], Now);

            var view = await Runtime(store, root).WorkAsync(new ProjectContextService(null), Now, default);
            if (!visible) {
                Assert.Empty(view.Items);
                Assert.Empty(view.Counts);
            }
            var row = visible ? Assert.Single(view.Items).Item : LedgerItemView.From(Assert.Single(await store.EvaluateAllAsync(Now)));
            Assert.Equal(verdict, row.Verdict);
            Assert.Equal(reason, row.ReasonCode);
            if (visible) Assert.Equal(1, view.Counts[verdict]);
            if (scenario == "unconfirmed-input") Assert.True(row.Session!.InputUnconfirmed);
            if (scenario == "reporter-silent") Assert.Equal("stuck", Assert.Single(view.Machines).Verdict!.Value);
        } finally { if (Directory.Exists(root)) Directory.Delete(root, recursive: true); }
    }

    private static LedgerEvent Observation(SessionFact session, WorkFact work) => new() {
        EventId = Guid.NewGuid(), Epoch = Guid.NewGuid(), Sequence = 1,
        ReporterId = "synthetic-reporter", MachineId = "synthetic-machine", ItemId = "synthetic-item",
        Provider = "codex", Source = "watcher", ObservedAtUtc = Now, Session = session,
        Work = work with { ClaimsAtUtc = work.ActiveClaim.HasValue ? Now : null, PullRequestsAtUtc = work.PullRequestsKnown == true ? Now : null }
    };

    private static LedgerEvent Heartbeat(LedgerEvent observation, long sequence) => observation with {
        EventId = Guid.NewGuid(), Sequence = sequence, ItemId = "machine", Provider = "machine", Session = null, Work = null, Heartbeat = true
    };

    private static LedgerRuntime Runtime(LedgerStore store, string root) => new(store, new() {
        DataDirectory = root, ReadToken = new('r', 32), WriteToken = new('w', 32),
        ExpectedReporters = new() { ["synthetic-machine"] = ["synthetic-reporter"] }
    });
}
