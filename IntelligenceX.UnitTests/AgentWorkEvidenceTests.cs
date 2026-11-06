using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using IntelligenceX.Telemetry.Agents;
using IntelligenceX.Telemetry.Usage;
using Xunit;

namespace IntelligenceX.UnitTests;

public sealed class AgentWorkEvidenceTests : IDisposable {
    [Fact]
    public void UnidentifiedQuestionOccurrencesRemainSeparateWhileKnownIdsDeduplicate() {
        Directory.CreateDirectory(Path.GetDirectoryName(CodexLog)!);
        var unidentified = "{\"type\":\"event_msg\",\"payload\":{\"type\":\"request_user_input\"}}\n";
        File.WriteAllText(CodexLog, Meta + Event("task_started") + unidentified + unidentified
            + Call("synthetic-known", "functions.request_user_input") + Call("synthetic-known", "functions.request_user_input"));
        var session = Assert.Single(LocalAgentWorkStateService.ReadLocal(new[] { Root("codex") }, _now).Sessions);
        Assert.Equal(3, session.PendingInputs.Count);
        Assert.Equal(2, session.PendingInputs.Count(input => input.Id is null));
        Assert.Single(session.PendingInputs, input => input.Id == "synthetic-known");
    }

    [Fact]
    public void PriorTurnBlockingQuestionRemainsAttentionWithoutBlockingItsSuccessor() {
        Directory.CreateDirectory(Path.GetDirectoryName(CodexLog)!);
        var first = Event("task_started").Replace("\"task_started\"", "\"task_started\",\"turn_id\":\"synthetic-first-turn\"");
        var next = Event("task_started").Replace("\"task_started\"", "\"task_started\",\"turn_id\":\"synthetic-next-turn\"");
        File.WriteAllText(CodexLog, Meta + first + Call("synthetic-question", "functions.request_user_input") + Event("turn_aborted") + next);
        var snapshot = LocalAgentWorkStateService.ReadLocal(new[] { Root("codex") }, _now);
        var session = Assert.Single(snapshot.Sessions);
        Assert.Equal("synthetic-next-turn", session.TurnId);
        Assert.True(session.TurnOpen);
        Assert.Equal(AgentWorkState.Running, session.State);
        var pending = Assert.Single(session.PendingInputs);
        Assert.True(pending.IsBlocking);
        Assert.Equal("synthetic-first-turn", pending.TurnId);
        Assert.Contains(snapshot.NeedsAttention, input => input.InputId == "synthetic-question");
    }

    private readonly string _directory = Path.Combine(Path.GetTempPath(), "ix-work-evidence", Guid.NewGuid().ToString("N"));
    private readonly DateTimeOffset _now = DateTimeOffset.UtcNow;
    private string CodexLog => Path.Combine(_directory, "sessions", "rollout-fake.jsonl");
    private SourceRootRecord Root(string provider) => new("evidence-" + provider, provider, UsageSourceKind.LocalLogs, _directory);

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void OversizedQuestionCounts_ReportTheRecordWithoutRetainingAnswerArrays(bool largeRecord) {
        Directory.CreateDirectory(Path.GetDirectoryName(CodexLog)!);
        var questions = largeRecord ? "\"questions\":[" + string.Join(",", Enumerable.Repeat("{}", 65)) + "]"
            : "\"questionCount\":65";
        var item = "{\"type\":\"event_msg\",\"payload\":{\"type\":\"item_completed\",\"item\":{\"type\":\"AgentMessage\",\"id\":\"bad\",\"delivery\":\"async\","
            + questions + (largeRecord ? ",\"ignored\":\"" + new string('x', 300 * 1024) + "\"" : "") + "}}}\n";
        File.WriteAllText(CodexLog, Meta + Event("task_started") + Call("bad", "functions.request_user_input_async")
            + item + Call("good", "functions.request_user_input_async")
            + "{\"type\":\"event_msg\",\"payload\":{\"type\":\"item_completed\",\"item\":{\"type\":\"AgentMessage\",\"id\":\"good\",\"delivery\":\"async\",\"questions\":[{}]}}}\n");
        var cache = new InMemoryRawArtifactStore();
        var snapshot = LocalAgentWorkStateService.ReadLocal(new[] { Root("codex") }, _now,
            options: new AgentWorkOptions { Cache = cache });
        Assert.Equal(new[] { "bad", "good" }, Assert.Single(snapshot.Sessions).PendingInputs.Select(input => input.Id));
        Assert.Equal("malformed-record", Assert.Single(snapshot.Diagnostics).Code);
        using var saved = System.Text.Json.JsonDocument.Parse(Assert.Single(cache.GetAll()).StateJson!);
        var answers = saved.RootElement.GetProperty("State").GetProperty("AsyncQuestions");
        Assert.False(answers.TryGetProperty("bad", out _));
        Assert.Equal(1, answers.GetProperty("good").GetArrayLength());
    }

