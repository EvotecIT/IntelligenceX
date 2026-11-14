using IntelligenceX.Ledger.Coordination;
using Xunit;
using static IntelligenceX.Ledger.Tests.CoordinationLifecycleTests;

namespace IntelligenceX.Ledger.Tests;

public sealed class CoordinationHandoffTests {
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void NestedWriteHelpRequiresSourceHandoffInsteadOfDeadlockingBehindItsParent(bool parentReadOnly) {
        var state = CreateState(); var engine = new CoordinationEngine(state, Now);
        var parent = engine.Submit(Help() with { ReadOnly = parentReadOnly }, Requester);
        Start(engine, state, parent);
        engine.ObserveWorker(new("child-worker", "review-host", "provider-b", true, null, Now,
            "child-session", "child-workspace", Candidate, "boot", TemporaryWorker: true), ReviewHost);
        var caller = new CoordinationPrincipal("helper-client", "client", "unused", ["sample"], "helper-host", "helper-worker");
        var child = Help("child") with { Capability = "review", RequiredProvider = "provider-b",
            ReturnTo = new("helper-worker", "helper-host", "provider-a", "helper-session", Candidate, parent.Request.Id, parent.Generation) };
        Assert.Equal("write-help-source-handoff-required", Assert.Throws<CoordinationConflictException>(() =>
            engine.Submit(child, caller)).Message);
        Assert.False(state.Work.ContainsKey("child"));
        Assert.Equal("admitted", engine.Submit(child with { ReadOnly = true }, caller).State);
        Assert.Equal("started", state.Work[parent.Request.Id].State);
    }
}
