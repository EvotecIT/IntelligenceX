using IntelligenceX.Ledger.Agents;
using Xunit;

namespace IntelligenceX.Ledger.Tests;

public sealed class CodexActiveTurnTests {
    // Protects resumed helpers: Codex refusing a turn because another is still active must be waited out and retried,
    // not counted as the helper failing to start (which ended two CanopyX release asks before they did their work).
    [Fact]
    public void AnActiveTurnRefusalIsRecognisedAndOtherRefusalsAreNot() {
        Assert.True(CodexRunner.ActiveTurnRefusal(new CodexRpcException("failed to submit turn input: ActiveTurnOutputSchemaMismatch", -32600)));
        Assert.False(CodexRunner.ActiveTurnRefusal(new CodexRpcException("model not available", -32600)));
    }

    // Protects the wait's bound: settling and retrying share one deadline, so a pause never runs past it.
    [Fact]
    public void APauseNeverRunsPastTheSharedDeadline() {
        Assert.Equal(TimeSpan.FromSeconds(15), CodexRunner.Remaining(DateTimeOffset.UtcNow.AddMinutes(5), TimeSpan.FromSeconds(15)));
        Assert.True(CodexRunner.Remaining(DateTimeOffset.UtcNow.AddSeconds(3), TimeSpan.FromSeconds(15)) <= TimeSpan.FromSeconds(3));
        Assert.Equal(TimeSpan.Zero, CodexRunner.Remaining(DateTimeOffset.UtcNow.AddSeconds(-1), TimeSpan.FromSeconds(15)));
    }

    [Fact]
    public void TheSameParametersCanBeSentInASecondRequest() {
        // A resumed ask forks an archived thread: the fork is refused, the source unarchived and the same fork sent again.
        var parameters = new System.Text.Json.Nodes.JsonObject { ["threadId"] = "source", ["excludeTurns"] = true };
        var first = CodexAppServer.Request(1, "thread/fork", parameters);
        var second = CodexAppServer.Request(3, "thread/fork", parameters);
        Assert.Equal(first["params"]!.ToJsonString(), second["params"]!.ToJsonString());
        Assert.Equal("source", second["params"]!["threadId"]!.GetValue<string>());
    }
}
