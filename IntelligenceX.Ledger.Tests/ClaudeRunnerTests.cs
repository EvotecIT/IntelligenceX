using IntelligenceX.Ledger.Agents;
using Xunit;

namespace IntelligenceX.Ledger.Tests;

public sealed class ClaudeRunnerTests {
    private const string Session = "6b1f0c7e-3a52-4c1e-9d0e-0f2a4b6c8d10";

    // Protects the helper conversation: the first turn creates the session under the saved id and every later turn
    // (an answer, a resume) continues that session instead of starting a new one without the earlier context.
    [Fact]
    public void TheFirstTurnCreatesTheSessionAndLaterTurnsContinueIt() {
        var first = ClaudeRunner.Arguments(Session, false, AgentService.HelperOutputSchema, null, "bypassPermissions").ToArray();
        var later = ClaudeRunner.Arguments(Session, true, AgentService.HelperOutputSchema, "opus", "bypassPermissions").ToArray();
        Assert.Equal(Session, first[Array.IndexOf(first, "--session-id") + 1]);
        Assert.DoesNotContain("--resume", first);
        Assert.Equal(Session, later[Array.IndexOf(later, "--resume") + 1]);
        Assert.Equal("opus", later[Array.IndexOf(later, "--model") + 1]);
        Assert.Contains("\"outcome\"", first[Array.IndexOf(first, "--json-schema") + 1]);
    }

    [Fact]
    public void TheStructuredAnswerModelAndTokensComeFromTheTranscript() {
        var outcome = ClaudeRunner.Parse([
            """{"type":"assistant","message":{"model":"claude-opus-5-5","content":[]}}""",
            """{"type":"result","subtype":"success","is_error":false,"result":"","structured_output":{"outcome":"question","text":"Which branch?"},"usage":{"input_tokens":10,"cache_creation_input_tokens":5,"cache_read_input_tokens":100,"output_tokens":20}}"""
        ], "claude-" + Session, null);
        Assert.Equal("completed", outcome.Status);
        Assert.Equal("claude-opus-5-5", outcome.Model);
        Assert.Equal(("question", "Which branch?"), AgentService.ParseAnswer(outcome.FinalText));
        Assert.Equal(new TurnTokens(15, 100, 20, 135), outcome.Tokens);
    }

    // Protects the machine from a delivery that can never succeed: retries spread out to an hour instead of starting a
    // Codex process every minute.
    [Fact]
    public void ARepeatedlyFailingDeliveryBacksOffToAnHour() {
        Assert.Equal(TimeSpan.FromMinutes(1), AgentService.BackoffDelay(1));
        Assert.Equal(TimeSpan.FromMinutes(8), AgentService.BackoffDelay(4));
        Assert.Equal(TimeSpan.FromMinutes(60), AgentService.BackoffDelay(1348));
    }

    // Protects the usage-limit wait: a limit must be recognised as one, so the helper waits instead of failing the ask.
    [Fact]
    public void AUsageLimitIsReportedAsOne() {
        var outcome = ClaudeRunner.Parse(["""{"type":"result","is_error":true,"api_error_status":429,"result":"Usage limit reached"}"""], "t", null);
        Assert.Equal("failed", outcome.Status);
        Assert.True(outcome.UsageLimited);
        Assert.Equal("failed", ClaudeRunner.Parse([], "t", null).Status);
    }
}
