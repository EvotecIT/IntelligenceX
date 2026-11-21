using IntelligenceX.Ledger.Contracts;
using IntelligenceX.Ledger.Coordination;
using Xunit;

namespace IntelligenceX.Ledger.Tests;

public sealed class CoordinationAskTests {
    private static readonly DateTimeOffset Now = new(2030, 1, 1, 0, 0, 0, TimeSpan.Zero);
    private static readonly string Commit = new('b', 40);
    private static readonly CoordinationPrincipal Sessions = new("pc-sessions", "client", "unused", ["sample"], "pc");
    private static readonly CoordinationPrincipal PcHost = new("pc-host", "host", "unused", ["sample"], "pc");
    private static readonly CoordinationPrincipal MacHost = new("mac-host", "host", "unused", ["sample"], "mac");

    private static CoordinationState CreateState() {
        var state = new CoordinationState { Policy = new(1, [
            new("pc", 1, 1, 4, 4096, ["windows-shell"], AllowedCreateProviders: ["codex"]),
            new("mac", 2, 1, 4, 4096, ["macos-shell"], AllowedCreateProviders: ["codex"]) ]) };
        Observe(new CoordinationEngine(state, Now), Now);
        return state;
    }

    private static void Observe(CoordinationEngine engine, DateTimeOffset at) {
        engine.ObserveHost(new("pc", "boot", at, ["windows-shell"], CreatableProviders: ["codex"]), PcHost);
        engine.ObserveHost(new("mac", "boot", at, ["macos-shell"], CreatableProviders: ["codex"]), MacHost);
    }

    private static CoordinationAsk Ask(string id = "ask-1", string machine = "mac") =>
        new(id, "Run the macOS tests and report failures.", "sample", "main", Commit, "codex", "requester-thread", machine);

    /// <summary>Binds and starts the helper thread the way the machine's agent service does.</summary>
    private static ExecutionReceipt StartHelper(CoordinationEngine engine, CoordinationWork work, string thread = "helper-thread") {
        engine.ObserveWorker(new("codex-" + thread, "mac", "codex", true, null, Now, thread, "workspace-" + thread,
            work.Request.Checkpoint, "boot", TemporaryWorker: true), MacHost);
        engine.BindSession(new(work.Request.Id, work.Generation, work.GrantId!, "codex-" + thread, "mac", "codex", thread,
            "workspace-" + thread, work.Request.Checkpoint), MacHost);
        var receipt = new ExecutionReceipt(work.GrantId!, work.Generation, "boot", "execution-" + work.Request.Id);
        engine.Receipt(work.Request.Id, "submitted", receipt, MacHost);
        engine.Receipt(work.Request.Id, "started", receipt, MacHost);
        return receipt;
    }

    [Fact]
    public void AskCreatesGoalAndAdmittedCreationGrantInOneCall() {
        var state = CreateState();
        var engine = new CoordinationEngine(state, Now);
        var work = engine.Ask(Ask(), Sessions);
        Assert.Equal("admitted", work.State);
        Assert.Equal(("mac", "codex", "worker-creation-required"), (work.Machine, work.Provider, work.Reason));
        Assert.Equal("ask", work.Origin);
        Assert.Equal("macos-shell", work.Request.Capability);
        Assert.Equal(("pc", "codex", "requester-thread"), (work.Request.ReturnTo!.MachineId, work.Request.ReturnTo.Provider, work.Request.ReturnTo.SessionId));
        Assert.Equal("pc-sessions", state.Goals["ask-ask-1"].Owner);
        Assert.Same(state.Work["ask-1"], engine.Ask(Ask(), Sessions));
        // A second ask from the same branch is not held behind the first: helpers use their own workspaces.
        Assert.Equal("admitted", engine.Ask(Ask("ask-2"), Sessions).State);
    }

