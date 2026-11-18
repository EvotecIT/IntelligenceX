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
                new { type = "tool_result", tool_use_id = "tool-1", content = new object[] {
                    new { type = "text", text = "{\"success\":true,\"message\":\"delivered\"}" } } } } } }),
            Line(new { type = "result", total_cost_usd = 0.0031, usage = new {
                input_tokens = 20, cache_creation_input_tokens = 100, cache_read_input_tokens = 5000, output_tokens = 30 } })
        ];
        var outcome = ClaudeSender.Parse(transcript, "haiku");
        Assert.Equal(("submitted", "claude-haiku-5-5", 0.0031), (outcome.Stage, outcome.Model, outcome.CostUsd));
        Assert.Equal(new TurnTokens(120, 5000, 30, 5150), outcome.Tokens);
    }

    [Fact]
    public void ClaudeSenderReportsARefusedSendAsNotSubmittedAndAnUnreadableOneAsUncertain() {
        Assert.Equal("not-submitted", ClaudeSender.Parse([Line(new { type = "result", usage = new { input_tokens = 1 } })], "haiku").Stage);
        string[] failed = [
            Line(new { type = "assistant", message = new { content = new object[] { new { type = "tool_use", id = "t", name = "SendMessage" } } } }),
            Line(new { type = "user", message = new { content = new object[] {
                new { type = "tool_result", tool_use_id = "t", content = new object[] {
                    new { type = "text", text = "{\"success\":false,\"message\":\"No agent named 'x' is reachable.\"}" } } } } } })
        ];
        Assert.Equal(("not-submitted", (string?)null), (ClaudeSender.Parse(failed, "haiku").Stage, ClaudeSender.Parse(failed, "haiku").Failure));
        string[] unknown = [failed[0], Line(new { type = "user", message = new { content = new object[] {
            new { type = "tool_result", tool_use_id = "t", content = "connection reset" } } } })];
        Assert.Equal("uncertain", ClaudeSender.Parse(unknown, "haiku").Stage);
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
    public void AnUnreadableRunRecordIsSetAsideInsteadOfStoppingTheSweep() {
        var directory = Path.Combine(Path.GetTempPath(), "ix-ledger-tests", Guid.NewGuid().ToString("N"));
        try {
            var file = RunRecord.FileFor(directory, "ask-1", 1);
            Directory.CreateDirectory(Path.GetDirectoryName(file)!);
            File.WriteAllText(file, "");
            Assert.Null(RunRecord.Load(directory, "ask-1", 1));
            Assert.False(File.Exists(file));
            Assert.Single(Directory.EnumerateFiles(Path.GetDirectoryName(file)!, "*.unreadable-*"));
        } finally {
            if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void RunRecordsStayUntilTheirHelperIsRetired() {
        var directory = Path.Combine(Path.GetTempPath(), "ix-ledger-tests", Guid.NewGuid().ToString("N"));
        try {
            RunRecord Done(string id, bool retired) => new RunRecord { RequestId = id, Generation = 1, GrantId = "grant", Workspace = "w",
                ThreadId = "thread-" + id, Phase = "done", Retired = retired }.Save(directory);
            Done("waiting-review", retired: false);
            Done("retired", retired: true);
            File.WriteAllText(RunRecord.TokensFile(directory, "waiting-review", 1), "{}");
            foreach (var file in Directory.EnumerateFiles(Path.Combine(directory, "runs"))) File.SetLastWriteTimeUtc(file, DateTime.UtcNow.AddDays(-30));
            RunRecord.Prune(directory, TimeSpan.FromDays(14), _ => false);
            Assert.NotNull(RunRecord.Load(directory, "waiting-review", 1));
            Assert.True(File.Exists(RunRecord.TokensFile(directory, "waiting-review", 1)));
            Assert.Null(RunRecord.Load(directory, "retired", 1));
        } finally {
            if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void TheInboxHookFinishesWithinTheTimeClaudeGivesIt() =>
        Assert.True((IntelligenceX.Ledger.Contracts.LedgerInbox.CollectBudget + IntelligenceX.Ledger.Contracts.LedgerInbox.RecordBudget).TotalSeconds
            < AgentInstaller.HookTimeoutSeconds - 2);

    [Fact]
    public void UsageThatArrivesBeforeTheTurnIdIsKnownCountsForThatTurn() {
        var watch = new TurnWatch("thread");
        watch.Observe("thread/tokenUsage/updated", Json(new { threadId = "thread", turnId = "turn-1", tokenUsage = new { last = new { inputTokens = 100, totalTokens = 110 } } }));
        watch.Observe("thread/tokenUsage/updated", Json(new { threadId = "thread", turnId = "turn-0", tokenUsage = new { last = new { inputTokens = 999, totalTokens = 999 } } }));
        watch.Follow("turn-1");
        Assert.Equal(new TurnTokens(100, 0, 0, 110), watch.Tokens);
        // A new turn starts from zero rather than adding to the turn before it.
        var next = new TurnWatch("thread", watch.Tokens);
        next.Reset();
        next.Follow("turn-2");
        next.Observe("thread/tokenUsage/updated", Json(new { threadId = "thread", turnId = "turn-2", tokenUsage = new { last = new { inputTokens = 5, totalTokens = 6 } } }));
        Assert.Equal(new TurnTokens(5, 0, 0, 6), next.Tokens);
    }

    [Fact]
    public void ATurnFollowedAfterARestartKeepsTheTokensRecordedBeforeIt() {
        var directory = Path.Combine(Path.GetTempPath(), "ix-ledger-tests", Guid.NewGuid().ToString("N"));
        try {
            Directory.CreateDirectory(Path.Combine(directory, "runs"));
            var file = RunRecord.TokensFile(directory, "ask-1", 1);
            var before = new TurnWatch("thread", null, tokens => File.WriteAllText(file, System.Text.Json.JsonSerializer.Serialize(tokens)));
            before.Follow("turn-1");
            before.Observe("thread/tokenUsage/updated", Json(new { threadId = "thread", turnId = "turn-1", tokenUsage = new { last = new { inputTokens = 100, cachedInputTokens = 60, outputTokens = 10, totalTokens = 110 } } }));

            var after = new TurnWatch("thread", RunRecord.LoadTokens(directory, "ask-1", 1));
            after.Follow("turn-1");
            after.Observe("thread/tokenUsage/updated", Json(new { threadId = "thread", turnId = "turn-1", tokenUsage = new { last = new { inputTokens = 200, cachedInputTokens = 150, outputTokens = 20, totalTokens = 220 } } }));
            Assert.Equal(new TurnTokens(300, 210, 30, 330), after.Tokens);
        } finally {
            if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true);
        }
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


    private static string ReviewAnswer(string verdict, string findings, string validation) =>
        "{\"outcome\":\"result\",\"text\":\"Summary.\",\"review\":{\"verdict\":\"" + verdict + "\",\"summary\":\"Looked at it.\"," +
        "\"findings\":[" + findings + "],\"validation\":[" + validation + "]}}";

    private static string Check(string name, string result, bool final) =>
        "{\"check\":\"" + name + "\",\"command\":\"dotnet test\",\"result\":\"" + result + "\",\"final\":" + (final ? "true" : "false") + ",\"notes\":\"\"}";

    private static string Finding(string severity, string detail = "Detail.") =>
        "{\"severity\":\"" + severity + "\",\"title\":\"Title\",\"file\":\"a.cs\",\"line\":1,\"detail\":\"" + detail + "\",\"recommendation\":\"Fix it.\"}";

    [Fact]
    public void AReviewPassesOnlyOnApprovalWithNoBlockingFindingAndNoFinalFailedCheck() {
        bool Pass(string answer) => JsonDocument.Parse(AgentService.ReviewResult(answer, "thread-1")!).RootElement.GetProperty("pass").GetBoolean();
        // A setup failure that was corrected and rerun does not count; the rerun does.
        Assert.True(Pass(ReviewAnswer("approve", Finding("medium"), Check("tests", "fail", false) + "," + Check("tests", "pass", true))));
        Assert.False(Pass(ReviewAnswer("approve", "", Check("tests", "pass", false) + "," + Check("tests", "fail", true))));
        Assert.False(Pass(ReviewAnswer("approve", Finding("high"), Check("tests", "pass", true))));
        Assert.False(Pass(ReviewAnswer("needs-attention", "", "")));
        var failed = JsonDocument.Parse(AgentService.ReviewResult(ReviewAnswer("approve", "", Check("build", "fail", true)), "thread-1")!).RootElement;
        Assert.Equal("build", Assert.Single(failed.GetProperty("failedChecks").EnumerateArray()).GetString());
        Assert.Null(AgentService.ReviewResult("{\"outcome\":\"result\",\"text\":\"No review.\"}", "thread-1"));
    }

    [Fact]
    public void ALongReviewDropsTheLeastSevereFindingsToFitAndStaysValidJson() {
        var findings = string.Join(",", Enumerable.Range(0, 40).Select(i => Finding(i == 0 ? "critical" : "low", new string('x', 300))));
        var result = AgentService.ReviewResult(ReviewAnswer("needs-attention", findings, Check("tests", "pass", true)), "thread-1")!;
        Assert.True(Encoding.UTF8.GetByteCount(result) <= 8192);
        var root = JsonDocument.Parse(result).RootElement;
        Assert.Contains("thread-1", root.GetProperty("truncated").GetString());
        Assert.Contains(root.GetProperty("findings").EnumerateArray(), f => f.GetProperty("severity").GetString() == "critical");
    }

    [Fact]
    public void ATurnStoppedByTheCodexUsageLimitIsToldApartFromOtherFailures() {
        using var limited = JsonDocument.Parse("{\"message\":\"You've hit your usage limit.\",\"codexErrorInfo\":\"usageLimitExceeded\"}");
        using var other = JsonDocument.Parse("{\"message\":\"Server overloaded.\",\"codexErrorInfo\":\"serverOverloaded\"}");
        Assert.True(new TurnOutcome("t", "failed", null, TurnTokens.None, "m", CodexRunner.ErrorText(limited.RootElement)).UsageLimited);
        Assert.False(new TurnOutcome("t", "failed", null, TurnTokens.None, "m", CodexRunner.ErrorText(other.RootElement)).UsageLimited);
        Assert.Equal("Server overloaded.", CodexRunner.ErrorText(other.RootElement));
    }


    [Fact]
    public void ChecksWithBlankNamesAreKeptApartByTheirCommands() {
        var answer = ReviewAnswer("approve", "",
            "{\"check\":\" \",\"command\":\"dotnet build\",\"result\":\"fail\",\"final\":true,\"notes\":\"\"}," +
            "{\"check\":\"\",\"command\":\"dotnet test\",\"result\":\"pass\",\"final\":true,\"notes\":\"\"}");
        var root = JsonDocument.Parse(AgentService.ReviewResult(answer, "thread-1")!).RootElement;
        Assert.False(root.GetProperty("pass").GetBoolean());
        Assert.Equal("dotnet build", Assert.Single(root.GetProperty("failedChecks").EnumerateArray()).GetString());

        // An unnamed check without a command is keyed by its position, never by a check that is named like one.
        var positional = ReviewAnswer("approve", "",
            "{\"check\":\"\",\"command\":\"\",\"result\":\"fail\",\"final\":true,\"notes\":\"\"}," + Check("#0", "pass", true));
        var kept = JsonDocument.Parse(AgentService.ReviewResult(positional, "thread-1")!).RootElement;
        Assert.False(kept.GetProperty("pass").GetBoolean());
        Assert.Equal("#0", Assert.Single(kept.GetProperty("failedChecks").EnumerateArray()).GetString());
    }

    [Fact]
    public void ACompactReviewFitsTheByteLimitEvenWhenItsTextIsEscapedNonAscii() {
        var name = new string('\u68c0', 100);
        var checks = string.Join(",", Enumerable.Range(0, 10).Select(i => "{\"check\":\"" + name + i + "\",\"command\":\"" + name +
            "\",\"result\":\"fail\",\"final\":true,\"notes\":\"" + name + "\"}"));
        var answer = "{\"outcome\":\"result\",\"text\":\"\",\"review\":{\"verdict\":\"approve\",\"summary\":\"" + new string('\u68c0', 3000) +
            "\",\"findings\":[],\"validation\":[" + checks + "]}}";
        var result = AgentService.ReviewResult(answer, "thread-1")!;
        Assert.True(Encoding.UTF8.GetByteCount(result) <= 8192);
        var root = JsonDocument.Parse(result).RootElement;
        Assert.False(root.GetProperty("pass").GetBoolean());
        Assert.NotEmpty(root.GetProperty("failedChecks").EnumerateArray());
    }

    [Fact]
    public void AReviewWhoseTextAloneIsTooLongStaysValidJsonWithItsDecision() {
        var notes = new string('n', 9000);
        var answer = "{\"outcome\":\"result\",\"text\":\"S\",\"review\":{\"verdict\":\"approve\",\"summary\":\"" + new string('s', 9000) +
            "\",\"findings\":[],\"validation\":[{\"check\":\"build\",\"command\":\"dotnet build\",\"result\":\"fail\",\"final\":true,\"notes\":\"" +
            notes + "\"}]}}";
        var result = AgentService.ReviewResult(answer, "thread-1")!;
        Assert.True(Encoding.UTF8.GetByteCount(result) <= 8192);
        var root = JsonDocument.Parse(result).RootElement;
        Assert.False(root.GetProperty("pass").GetBoolean());
        Assert.Equal("build", Assert.Single(root.GetProperty("failedChecks").EnumerateArray()).GetString());
        Assert.Contains("thread-1", root.GetProperty("truncated").GetString());
    }

    [Fact]
    public void AReviewThatEndsWithoutAVerdictStillAnswersInTheReviewShape() {
        var root = JsonDocument.Parse(AgentService.ReviewWithoutVerdict("Stopped with an open question.", "thread-1")).RootElement;
        Assert.False(root.GetProperty("pass").GetBoolean());
        Assert.Equal("needs-attention", root.GetProperty("verdict").GetString());
        Assert.Equal("Stopped with an open question.", root.GetProperty("summary").GetString());
    }
}
