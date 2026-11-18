using IntelligenceX.Ledger.Contracts;
using IntelligenceX.Ledger.Coordination;
using IntelligenceX.Ledger.Hosting;
using Xunit;

namespace IntelligenceX.Ledger.Tests;

public sealed class CoordinationWakeTests {
    private static readonly DateTimeOffset Now = new(2030, 1, 1, 0, 0, 0, TimeSpan.Zero);
    private static readonly CoordinationPrincipal Sessions = new("pc-sessions", "client", "unused", ["sample"], "pc");
    private static readonly CoordinationPrincipal PcHost = new("pc-host", "host", "unused", ["sample"], "pc");
    private static readonly CoordinationPrincipal MacHost = new("mac-host", "host", "unused", ["sample"], "mac");

    private static CoordinationState CreateState() => new() { Policy = new(1, [
        new("pc", 1, 1, 4, 4096, ["windows-shell"], AllowedCreateProviders: ["codex"]) ]) };

    private static CoordinationWakeRequest Ci(string id = "wake-1", int pr = 7) =>
        new(id, "CI finished: merge if green, otherwise fix it.", "codex", "Thread-A", Repo: "EvotecIT/Sample", Pr: pr);

    [Fact]
    public void ACiWakeWaitsForTheChecksThenIsDeliveredOnceToItsSession() {
        var state = CreateState();
        var engine = new CoordinationEngine(state, Now);
        var wake = engine.Wake(Ci(), Sessions);
        Assert.Equal(("waiting", "pc", "thread-a"), (wake.State, wake.MachineId, wake.SessionId));
        Assert.Equal([("evotecit/sample", 7)], engine.WatchedPullRequests());
        Assert.Empty(engine.WakesToDeliver(PcHost, 0).Items);

        Assert.Equal(1, engine.ChecksFinished("evotecit/sample", 7, "The checks on EvotecIT/Sample#7 passed.", Now));
        var due = Assert.Single(engine.WakesToDeliver(PcHost, 0).Items);
        Assert.Equal("The checks on EvotecIT/Sample#7 passed.", due.Outcome);
        Assert.Empty(engine.WakesToDeliver(MacHost, 0).Items);
        Assert.Equal("wake-delivery-owner-conflict", Assert.Throws<CoordinationConflictException>(() =>
            engine.RecordWakeDelivery("wake-1", new("attempt-1", "sending"), MacHost)).Message);

        engine.RecordWakeDelivery("wake-1", new("attempt-1", "sending"), PcHost);
        Assert.Empty(engine.WakesToDeliver(PcHost, 0).Items);
        Assert.Equal("delivery-needs-reconciliation", Assert.Throws<CoordinationConflictException>(() =>
            engine.RecordWakeDelivery("wake-1", new("attempt-2", "sending"), PcHost)).Message);
        Assert.Equal("delivered", engine.RecordWakeDelivery("wake-1", new("attempt-1", "submitted", "codex-turn-1"), PcHost).State);
        Assert.Equal("wake-delivery-started", Assert.Throws<CoordinationConflictException>(() => engine.CancelWake("wake-1", Sessions)).Message);
        Assert.Empty(engine.WatchedPullRequests());
    }

    [Fact]
    public void ASendNotSubmittedReturnsToTheFeedAndOneLeftSendingBecomesUncertain() {
        var state = CreateState();
        var engine = new CoordinationEngine(state, Now);
        engine.Wake(Ci(), Sessions);
        engine.ChecksFinished("EvotecIT/Sample", 7, "Passed.", Now);
        engine.RecordWakeDelivery("wake-1", new("attempt-1", "sending"), PcHost);
        engine.RecordWakeDelivery("wake-1", new("attempt-1", "not-submitted", "codex-thread-unreachable"), PcHost);
        Assert.Single(engine.WakesToDeliver(PcHost, 0).Items);

        engine.RecordWakeDelivery("wake-1", new("attempt-2", "sending"), PcHost);
        var later = new CoordinationEngine(state, Now.AddMinutes(state.Policy.Ask.DeliveryUnconfirmedMinutes + 1));
        later.ApplyWakes();
        Assert.Equal("uncertain", state.Wakes["wake-1"].State);
        Assert.Empty(later.WakesToDeliver(PcHost, 0).Items);

        var purge = new CoordinationEngine(state, Now.AddDays(state.Policy.RetainFinishedDays + 1));
        purge.ApplyWakes();
        Assert.False(state.Wakes.ContainsKey("wake-1"));
    }

    [Fact]
    public void ATimedWakeFallsDueAndAnUnfinishedCiWakeExpiresWithItsReason() {
        var state = CreateState();
        var engine = new CoordinationEngine(state, Now);
        engine.Wake(new("timer", "Check the reviewer.", "claude", "Session-B", "Session B [abc123]", AfterMinutes: 45), Sessions);
        engine.Wake(Ci(), Sessions);
        Assert.Equal("Session-B", state.Wakes["timer"].SessionId);

        new CoordinationEngine(state, Now.AddMinutes(44)).ApplyWakes();
        Assert.Equal("waiting", state.Wakes["timer"].State);
        var at45 = new CoordinationEngine(state, Now.AddMinutes(45));
        at45.ApplyWakes();
        Assert.Equal(("due", "The 45 minutes you asked to wait have passed."), (state.Wakes["timer"].State, state.Wakes["timer"].Outcome));
        Assert.Equal("waiting", state.Wakes["wake-1"].State);

        engine.Wake(new("long-timer", "Check again in four days.", "codex", "thread-a", AfterMinutes: 4 * 24 * 60), Sessions);
        new CoordinationEngine(state, Now.AddHours(state.Policy.Ask.WakeExpiryHours)).ApplyWakes();
        Assert.Equal("due", state.Wakes["wake-1"].State);
        Assert.Contains("did not finish within 72 hours", state.Wakes["wake-1"].Outcome);
        Assert.Equal("waiting", state.Wakes["long-timer"].State);
    }

