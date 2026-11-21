using IntelligenceX.Ledger.Contracts;
using IntelligenceX.Ledger.Hosting;
using Xunit;

namespace IntelligenceX.Ledger.Tests;

public sealed class DormantSessionTests {
    private static readonly DateTimeOffset Now = new(2026, 1, 12, 12, 0, 0, TimeSpan.Zero);

    private static LedgerItem Session(double ageDays, bool turnOpen = true, bool usageLimit = false, bool archived = false) => new() {
        Id = "synthetic-session", MachineId = "synthetic-machine", Provider = "claude", Kind = "session",
        FirstObservedAtUtc = Now.AddDays(-30),
        Session = new() { TurnOpen = turnOpen, LastActivityAtUtc = Now.AddDays(-ageDays), UsageLimit = usageLimit, Archived = archived }
    };

    [Fact]
    public void ASessionSilentForDaysLeavesCurrentWorkWhateverItsTurnOrArchiveState() {
        // A turn left open by a client that ended, and an archived session, both from four days ago.
        Assert.True(LedgerRuntime.IsDormantSession(Session(4), Now));
        Assert.True(LedgerRuntime.IsDormantSession(Session(4, archived: true), Now));
        // Two days of silence is still current: it may be a long quiet turn.
        Assert.False(LedgerRuntime.IsDormantSession(Session(2), Now));
    }

    [Fact]
    public void UsageLimitsClaimsAndDeclaredWorkKeepASessionCurrentLonger() {
        // A session stopped at its usage limit stays resumable until the weekly limit has reset.
        Assert.False(LedgerRuntime.IsDormantSession(Session(5, turnOpen: false, usageLimit: true), Now));
        Assert.True(LedgerRuntime.IsDormantSession(Session(8, turnOpen: false, usageLimit: true), Now));
        Assert.False(LedgerRuntime.IsDormantSession(Session(10) with { Work = new() { ActiveClaim = true } }, Now));
        var declared = Session(10);
        declared.Declarations["synthetic"] = new(new WorkerDeclaration { Id = "synthetic", EpisodeId = "synthetic", Kind = "wait",
            Scope = new TaskScope("synthetic-task", 1, "synthetic-turn"), CheckpointRef = "synthetic-checkpoint",
            ConditionRef = "synthetic-condition", NextActor = "worker", Blocking = true, RecordedAtUtc = Now.AddDays(-10) },
            new(Guid.NewGuid(), "evidence", "1", Now.AddDays(-10), Now.AddDays(-10)));
        Assert.False(LedgerRuntime.IsDormantSession(declared, Now));
    }
}