    [Fact]
    public void AnAskIdIsReusedOnlyForTheSameAskEvenAfterItIsArchived() {
        var state = CreateState();
        var engine = new CoordinationEngine(state, Now);
        engine.Ask(Ask(), Sessions);
        Assert.Equal("request-id-conflict", Assert.Throws<CoordinationConflictException>(() =>
            engine.Ask(Ask() with { Commit = new string('c', 40) }, Sessions)).Message);
        Assert.Equal("request-id-conflict", Assert.Throws<CoordinationConflictException>(() => engine.Ask(Ask(machine: "pc"), Sessions)).Message);

        Complete(engine, state, "done-1");
        var archiving = new CoordinationEngine(state, Now.AddDays(state.Policy.RetainFinishedDays + 1));
        archiving.Schedule();
        var entry = Assert.Single(archiving.Archived, e => e.RequestId == "done-1");
        var retry = new CoordinationEngine(state, Now.AddDays(state.Policy.RetainFinishedDays + 1));
        Assert.Equal("succeeded", retry.Ask(Ask("done-1"), Sessions, entry).State);
        Assert.False(state.Work.ContainsKey("done-1"));
        Assert.Equal("request-id-conflict", Assert.Throws<CoordinationConflictException>(() =>
            retry.Ask(Ask("done-1") with { Task = "Something else." }, Sessions, entry)).Message);
    }

    [Fact]
    public void ADeliveryLeftSendingBecomesUncertainAndTheRequesterIsToldWhereToReadIt() {
        var state = CreateState();
        var engine = new CoordinationEngine(state, Now);
        var work = engine.Ask(Ask(), Sessions);
        var receipt = StartHelper(engine, work);
        engine.RaiseQuestion("ask-1", new(work.Generation, work.GrantId!, "boot", "Which Xcode version?"), MacHost);
        engine.RecordQuestionDelivery("ask-1", 1, new("question", "send-1", "sending"), PcHost);
        engine.Answer("ask-1", 1, new("16.2"), Sessions);
        engine.RecordQuestionDelivery("ask-1", 1, new("answer", "answer-1", "sending"), MacHost);
        engine.RecordQuestionDelivery("ask-1", 1, new("answer", "answer-1", "submitted", "codex-turn-2"), MacHost);
        engine.Receipt("ask-1", "terminal", receipt with { ResultRef = "result-1", Succeeded = true, ExclusionVerified = true }, MacHost);
        engine.ObserveWorker(new("codex-requester-thread", "pc", "codex", true, null, Now, "requester-thread", BootId: "boot"), PcHost);
        engine.BeginResultDelivery("ask-1", work.Generation, "deliver-1", PcHost);

        var later = new CoordinationEngine(state, Now.AddMinutes(state.Policy.Ask.DeliveryUnconfirmedMinutes + 1));
        later.Schedule();
        Assert.Equal(("uncertain", "delivery-unconfirmed"), (state.ResultOutbox["ask-1"].State, state.ResultOutbox["ask-1"].EvidenceRef));
        Assert.Equal("uncertain", state.Questions["ask-1#1"].Delivery!.Stage);
        var notice = Assert.Single(state.Questions.Values, q => q.Kind == "notice");
        Assert.Contains("request_result", notice.Text);
        // A late receipt from the host that sent it still settles it.
        later.RecordResultDelivery("ask-1", work.Generation, new("deliver-1", "submitted", "codex-turn"), PcHost);
        Assert.NotNull(state.ResultOutbox["ask-1"].ConsumedAtUtc);
    }

    [Fact]
    public void RoutingPicksAProviderThatAMachineWithTheCapabilityOffers() {
        var state = new CoordinationState { Policy = new(1, [
            new("pc", 1, 1, 4, 4096, ["windows-shell"], AllowedCreateProviders: ["claude"]),
            new("mac", 2, 1, 4, 4096, ["macos-shell"], AllowedCreateProviders: ["codex"]) ]) };
        state.Policy.Routing["engine"] = ["claude", "codex"];
        var engine = new CoordinationEngine(state, Now);
        Observe(engine, Now);
        var work = engine.Ask(Ask(machine: null!) with { MachineId = null, Capability = "macos-shell" }, Sessions);
        Assert.Equal(("codex", "macos-shell"), (work.Request.RequiredProvider, work.Request.Capability));
    }

    [Fact]
    public void AResultAHostReportsUncertainTellsTheAskerOnceWhereToReadIt() {
        var state = CreateState();
        var engine = new CoordinationEngine(state, Now);
        var work = engine.Ask(Ask(), Sessions);
        var receipt = StartHelper(engine, work);
        engine.Receipt("ask-1", "terminal", receipt with { ResultRef = "result-1", Succeeded = true, ExclusionVerified = true }, MacHost);
        engine.ObserveWorker(new("codex-requester-thread", "pc", "codex", true, null, Now, "requester-thread", BootId: "boot"), PcHost);
        engine.BeginResultDelivery("ask-1", work.Generation, "deliver-1", PcHost);
        engine.RecordResultDelivery("ask-1", work.Generation, new("deliver-1", "uncertain", "codex-delivery-unresolved"), PcHost);
        engine.RecordResultDelivery("ask-1", work.Generation, new("deliver-1", "uncertain", "codex-delivery-unresolved"), PcHost);
        Assert.Contains("request_result", Assert.Single(state.Questions.Values, q => q.Kind == "notice").Text);
    }

