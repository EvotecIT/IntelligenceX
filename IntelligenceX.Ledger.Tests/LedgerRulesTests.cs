using IntelligenceX.Ledger.Contracts;
using IntelligenceX.Ledger.Rules;
using Xunit;

namespace IntelligenceX.Ledger.Tests;

public sealed class LedgerRulesTests {
    private static readonly DateTimeOffset Now = new(2026, 1, 12, 12, 0, 0, TimeSpan.Zero);
    private readonly LedgerRules _rules = new(new());
    private static readonly TaskScope Scope = new("synthetic-task", 1);
    private static LedgerItem Item(SessionFact? session = null, WorkFact? work = null) => new() {
        Id = "synthetic-session", MachineId = "synthetic-machine", Provider = "codex",
        FirstObservedAtUtc = Now.AddHours(-2), Session = session, Work = work, Scope = Scope
    };
    private static SessionFact Open(int quietMinutes = 1) => new() { TurnOpen = true,
        TurnStartedAtUtc = Now.AddHours(-1), LastActivityAtUtc = Now.AddMinutes(-quietMinutes) };
    private static void Delivery(LedgerItem item, string stage, DateTimeOffset at, string? outcome = null) {
        if (!item.Deliveries.TryGetValue("synthetic-delivery", out var state))
            item.Deliveries["synthetic-delivery"] = state = new() { Id = "synthetic-delivery" };
        state.Stages[stage] = new("known", at, Guid.NewGuid(), outcome);
    }
    private static void AssertVerdict(Verdict verdict, string value, string reason, DateTimeOffset since) {
        Assert.Equal(value, verdict.Value); Assert.Equal(reason, verdict.ReasonCode); Assert.Equal(since, verdict.SinceUtc);
    }

    [Fact]
    public void HeldCoordinatorGoalNeedsYouFromHoldTime() {
        var item = Item(Open(40), new() { Scope = Scope, GoalHeld = true, ActiveClaim = true, GoalHeldAtUtc = Now.AddMinutes(-35) });
        AssertVerdict(_rules.Evaluate(item, Now), "needs-you", "goal-held", Now.AddMinutes(-35));
    }

    [Fact]
    public void FailedWakeRemainsHeldWithoutInferringExecution() {
        var item = Item();
        Delivery(item, "held", Now.AddMinutes(-17), "failed");
        AssertVerdict(_rules.Evaluate(item, Now), "stuck", "wake-held", Now.AddMinutes(-17));
        Assert.Equal("unknown", item.Deliveries["synthetic-delivery"].Stages["started"].Knowledge);
        Delivery(item, "started", Now);
        Assert.Equal("active", _rules.Evaluate(item with { Session = Open() }, Now).ReasonCode);
    }

    [Fact]
    public void InterruptedTurnBecomesStuckAfterGraceAndNewTurnClearsIt() {
        var item = Item(new() { TurnOpen = false, Outcome = "interrupted", TurnEndedAtUtc = Now.AddMinutes(-10) });
        Assert.DoesNotContain("interrupted", _rules.Evaluate(item, Now.AddTicks(-1)).AlsoMatched);
        AssertVerdict(_rules.Evaluate(item, Now), "stuck", "interrupted", Now);
        var successor = item with { Session = Open() with { TurnStartedAtUtc = Now.AddMinutes(-1) } };
        AssertVerdict(_rules.Evaluate(successor, Now), "working", "active", Now.AddMinutes(-1));
    }

    [Fact]
    public void AppDeliveryIsUncertainUntilExplicitAppInterventionIsObserved() {
        var item = Item(Open() with { AppOwned = true });
        Delivery(item, "submitted", Now.AddMinutes(-10));
        AssertVerdict(_rules.Evaluate(item, Now), "stuck", "delivery-uncertain", Now.AddMinutes(-10));
        // A terminal failed delivery retires its admission wait; explicit next action needs the app.
        Delivery(item, "terminal", Now, "failed");
        item = item with { Work = new() { Scope = Scope, NeedsApp = true, NeedsAppAtUtc = Now } };
        AssertVerdict(_rules.Evaluate(item, Now), "needs-you", "needs-app", Now);
    }

    [Fact]
    public void QueueBehindStalledGoalReportsGoalAndQueueEvidence() {
        var item = Item(Open(40), new() { Scope = Scope, GoalHeld = true, GoalHeldAtUtc = Now.AddMinutes(-45),
            QueuedInputs = 1, OldestQueuedAtUtc = Now.AddMinutes(-20), Stalled = true });
        var verdict = _rules.Evaluate(item, Now);
        AssertVerdict(verdict, "needs-you", "goal-held", Now.AddMinutes(-45));
        Assert.Contains("queue-behind-stall", verdict.AlsoMatched);
        Assert.Contains("no-progress", verdict.AlsoMatched);
    }

