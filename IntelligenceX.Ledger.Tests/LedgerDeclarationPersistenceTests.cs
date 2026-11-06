using IntelligenceX.Ledger.Contracts;
using IntelligenceX.Ledger.Storage;
using Xunit;

namespace IntelligenceX.Ledger.Tests;

public sealed class LedgerDeclarationPersistenceTests : IDisposable {
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "ix-ledger-declarations", Guid.NewGuid().ToString("N"));
    private static readonly DateTimeOffset Now = new(2026, 1, 12, 12, 0, 0, TimeSpan.Zero);
    private static readonly TaskScope Scope = new("synthetic-task", 1, "synthetic-turn");
    private static WorkerDeclaration Declaration() => new() {
        Id = "synthetic-declaration", EpisodeId = "synthetic-episode", Scope = Scope, Kind = "wait", CheckpointRef = "synthetic-checkpoint",
        ConditionRef = "synthetic-condition", NextActor = "dependency", Blocking = true,
        RecordedAtUtc = Now, ReviewAfterUtc = Now.AddHours(1)
    };
    private static LedgerEvent Event(long sequence) => new() {
        EventId = Guid.NewGuid(), Epoch = Guid.NewGuid(), Sequence = sequence, MachineId = "synthetic-machine",
        ReporterId = "synthetic-reporter", ItemId = "synthetic-item", Provider = "codex", Source = "evidence", ObservedAtUtc = Now
    };

    [Fact]
    public async Task DeclarationIdentitySurvivesRestartAndConflictsRollbackWholeBatch() {
        var path = Path.Combine(_directory, "ledger.sqlite");
        var first = Event(1) with { Declaration = Declaration() };
        using (var initial = new LedgerStore(path, new(new()))) {
            await initial.InitializeAsync();
            await initial.IngestAsync([first], Now);
        }
        using var store = new LedgerStore(path, new(new()));
        await store.InitializeAsync();
        await store.IngestAsync([Event(2) with { Declaration = first.Declaration }], Now.AddMinutes(5));
        var item = Assert.Single(await store.EvaluateAllAsync(Now.AddMinutes(5)));
        Assert.Equal("waiting-task", item.Current!.ReasonCode);
        Assert.Equal(Now, item.Current.SinceUtc);
        Assert.Equal(first.EventId, Assert.Single(item.Declarations.Values).Evidence.EventId);
        Assert.Equal(Now.AddHours(1), Assert.Single(item.Declarations.Values).Fact.ReviewAfterUtc);
        var changed = Event(3) with { Declaration = first.Declaration! with { ReviewAfterUtc = Now.AddHours(2) } };
        var unrelated = Event(4) with { ItemId = "synthetic-other-item", Session = new() { TurnOpen = true, LastActivityAtUtc = Now } };
        await Assert.ThrowsAsync<EventConflictException>(() => store.IngestAsync([unrelated, changed], Now.AddMinutes(6)));
        Assert.Single(await store.EvaluateAllAsync(Now.AddMinutes(6)));
        Assert.Equal(2, (await store.HistoryAsync(first.ItemId, 0, 100, default)).Length);
    }

    [Fact]
    public async Task DelayedOldTaskFactsCannotResurrectGoalPauseOrUsageFailure() {
        using var store = new LedgerStore(Path.Combine(_directory, "ledger.sqlite"), new(new()));
        await store.InitializeAsync();
        var old = Event(1) with { Session = new() { Scope = Scope, TurnId = Scope.TurnId, TurnOpen = false,
            Outcome = "failed", UsageLimit = true, AccountId = "synthetic-account", TurnStartedAtUtc = Now.AddMinutes(-1), TurnEndedAtUtc = Now } };
        await store.IngestAsync([old, Event(2) with { Control = new(Scope, "synthetic-stop", "stopped", Now) }], Now);
        var successor = new TaskScope("synthetic-new-task", 2, "synthetic-new-turn");
        var newer = Event(3) with { ObservedAtUtc = Now.AddMinutes(2), Session = new() { Scope = successor,
            TurnId = successor.TurnId, TurnOpen = true, TurnStartedAtUtc = Now.AddMinutes(1), LastActivityAtUtc = Now.AddMinutes(2) } };
        await store.IngestAsync([newer,
            Event(4) with { ObservedAtUtc = Now.AddMinutes(3), Work = new() { Scope = Scope, GoalHeld = true, ActiveClaim = true } },
            old with { EventId = Guid.NewGuid(), Sequence = 5, ObservedAtUtc = Now.AddMinutes(3) },
            Event(6) with { ObservedAtUtc = Now.AddMinutes(3), Declaration = Declaration() }], Now.AddMinutes(3));
        var item = Assert.Single(await store.EvaluateAllAsync(Now.AddMinutes(3)));
        Assert.Equal(successor, item.Scope);
        Assert.Equal("working", item.Current!.Value);
        Assert.Equal("active", item.Current.ReasonCode);
        Assert.False(item.Session!.UsageLimit);
        Assert.Null(item.Control);
        Assert.Empty(item.Declarations);
        Assert.Equal(6, (await store.HistoryAsync(item.Id, 0, 100, default)).Length);
    }

    [Fact]
    public async Task NewCandidateSupersedesOldConditionWithoutRenewingItsDeclaration() {
        using var store = new LedgerStore(Path.Combine(_directory, "ledger.sqlite"), new(new()));
        await store.InitializeAsync();
        var old = Declaration() with { CandidateRef = "synthetic-old-candidate", ReviewAfterUtc = Now.AddMinutes(1) };
        await store.IngestAsync([Event(1) with { Declaration = old }], Now);
        var newer = Declaration() with { Id = "synthetic-new-declaration", ConditionRef = "synthetic-new-condition", Kind = "operation",
            CandidateRef = "synthetic-new-candidate", RecordedAtUtc = Now.AddMinutes(2) };
        await store.IngestAsync([Event(2) with { Declaration = newer, ObservedAtUtc = Now.AddMinutes(2) },
            Event(3) with { Declaration = old, ObservedAtUtc = Now.AddMinutes(3) }], Now.AddMinutes(3));
        var row = Assert.Single(await store.EvaluateAllAsync(Now.AddMinutes(3)));
        Assert.Equal("synthetic-new-candidate", row.CandidateRef);
        Assert.Equal("working", row.Current!.Value);
        Assert.DoesNotContain(row.Current.Matches, m => m.ReasonCode == "wait-review");
        Assert.Equal(2, row.Declarations.Count);
        await store.IngestAsync([Event(4) with { ObservedAtUtc = Now.AddMinutes(4), Declaration = old with {
            Id = "synthetic-old-resolution", Resolved = true, RecordedAtUtc = Now.AddMinutes(4) } }], Now.AddMinutes(4));
        var resolvedOld = Assert.Single(await store.EvaluateAllAsync(Now.AddMinutes(4)));
        Assert.Equal("synthetic-new-candidate", resolvedOld.CandidateRef);
        Assert.Equal("working", resolvedOld.Current!.Value);
    }

    [Fact]
    public async Task ClosingOneOwnedPullRequestPreservesOtherReviewEpisode() {
        using var store = new LedgerStore(Path.Combine(_directory, "ledger.sqlite"), new(new()));
        await store.InitializeAsync();
        var session = Event(1) with { Session = new() { Outcome = "completed", TurnOpen = false, TurnEndedAtUtc = Now } };
        var work = Event(2) with { Work = new() { PullRequestsKnown = true, PullRequestsAtUtc = Now,
            OwnedPullRequests = ["synthetic-pr-one", "synthetic-pr-two"], OpenPullRequests = ["synthetic-pr-one", "synthetic-pr-two"] } };
        await store.IngestAsync([session, work], Now);
        var first = Assert.Single(await store.EvaluateAllAsync(Now));
        Assert.Equal(2, first.InferredReviewWaits.Count);
        await store.IngestAsync([Event(3) with { ObservedAtUtc = Now.AddHours(1), Work = work.Work! with {
            PullRequestsAtUtc = Now.AddHours(1), OpenPullRequests = ["synthetic-pr-two"] } }], Now.AddHours(1));
        var after = Assert.Single(await store.EvaluateAllAsync(Now.AddHours(1)));
        Assert.Equal(Now.AddHours(24), Assert.Single(after.InferredReviewWaits.Values).ReviewAfterUtc);
        Assert.NotEqual(first.Current!.EpisodeId, after.Current!.EpisodeId);
        Assert.Equal("wait-review", Assert.Single(await store.EvaluateAllAsync(Now.AddHours(25))).Current!.ReasonCode);
    }

    public void Dispose() {
        if (Directory.Exists(_directory)) Directory.Delete(_directory, true);
    }
}