    [Theory]
    [InlineData("codex")]
    [InlineData("claude")]
    public void MissingRequestIdentifiers_PreserveEachUnconfirmedQuestion(string provider) {
        Directory.CreateDirectory(Path.GetDirectoryName(CodexLog)!);
        var call = provider == "codex"
            ? Call("unused", "functions.request_user_input_async").Replace(",\"call_id\":\"unused\"", "")
            : "{\"type\":\"assistant\",\"sessionId\":\"fake\",\"message\":{\"content\":[{\"type\":\"tool_use\",\"name\":\"AskUserQuestion\"}]}}\n";
        File.WriteAllText(provider == "codex" ? CodexLog : Path.Combine(_directory, "fake.jsonl"),
            (provider == "codex" ? Meta + Event("task_started") : "") + call + call);
        var snapshot = LocalAgentWorkStateService.ReadLocal(new[] { Root(provider) }, _now, options: new AgentWorkOptions { ActiveWithin = null });
        Assert.Equal(2, Assert.Single(snapshot.Sessions).PendingInputs.Count);
        Assert.Equal(2, snapshot.NeedsAttention.Count);
        Assert.All(snapshot.NeedsAttention, item => Assert.Equal(AgentAttentionKind.UnconfirmedInput, item.Kind));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("\\udc00")]
    [InlineData("\\ud800\\u0041")]
    public void InvalidProjectedReplyIdentity_ReportsTheRecordAndKeepsOtherSessionsAvailable(string? invalidEscape) {
        Directory.CreateDirectory(Path.GetDirectoryName(CodexLog)!);
        var invalid = invalidEscape is null
            ? "{\"type\":\"response_item\",\"payload\":{\"type\":\"message\",\"role\":\"user\",\"content\":[{\"questionReplyIds\":[42]}]}}\n"
            : System.Text.Json.JsonSerializer.Serialize(new { type = "response_item", payload = new {
                type = "message", role = "user", content = new[] { new { type = "input_text",
                    text = "<send_user_message_question_reply>bad-surrogate" + new string('x', 300 * 1024) + "</send_user_message_question_reply>" } }
            }}).Replace("bad-surrogate", invalidEscape) + "\n";
        File.WriteAllText(CodexLog, Meta + Event("task_started") + Call("question", "functions.request_user_input_async")
            + invalid);
        File.WriteAllText(Path.Combine(Path.GetDirectoryName(CodexLog)!, "rollout-other.jsonl"), Meta.Replace("fake", "other") + Event("task_complete"));
        var snapshot = LocalAgentWorkStateService.ReadLocal(new[] { Root("codex") }, _now);
        Assert.Equal(2, snapshot.Sessions.Count);
        Assert.Equal("question", Assert.Single(snapshot.Sessions.Single(session => session.Id == "fake").PendingInputs).Id);
        Assert.Equal("malformed-record", Assert.Single(snapshot.Diagnostics).Code);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Codex_DesktopRepliesResolveOnlyFullyAnsweredMatchingCallsAcrossCacheTails(bool largeAnswer) {
        Directory.CreateDirectory(Path.GetDirectoryName(CodexLog)!);
        string Completed(string id, int count) => System.Text.Json.JsonSerializer.Serialize(new {
            type = "event_msg", payload = new { type = "item_completed", item = new {
                type = "AgentMessage", id, delivery = "async",
                questions = Enumerable.Range(0, count).Select(_ => new { title = "private sentinel" }).ToArray()
            }}
        }) + "\n";
        string Reply(string id, int index) {
            var body = System.Text.Json.JsonSerializer.Serialize(new[] { new {
                questionItemId = System.Text.Json.JsonSerializer.Serialize(new object[] { "request_user_input_async", id, index }),
                question = "private sentinel", answer = largeAnswer ? new string('x', 4 * 1024 * 1024) + "private sentinel 中文 🚀" : "private sentinel"
            }});
            return System.Text.Json.JsonSerializer.Serialize(new {
                type = "response_item", payload = new { type = "message", role = "user", content = new[] {
                    new { type = "input_text", text = "<send_user_message_question_reply>\n" + body + "\n</send_user_message_question_reply>" }
                }}
            }) + "\n";
        }
        File.WriteAllText(CodexLog, Meta + Event("task_started")
            + Call("pair", "functions.request_user_input_async") + Completed("pair", 2) + Output("pair")
            + Call("other", "functions.request_user_input_async") + Completed("other", 1));
        var cache = new InMemoryRawArtifactStore();
        AgentWorkSnapshot Read() => LocalAgentWorkStateService.ReadLocal(new[] { Root("codex") }, _now,
            options: new AgentWorkOptions { Cache = cache });
        Assert.Equal(2, Assert.Single(Read().Sessions).PendingInputs.Count);
        File.AppendAllText(CodexLog, Reply("unknown", 0) + Reply("pair", 1) + Reply("pair", 1));
        var partial = Read();
        Assert.Equal(2, Assert.Single(partial.Sessions).PendingInputs.Count);
        Assert.Empty(partial.Sessions[0].ResolvedInputs);
        Assert.Equal(1, partial.Scan!.FilesTailed);
        File.AppendAllText(CodexLog, Reply("pair", 0));
        var before = GC.GetAllocatedBytesForCurrentThread();
        var resolved = Read();
        var allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        Assert.Equal("other", Assert.Single(Assert.Single(resolved.Sessions).PendingInputs).Id);
        Assert.Equal("pair", Assert.Single(resolved.Sessions[0].ResolvedInputs).Id);
        Assert.Equal("other", Assert.Single(resolved.NeedsAttention).InputId);
        Assert.Equal(AgentWorkState.Running, resolved.Sessions[0].State);
        Assert.Equal(1, resolved.Scan!.FilesTailed);
        Assert.Empty(resolved.Diagnostics);
        Assert.True(allocated < 1024 * 1024, "Reply bodies must not be materialized while extracting identities.");
        Assert.DoesNotContain("private sentinel", AgentWorkJson.Serialize(resolved));
        Assert.DoesNotContain("private sentinel", Assert.Single(cache.GetAll()).StateJson!);
        Assert.Equal("other", Assert.Single(Assert.Single(Read().Sessions).PendingInputs).Id);
        Assert.Equal("pair", Assert.Single(Assert.Single(Read().Sessions).ResolvedInputs).Id);
    }

    [Fact]
    public void Codex_AsyncAcknowledgementAndUnrelatedActivityDoNotAnswerQuestions() {
        Directory.CreateDirectory(Path.GetDirectoryName(CodexLog)!);
        File.WriteAllText(CodexLog, Meta + Event("task_started") + Call("one", "functions.request_user_input_async"));
        var options = new AgentWorkOptions { Cache = new InMemoryRawArtifactStore() };
        AgentWorkSnapshot Read() => LocalAgentWorkStateService.ReadLocal(new[] { Root("codex") }, _now, options: options);
        var first = Assert.Single(Read().Sessions);
        Assert.Equal(AgentWorkState.Running, first.State);
        Assert.True(Assert.Single(first.PendingInputs).AnswerIsUnconfirmed);
        File.AppendAllText(CodexLog, Output("one") + Event("task_complete") + Event("task_started")
            + Call("two", "functions.request_user_input_async") + Output("two")
            + "{\"type\":\"event_msg\",\"payload\":{\"type\":\"user_message\",\"message\":\"private sentinel\"}}\n"
            + Call("blocking", "functions.request_user_input") + Output("blocking"));
        var snapshot = Read();
        var session = Assert.Single(snapshot.Sessions);
        Assert.Equal(AgentWorkState.Running, session.State);
        Assert.Equal(new[] { "one", "two" }, session.PendingInputs.Select(static input => input.Id));
        Assert.All(session.PendingInputs, static input => { Assert.False(input.IsBlocking); Assert.True(input.AnswerIsUnconfirmed); });
        Assert.Equal(1, snapshot.Scan!.FilesTailed);
        Assert.All(snapshot.NeedsAttention, item => {
            Assert.Equal(AgentAttentionKind.UnconfirmedInput, item.Kind);
            Assert.Equal(session.Machine, item.Machine);
            Assert.Equal(session.SourceRootId, item.SourceRootId);
        });
        Assert.Equal(new[] { "one", "two" }, snapshot.NeedsAttention.Select(static item => item.InputId));
        Assert.Equal(AgentObservationSources.SessionLog, session.ObservationSources);
        Assert.NotNull(session.ObservedAtUtc);
        var unchanged = Assert.Single(Read().Sessions);
        Assert.Equal(session.ObservedAtUtc, unchanged.ObservedAtUtc);
        Assert.DoesNotContain("private sentinel", AgentWorkJson.Serialize(snapshot));
        Assert.DoesNotContain("private sentinel", Assert.Single(options.Cache!.GetAll()).StateJson!);
    }

    [Fact]
    public void Claude_OnlyTheMatchingQuestionToolResultResolvesInput() {
        Directory.CreateDirectory(_directory);
        var path = Path.Combine(_directory, "fake.jsonl");
        File.WriteAllText(path, "{\"type\":\"assistant\",\"sessionId\":\"fake\",\"timestamp\":\"" + _now.ToString("O")
            + "\",\"message\":{\"stop_reason\":\"tool_use\",\"content\":[{\"type\":\"tool_use\",\"id\":\"question\",\"name\":\"AskUserQuestion\",\"input\":{\"questions\":\"private sentinel\"}}]}}\n"
            + "{\"type\":\"system\",\"subtype\":\"turn_duration\"}\n");
        var options = new AgentWorkOptions { Cache = new InMemoryRawArtifactStore() };
        AgentWorkSnapshot Read() => LocalAgentWorkStateService.ReadLocal(new[] { Root("claude") }, _now, options: options);
        Assert.Equal(AgentWorkState.WaitingOnPerson, Assert.Single(Read().Sessions).State);
        File.AppendAllText(path, ClaudeAnswer("other"));
        Assert.Single(Assert.Single(Read().Sessions).PendingInputs);
        File.AppendAllText(path, ClaudeAnswer("question") + "{\"type\":\"result\",\"is_error\":false}\n");
        var resolved = Read();
        Assert.Empty(Assert.Single(resolved.Sessions).PendingInputs);
        Assert.Equal(AgentWorkState.Idle, resolved.Sessions[0].State);
        Assert.Equal(AgentTurnOutcome.Completed, resolved.Sessions[0].LastTurnOutcome);
        Assert.Empty(resolved.NeedsAttention);
    }

    [Theory]
    [InlineData("codex", "\"ServerOverloaded\"", AgentFailureCause.Capacity)]
    [InlineData("codex", "\"UsageLimitExceeded\"", AgentFailureCause.UsageLimit)]
    [InlineData("codex", "\"RateLimitExceeded\"", AgentFailureCause.RateLimit)]
    [InlineData("codex", "\"Unauthorized\"", AgentFailureCause.Authentication)]
    [InlineData("codex", "{\"ResponseStreamDisconnected\":{\"http_status_code\":503}}", AgentFailureCause.Transport)]
    [InlineData("codex", "\"private sentinel overloaded_error\"", AgentFailureCause.Unknown)]
    [InlineData("claude", "\"overloaded_error\"", AgentFailureCause.Capacity)]
    [InlineData("claude", "\"authentication_error\"", AgentFailureCause.Authentication)]
    [InlineData("claude", "\"rate_limit_error\"", AgentFailureCause.RateLimit)]
    [InlineData("claude", "\"private sentinel capacity\"", AgentFailureCause.Unknown)]
    public void FailureCause_UsesStructuredCodesAndDoesNotRetainPayloads(string provider, string code, AgentFailureCause cause) {
        Directory.CreateDirectory(Path.GetDirectoryName(CodexLog)!);
        var error = "{\"message\":\"private sentinel " + new string('x', 8192) + "\",\"" + (provider == "codex" ? "codex_error_info" : "type") + "\":" + code + "}";
        var path = provider == "codex" ? CodexLog : Path.Combine(_directory, "fake.jsonl");
        File.WriteAllText(path, provider == "codex" ? Meta + Event("task_started")
            + "{\"type\":\"event_msg\",\"timestamp\":\"" + _now.ToString("O") + "\",\"payload\":{\"type\":\"task_complete\",\"error\":" + error + "}}\n"
            : "{\"type\":\"result\",\"sessionId\":\"fake\",\"is_error\":true,\"timestamp\":\"" + _now.ToString("O") + "\",\"error\":" + error + "}\n"
                + "{\"type\":\"system\",\"subtype\":\"turn_duration\"}\n");
        var options = new AgentWorkOptions { Cache = new InMemoryRawArtifactStore() };
        var snapshot = LocalAgentWorkStateService.ReadLocal(new[] { Root(provider) }, _now, options: options);
        var session = Assert.Single(snapshot.Sessions);
        Assert.Equal(AgentWorkState.Idle, session.State);
        Assert.Equal(AgentTurnOutcome.Failed, session.LastTurnOutcome);
        Assert.False(session.TurnOpen);
        Assert.Equal(_now, session.TurnEndedAtUtc);
        Assert.Equal(_now, session.LastTurnActivityAtUtc);
        Assert.Equal(cause, session.FailureCause);
        Assert.Equal(cause == AgentFailureCause.Unknown ? AgentFailureEvidence.TerminalEvent : AgentFailureEvidence.ProviderCode, session.FailureEvidence);
        Assert.Equal(AgentAttentionKind.FailedTurn, Assert.Single(snapshot.NeedsAttention).Kind);
        Assert.DoesNotContain("private sentinel", AgentWorkJson.Serialize(snapshot));
        Assert.DoesNotContain("private sentinel", Assert.Single(options.Cache!.GetAll()).StateJson!);
        if (provider == "codex") {
            File.AppendAllText(path, Event("task_started"));
            var resumed = Assert.Single(LocalAgentWorkStateService.ReadLocal(new[] { Root(provider) }, _now, options: options).Sessions);
            Assert.Equal(AgentWorkState.Running, resumed.State);
            Assert.True(resumed.TurnOpen);
            Assert.Equal(_now, resumed.TurnStartedAtUtc);
            Assert.Null(resumed.TurnEndedAtUtc);
            Assert.Equal(AgentFailureEvidence.None, resumed.FailureEvidence);
            Assert.Equal(AgentFailureCause.Unknown, resumed.FailureCause);
        }
    }

    [Fact]
    public void KnownUnresolvedInput_SurvivesActivityCutoffWithoutReopeningHistory() {
        Directory.CreateDirectory(Path.GetDirectoryName(CodexLog)!);
        File.WriteAllText(CodexLog, Meta + Event("task_started") + Call("question", "functions.request_user_input_async") + Event("task_complete"));
        var collection = new AgentLocalCollection(new AgentWorkOptions(), incremental: true);
        collection.Reconcile(new[] { Root("codex") }, _now, default);
        Assert.Single(collection.Snapshot(Array.Empty<AgentSessionState>(), _now).NeedsAttention);
        File.SetLastWriteTimeUtc(CodexLog, _now.AddDays(-3).UtcDateTime);
        collection.Reconcile(new[] { Root("codex") }, _now.AddDays(3), default);
        var aged = collection.Snapshot(Array.Empty<AgentSessionState>(), _now.AddDays(3));
        Assert.Equal("question", Assert.Single(aged.NeedsAttention).InputId);
        Assert.Equal(AgentWorkState.Idle, Assert.Single(aged.Sessions).State);
        Assert.Equal(0, aged.Scan!.FilesRead);
        Assert.Equal(1, aged.Scan.FilesSkipped);
    }

    [Fact]
    public async Task Watcher_MetadataFailureRetainsObservationAndLastSuccessfulRefresh() {
        var calls = 0;
        var metadata = new[] { new AgentSessionState { Provider = "codex", Id = "fake", SourceRootId = "db-fixture",
            LastActivityAtUtc = _now, ObservationSources = AgentObservationSources.DesktopMetadata } };
        var failed = new TaskCompletionSource<AgentWorkSnapshot>(TaskCreationOptions.RunContinuationsAsynchronously);
        await using var watcher = new AgentWorkWatcher(Array.Empty<SourceRootRecord>(), new AgentWorkOptions {
            MetadataRefreshInterval = TimeSpan.FromMilliseconds(50)
        }, readMetadataAsync: _ => Interlocked.Increment(ref calls) == 1
            ? Task.FromResult<System.Collections.Generic.IReadOnlyList<AgentSessionState>>(metadata)
            : throw new IOException("private sentinel"));
        watcher.SnapshotChanged += (_, change) => {
            if (change.Snapshot.Diagnostics.Any(static item => item.Code == "unreadable-metadata")) failed.TrySetResult(change.Snapshot);
        };
        await watcher.StartAsync();
        var partial = await failed.Task.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.NotNull(partial.MetadataCheckedAtUtc);
        Assert.True(partial.MetadataCheckedAtUtc <= partial.CapturedAtUtc);
        Assert.Equal(AgentObservationSources.DesktopMetadata, Assert.Single(partial.Sessions).ObservationSources);
        Assert.DoesNotContain("private sentinel", AgentWorkJson.Serialize(partial));
    }

    private string Event(string type) => "{\"type\":\"event_msg\",\"timestamp\":\"" + _now.ToString("O") + "\",\"payload\":{\"type\":\"" + type + "\"}}\n";
    private static string Call(string id, string name) => "{\"type\":\"response_item\",\"payload\":{\"type\":\"function_call\",\"name\":\"" + name + "\",\"call_id\":\"" + id + "\",\"arguments\":\"private sentinel\"}}\n";
    private static string Output(string id) => "{\"type\":\"response_item\",\"payload\":{\"type\":\"function_call_output\",\"call_id\":\"" + id + "\",\"output\":\"private sentinel\"}}\n";
    private static string ClaudeAnswer(string id) => "{\"type\":\"user\",\"message\":{\"content\":[{\"type\":\"tool_result\",\"tool_use_id\":\"" + id + "\",\"content\":\"private sentinel\"}]}}\n";
    private const string Meta = "{\"type\":\"session_meta\",\"payload\":{\"id\":\"fake\"}}\n";
    public void Dispose() { if (Directory.Exists(_directory)) Directory.Delete(_directory, true); }
}
