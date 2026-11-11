using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using IntelligenceX.Ledger.Contracts;
using IntelligenceX.Ledger.Coordination;
using Xunit;
using static IntelligenceX.Ledger.Tests.CoordinationLifecycleTests;

namespace IntelligenceX.Ledger.Tests;

public sealed class CoordinationArtifactArchiveTests {
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void PressureArchivesConsumedCompletedAssignmentsAndRetainsImmutableReplay(bool personal) {
        var root = LedgerTestPaths.NewDirectory("artifact-archive");
        try {
            var state = CreateState();
            var completed = Complete(root, state, personal: personal);
            var before = JsonSerializer.Serialize(state);
            FillActiveBudget(root);
            var artifacts = new CoordinationArtifactStore(root);
            var next = artifacts.Put("next", 1, "grant-next", "Next independent answer.", state);
            Assert.Equal(4096, Directory.GetFiles(root).Length);
            Assert.False(File.Exists(completed.Assignment));
            Assert.Equal(2, Directory.GetFiles(Path.Combine(root, "archive")).Length);
            Assert.Equal(before, JsonSerializer.Serialize(state));
            var reopened = new CoordinationArtifactStore(root);
            Assert.Equal(completed.Artifact, reopened.ReadForAssignment("help", completed.Work.Generation,
                completed.Work.GrantId!, completed.Artifact.ResultRef));
            Assert.Equal(completed.Artifact, reopened.Put("help", completed.Work.Generation,
                completed.Work.GrantId!, completed.Artifact.Content));
            Assert.Equal(next, reopened.Put("next", 1, "grant-next", next.Content));
            Assert.Equal("result-content-identity-conflict", Assert.Throws<CoordinationConflictException>(() =>
                reopened.Put("help", completed.Work.Generation, completed.Work.GrantId!, "Changed answer.")).Message);
        } finally { Directory.Delete(root, recursive: true); }
    }

    [Theory]
    [InlineData("unconsumed")]
    [InlineData("retirement-pending")]
    [InlineData("binding-grant")]
    [InlineData("result-generation")]
    [InlineData("execution-uncertain")]
    public void PressureRetainsAssignmentsWithIncompleteOrContradictoryLifecycle(string hold) {
        var root = LedgerTestPaths.NewDirectory("artifact-held");
        try {
            var state = CreateState();
            var completed = Complete(root, state, consumed: hold != "unconsumed", retired: hold != "retirement-pending");
            if (hold == "binding-grant") state.Bindings["help"] = state.Bindings["help"] with { GrantId = "other-grant" };
            if (hold == "result-generation") state.ResultOutbox["help"] = state.ResultOutbox["help"] with { Generation = 2 };
            if (hold == "execution-uncertain") state.Work["help"] = state.Work["help"] with { State = "reconcile" };
            FillActiveBudget(root);
            var artifacts = new CoordinationArtifactStore(root);
            Assert.Equal("result-store-capacity", Assert.Throws<CoordinationConflictException>(() =>
                artifacts.Put("next", 1, "grant-next", "Next answer.", state)).Message);
            Assert.False(Directory.Exists(Path.Combine(root, "archive")));
            Assert.Equal(4096, Directory.GetFiles(root).Length);
            Assert.Equal(completed.Artifact, artifacts.Put("help", completed.Work.Generation,
                completed.Work.GrantId!, completed.Artifact.Content));
        } finally { Directory.Delete(root, recursive: true); }
    }

    [Fact]
    public void InterruptedContentMovePreservesSharedActiveBindingAndResumesArchivalAfterRestart() {
        var root = LedgerTestPaths.NewDirectory("artifact-interrupted");
        try {
            var state = CreateState();
            var completed = Complete(root, state);
            var artifacts = new CoordinationArtifactStore(root);
            var active = artifacts.Put("active", 1, "active-grant", completed.Artifact.Content);
            var activeBinding = Directory.GetFiles(root, "assignment-*.txt").Single(path => path != completed.Assignment);
            var archive = Path.Combine(root, "archive");
            Directory.CreateDirectory(archive);
            File.Move(Path.Combine(root, completed.Artifact.ResultRef + ".txt"), Path.Combine(archive, completed.Artifact.ResultRef + ".txt"));
            var reopened = new CoordinationArtifactStore(root);
            Assert.Equal(completed.Artifact, reopened.ReadForAssignment("help", completed.Work.Generation,
                completed.Work.GrantId!, completed.Artifact.ResultRef));
            Assert.Equal(active, reopened.ReadForAssignment("active", 1, "active-grant", active.ResultRef));
            Assert.Equal(active, reopened.Put("active", 1, "active-grant", active.Content));
            FillActiveBudget(root);
            var next = reopened.Put("next", 1, "next-grant", active.Content, state);
            Assert.False(File.Exists(completed.Assignment));
            Assert.True(File.Exists(activeBinding));
            Assert.Equal(active, new CoordinationArtifactStore(root).Put("active", 1, "active-grant", active.Content));
            Assert.Equal(completed.Artifact, reopened.Put("help", completed.Work.Generation,
                completed.Work.GrantId!, completed.Artifact.Content));
            Assert.Equal(active, next);
            Assert.False(File.Exists(Path.Combine(root, active.ResultRef + ".txt")));
            Assert.Equal(4096, Directory.GetFiles(root).Length);
        } finally { Directory.Delete(root, recursive: true); }
    }