    [Fact]
    public void AResultQueuedIntoTheAskersOwnThreadSendsNoNoticeBehindIt() {
        var state = CreateState();
        var engine = new CoordinationEngine(state, Now);
        var work = engine.Ask(Ask(), Sessions);
        var receipt = StartHelper(engine, work);
        engine.Receipt("ask-1", "terminal", receipt with { ResultRef = "result-1", Succeeded = true, ExclusionVerified = true }, MacHost);
        engine.ObserveWorker(new("codex-requester-thread", "pc", "codex", true, null, Now, "requester-thread", BootId: "boot"), PcHost);
        engine.BeginResultDelivery("ask-1", work.Generation, "deliver-1", PcHost);
        // Queued while the thread was busy: the result waits there for the next turn, and a notice would queue behind it.
        engine.RecordResultDelivery("ask-1", work.Generation, new("deliver-1", "uncertain", "codex-queued-not-resumed"), PcHost);
        Assert.Equal("uncertain", state.ResultOutbox["ask-1"].State);
        Assert.DoesNotContain(state.Questions.Values, q => q.Kind == "notice");
    }

    [Fact]
    public void AnAskGetsANewHelperEvenWhenAnIdleTemporaryWorkerMatches() {
        var state = CreateState();
        var engine = new CoordinationEngine(state, Now);
        engine.ObserveWorker(new("codex-idle", "mac", "codex", true, null, Now, "idle", "workspace-idle",
            new WorkCheckpoint("sample", "main", Commit, null, "ask", "ask"), "boot", TemporaryWorker: true), MacHost);
        var work = engine.Ask(Ask(), Sessions);
        Assert.Equal(("admitted", null, "worker-creation-required"), (work.State, work.Assignee, work.Reason));
    }

    [Fact]
    public void AnAskReturnsToTheWorkerTheSessionIsAlreadyEnrolledAs() {
        var state = CreateState();
        var engine = new CoordinationEngine(state, Now);
        engine.ObserveWorker(new("pc-session-worker", "pc", "codex", true, null, Now, "requester-thread", BootId: "boot"), PcHost);
        Assert.Equal("pc-session-worker", engine.Ask(Ask(), Sessions).Request.ReturnTo!.WorkerId);
    }

    [Fact]
    public void AQuestionTheHelperStoppedWaitingOnIsNotOfferedToTheRequester() {
        var state = CreateState();
        var engine = new CoordinationEngine(state, Now);
        var work = engine.Ask(Ask(), Sessions);
        StartHelper(engine, work);
        engine.RaiseQuestion("ask-1", new(work.Generation, work.GrantId!, "boot", "Which Xcode version?"), MacHost);
        engine.RecordQuestionDelivery("ask-1", 1, new("question", "hold-1", "held", "claude-signed-out"), PcHost);
        Assert.Single(engine.QuestionsToDeliver(PcHost, 0).Items);
        var later = new CoordinationEngine(state, Now.AddMinutes(state.Policy.Ask.QuestionWaitMinutes + 1));
        later.Schedule();
        Assert.Equal("expired", state.Questions["ask-1#1"].State);
        Assert.Empty(later.QuestionsToDeliver(PcHost, 0).Items);
        Assert.Single(later.QuestionsToDeliver(MacHost, 0).Items);
    }