    [Fact]
    public void WakesAreValidatedRetriedByIdAndANewerOneForTheSamePullRequestReplacesTheOlder() {
        var state = CreateState();
        var engine = new CoordinationEngine(state, Now);
        Assert.Equal("invalid-wake", Assert.Throws<CoordinationConflictException>(() =>
            engine.Wake(Ci() with { AfterMinutes = 10 }, Sessions)).Message);
        Assert.Equal("invalid-wake", Assert.Throws<CoordinationConflictException>(() =>
            engine.Wake(Ci() with { Repo = "not a repo" }, Sessions)).Message);
        Assert.Equal("invalid-wake", Assert.Throws<CoordinationConflictException>(() =>
            engine.Wake(new("neither", "Nothing to wait for.", "codex", "thread-a"), Sessions)).Message);
        Assert.Equal("wake-role-denied", Assert.Throws<CoordinationConflictException>(() => engine.Wake(Ci(), PcHost)).Message);

        var first = engine.Wake(Ci(), Sessions);
        Assert.Same(state.Wakes["wake-1"], engine.Wake(Ci(), Sessions));
        Assert.Equal(first, engine.Wake(Ci(), Sessions));
        Assert.Equal("wake-id-conflict", Assert.Throws<CoordinationConflictException>(() =>
            engine.Wake(Ci() with { Prompt = "Something else." }, Sessions)).Message);
        engine.Wake(new("timer", "Later.", "codex", "thread-a", AfterMinutes: 5), Sessions);
        Assert.Equal("wake-id-conflict", Assert.Throws<CoordinationConflictException>(() =>
            engine.Wake(new("timer", "Later.", "codex", "thread-a", AfterMinutes: 60), Sessions)).Message);

        engine.Wake(Ci("wake-2"), Sessions);
        Assert.Equal(("cancelled", "Replaced by wake wake-2."), (state.Wakes["wake-1"].State, state.Wakes["wake-1"].Outcome));
        Assert.Equal("waiting", engine.Wake(Ci("wake-3", pr: 8), Sessions).State);
        Assert.Equal("waiting", state.Wakes["wake-2"].State);
        Assert.Equal("cancelled", engine.CancelWake("wake-3", Sessions).State);
        Assert.Equal("wake-not-found", Assert.Throws<CoordinationConflictException>(() =>
            engine.CancelWake("wake-2", Sessions with { Id = "other-sessions" })).Message);
    }

    [Fact]
    public void AReadMadeBeforeAWakeExistedDoesNotFireItAndDueWakesCountTowardTheLimit() {
        var state = CreateState();
        state.Policy.Ask = state.Policy.Ask with { MaximumWakes = 2 };
        var engine = new CoordinationEngine(state, Now);
        engine.Wake(Ci(), Sessions);
        // A cached read from a minute earlier (perhaps of an older head) leaves the new wake waiting.
        Assert.Equal(0, engine.ChecksFinished("EvotecIT/Sample", 7, "Passed.", Now.AddMinutes(-1)));
        Assert.Equal("waiting", state.Wakes["wake-1"].State);

        engine.ChecksFinished("EvotecIT/Sample", 7, "Passed.", Now);
        engine.Wake(new("timer", "Later.", "claude", "session-b", AfterMinutes: 5), Sessions);
        Assert.Equal("wake-limit-reached", Assert.Throws<CoordinationConflictException>(() =>
            engine.Wake(Ci("wake-2", pr: 8), Sessions)).Message);

        // The inbox hook reads only its own session's wakes.
        Assert.Equal("wake-1", Assert.Single(engine.WakesToDeliver(PcHost, 0, "thread-a").Items).Id);
        Assert.Empty(engine.WakesToDeliver(PcHost, 0, "session-b").Items);
    }

    [Theory]
    [InlineData("open", "green", 0, true, null, "The checks on EvotecIT/Sample#7 on abcdef1 passed.")]
    [InlineData("open", "failing", 0, true, null, "The checks on EvotecIT/Sample#7 on abcdef1 finished with 1 failing: build.")]
    [InlineData("open", "failing", 2, true, null, null)]
    [InlineData("open", "pending", 2, true, null, null)]
    [InlineData("open", "green", 0, false, null, null)]
    [InlineData("open", "green", 0, true, "1234567", null)]
    [InlineData("open", "green", -1, true, null, null)]
    [InlineData("merged", "green", 0, true, null, "EvotecIT/Sample#7 was merged.")]
    [InlineData("unknown", "unknown", 0, false, null, null)]
    public void TheWatcherFiresOnlyOnFinishedChecksForTheCurrentHeadOrAClosedPullRequest(string state, string checks, int pending,
        bool rechecked, string? checksHead, string? expected) {
        // pending -1 stands for a fresh head with no checks reported yet.
        var read = new PullRequestReadiness {
            Repo = "EvotecIT/Sample", Pr = 7, HeadSha = "abcdef1234567890", ChecksHeadSha = checksHead, State = state, Checks = checks,
            PendingCheckCount = Math.Max(0, pending), CheckCount = pending < 0 ? 0 : 3, FailingCheckCount = checks == "failing" ? 1 : 0, FailingChecks = checks == "failing" ? ["build"] : [],
            RecheckedAfterObservedHead = rechecked
        };
        Assert.Equal(expected, WakeWatcher.Outcome(read));
    }
}
