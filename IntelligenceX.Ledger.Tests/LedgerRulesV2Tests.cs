using IntelligenceX.Ledger.Contracts;
using IntelligenceX.Ledger.Rules;
using Xunit;

namespace IntelligenceX.Ledger.Tests;

public sealed class LedgerRulesV2Tests {
    private static readonly DateTimeOffset Now = new(2026, 1, 12, 12, 0, 0, TimeSpan.Zero);
    private static readonly TaskScope Scope = new("synthetic-task", 2, "synthetic-turn");
    private static readonly EvidenceRef Evidence = new(Guid.Parse("11111111-1111-1111-1111-111111111111"), "evidence", "2", Now, Now);
    private readonly LedgerRules _rules = new(new());
    private static LedgerItem Item() => new() {
        Id = "synthetic-item", MachineId = "synthetic-machine", Provider = "codex", Scope = Scope,
        FirstObservedAtUtc = Now.AddHours(-5), SessionEvidence = Evidence,
        Session = new() { TurnOpen = true, TurnId = Scope.TurnId, TurnStartedAtUtc = Now.AddHours(-2), LastActivityAtUtc = Now.AddHours(-1) }
    };
    private static WorkerDeclaration Declaration(string kind = "operation") => new() {
        Id = "synthetic-declaration", EpisodeId = "synthetic-episode", Scope = Scope, CheckpointRef = "synthetic-checkpoint", ConditionRef = "synthetic-condition",
        Kind = kind, NextActor = "worker", Blocking = true, RecordedAtUtc = Now.AddHours(-1), ReviewAfterUtc = Now.AddHours(1)
    };
    private static LedgerItem Declared(WorkerDeclaration declaration) {
        var item = Item();
        item.Declarations[declaration.ConditionRef] = new(declaration, Evidence);
        return item;
    }

    [Theory]
    [InlineData("stopped")]
    [InlineData("paused")]
    [InlineData("budget")]
    public void DeliberateNativeHoldIsQuietUntilWorkQueuesBehindIt(string state) {
        var item = Item() with {
            Session = new() { TurnId = Scope.TurnId, TurnOpen = false, Outcome = "interrupted", TurnEndedAtUtc = Now.AddHours(-1) },
            Control = new(Scope, "synthetic-stop", state, Now.AddHours(-1)), ControlEvidence = Evidence,
            Work = new() { Scope = Scope, GoalHeld = true, QueuedInputs = 0, ActiveClaim = true }
        };
        var quiet = _rules.Evaluate(item, Now);
        Assert.Equal("waiting", quiet.Value);
        Assert.Equal("paused-by-user", quiet.ReasonCode);
        Assert.True(quiet.Hold!.Quiet);
        Assert.Equal("suppressed", quiet.WakeEligibility!.State);
        var queued = _rules.Evaluate(item with { Work = item.Work with { QueuedInputs = 1, OldestQueuedAtUtc = Now.AddMinutes(-5) } }, Now);
        Assert.Equal("stuck", queued.Value);
        Assert.Equal("queue-behind-stall", queued.ReasonCode);
        Assert.Equal("paused-by-user", queued.Hold!.Type);
        Assert.False(queued.Hold.Quiet);
        Assert.DoesNotContain("interrupted", queued.AlsoMatched);
    }

    [Fact]
    public void DeclaredLongOperationUsesItsReviewBoundaryWithoutChangingProgress() {
        var item = Declared(Declaration());
        var before = _rules.Evaluate(item, Now.AddHours(1));
        Assert.Equal("working", before.Value);
        Assert.Equal(Now.AddHours(-1), before.LastProgressAtUtc);
        Assert.DoesNotContain("no-progress", before.AlsoMatched);
        var after = _rules.Evaluate(item, Now.AddHours(1).AddTicks(1));
        Assert.Equal("wait-review", after.ReasonCode);
        Assert.Equal(Now.AddHours(1), after.SinceUtc);
        var match = Assert.Single(after.Matches);
        Assert.Equal(Evidence, Assert.Single(match.Evidence));
    }

    [Theory]
    [InlineData(false, "wait-review")]
    [InlineData(true, "wait-overdue")]
    public void DependencyReviewAndActualDeadlineRemainDistinct(bool actualDeadline, string expected) {
        var item = Declared(Declaration("wait") with {
            DueAtUtc = actualDeadline ? Now.AddMinutes(-1) : null, ReviewAfterUtc = Now.AddMinutes(-2)
        }) with { Session = null };
        var verdict = _rules.Evaluate(item, Now);
        Assert.Equal("stuck", verdict.Value);
        Assert.Equal(expected, verdict.ReasonCode);
        Assert.Equal(actualDeadline ? Now.AddMinutes(-1) : Now.AddMinutes(-2), verdict.SinceUtc);
    }

