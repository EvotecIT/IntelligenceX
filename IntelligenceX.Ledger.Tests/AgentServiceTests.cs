using System.Text;
using System.Text.Json;
using IntelligenceX.Ledger.Agents;
using Xunit;

namespace IntelligenceX.Ledger.Tests;

public sealed class AgentServiceTests {
    [Theory]
    [InlineData("{\"outcome\":\"question\",\"text\":\"Which branch?\"}", "question", "Which branch?")]
    [InlineData("{\"outcome\":\"result\",\"text\":\"All tests pass.\"}", "result", "All tests pass.")]
    [InlineData("Plain answer without the schema.", "result", "Plain answer without the schema.")]
    [InlineData("  ", "none", "")]
    public void HelperAnswersAreReadFromTheStructuredFinalMessage(string text, string outcome, string body) =>
        Assert.Equal((outcome, body), AgentService.ParseAnswer(text));

    [Fact]
    public void LongResultsAreBoundedOnACharacterBoundaryWithAPointerToTheThread() {
        var text = string.Concat(Enumerable.Repeat("żółw ", 3000));
        var bounded = AgentService.Bound(text, "thread-1");
        Assert.True(Encoding.UTF8.GetByteCount(bounded) <= 8192);
        Assert.EndsWith("Codex thread thread-1.]", bounded);
        Assert.Equal("short", AgentService.Bound("short", "thread-1"));
    }

    [Fact]
    public void ClaudeSenderReadsTheSendMessageResultAndUsage() {
        string[] transcript = [
            Line(new { type = "system", subtype = "init" }),
            Line(new { type = "assistant", message = new { model = "claude-haiku-5-5", content = new object[] {
                new { type = "tool_use", id = "tool-1", name = "SendMessage", input = new { to = "session" } } } } }),
            Line(new { type = "user", message = new { content = new object[] {
                new { type = "tool_result", tool_use_id = "tool-1", content = "{\"success\":true,\"message\":\"delivered\"}" } } } }),
            Line(new { type = "result", total_cost_usd = 0.0031, usage = new {
                input_tokens = 20, cache_creation_input_tokens = 100, cache_read_input_tokens = 5000, output_tokens = 30 } })
        ];
        var outcome = ClaudeSender.Parse(transcript, "haiku");
        Assert.Equal(("submitted", "claude-haiku-5-5", 0.0031), (outcome.Stage, outcome.Model, outcome.CostUsd));
        Assert.Equal(new TurnTokens(120, 5000, 30, 5150), outcome.Tokens);
    }

    [Fact]
    public void ClaudeSenderNeverReportsAMissingOrFailedSendAsSubmitted() {
        Assert.Equal("not-submitted", ClaudeSender.Parse([Line(new { type = "result", usage = new { input_tokens = 1 } })], "haiku").Stage);
        string[] failed = [
            Line(new { type = "assistant", message = new { content = new object[] { new { type = "tool_use", id = "t", name = "SendMessage" } } } }),
            Line(new { type = "user", message = new { content = new object[] {
                new { type = "tool_result", tool_use_id = "t", is_error = true, content = "recipient not found" } } } })
        ];
        Assert.Equal("uncertain", ClaudeSender.Parse(failed, "haiku").Stage);
    }

    [Fact]
    public async Task TurnWatchSumsOnlyItsOwnTurnAndCompletesOnItsNotification() {
        var watch = new TurnWatch("thread");
        watch.Follow("turn-2");
        watch.Observe("thread/tokenUsage/updated", Json(new { threadId = "thread", turnId = "turn-1", tokenUsage = new { last = new { inputTokens = 999, totalTokens = 999 } } }));
        watch.Observe("thread/tokenUsage/updated", Json(new { threadId = "thread", turnId = "turn-2", tokenUsage = new { last = new { inputTokens = 100, cachedInputTokens = 60, outputTokens = 10, totalTokens = 110 } } }));
        watch.Observe("thread/tokenUsage/updated", Json(new { threadId = "thread", turnId = "turn-2", tokenUsage = new { last = new { inputTokens = 200, cachedInputTokens = 150, outputTokens = 20, totalTokens = 220 } } }));
        watch.Observe("item/completed", Json(new { threadId = "thread", turnId = "turn-2", item = new { type = "agentMessage", text = "{\"outcome\":\"result\",\"text\":\"done\"}" } }));
        Assert.False(watch.Completed.IsCompleted);
        watch.Observe("turn/completed", Json(new { threadId = "other", turn = new { id = "turn-2", status = "completed" } }));
        Assert.False(watch.Completed.IsCompleted);
        watch.Observe("turn/completed", Json(new { threadId = "thread", turn = new { id = "turn-2", status = "completed", items = Array.Empty<object>() } }));
        var (status, text, _) = await watch.Completed;
        Assert.Equal(("completed", "{\"outcome\":\"result\",\"text\":\"done\"}"), (status, text));
        Assert.Equal(new TurnTokens(300, 210, 30, 330), watch.Tokens);
    }

    [Fact]
    public void BootIdentitySurvivesServiceRestartsWithinOneBoot() {
        var directory = Path.Combine(Path.GetTempPath(), "ix-agent-boot-" + Guid.NewGuid().ToString("N"));
        try {
            Directory.CreateDirectory(directory);
            var first = BootIdentity.Get(directory, "pc");
            Assert.StartsWith("boot-", first);
            Assert.Equal(first, BootIdentity.Get(directory, "pc"));
        } finally { Directory.Delete(directory, recursive: true); }
    }

    private static string Line(object value) => JsonSerializer.Serialize(value);
    private static JsonElement Json(object value) => JsonSerializer.SerializeToElement(value);
}