    [Fact]
    public void ArchivedRequestsTakeTheirResultFilesToTheArchiveTier() {
        var root = Path.Combine(Path.GetTempPath(), "ix-ledger-tests", Guid.NewGuid().ToString("N"));
        try {
            var state = CreateState();
            Complete(new CoordinationEngine(state, Now), state, "done-1");
            var archiving = new CoordinationEngine(state, Now.AddDays(state.Policy.RetainFinishedDays + 1));
            archiving.Schedule();
            var entry = Assert.Single(archiving.Archived);
            var artifacts = new CoordinationArtifactStore(root);
            var artifact = artifacts.Put("done-1", entry.Work.Generation, entry.Work.GrantId!, "The tests pass.");
            artifacts.ArchiveEntries([entry with { Work = entry.Work with { ResultRef = artifact.ResultRef } }]);
            Assert.Empty(Directory.EnumerateFiles(root));
            Assert.Equal(2, Directory.EnumerateFiles(Path.Combine(root, CoordinationArtifactStore.ArchiveDirectoryName)).Count());
            Assert.Equal("The tests pass.", artifacts.ReadForAssignment("done-1", entry.Work.Generation, entry.Work.GrantId!, artifact.ResultRef)!.Content);
        } finally {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void AskKeepsTheAddressAClaudeSessionIsReachedBy() {
        var state = CreateState();
        var engine = new CoordinationEngine(state, Now);
        var work = engine.Ask(Ask() with { ReturnProvider = "claude", ReturnSessionId = "cli-session", ReturnAddress = "Build the agent service [bba9bb]" }, Sessions);
        Assert.Equal(("claude", "cli-session", "Build the agent service [bba9bb]"),
            (work.Request.ReturnTo!.Provider, work.Request.ReturnTo.SessionId, work.Request.ReturnTo.Address));
        Assert.Equal("invalid-ask", Assert.Throws<CoordinationConflictException>(() =>
            engine.Ask(Ask("bad") with { ReturnAddress = "line\nbreak" }, Sessions)).Message);
    }

    [Fact]
    public void AskRoutesByPolicyAndRejectsWhatPolicyDoesNotAllow() {
        var state = CreateState();
        var engine = new CoordinationEngine(state, Now);
        Assert.Equal("codex", engine.Ask(Ask() with { Kind = "engine" }, Sessions).Provider);
        Assert.Equal("ask-provider-unavailable", Assert.Throws<CoordinationConflictException>(() =>
            engine.Ask(Ask("other") with { Provider = "claude" }, Sessions)).Message);
        Assert.Equal("ask-machine-or-capability-required", Assert.Throws<CoordinationConflictException>(() =>
            engine.Ask(Ask("any") with { MachineId = null }, Sessions)).Message);
        Assert.Equal("ask-role-denied", Assert.Throws<CoordinationConflictException>(() => engine.Ask(Ask("host"), MacHost)).Message);
        Assert.Equal("project-not-authorized", Assert.Throws<CoordinationConflictException>(() =>
            engine.Ask(Ask("foreign") with { Project = "elsewhere" }, Sessions)).Message);
        Assert.Equal("request-id-conflict", Assert.Throws<CoordinationConflictException>(() =>
            engine.Ask(Ask() with { Task = "Something else." }, Sessions)).Message);
    }

    [Fact]
    public void QuestionReachesRequesterAndAnswerResumesTheSameHelper() {
        var state = CreateState();
        var engine = new CoordinationEngine(state, Now);
        var work = engine.Ask(Ask(), Sessions);
        var receipt = StartHelper(engine, work);
        var question = engine.RaiseQuestion("ask-1", new(work.Generation, work.GrantId!, "boot", "Which Xcode version?"), MacHost);
        Assert.Equal("question", state.Work["ask-1"].State);
        Assert.Same(question, engine.RaiseQuestion("ask-1", new(work.Generation, work.GrantId!, "boot", "Which Xcode version?"), MacHost));
        engine.Receipt("ask-1", "renew", receipt, MacHost);

        // The requester's host delivers the question; the helper's host has nothing to deliver yet.
        Assert.Equal("question", Assert.Single(engine.QuestionsToDeliver(PcHost, 0).Items).Target);
        Assert.Empty(engine.QuestionsToDeliver(MacHost, 0).Items);
        engine.RecordQuestionDelivery("ask-1", 1, new("question", "send-1", "sending"), PcHost);
        Assert.Empty(engine.QuestionsToDeliver(PcHost, 0).Items);
        Assert.Equal("delivery-needs-reconciliation", Assert.Throws<CoordinationConflictException>(() =>
            engine.RecordQuestionDelivery("ask-1", 1, new("question", "send-2", "sending"), PcHost)).Message);
        engine.RecordQuestionDelivery("ask-1", 1, new("question", "send-1", "submitted", "codex-turn-1"), PcHost);

        Assert.Equal("answer-owner-conflict", Assert.Throws<CoordinationConflictException>(() =>
            engine.Answer("ask-1", 1, new("16.2"), new("someone", "client", "unused", ["sample"], "pc"))).Message);
        engine.Answer("ask-1", 1, new("Xcode 16.2"), Sessions);
        var answer = Assert.Single(engine.QuestionsToDeliver(MacHost, 0).Items);
        Assert.Equal(("answer", "Xcode 16.2"), (answer.Target, answer.Question.Answer));
        engine.RecordQuestionDelivery("ask-1", 1, new("answer", "answer-1", "sending"), MacHost);
        engine.RecordQuestionDelivery("ask-1", 1, new("answer", "answer-1", "submitted", "codex-turn-2"), MacHost);
        Assert.Equal("started", state.Work["ask-1"].State);
        Assert.Equal("resumed", state.Questions["ask-1#1"].State);

        engine.Receipt("ask-1", "terminal", receipt with { ResultRef = "result-1", Succeeded = true, ExclusionVerified = true }, MacHost);
        Assert.Equal("intent", state.ResultOutbox["ask-1"].State);
        Assert.Contains("ask-1", state.Retirements.Keys);
    }

    [Fact]
    public void AskResultIsDeliveredOnceToTheAskingSessionAndEndsTheAsk() {
        var state = CreateState();
        var engine = new CoordinationEngine(state, Now);
        var work = engine.Ask(Ask(), Sessions);
        var receipt = StartHelper(engine, work);
        engine.Receipt("ask-1", "terminal", receipt with { ResultRef = "result-1", Succeeded = true, ExclusionVerified = true }, MacHost);
        Assert.Equal("result-recipient-held", Assert.Throws<CoordinationConflictException>(() =>
            engine.BeginResultDelivery("ask-1", work.Generation, "deliver-1", PcHost)).Message);
        // The requester's host attests the asking session just before delivering to it.
        engine.ObserveWorker(new("codex-requester-thread", "pc", "codex", true, null, Now, "requester-thread", BootId: "boot"), PcHost);
        Assert.Equal("result-host-conflict", Assert.Throws<CoordinationConflictException>(() =>
            engine.BeginResultDelivery("ask-1", work.Generation, "deliver-1", MacHost)).Message);
        engine.BeginResultDelivery("ask-1", work.Generation, "deliver-1", PcHost);
        var delivered = engine.RecordResultDelivery("ask-1", work.Generation, new("deliver-1", "submitted", "codex-turn-3"), PcHost);
        Assert.NotNull(delivered.ConsumedAtUtc);
        Assert.True(state.Work["ask-1"].Stages!.ContainsKey("accepted"));
        Assert.Equal("delivery-needs-reconciliation", Assert.Throws<CoordinationConflictException>(() =>
            engine.BeginResultDelivery("ask-1", work.Generation, "deliver-2", PcHost)).Message);
    }

    [Fact]
    public void UncertainOrNegativeDeliveriesFollowTheirOwnRules() {
        var state = CreateState();
        var engine = new CoordinationEngine(state, Now);
        var work = engine.Ask(Ask(), Sessions);
        StartHelper(engine, work);
        engine.RaiseQuestion("ask-1", new(work.Generation, work.GrantId!, "boot", "Which branch?"), MacHost);
        engine.RecordQuestionDelivery("ask-1", 1, new("question", "send-1", "sending"), PcHost);
        engine.RecordQuestionDelivery("ask-1", 1, new("question", "send-1", "not-submitted", "codex-thread-unreachable"), PcHost);
        Assert.Single(engine.QuestionsToDeliver(PcHost, 0).Items);
        engine.RecordQuestionDelivery("ask-1", 1, new("question", "send-2", "sending"), PcHost);
        engine.RecordQuestionDelivery("ask-1", 1, new("question", "send-2", "uncertain", "codex-delivery-unresolved"), PcHost);
        Assert.Empty(engine.QuestionsToDeliver(PcHost, 0).Items);
    }

    [Fact]
    public void PolicyWaitsExpireQuestionsAndTellRequestersAboutUnadmittedAsks() {
        var state = CreateState();
        var engine = new CoordinationEngine(state, Now);
        var work = engine.Ask(Ask(), Sessions);
        StartHelper(engine, work);
        engine.RaiseQuestion("ask-1", new(work.Generation, work.GrantId!, "boot", "Proceed?"), MacHost);
        var later = Now.AddMinutes(state.Policy.Ask.QuestionWaitMinutes + 1);
        var next = new CoordinationEngine(state, later);
        Observe(next, later);
        Assert.Equal("expired", state.Questions["ask-1#1"].State);
        Assert.Equal("answer", Assert.Single(next.QuestionsToDeliver(MacHost, 0).Items).Target);

        // The pc machine has one agent slot; a second ask there waits and its requester hears about it once.
        var held = CreateState();
        var first = new CoordinationEngine(held, Now);
        first.Ask(Ask("a", "pc"), Sessions);
        Assert.Equal("queued", first.Ask(Ask("b", "pc"), Sessions).State);
        var overdue = Now.AddMinutes(held.Policy.Ask.AdmissionMinutes + 1);
        var check = new CoordinationEngine(held, overdue);
        Observe(check, overdue);
        Observe(new CoordinationEngine(held, overdue.AddSeconds(1)), overdue.AddSeconds(1));
        var notice = Assert.Single(held.Questions.Values);
        Assert.Equal(("b", "notice"), (notice.RequestId, notice.Kind));
        Assert.Equal("question", Assert.Single(check.QuestionsToDeliver(PcHost, 0).Items).Target);
    }

    /// <summary>Runs an ask to a delivered result and a retired helper, as the two hosts do.</summary>
    internal static void Complete(CoordinationEngine engine, CoordinationState state, string id) {
        var work = engine.Ask(Ask(id), Sessions);
        var receipt = StartHelper(engine, work, "thread-" + id);
        engine.Receipt(id, "terminal", receipt with { ResultRef = "result-" + id, Succeeded = true, ExclusionVerified = true }, MacHost);
        engine.ObserveWorker(new("codex-requester-thread", "pc", "codex", true, null, Now, "requester-thread", BootId: "boot"), PcHost);
        engine.BeginResultDelivery(id, work.Generation, "deliver-" + id, PcHost);
        engine.RecordResultDelivery(id, work.Generation, new("deliver-" + id, "submitted", "codex-turn"), PcHost);
        engine.RecordUsage(id, new("worker", "codex", "gpt-6", 10, 0, 1, 11, "mac", Now), MacHost);
        var binding = state.Bindings[id];
        foreach (var stage in new[] { "processes-released", "descendants-resolved", "archived", "source-preserved", "workspace-released", "verified" })
            engine.Retire(new(binding, stage, stage + "-" + id, Dirty: stage is not ("source-preserved" or "workspace-released" or "verified"),
                Unpublished: stage is not ("source-preserved" or "workspace-released" or "verified")), MacHost);
    }

    [Fact]
    public void FinishedRequestsLeaveTheLiveStateAfterTheRetentionPeriod() {
        var state = CreateState();
        var engine = new CoordinationEngine(state, Now);
        Complete(engine, state, "done-1");
        Assert.NotNull(state.Retirements["done-1"].CompletedAtUtc);
        var early = new CoordinationEngine(state, Now.AddDays(state.Policy.RetainFinishedDays - 1));
        early.Schedule();
        Assert.Empty(early.Archived);
        var later = Now.AddDays(state.Policy.RetainFinishedDays + 1);
        var archiving = new CoordinationEngine(state, later);
        archiving.Schedule();
        var entry = Assert.Single(archiving.Archived);
        Assert.Equal(("done-1", "succeeded", 1), (entry.RequestId, entry.Work.State, entry.Usage.Length));
        Assert.NotNull(entry.Retirement);
        Assert.NotNull(entry.Goal);
        Assert.False(state.Work.ContainsKey("done-1"));
        Assert.False(state.Goals.ContainsKey("ask-done-1") || state.Bindings.ContainsKey("done-1") || state.ResultOutbox.ContainsKey("done-1") ||
            state.Retirements.ContainsKey("done-1") || state.Usage.ContainsKey("done-1"));
        // Workers last seen before the cutoff with nothing pointing at them go too.
        Assert.DoesNotContain("codex-thread-done-1", state.Workers.Keys);
    }

    [Fact]
    public void ARequestStillNamedByLiveWorkStaysInTheLiveState() {
        var state = CreateState();
        var engine = new CoordinationEngine(state, Now);
        Complete(engine, state, "base");
        state.Work["follow-up"] = state.Work["base"] with { Request = state.Work["base"].Request with { Id = "follow-up", Dependencies = ["base"] },
            State = "queued", Order = 999 };
        var archiving = new CoordinationEngine(state, Now.AddDays(state.Policy.RetainFinishedDays + 1));
        archiving.Schedule();
        Assert.DoesNotContain(archiving.Archived, entry => entry.RequestId == "base");
        Assert.True(state.Work.ContainsKey("base"));
    }

    [Fact]
    public async Task ArchivedRequestsStayReadableWithTheirUsage() {
        var directory = Path.Combine(Path.GetTempPath(), "ix-ledger-tests", Guid.NewGuid().ToString("N"));
        try {
            using var store = new CoordinationStore(Path.Combine(directory, "coordination.sqlite"), CreateState().Policy);
            await store.InitializeAsync();
            await store.ExecuteAsync((engine, state) => {
                Observe(engine, Now);
                Complete(engine, state, "kept");
                return true;
            }, Now);
            var later = Now.AddDays(15);
            var bytesBefore = await store.ReadAsync(state => System.Text.Json.JsonSerializer.Serialize(state).Length);
            var archivedCount = await store.ExecuteAsync((engine, _) => { engine.Schedule(); return engine.Archived.Count; }, later);
            Assert.Equal(1, archivedCount);
            Assert.True(await store.ReadAsync(state => System.Text.Json.JsonSerializer.Serialize(state).Length) < bytesBefore);
            var entry = await store.ArchivedAsync("kept");
            Assert.Equal(("kept", 1), (entry!.RequestId, entry.Usage.Length));
            // The archived id stays taken: the store reads the archive inside the command that would reuse it.
            Assert.Equal("succeeded", (await store.ExecuteAsync("kept", (engine, _, archived) => engine.Ask(Ask("kept"), Sessions, archived), later)).State);
            await Assert.ThrowsAsync<CoordinationConflictException>(() =>
                store.ExecuteAsync("kept", (engine, _, archived) => engine.Ask(Ask("kept") with { Task = "Something else." }, Sessions, archived), later));
            var since = await store.ArchivedSinceAsync(Now.AddDays(-1));
            var report = await store.ExecuteAsync((engine, _) => engine.UsageReport(DateOnly.FromDateTime(Now.UtcDateTime), _ => true, since), later);
            Assert.Equal(11, Assert.Single(report.ByRequest).TotalTokens);
        } finally {
            if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void UsageIsRecordedOnceAndTotalledByDay() {
        var state = CreateState();
        var engine = new CoordinationEngine(state, Now);
        engine.Ask(Ask(), Sessions);
        var wake = new CoordinationUsage("wake", "codex", "gpt-6", 1200, 800, 90, 1290, "pc", Now, SessionId: "requester-thread");
        engine.RecordUsage("ask-1", wake, PcHost);
        engine.RecordUsage("ask-1", wake, PcHost);
        engine.RecordUsage("ask-1", new("delivery", "claude", "haiku", 3000, 0, 40, 3040, "pc", Now, 0.004), PcHost);
        Assert.Equal("usage-owner-conflict", Assert.Throws<CoordinationConflictException>(() =>
            engine.RecordUsage("ask-1", wake with { MachineId = "elsewhere" }, PcHost)).Message);
        Assert.Equal(2, state.Usage["ask-1"].Count);
        var report = engine.UsageReport(DateOnly.FromDateTime(Now.UtcDateTime), _ => true);
        var total = Assert.Single(report.ByRequest);
        Assert.Equal((2, 4330L, 0.004), (total.Count, total.TotalTokens, total.CostUsd));
        Assert.Equal(["delivery/claude/haiku", "wake/codex/gpt-6"], report.ByKind.Select(row => row.Key).ToArray());
    }

    [Fact]
    public void AskPolicyIsValidatedAndEditableByPath() {
        var state = CreateState();
        var engine = new CoordinationEngine(state, Now);
        var operator_ = new CoordinationPrincipal("operator", "operator", "unused", ["sample"]);
        var updated = engine.SetPolicyValue(new("ask.questionWaitMinutes", System.Text.Json.JsonDocument.Parse("30").RootElement, 1), operator_);
        Assert.Equal(30, updated.Ask.QuestionWaitMinutes);
        Assert.Equal("invalid-ask-policy", Assert.Throws<CoordinationConflictException>(() =>
            engine.SetPolicyValue(new("ask.codexEffort", System.Text.Json.JsonDocument.Parse("\"extreme\"").RootElement, 2), operator_)).Message);
    }


    [Fact]
    public void AFollowUpForksTheEarlierHelperOnTheSameMachineWithItsOwnOptions() {
        var state = CreateState();
        var engine = new CoordinationEngine(state, Now);
        var running = engine.Ask(Ask("running"), Sessions);
        StartHelper(engine, running, "running-thread");
        Assert.Equal("ask-continues-unavailable", Assert.Throws<CoordinationConflictException>(() =>
            engine.Ask(Ask("too-early", machine: null!) with { Continues = "running" }, Sessions)).Message);

        Complete(engine, state, "first");
        var followUp = Ask("second", machine: null!) with { Continues = "first", Model = "gpt-6", Effort = "high", Output = "review" };
        var work = engine.Ask(followUp, Sessions);
        Assert.Equal(("mac", "codex"), (work.Request.MachineId, work.Request.RequiredProvider));
        Assert.Equal(new CoordinationHelperOptions("gpt-6", "high", "review", "first", "thread-first"), work.Request.Helper);
        Assert.Same(state.Work["second"], engine.Ask(followUp, Sessions));
        Assert.Equal("request-id-conflict", Assert.Throws<CoordinationConflictException>(() =>
            engine.Ask(followUp with { Effort = "low" }, Sessions)).Message);
        Assert.Equal("ask-continues-machine-conflict", Assert.Throws<CoordinationConflictException>(() =>
            engine.Ask(Ask("third", machine: "pc") with { Continues = "first" }, Sessions)).Message);
        var other = Sessions with { Id = "other-sessions" };
        Assert.Equal("ask-continues-unavailable", Assert.Throws<CoordinationConflictException>(() =>
            engine.Ask(Ask("fourth", machine: null!) with { Continues = "first" }, other)).Message);
    }

    [Fact]
    public void HelperOptionsAreValidatedAndAForkIsNeverSuppliedDirectly() {
        var state = CreateState();
        var engine = new CoordinationEngine(state, Now);
        Assert.Equal("invalid-helper-options", Assert.Throws<CoordinationConflictException>(() =>
            engine.Ask(Ask("bad-effort") with { Effort = "extreme" }, Sessions)).Message);
        Assert.Equal("invalid-helper-options", Assert.Throws<CoordinationConflictException>(() =>
            engine.Ask(Ask("bad-output") with { Output = "essay" }, Sessions)).Message);
        // A plain ask carries the policy's choice for its kind and provider: engine work on Codex, at high effort.
        Assert.Equal((null, "high"), (engine.Ask(Ask("plain"), Sessions).Request.Helper!.Model, state.Work["plain"].Request.Helper!.Effort));
        Assert.Equal("ultra", engine.Ask(Ask("ultra") with { Model = "gpt-6.1-sol", Effort = "ultra" }, Sessions).Request.Helper!.Effort);

        var request = state.Work["plain"].Request with { Id = "forged", Helper = new(ContinuesRequestId: "plain", ForkSessionId: "someone-else") };
        Assert.Equal("invalid-helper-options", Assert.Throws<CoordinationConflictException>(() => engine.Submit(request, Sessions)).Message);
    }


    [Fact]
    public void AReviewRoutesToAnOfferedProviderAndAFollowUpNeedsTheEarlierProject() {
        var state = CreateState();
        var engine = new CoordinationEngine(state, Now);
        // These machines offer only Codex, so a review that names Claude has nowhere to run.
        Assert.Equal("ask-provider-unavailable", Assert.Throws<CoordinationConflictException>(() =>
            engine.Ask(Ask("claude-review") with { Provider = "claude", Output = "review" }, Sessions)).Message);
        Assert.Equal("codex", engine.Ask(Ask("routed-review") with { Output = "review" }, Sessions).Request.RequiredProvider);

        Complete(engine, state, "first");
        var moved = Sessions with { Projects = ["other"] };
        Assert.Equal("ask-continues-project-denied", Assert.Throws<CoordinationConflictException>(() =>
            engine.Ask(Ask("second", machine: null!) with { Continues = "first", Project = "other" }, moved)).Message);
        Assert.Equal("macos-shell", engine.Ask(Ask("third", machine: null!) with { Continues = "first" }, Sessions).Request.Capability);
    }
}