    [Fact]
    public void OldTaskGoalAndStopCannotBlockNewerTask() {
        var old = Scope with { Generation = 1 };
        var item = Item() with { Session = Item().Session! with { LastActivityAtUtc = Now },
            Control = new(old, "synthetic-old-stop", "stopped", Now),
            Work = new() { Scope = old, GoalHeld = true, QueuedInputs = 3, Stalled = true, ActiveClaim = true }
        };
        var verdict = _rules.Evaluate(item, Now);
        Assert.Equal("working", verdict.Value);
        Assert.Empty(verdict.AlsoMatched);
        Assert.Equal(Scope, Assert.Single(verdict.Matches).Scope);
    }

    [Fact]
    public void NewInputEpisodeGetsItsOwnSinceEvenWhenReasonDoesNotChange() {
        var item = Item() with { Session = Item().Session! with {
            Question = true, InputId = "synthetic-first-input", QuestionAtUtc = Now.AddMinutes(-5)
        } };
        var first = _rules.Evaluate(item, Now);
        var next = _rules.Evaluate(item with { Current = first, Session = item.Session! with {
            InputId = "synthetic-second-input", QuestionAtUtc = Now
        } }, Now);
        Assert.Equal("question", next.ReasonCode);
        Assert.NotEqual(first.EpisodeId, next.EpisodeId);
        Assert.Equal(Now, next.SinceUtc);
        Assert.Equal(Now.AddHours(-1), next.LastProgressAtUtc);
    }

    [Fact]
    public void UnconfirmedOrOptionalInputDoesNotStopParentWork() {
        var item = Item() with { Session = Item().Session! with {
            LastActivityAtUtc = Now, InputUnconfirmed = true, InputId = "synthetic-input"
        } };
        Assert.Equal("working", _rules.Evaluate(item, Now).Value);
        item = item with { Session = item.Session! with { Question = true, QuestionBlocking = false } };
        Assert.Equal("working", _rules.Evaluate(item, Now).Value);
    }

    [Fact]
    public void NewCandidateRestartsDependencyEpisode() {
        var old = Declaration("wait") with { CandidateRef = "synthetic-old-candidate" };
        var item = Declared(old);
        var first = _rules.Evaluate(item, Now);
        item.Declarations[old.ConditionRef] = new(old with { Id = "synthetic-new-declaration", CandidateRef = "synthetic-new-candidate", RecordedAtUtc = Now }, Evidence);
        var next = _rules.Evaluate(item with { Current = first }, Now);
        Assert.Equal("waiting", next.Value);
        Assert.NotEqual(first.EpisodeId, next.EpisodeId);
        Assert.Equal(Now, next.SinceUtc);
    }

    [Fact]
    public void InferredPrWaitRequiresTaskOwnershipAndHasReviewTimeOnly() {
        var item = Item() with { Session = new() { Outcome = "completed", TurnOpen = false, TurnEndedAtUtc = Now },
            Work = new() { OpenPullRequests = ["synthetic-pr"] } };
        Assert.DoesNotContain(_rules.Evaluate(item, Now).Matches, m => m.ReasonCode == "waiting-review");
        var owned = item with { Work = item.Work! with { OwnedPullRequests = ["synthetic-pr"] } };
        Assert.Equal("waiting-review", _rules.Evaluate(owned, Now).ReasonCode);
        Assert.Equal("wait-review", _rules.Evaluate(owned, Now.AddHours(24).AddTicks(1)).ReasonCode);
    }

    [Fact]
    public void ClosedUnmergedWorkIsCancelledAndQueuedInputStillPreventsDone() {
        var item = Item() with { Session = new() { TurnOpen = false, Outcome = "completed", TurnEndedAtUtc = Now },
            Work = new() { ActiveClaim = false, PullRequestsKnown = true, HadPullRequests = true,
                CancelledPullRequests = ["synthetic-pr"], QueuedInputs = 0 } };
        Assert.Equal("cancelled", _rules.Evaluate(item, Now).ReasonCode);
        Assert.NotEqual("done", _rules.Evaluate(item with { Work = item.Work! with { QueuedInputs = 1 } }, Now).Value);
    }