    [Fact]
    public void CompletedUnarchivedWorkerIsDoneWhenTaskGatesAreClosed() {
        var item = Item(new() { TurnOpen = false, Outcome = "completed", TurnEndedAtUtc = Now.AddMinutes(-3) },
            new() { ActiveClaim = false, PullRequestsKnown = true, HadPullRequests = true, QueuedInputs = 0 });
        AssertVerdict(_rules.Evaluate(item, Now), "done", "complete", Now.AddMinutes(-3));
    }

    [Theory]
    [InlineData(9, 59, "working", "active")]
    [InlineData(10, 0, "working", "quiet")]
    [InlineData(29, 59, "working", "quiet")]
    [InlineData(30, 0, "stuck", "no-progress")]
    public void ActivityThresholdsFollowPublishedIntervals(int minutes, int seconds, string value, string reason) {
        var activity = Now.AddMinutes(-minutes).AddSeconds(-seconds);
        var item = Item(Open() with { LastActivityAtUtc = activity });
        AssertVerdict(_rules.Evaluate(item, Now), value, reason, reason == "no-progress" ? activity : Now.AddHours(-1));
    }

    [Fact]
    public void ReobservationAndHeartbeatPreserveSinceAndAppQueueGapIsOnlyCoverage() {
        var item = Item(Open(11)) with { Coverage = ["queue-not-visible"] };
        var first = _rules.Evaluate(item, Now);
        var later = _rules.Evaluate(item with { Current = first, Session = item.Session! with { TurnStartedAtUtc = Now } }, Now.AddMinutes(1), true);
        AssertVerdict(later, "working", "quiet", Now.AddHours(-1));
        Assert.Contains("reporter-silent", later.AlsoMatched); Assert.True(later.LastKnown);
    }

    [Fact]
    public void OnlyDecisiveUnknownPreventsDoneButNotKnownWaitOrQuestion() {
        var incomplete = Item(new() { Outcome = "completed", TurnEndedAtUtc = Now.AddMinutes(-1) });
        Assert.Equal("unknown", _rules.Evaluate(incomplete, Now).ReasonCode);
        var waiting = incomplete with { Work = new() { Scope = Scope, Wait = new("ci", "synthetic-check", Now.AddMinutes(-1), Now.AddHours(1)) } };
        Assert.Equal("waiting-ci", _rules.Evaluate(waiting, Now).ReasonCode);
        var question = waiting with { Session = new() { Question = true, InputId = "synthetic-input", QuestionAtUtc = Now.AddMinutes(-2) } };
        var verdict = _rules.Evaluate(question, Now);
        AssertVerdict(verdict, "needs-you", "question", Now.AddMinutes(-2));
        Assert.Contains("waiting-ci", verdict.AlsoMatched);
    }

    [Fact]
    public void CompletedTurnWithOpenPrGetsBoundedReviewWait() {
        var item = Item(new() { Outcome = "completed", TurnEndedAtUtc = Now.AddHours(-2) },
            new() { OpenPullRequests = ["synthetic-pr"], OwnedPullRequests = ["synthetic-pr"] });
        AssertVerdict(_rules.Evaluate(item, Now), "waiting", "waiting-review", Now.AddHours(-2));
        AssertVerdict(_rules.Evaluate(item, Now.AddHours(25)), "stuck", "wait-review", Now.AddHours(22));
    }

    [Fact]
    public void DoneRemainsStickyUntilNewWorkEvidenceAppears() {
        var item = Item(new() { Outcome = "completed", TurnEndedAtUtc = Now.AddMinutes(-2) },
            new() { ActiveClaim = false, PullRequestsKnown = true, QueuedInputs = 0 });
        item = item with { Current = _rules.Evaluate(item, Now) };
        AssertVerdict(_rules.Evaluate(item with { Work = null }, Now.AddHours(2)), "done", "idle", Now.AddMinutes(-2));
        Assert.Equal("working", _rules.Evaluate(item with { Session = Open() }, Now).Value);
        Assert.Equal("question", _rules.Evaluate(item with { Session = item.Session! with {
            Question = true, InputId = "synthetic-input", QuestionAtUtc = Now } }, Now).ReasonCode);
        Assert.Equal("waiting-review", _rules.Evaluate(item with { Work = item.Work! with {
            OpenPullRequests = ["new-synthetic-pr"], OwnedPullRequests = ["new-synthetic-pr"] } }, Now).ReasonCode);
        Assert.Equal("idle", _rules.Evaluate(item with { Current = null, Work = item.Work! with { QueuedInputs = null },
            Coverage = ["queue-not-visible"] }, Now).ReasonCode);
    }
}