    [Fact]
    public void NeverCreatedReconciliationCanArchiveAfterRequesterConsumptionWithoutRetirement() {
        var root = LedgerTestPaths.NewDirectory("unbound-result-archive");
        try {
            var state = CreateState();
            state.Policy = state.Policy with { Machines = state.Policy.Machines.Select(machine =>
                machine.Id == "helper-host" ? machine with { AllowedCreateProviders = ["provider-a"] } : machine).ToArray() };
            state.Workers.Remove("helper-worker");
            var startedAt = Now.AddSeconds(1);
            var engine = new CoordinationEngine(state, startedAt);
            engine.ObserveHost(state.Hosts["helper-host"] with {
                ObservedAtUtc = startedAt, CreatableProviders = ["provider-a"]
            }, HelperHost);
            var work = engine.Submit(Help() with { ReadOnly = true }, Requester);
            Assert.Equal("admitted", work.State);
            Assert.Null(work.Assignee);
            var artifacts = new CoordinationArtifactStore(root);
            var result = artifacts.Put("help", work.Generation, work.GrantId!, "No native worker was created.");
            var recoveredAt = startedAt.AddSeconds(state.Policy.GrantSeconds + 1);
            engine = new CoordinationEngine(state, recoveredAt);
            engine.ObserveHost(state.Hosts["helper-host"] with { ObservedAtUtc = recoveredAt }, HelperHost);
            var exclusion = new CoordinationReconciliation("help", work.Generation, work.GrantId!, work.BootId!,
                work.BootId!, recoveredAt, "never-created", "verified-native-absence", result.ResultRef, NativeExclusionVerified: true);
            Assert.Equal("cancelled", engine.Reconcile(exclusion, HelperHost).State);
            Assert.Empty(state.Bindings);
            Assert.Empty(state.Retirements);
            FillActiveBudget(root);
            Assert.Equal("result-store-capacity", Assert.Throws<CoordinationConflictException>(() =>
                artifacts.Put("next", 1, "next-grant", "Next independent answer.", state)).Message);
            engine.ConsumeResult("help", work.Generation, result.ResultRef, Candidate, Requester);
            var reopened = JsonSerializer.Deserialize<CoordinationState>(JsonSerializer.Serialize(state))!;
            var next = artifacts.Put("next", 1, "next-grant", "Next independent answer.", reopened);
            Assert.Equal("Next independent answer.", next.Content);
            Assert.Equal(4096, Directory.GetFiles(root).Length);
            Assert.Equal(2, Directory.GetFiles(Path.Combine(root, "archive")).Length);
            Assert.Equal(result, new CoordinationArtifactStore(root).Put("help", work.Generation, work.GrantId!, result.Content));
            Assert.Empty(reopened.Bindings);
            Assert.Empty(reopened.Retirements);
            Assert.Equal(exclusion, reopened.Reconciliations["help"]);
        } finally { Directory.Delete(root, recursive: true); }
    }

    internal static (CoordinationWork Work, CoordinationArtifact Artifact, string Assignment) Complete(string root,
        CoordinationState state, bool personal = false, bool consumed = true, bool retired = true) {
        var engine = new CoordinationEngine(state, Now);
        var worker = state.Workers["helper-worker"] with {
            Id = "result-worker", SessionId = "result-session", WorkspaceId = "result-workspace", TemporaryWorker = !personal
        };
        engine.ObserveWorker(worker, HelperHost);
        var work = engine.Submit(Help() with {
            WorkerId = worker.Id, MachineId = worker.MachineId, ReuseSession = personal, ReadOnly = true
        }, Requester);
        var binding = Start(engine, state, work);
        var artifact = new CoordinationArtifactStore(root).Put("help", work.Generation, work.GrantId!, "Verified durable answer.");
        engine.Receipt("help", "terminal", Execution(work, artifact.ResultRef), HelperHost);
        if (consumed) engine.ConsumeResult("help", work.Generation, artifact.ResultRef, Candidate, Requester);
        if (retired && !personal) Clean(engine, binding);
        return (work, artifact, Directory.GetFiles(root, "assignment-*.txt").Single());
    }

    internal static void FillActiveBudget(string root, int count = 4096) {
        // Valid immutable content without bindings models writes interrupted before assignment publication.
        // These uncertain files must count toward backpressure and must never be reclaimed by completion.
        var existing = Directory.GetFiles(root).Length;
        for (var index = existing; index < count; index++) {
            var bytes = Encoding.UTF8.GetBytes("Unbound interrupted result " + index);
            var name = "result-" + Convert.ToHexStringLower(SHA256.HashData(bytes)) + ".txt";
            File.WriteAllBytes(Path.Combine(root, name), bytes);
        }
    }
}
