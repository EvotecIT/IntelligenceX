using IntelligenceX.Ledger.Contracts;
using IntelligenceX.Ledger.Storage;
using Xunit;

namespace IntelligenceX.Ledger.Tests;

public sealed class LedgerReviewRegressionTests : IDisposable {
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "ix-ledger-review", Guid.NewGuid().ToString("N"));
    private static readonly DateTimeOffset Now = new(2026, 1, 12, 12, 0, 0, TimeSpan.Zero);
    private static readonly TaskScope Scope = new("synthetic-task", 1, "synthetic-first-turn");
    private static LedgerEvent Event(int sequence) => new() {
        EventId = Guid.NewGuid(), Epoch = Guid.NewGuid(), Sequence = sequence, ObservedAtUtc = Now,
        MachineId = "synthetic-machine", ReporterId = "synthetic-reporter", ItemId = "synthetic-item", Provider = "codex", Source = "evidence"
    };
    private static WorkerDeclaration Declaration(string episode) => new() {
        Id = episode, EpisodeId = episode, Scope = Scope, Kind = "wait", CheckpointRef = "synthetic-checkpoint",
        ConditionRef = "synthetic-condition", NextActor = "dependency", Blocking = true,
        RecordedAtUtc = Now, ReviewAfterUtc = Now.AddHours(1)
    };

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task EqualStartRetryEpisodesRejectAmbiguityInEitherArrivalOrder(bool reverse) {
        using var store = new LedgerStore(Path.Combine(_directory, "ledger.sqlite"), new(new()));
        await store.InitializeAsync();
        var first = Declaration(reverse ? "synthetic-second" : "synthetic-first");
        var second = Declaration(reverse ? "synthetic-first" : "synthetic-second");
        await store.IngestAsync([Event(1) with { Declaration = first }], Now);
        await Assert.ThrowsAsync<EventConflictException>(() => store.IngestAsync([Event(2) with { Declaration = second }], Now));
        var row = Assert.Single(await store.EvaluateAllAsync(Now));
        Assert.Equal(first.EpisodeId, Assert.Single(row.Declarations.Values).Fact.EpisodeId);
        Assert.Single(await store.HistoryAsync(row.Id, 0, 100, default));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task NamedWaitAcrossTurnsAndCandidatesIgnoresPriorOccurrenceResolution(bool nestedScope) {
        using var store = new LedgerStore(Path.Combine(_directory, "ledger.sqlite"), new(new()));
        await store.InitializeAsync();
        var first = new WaitFact("ci", "synthetic-check", Now, Now.AddHours(1), Scope: nestedScope ? Scope : null, CandidateRef: "synthetic-first-candidate");
        await store.IngestAsync([Event(1) with { Session = new() { Scope = Scope, TurnId = Scope.TurnId, TurnOpen = true,
            TurnStartedAtUtc = Now, LastActivityAtUtc = Now }, Work = new() { Scope = Scope, Wait = first } }], Now);
        var nextScope = Scope with { TurnId = "synthetic-next-turn" };
        var next = first with { Scope = nestedScope ? nextScope : null, RecordedAtUtc = Now.AddMinutes(1), DueAtUtc = Now.AddHours(2) };
        await store.IngestAsync([Event(2) with { ObservedAtUtc = Now.AddMinutes(1), Session = new() {
            Scope = nextScope, TurnId = nextScope.TurnId, TurnOpen = true, TurnStartedAtUtc = Now.AddMinutes(1), LastActivityAtUtc = Now.AddMinutes(1) },
            Work = new() { Scope = nextScope, Wait = next } }], Now.AddMinutes(1));
        var row = Assert.Single(await store.EvaluateAllAsync(Now.AddMinutes(1)));
        Assert.Equal(next, row.Work!.Wait);
        Assert.Equal("waiting-ci", row.Current!.ReasonCode);
        await store.IngestAsync([Event(3) with { ObservedAtUtc = Now.AddMinutes(2), Work = new() { Scope = Scope, Wait = first with { Resolved = true } } }], Now.AddMinutes(2));
        Assert.Equal(next, Assert.Single(await store.EvaluateAllAsync(Now.AddMinutes(2))).Work!.Wait);
        var candidate = next with { CandidateRef = "synthetic-next-candidate", RecordedAtUtc = Now.AddMinutes(3) };
        await store.IngestAsync([Event(4) with { ObservedAtUtc = Now.AddMinutes(3), Work = new() { Scope = nextScope, Wait = candidate } },
            Event(5) with { ObservedAtUtc = Now.AddMinutes(4), Work = new() { Scope = nextScope, Wait = next with { Resolved = true } } }], Now.AddMinutes(4));
        Assert.Equal(candidate, Assert.Single(await store.EvaluateAllAsync(Now.AddMinutes(4))).Work!.Wait);
        await store.IngestAsync([Event(6) with { ObservedAtUtc = Now.AddMinutes(5), Work = new() { Scope = nextScope, Wait = candidate with { Resolved = true } } }], Now.AddMinutes(5));
        Assert.Null(Assert.Single(await store.EvaluateAllAsync(Now.AddMinutes(5))).Work!.Wait);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task AdvancingTaskGenerationResetsTaskConditionClocksAndPreservesOnlySameTurnClocks(bool sameTurn) {
        using var store = new LedgerStore(Path.Combine(_directory, "ledger.sqlite"), new(new()));
        await store.InitializeAsync();
        await store.IngestAsync([Event(1) with { ObservedAtUtc = Now.AddHours(-1), Session = new() { Scope = Scope, TurnId = Scope.TurnId, TurnOpen = true },
            Work = new() { Scope = Scope, GoalHeld = true, QueuedInputs = 1 } }], Now.AddHours(-1));
        var successor = new TaskScope("synthetic-next-task", 2, sameTurn ? Scope.TurnId : "synthetic-next-turn");
        await store.IngestAsync([Event(2) with { Control = new(successor, "synthetic-control", "active", Now) },
            Event(3) with { Work = new() { Scope = successor, GoalHeld = true, QueuedInputs = 1 } }], Now);
        var row = Assert.Single(await store.EvaluateAllAsync(Now));
        Assert.Equal(Now, row.Current!.SinceUtc);
        Assert.Equal("goal-held", row.Current.ReasonCode);
        Assert.Equal(Now, row.ConditionFirstObservedAtUtc["goal-held"]);
        Assert.Equal(sameTurn, row.ConditionFirstObservedAtUtc.ContainsKey("active"));
        if (sameTurn) Assert.Equal(Now.AddHours(-1), row.ConditionFirstObservedAtUtc["active"]);
    }

    [Theory]
    [InlineData("question", false, false)]
    [InlineData("question", true, false)]
    [InlineData("question", true, true)]
    [InlineData("active", true, false)]
    [InlineData("usage-limit", false, false)]
    public async Task SessionConditionClocksFollowNewEpisodesAndSurviveSameEpisodeRefresh(string reason, bool newTurn, bool reuseInput) {
        using var store = new LedgerStore(Path.Combine(_directory, "ledger.sqlite"), new(new()));
        await store.InitializeAsync();
        var initial = new SessionFact { Scope = Scope, TurnId = Scope.TurnId, TurnOpen = true,
            LastActivityAtUtc = Now.AddHours(-1), Question = reason == "question", InputId = reason == "question" ? "synthetic-first-input" : null,
            UsageLimit = reason == "usage-limit", AccountId = reason == "usage-limit" ? "synthetic-first-account" : null };
        await store.IngestAsync([Event(1) with { ObservedAtUtc = Now.AddHours(-1), Session = initial }], Now.AddHours(-1));
        var previous = Assert.Single(await store.EvaluateAllAsync(Now.AddHours(-1)));
        Assert.Equal(Now.AddHours(-1), previous.Current!.SinceUtc);
        var nextScope = Scope with { TurnId = newTurn ? "synthetic-next-turn" : Scope.TurnId };
        var next = initial with { Scope = nextScope, TurnId = nextScope.TurnId, LastActivityAtUtc = Now,
            InputId = reason == "question" ? reuseInput ? initial.InputId : "synthetic-next-input" : null,
            AccountId = reason == "usage-limit" ? "synthetic-next-account" : null };
        await store.IngestAsync([Event(2) with { Session = next }], Now);
        var changed = Assert.Single(await store.EvaluateAllAsync(Now));
        Assert.Equal(reason, changed.Current!.ReasonCode);
        Assert.Equal(Now, changed.Current.SinceUtc);
        Assert.NotEqual(previous.Current.EpisodeId, changed.Current.EpisodeId);
        await store.IngestAsync([Event(3) with { ObservedAtUtc = Now.AddMinutes(1), Session = next with { LastActivityAtUtc = Now.AddMinutes(1) } }], Now.AddMinutes(1));
        var refreshed = Assert.Single(await store.EvaluateAllAsync(Now.AddMinutes(1)));
        Assert.Equal(Now, refreshed.Current!.SinceUtc);
        Assert.Equal(changed.Current.EpisodeId, refreshed.Current.EpisodeId);
    }

    public void Dispose() { if (Directory.Exists(_directory)) Directory.Delete(_directory, true); }
}
