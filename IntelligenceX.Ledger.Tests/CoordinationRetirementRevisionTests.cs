using IntelligenceX.Ledger.Contracts;
using IntelligenceX.Ledger.Coordination;
using Xunit;
using static IntelligenceX.Ledger.Tests.CoordinationLifecycleTests;

namespace IntelligenceX.Ledger.Tests;

public sealed class CoordinationRetirementRevisionTests {
    [Fact]
    public void ConditionalRetirementPreservesConcurrentHoldsAndAcknowledgesIdenticalStageReplays() {
        var state = new CoordinationState { Policy = Policy };
        var engine = new CoordinationEngine(state, Now);
        Initialize(engine, state);
        var work = engine.Submit(Help(), Requester);
        var binding = Start(engine, state, work);
        engine.Receipt(work.Request.Id, "terminal", Execution(work, "sample-result"), HelperHost);
        var original = state.Retirements[work.Request.Id];
        Assert.Equal(0, original.Revision);
        var held = engine.Retire(new(binding, "waiting", "review-evidence", WaitingOn: "review-required",
            ExpectedRevision: original.Revision), HelperHost);
        Assert.Equal(1, held.Revision);
        var archived = new CoordinationRetirementReceipt(binding, "archived", "native-archive",
            ExpectedRevision: original.Revision);
        var before = engine.Changes.Count;
        var error = Assert.Throws<CoordinationConflictException>(() => engine.Retire(archived, HelperHost));
        Assert.Equal("retirement-revision-conflict", error.Message);
        Assert.Equal(before, engine.Changes.Count);
        Assert.Equal("review-required", state.Retirements[work.Request.Id].WaitingOn);
        Assert.False(state.Retirements[work.Request.Id].Evidence.ContainsKey("archived"));
        var recorded = engine.Retire(archived with { ExpectedRevision = held.Revision }, HelperHost);
        Assert.Equal(2, recorded.Revision);
        Assert.Equal("review-required", recorded.WaitingOn);
        before = engine.Changes.Count;
        Assert.Equal(recorded, engine.Retire(archived, HelperHost));
        Assert.Equal(before, engine.Changes.Count);
        Assert.Equal("review-required", state.Retirements[work.Request.Id].WaitingOn);
    }
}