    [Fact]
    public void EligibilityRequiresResolvedConditionAndQualifiedCurrentControls() {
        var item = Declared(Declaration("wait") with { Resolved = true }) with {
            Session = new() { TurnOpen = false, TurnId = Scope.TurnId, Outcome = "completed", TurnEndedAtUtc = Now, AppOwned = false },
            Work = new() { ActiveClaim = false, PullRequestsKnown = true, QueuedInputs = 0 }
        };
        Assert.Equal("unknown", _rules.Evaluate(item, Now).WakeEligibility!.State);
        var qualified = item with { Control = new(Scope, "synthetic-control", "active", Now), ControlEvidence = Evidence };
        var verdict = _rules.Evaluate(qualified, Now);
        Assert.Equal("eligible", verdict.WakeEligibility!.State);
        Assert.Equal("condition-resolved", verdict.WakeEligibility.ReasonCode);
        Assert.Equal("synthetic-condition", verdict.WakeEligibility.ConditionRef);
        Assert.Equal("suppressed", _rules.Evaluate(qualified with { Session = qualified.Session! with { AppOwned = true } }, Now).WakeEligibility!.State);
        var stale = _rules.Evaluate(qualified, Now.AddMinutes(11)).WakeEligibility!;
        Assert.Equal("unknown", stale.State);
        Assert.Contains("native-controls-freshness", stale.MissingFacts);
    }

    [Fact]
    public void HumanStopRemainsBindingWhenAnotherTurnAppearsInSameTask() {
        var item = Item() with { Control = new(Scope, "synthetic-stop", "stopped", Now), ControlEvidence = Evidence,
            Session = Item().Session! with { TurnId = "synthetic-successor-turn", LastActivityAtUtc = Now } };
        Assert.Equal("paused-by-user", _rules.Evaluate(item, Now).ReasonCode);
    }

    [Fact]
    public void ConcurrentConditionsKeepIndependentEvidenceAndWaitDetails() {
        var item = Declared(Declaration());
        var secondEvidence = Evidence with { EventId = Guid.Parse("22222222-2222-2222-2222-222222222222") };
        var wait = Declaration("wait") with { Id = "synthetic-second-declaration", ConditionRef = "synthetic-second-condition", ReviewAfterUtc = Now.AddMinutes(-1) };
        item.Declarations[wait.ConditionRef] = new(wait, secondEvidence);
        var verdict = _rules.Evaluate(item, Now);
        Assert.Equal("wait-review", verdict.ReasonCode);
        var decisive = Assert.Single(verdict.Matches, m => m.ReasonCode == "wait-review");
        var working = Assert.Single(verdict.Matches, m => m.Verdict == "working");
        Assert.Equal(secondEvidence, Assert.Single(decisive.Evidence));
        Assert.Equal(Evidence, Assert.Single(working.Evidence));
        Assert.NotEqual(decisive.EpisodeId, working.EpisodeId);
        Assert.Equal(wait.ConditionRef, LedgerItemView.From(item with { Current = verdict }).Wait!.ConditionId);
    }

    [Fact]
    public void OptionalQuestionIsActionableOnItsOwnItemWhileParentWorks() {
        var parent = Item() with { Session = Item().Session! with { LastActivityAtUtc = Now } };
        var child = Declared(Declaration("question") with { NextActor = "human", Blocking = false }) with { Kind = "input", ParentItemId = parent.Id };
        Assert.Equal("working", _rules.Evaluate(parent, Now).Value);
        Assert.Equal("needs-you", _rules.Evaluate(child, Now).Value);
    }

    [Fact]
    public void UrgentSafetyCarriesPolicyEvidenceAndManualAction() {
        var item = Item() with { Safety = new(Scope, "synthetic-safety-episode", "credential-exposure", "urgent-safety", 1,
            "synthetic-evidence-ref", Now, "press-stop"), SafetyEvidence = Evidence,
            Session = Item().Session! with { AppOwned = true } };
        var verdict = _rules.Evaluate(item, Now);
        Assert.Equal("needs-you", verdict.Value);
        Assert.Equal("urgent-safety", verdict.Hold!.Type);
        var view = LedgerItemView.From(item with { Current = verdict });
        Assert.Equal(1, view.Safety!.PolicyVersion);
        Assert.Equal("press-stop", view.Safety.ManualAction);
        Assert.Contains(Evidence, view.Evidence);
        Assert.Equal("suppressed", view.WakeEligibility!.State);
    }

    [Fact]
    public void HandoffToWorkerWaitsUntilItsReviewTimeWithoutAssigningHumanAction() {
        var item = Declared(Declaration("handoff") with { AcceptanceBy = "worker", ResultRef = "synthetic-result" });
        var waiting = _rules.Evaluate(item, Now);
        Assert.Equal("waiting", waiting.Value);
        Assert.Equal("waiting-acceptance", waiting.ReasonCode);
        Assert.Equal("wait-review", _rules.Evaluate(item, Now.AddHours(1).AddTicks(1)).ReasonCode);
    }
}
