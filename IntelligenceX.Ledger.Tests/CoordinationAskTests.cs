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
}
