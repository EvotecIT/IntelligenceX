using IntelligenceX.Ledger.Context;
using IntelligenceX.Ledger.Contracts;
using IntelligenceX.Ledger.Hosting;
using IntelligenceX.Ledger.Storage;
using Xunit;

namespace IntelligenceX.Ledger.Tests;

public sealed class EndedSessionTests {
    private static readonly DateTimeOffset Now = new(2026, 1, 12, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task NormalCompletionLeavesCurrentWorkUntilUnconfirmedInputReopensFollowUp() {
        var root = LedgerTestPaths.NewDirectory("ended-session");
        try {
            using var store = new LedgerStore(Path.Combine(root, "ledger.sqlite"), new(new()));
            await store.InitializeAsync();
            var observation = Observation(new() {
                TurnId = "synthetic-turn", TurnOpen = true, Archived = false, TurnStartedAtUtc = Now.AddHours(-1), LastActivityAtUtc = Now
            }, new() { ActiveClaim = false, PullRequestsKnown = true });
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
            Assert.Equal("done", retained.Current!.Value);
            Assert.Equal("idle", retained.Current.ReasonCode);
            Assert.Equal(ended, retained.Current.SinceUtc);
            Assert.False(retained.Session!.Archived);
            await store.IngestAsync([observation with { EventId = Guid.NewGuid(), Sequence = 4, ObservedAtUtc = ended.AddMinutes(1),
                Session = retained.Session with { InputUnconfirmed = true } }], ended.AddMinutes(1));
            runtime.InvalidateWork();
            var followUp = Assert.Single((await runtime.WorkAsync(context, ended.AddMinutes(1), default)).Items).Item;
            Assert.Equal("stuck", followUp.Verdict);
            Assert.Equal("unknown", followUp.ReasonCode);
            Assert.Equal(["input-status"], followUp.MissingFacts);
        } finally { if (Directory.Exists(root)) Directory.Delete(root, recursive: true); }
    }

    [Theory]
    [InlineData("interrupted", "stuck", "interrupted")]
    [InlineData("failed", "stuck", "interrupted")]
    [InlineData("active-claim", "stuck", "unknown")]
    [InlineData("open-pr", "waiting", "waiting-review")]
    [InlineData("wait", "waiting", "waiting-ci")]
    [InlineData("question", "needs-you", "question")]
    [InlineData("usage-limit", "needs-you", "usage-limit")]
    [InlineData("unknown-turn", "stuck", "unknown")]
    [InlineData("archive-only", "stuck", "unknown")]
    [InlineData("unknown-outcome", "stuck", "unknown")]
    [InlineData("unknown-claim", "stuck", "unknown")]
    [InlineData("unknown-prs", "stuck", "unknown")]
    [InlineData("queued-input", "stuck", "unknown")]
    [InlineData("unconfirmed-input", "stuck", "unknown")]
    [InlineData("reporter-silent", "done", "idle")]
    public async Task EndedSessionCloseoutPreservesOutstandingWorkAndUnknownFacts(string scenario, string verdict, string reason) {
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
                case "archive-only": session = session with { TurnOpen = null, Outcome = "unknown", Archived = true, ArchiveObservedAtUtc = Now }; break;
                case "unknown-outcome": session = session with { Outcome = "unknown" }; break;
                case "unknown-claim": work = work with { ActiveClaim = null }; break;
                case "unknown-prs": work = work with { PullRequestsKnown = null }; break;
                case "queued-input": work = work with { QueuedInputs = 1, OldestQueuedAtUtc = Now.AddMinutes(-5) }; break;
                case "unconfirmed-input": session = session with { InputUnconfirmed = true }; break;
            }
            // A claim keeps the archive-only observation visible without supplying terminal evidence.
            if (scenario == "archive-only") work = work with { ActiveClaim = true };
            var observation = Observation(session, work);
            await store.IngestAsync(scenario == "reporter-silent" ? [observation] : [observation, Heartbeat(observation, 2)], Now);

            var view = await Runtime(store, root).WorkAsync(new ProjectContextService(null), Now, default);
            var row = Assert.Single(view.Items).Item;
            Assert.Equal(verdict, row.Verdict);
            Assert.Equal(reason, row.ReasonCode);
            Assert.Equal(1, view.Counts[verdict]);
            if (scenario == "unconfirmed-input") Assert.True(row.Session!.InputUnconfirmed);
            if (scenario == "reporter-silent") Assert.True(row.LastKnown);
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
