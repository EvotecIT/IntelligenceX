using IntelligenceX.Ledger.Contracts;
using IntelligenceX.Ledger.Storage;
using Xunit;

namespace IntelligenceX.Ledger.Tests;

public sealed class LedgerScopeRegressionTests : IDisposable {
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "ix-ledger-scope", Guid.NewGuid().ToString("N"));
    private static readonly DateTimeOffset Now = new(2026, 1, 12, 12, 0, 0, TimeSpan.Zero);
    private static readonly TaskScope Scope = new("synthetic-task", 1, "synthetic-turn");
    private static LedgerEvent Event(string item = "synthetic-item", int minutes = 0) => new() {
        EventId = Guid.NewGuid(), Epoch = Guid.NewGuid(), Sequence = 1, ItemId = item,
        MachineId = "synthetic-machine", ReporterId = "synthetic-reporter", Provider = "codex",
        Source = "evidence", ObservedAtUtc = Now.AddMinutes(minutes)
    };
    private static WorkerDeclaration Declaration(string id, string candidate, int minutes) => new() {
        Id = id, EpisodeId = id, Scope = Scope, Kind = "wait", CheckpointRef = "synthetic-checkpoint",
        ConditionRef = "synthetic-condition", CandidateRef = candidate, NextActor = "worker", Blocking = true,
        RecordedAtUtc = Now.AddMinutes(minutes), ReviewAfterUtc = Now.AddHours(1)
    };

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task LateResolutionCannotReplaceAnotherCandidateOrRetryEpisode(bool sameCandidate) {
        using var store = new LedgerStore(Path.Combine(_directory, "ledger.sqlite"), new(new()));
        await store.InitializeAsync();
        var first = Declaration("synthetic-first", "synthetic-candidate-a", 0);
        var second = Declaration("synthetic-second", sameCandidate ? first.CandidateRef! : "synthetic-candidate-b", 1);
        await store.IngestAsync([Event() with { Session = new() { Scope = Scope, TurnId = Scope.TurnId,
            TurnOpen = true, TurnStartedAtUtc = Now.AddMinutes(-5), LastActivityAtUtc = Now } },
            Event() with { Declaration = first }, Event(minutes: 1) with { Declaration = second }], Now.AddMinutes(1));
        var before = Assert.Single(await store.EvaluateAllAsync(Now.AddMinutes(1)));
        var resolution = first with { Id = "synthetic-late-resolution", Resolved = true, RecordedAtUtc = Now.AddMinutes(2) };
        await store.IngestAsync([Event(minutes: 2) with { Declaration = resolution },
            Event(minutes: 3) with { Declaration = second }], Now.AddMinutes(3));
        var after = Assert.Single(await store.EvaluateAllAsync(Now.AddMinutes(3)));
        Assert.Equal("waiting-task", after.Current!.ReasonCode);
        Assert.Equal(before.Current!.EpisodeId, after.Current.EpisodeId);
        Assert.Equal(Now.AddMinutes(1), after.Current.SinceUtc);
        Assert.Contains(after.Declarations.Values, d => d.Fact == second);
        Assert.Equal(5, (await store.HistoryAsync(after.Id, 0, 100, default)).Length);
        var checkpoint = second with { Id = "synthetic-checkpoint-update", RecordedAtUtc = Now.AddMinutes(4) };
        await store.IngestAsync([Event(minutes: 4) with { Declaration = checkpoint },
            Event(minutes: 5) with { Declaration = second }], Now.AddMinutes(5));
        var retained = Assert.Single(await store.EvaluateAllAsync(Now.AddMinutes(5)));
        Assert.Equal(after.Current.EpisodeId, retained.Current!.EpisodeId);
        Assert.Equal(Now.AddMinutes(1), retained.Current.SinceUtc);
        Assert.Equal(2, Assert.Single(retained.Current.Matches).Evidence.Length);
        Assert.Contains(retained.Declarations.Values, d => d.Fact == checkpoint);
    }

    [Theory]
    [InlineData("resolution-first")]
    [InlineData("superseded-checkpoint")]
    public async Task RetryResolutionSurvivesReorderedDeliveryAndOlderEpisodeUpdates(string order) {
        using var store = new LedgerStore(Path.Combine(_directory, "ledger.sqlite"), new(new()));
        await store.InitializeAsync();
        var first = Declaration("synthetic-first", "synthetic-candidate", 0);
        var retry = Declaration("synthetic-retry", "synthetic-candidate", 2);
        var resolution = retry with { Id = "synthetic-resolution", Resolved = true, RecordedAtUtc = Now.AddMinutes(3) };
        await store.IngestAsync([Event() with { Session = new() { Scope = Scope, TurnId = Scope.TurnId,
            TurnOpen = true, TurnStartedAtUtc = Now.AddMinutes(-5), LastActivityAtUtc = Now } },
            Event() with { Declaration = first }], Now);
        var oldUpdate = first with { Id = "synthetic-old-update", RecordedAtUtc = Now.AddMinutes(4) };
        var facts = order == "resolution-first" ? new[] { resolution, retry } : new[] { retry, resolution, oldUpdate };
        foreach (var fact in facts)
            await store.IngestAsync([Event(minutes: 5) with { Declaration = fact }], Now.AddMinutes(5));
        await store.IngestAsync([Event(minutes: 6) with { Declaration = resolution }], Now.AddMinutes(6));
        var current = Assert.Single(await store.EvaluateAllAsync(Now.AddMinutes(6)));
        Assert.Equal("active", current.Current!.ReasonCode);
        Assert.DoesNotContain(current.Current.Matches, match => match.ReasonCode == "waiting-task");
        Assert.Equal(resolution, current.Declarations.Values.Single(d => d.Fact.EpisodeId == retry.EpisodeId).Fact);
    }

    [Fact]
    public async Task ReorderedCheckpointKeepsLatestStateAndRepairsOriginalEpisodeEvidence() {
        var path = Path.Combine(_directory, "ledger.sqlite");
        var opening = Event() with { Declaration = Declaration("synthetic-opening", "synthetic-candidate", 0) };
        var checkpoint = opening.Declaration! with { Id = "synthetic-checkpoint", RecordedAtUtc = Now.AddMinutes(2),
            ReviewAfterUtc = Now.AddHours(2) };
        using (var store = new LedgerStore(path, new(new()))) {
            await store.InitializeAsync();
            await store.IngestAsync([Event() with { Session = new() { Scope = Scope, TurnId = Scope.TurnId,
                TurnOpen = true, TurnStartedAtUtc = Now.AddMinutes(-5), LastActivityAtUtc = Now } },
                Event(minutes: 2) with { Declaration = checkpoint }, opening], Now.AddMinutes(3));
        }
        using var restarted = new LedgerStore(path, new(new()));
        await restarted.InitializeAsync();
        var current = Assert.Single(await restarted.EvaluateAllAsync(Now.AddMinutes(3)));
        Assert.Equal("waiting-task", current.Current!.ReasonCode);
        Assert.Equal(Now, current.Current.SinceUtc);
        var declaration = Assert.Single(current.Declarations.Values);
        Assert.Equal(checkpoint, declaration.Fact);
        Assert.Equal(opening.EventId, declaration.EpisodeEvidence!.EventId);
        Assert.Contains(Assert.Single(current.Current.Matches).Evidence, e => e.EventId == opening.EventId);
    }

    [Theory]
    [InlineData("merged")]
    [InlineData("closed")]
    public async Task QualifiedLocalOpenPrRetiresOnNewerExactTerminalProof(string terminal) {
        using var store = new LedgerStore(Path.Combine(_directory, "ledger.sqlite"), new(new()));
        await store.InitializeAsync();
        await store.IngestAsync([Event() with { Session = new() { Scope = Scope, TurnId = Scope.TurnId,
            TurnOpen = false, Outcome = "completed", TurnStartedAtUtc = Now.AddMinutes(-5), TurnEndedAtUtc = Now } },
            Event() with { Work = new() { Scope = Scope, ActiveClaim = false, ClaimsAtUtc = Now,
                OwnedPullRequests = ["synthetic-pr"], OpenPullRequests = ["synthetic-pr"],
                PullRequestsKnown = true, PullRequestsAtUtc = Now } }], Now);
        Assert.Equal("waiting-review", Assert.Single(await store.EvaluateAllAsync(Now)).Current!.ReasonCode);
        await store.IngestAsync([Event("project-evidence", 1) with {
            GlobalWork = new(Now.AddMinutes(1), [], Now.AddMinutes(1), [],
                [new("synthetic-pr", terminal, Now.AddMinutes(1))])
        }], Now.AddMinutes(1));
        var current = Assert.Single(await store.EvaluateAllAsync(Now.AddMinutes(1)));
        Assert.Empty(current.Work!.OpenPullRequests);
        Assert.Empty(current.InferredReviewWaits);
        Assert.Equal("done", current.Current!.Value);
        Assert.Equal(terminal == "closed" ? "cancelled" : "complete", current.Current.ReasonCode);
    }

    [Fact]
    public async Task SessionAssociationsCannotBecomeOwnershipForAnotherTaskGeneration() {
        using var store = new LedgerStore(Path.Combine(_directory, "ledger.sqlite"), new(new()));
        await store.InitializeAsync();
        LedgerEvent Links(TaskScope? scope, string[] ids, int minutes) => Event("project-evidence", minutes) with {
            GlobalWork = new(Now.AddMinutes(minutes), [], Now.AddMinutes(minutes), [],
                [new("synthetic-old-pr", "open", Now.AddMinutes(minutes)), new("synthetic-new-pr", "open", Now.AddMinutes(minutes))],
                new() { ["synthetic-item"] = new(ids, true, Now.AddMinutes(minutes), scope) })
        };
        await store.IngestAsync([Event() with { Session = new() { Scope = Scope, TurnId = Scope.TurnId,
            TurnOpen = false, Outcome = "completed", TurnStartedAtUtc = Now.AddMinutes(-5), TurnEndedAtUtc = Now } },
            Links(Scope, ["synthetic-old-pr"], 0)], Now);
        Assert.Equal("waiting-review", Assert.Single(await store.EvaluateAllAsync(Now)).Current!.ReasonCode);
        var next = new TaskScope("synthetic-next-task", 2, "synthetic-next-turn");
        await store.IngestAsync([Event(minutes: 2) with { Session = new() { Scope = next, TurnId = next.TurnId,
            TurnOpen = false, Outcome = "completed", TurnStartedAtUtc = Now.AddMinutes(1), TurnEndedAtUtc = Now.AddMinutes(2) } },
            Event(minutes: 3) with { Work = new() { Scope = next, ActiveClaim = false, ClaimsAtUtc = Now.AddMinutes(3),
                PullRequestsKnown = true, PullRequestsAtUtc = Now.AddMinutes(3) } },
            Links(null, ["synthetic-old-pr"], 4),
            Event(minutes: 4) with { Work = new() { OwnedPullRequests = ["synthetic-old-pr"] } }], Now.AddMinutes(4));
        var current = Assert.Single(await store.EvaluateAllAsync(Now.AddMinutes(4)));
        Assert.Equal("idle", current.Current!.ReasonCode);
        Assert.Empty(current.Work!.OwnedPullRequests);
        Assert.Empty(current.InferredReviewWaits);
        Assert.Contains("synthetic-old-pr", current.LinkedPullRequestIds);
        await store.IngestAsync([Links(next, ["synthetic-new-pr"], 5)], Now.AddMinutes(5));
        var owned = Assert.Single(await store.EvaluateAllAsync(Now.AddMinutes(5)));
        Assert.Equal("waiting-review", owned.Current!.ReasonCode);
        Assert.Equal(["synthetic-new-pr"], owned.Work!.OwnedPullRequests);
        Assert.Equal("synthetic-new-pr", owned.ImplicitReviewWait!.ConditionId);
    }

    [Theory]
    [InlineData("task")]
    [InlineData("turn")]
    [InlineData("native-unconfirmed")]
    public async Task OptionalInputFollowsParentSupersessionAndKeepsHistoryAcrossRestart(string mode) {
        var path = Path.Combine(_directory, "ledger.sqlite");
        var parent = Event("synthetic-parent") with { Session = new() {
            Scope = mode == "native-unconfirmed" ? null : Scope, TurnId = Scope.TurnId, TurnOpen = true,
            TurnStartedAtUtc = Now.AddMinutes(-5), LastActivityAtUtc = Now
        } };
        var question = Declaration("synthetic-question", "synthetic-candidate", -2) with {
            Kind = "question", NextActor = "human", Blocking = false, CandidateRef = null
        };
        var child = Event("synthetic-child") with { ParentItemId = parent.ItemId, ItemKind = "input",
            Declaration = mode == "native-unconfirmed" ? null : question,
            Session = mode == "native-unconfirmed" ? new() { InputId = "synthetic-input", InputUnconfirmed = true,
                TurnId = Scope.TurnId, QuestionAtUtc = Now.AddMinutes(-2) } : null
        };
        using (var initial = new LedgerStore(path, new(new()))) {
            await initial.InitializeAsync();
            await initial.IngestAsync([parent, child], Now);
            Assert.Equal(mode == "native-unconfirmed" ? "unknown" : "question",
                (await initial.EvaluateAllAsync(Now)).Single(i => i.Id == child.ItemId).Current!.ReasonCode);
            var next = mode == "task" ? new TaskScope("synthetic-next-task", 2, "synthetic-next-turn") : Scope with { TurnId = "synthetic-next-turn" };
            await initial.IngestAsync([parent with { EventId = Guid.NewGuid(), Epoch = Guid.NewGuid(), ObservedAtUtc = Now.AddMinutes(2),
                Session = parent.Session! with { Scope = mode == "native-unconfirmed" ? null : next, TurnId = next.TurnId,
                    TurnStartedAtUtc = Now.AddMinutes(1), LastActivityAtUtc = Now.AddMinutes(2) } }], Now.AddMinutes(2));
            var cancelled = (await initial.EvaluateAllAsync(Now.AddMinutes(2))).Single(i => i.Id == child.ItemId);
            Assert.Equal("done", cancelled.Current!.Value);
            Assert.Equal("cancelled", cancelled.Current.ReasonCode);
            Assert.NotNull(cancelled.Supersession);
            Assert.Single(await initial.HistoryAsync(child.ItemId, 0, 100, default));
        }
        using var restarted = new LedgerStore(path, new(new()));
        await restarted.InitializeAsync();
        var retained = (await restarted.EvaluateAllAsync(Now.AddMinutes(3))).Single(i => i.Id == child.ItemId);
        Assert.Equal("cancelled", retained.Current!.ReasonCode);
        Assert.Equal("suppressed", retained.Current.WakeEligibility!.State);
    }

    [Fact]
    public async Task UnqualifiedDimensionsCannotBorrowOrClearAnotherDimensionsTaskScope() {
        using var store = new LedgerStore(Path.Combine(_directory, "ledger.sqlite"), new(new()));
        await store.InitializeAsync();
        await store.IngestAsync([Event() with { Session = new() { Scope = Scope, TurnId = Scope.TurnId, AppOwned = true,
            TurnOpen = false, Outcome = "completed", TurnEndedAtUtc = Now } },
            Event() with { Work = new() { Scope = Scope, ActiveClaim = true, ClaimsAtUtc = Now,
                PullRequestsKnown = true, PullRequestsAtUtc = Now } },
            Event(minutes: 1) with { Work = new() { GoalHeld = true, NeedsApp = true,
                Wait = new("ci", "synthetic-condition", Now, ReviewAfterUtc: Now.AddHours(1)) } }], Now.AddMinutes(1));
        var unknown = Assert.Single(await store.EvaluateAllAsync(Now.AddMinutes(1)));
        Assert.Equal("unknown", unknown.Current!.ReasonCode);
        Assert.Contains("goal-task-scope", unknown.Current.MissingFacts!);
        await store.IngestAsync([Event(minutes: -1) with { Work = new() { Scope = Scope, GoalHeld = true,
                GoalHeldAtUtc = Now.AddMinutes(-2) } },
            Event(minutes: 2) with { Work = new() { GoalHeld = false } }], Now.AddMinutes(2));
        var qualified = Assert.Single(await store.EvaluateAllAsync(Now.AddMinutes(2)));
        Assert.Equal("goal-held", qualified.Current!.ReasonCode);
        Assert.True(qualified.ReportedWork!.GoalHeld);
        Assert.Equal(Scope, qualified.WorkDimensionScopes["goal"]);
    }

    public void Dispose() {
        if (Directory.Exists(_directory)) Directory.Delete(_directory, true);
    }
}
