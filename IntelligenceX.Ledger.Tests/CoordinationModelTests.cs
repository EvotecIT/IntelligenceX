using System.Text.Json;
using System.Text.Json.Nodes;
using IntelligenceX.Ledger.Agents;
using IntelligenceX.Ledger.Contracts;
using IntelligenceX.Ledger.Coordination;
using Xunit;

namespace IntelligenceX.Ledger.Tests;

public sealed class CoordinationModelTests {
    private static readonly DateTimeOffset Now = new(2030, 1, 1, 0, 0, 0, TimeSpan.Zero);
    private const string Commit = "0123456789abcdef0123456789abcdef01234567";
    private static readonly CoordinationPrincipal Sessions = new("pc-sessions", "client", "unused", ["sample"], "pc");
    private static readonly CoordinationPrincipal Operator = new("operator", "operator", "unused", ["sample"]);

    private static CoordinationEngine Engine(out CoordinationState state) {
        state = new CoordinationState { Policy = new(1, [
            new("pc", 4, 1, 4, 4096, ["windows-shell"], AllowedCreateProviders: ["claude", "codex"]) ]) };
        return new CoordinationEngine(state, Now);
    }

    private static CoordinationAsk Ask(string id) =>
        new(id, "Review the change.", "sample", "main", Commit, "codex", "requester-thread", "pc");

    [Fact]
    public void EachKindGetsThePolicyModelForItsProvider() {
        var engine = Engine(out _);
        var review = engine.Ask(Ask("review") with { Output = "review" }, Sessions);
        Assert.Equal(("claude", "sonnet", "medium"), (review.Request.RequiredProvider, review.Request.Helper!.Model, review.Request.Helper.Effort));
        var work = engine.Ask(Ask("engine"), Sessions);
        Assert.Equal(("claude", "opus", "medium"), (work.Request.RequiredProvider, work.Request.Helper!.Model, work.Request.Helper.Effort));
        var visual = engine.Ask(Ask("visual") with { Output = "visual-review" }, Sessions);
        Assert.Equal(("opus", "medium"), (visual.Request.Helper!.Model, visual.Request.Helper.Effort));
        // Naming Codex keeps the kind: a review at medium effort on the machine's Codex model.
        var codex = engine.Ask(Ask("codex-review") with { Output = "review", Provider = "codex" }, Sessions);
        Assert.Equal(("codex", null, "medium"), (codex.Request.RequiredProvider, codex.Request.Helper!.Model, codex.Request.Helper.Effort));
        var quick = engine.Ask(Ask("quick") with { Kind = "quick" }, Sessions);
        Assert.Equal(("sonnet", "low"), (quick.Request.Helper!.Model, quick.Request.Helper.Effort));
    }

    [Fact]
    public void TheAskWinsOverThePolicyAndARetryWithoutItsChoiceStillMatches() {
        var engine = Engine(out var state);
        var risky = engine.Ask(Ask("risky") with { Output = "review", Model = "opus", Effort = "high" }, Sessions);
        Assert.Equal(("claude", "opus", "high"), (risky.Request.RequiredProvider, risky.Request.Helper!.Model, risky.Request.Helper.Effort));
        Assert.Equal("ask-effort-unsupported", Assert.Throws<CoordinationConflictException>(() =>
            engine.Ask(Ask("ultra") with { Provider = "claude", Effort = "ultra" }, Sessions)).Message);

        var first = engine.Ask(Ask("again") with { Output = "review" }, Sessions);
        Assert.Same(state.Work["again"], engine.Ask(Ask("again") with { Output = "review" }, Sessions));
        Assert.Equal("request-id-conflict", Assert.Throws<CoordinationConflictException>(() =>
            engine.Ask(Ask("again") with { Output = "review", Model = "haiku" }, Sessions)).Message);
        Assert.Equal("sonnet", first.Request.Helper!.Model);
    }

    [Fact]
    public void ModelsAreEditedByPathAndClaudeCannotBeGivenUltra() {
        var engine = Engine(out var state);
        var updated = engine.SetPolicyValue(new("ask.models.review.claude.model", JsonDocument.Parse("\"opus\"").RootElement, 1), Operator);
        Assert.Equal("opus", updated.Ask.Models["review"]["claude"].Model);
        Assert.Equal("medium", updated.Ask.Models["review"]["claude"].Effort);
        Assert.Equal("opus", engine.Ask(Ask("after-edit") with { Output = "review" }, Sessions).Request.Helper!.Model);
        Assert.Equal("invalid-ask-policy", Assert.Throws<CoordinationConflictException>(() =>
            engine.SetPolicyValue(new("ask.models.review.claude.effort", JsonDocument.Parse("\"ultra\"").RootElement, state.Policy.Revision), Operator)).Message);
        Assert.Equal("invalid-ask-policy", Assert.Throws<CoordinationConflictException>(() =>
            engine.SetPolicyValue(new("ask.models.review.gemini", JsonDocument.Parse("{\"model\":\"x\"}").RootElement, state.Policy.Revision), Operator)).Message);
    }

    [Fact]
    public void CodexGlobalDefaultsAreRecordedAtAdmissionAndUltraNeverReachesClaude() {
        var engine = Engine(out var state);
        state.Policy.Ask.CodexModel = "gpt-6";
        state.Policy.Ask.Models["review"].Remove("codex");
        var work = engine.Ask(Ask("codex-review") with { Output = "review", Provider = "codex" }, Sessions);
        // Recorded on the request, so editing the policy afterwards leaves this ask as it was.
        Assert.Equal(("gpt-6", null), (work.Request.Helper!.Model, work.Request.Helper.Effort));
        state.Policy.Ask.CodexModel = "gpt-7";
        Assert.Same(state.Work["codex-review"], engine.Ask(Ask("codex-review") with { Output = "review", Provider = "codex", Model = "gpt-6" }, Sessions));

        var claudeUltra = work.Request with { Id = "submitted-ultra", RequiredProvider = "claude", Helper = new(Effort: "ultra") };
        Assert.Equal("invalid-helper-options", Assert.Throws<CoordinationConflictException>(() => engine.Submit(claudeUltra, Sessions)).Message);
    }

    [Fact]
    public void TheClaudeHelperIsStartedWithTheChosenModelAndEffort() {
        var arguments = ClaudeRunner.Arguments("session", false, new JsonObject(), "sonnet", "bypassPermissions", "medium").ToArray();
        Assert.Equal(["--model", "sonnet", "--effort", "medium"], arguments[^4..]);
        Assert.DoesNotContain("--effort", ClaudeRunner.Arguments("session", false, new JsonObject(), null, "bypassPermissions").ToArray());
    }
}
