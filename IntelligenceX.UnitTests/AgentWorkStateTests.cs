using System;
using System.IO;
using System.Linq;
using System.Threading;
using IntelligenceX.Telemetry.Agents;
using IntelligenceX.Telemetry.Usage;
using Xunit;

namespace IntelligenceX.UnitTests;

public sealed class AgentWorkStateTests : IDisposable {
    private static readonly DateTimeOffset Now = DateTimeOffset.Parse("2026-11-01T12:00:00Z");
    private readonly string _root = Path.Combine(Path.GetTempPath(), "ix-agent-fixtures", Guid.NewGuid().ToString("N"));

    [Theory]
    [InlineData("task_started", "2026-11-01T11:59:00Z", AgentWorkState.Running, AgentTurnOutcome.Unknown, true)]
    [InlineData("task_started", "2026-11-01T10:00:00Z", AgentWorkState.Idle, AgentTurnOutcome.Incomplete, true)]
    [InlineData("task_started", "2026-11-01T12:01:00Z", AgentWorkState.Idle, AgentTurnOutcome.Incomplete, true)]
    [InlineData("task_complete", "2026-11-01T11:59:00Z", AgentWorkState.Idle, AgentTurnOutcome.Completed, false)]
    [InlineData("turn_aborted", "2026-11-01T11:59:00Z", AgentWorkState.Idle, AgentTurnOutcome.Interrupted, false)]
    [InlineData("turn_failed", "2026-11-01T11:59:00Z", AgentWorkState.Idle, AgentTurnOutcome.Failed, false)]
    [InlineData("request_user_input", "2026-11-01T11:59:00Z", AgentWorkState.WaitingOnPerson, AgentTurnOutcome.Unknown, false)]
    public void Codex_UsesStructuredLifecycle(string type, string timestamp, AgentWorkState state, AgentTurnOutcome outcome, bool inferred) {
        Write("sessions/rollout-fake.jsonl", CodexMeta + "\n" +
            $$$"""{"timestamp":"{{{timestamp}}}","type":"event_msg","payload":{"type":"{{{type}}}","turn_id":"synthetic-turn","message":"private sentinel"}}""");
        var session = Assert.Single(Read("codex").Sessions);
        Assert.Equal("fake-session", session.Id);
        Assert.Equal("Sample/Project", session.Repository);
        Assert.Equal("feature/work", session.Branch);
        Assert.Equal(state, session.State);
        Assert.Equal(outcome, session.LastTurnOutcome);
        Assert.Equal(inferred, session.StateIsInferred);
        if (type == "task_started") Assert.Equal("synthetic-turn", session.TurnId);
        Assert.DoesNotContain("private sentinel", AgentWorkJson.Serialize(Read("codex")));
    }

    [Fact]
    public void Codex_InputToolWaitEndsOnlyForMatchingResponseAndNewTurnClearsAbort() {
        var path = "sessions/rollout-fake.jsonl";
        var prefix = CodexMeta + "\n" + """
            {"timestamp":"2026-11-01T11:58:00Z","type":"event_msg","payload":{"type":"turn_aborted"}}
            {"timestamp":"2026-11-01T11:59:00Z","type":"event_msg","payload":{"type":"task_started","turn_id":"synthetic-input-turn"}}
            {"timestamp":"2026-11-01T11:59:01Z","type":"response_item","payload":{"type":"function_call","name":"functions.request_user_input","call_id":"ask","arguments":"private sentinel"}}
            {"timestamp":"2026-11-01T11:59:02Z","type":"response_item","payload":{"type":"function_call_output","call_id":"other","output":"private sentinel"}}
            """;
        Write(path, prefix);
        var waiting = Assert.Single(Read("codex").Sessions);
        Assert.Equal(AgentWorkState.WaitingOnPerson, waiting.State);
        Assert.Equal("synthetic-input-turn", Assert.Single(waiting.PendingInputs).TurnId);
        Write(path, prefix + "\n" + """
            {"timestamp":"2026-11-01T11:59:03Z","type":"response_item","payload":{"type":"function_call_output","call_id":"ask","output":"private sentinel"}}
            """);
        var session = Assert.Single(Read("codex").Sessions);
        Assert.Equal(AgentWorkState.Running, session.State);
        Assert.Equal(AgentTurnOutcome.Unknown, session.LastTurnOutcome);
    }

    [Fact]
    public void Codex_DeduplicatesArchivesAndUsesExplicitTitleIndexesWithoutFileTimes() {
        var content = CodexMeta + "\n" + """
            {"timestamp":"2026-10-01T12:00:00Z","type":"event_msg","payload":{"type":"task_complete"}}
            """;
        Write("sessions/rollout-fake.jsonl", content);
        Write("archived_sessions/rollout-fake.jsonl", content);
        Write("session_index.jsonl", """{"id":"fake-session","thread_name":"Index title","updated_at":"2026-11-01T11:00:00Z"}""");
        Write(".codex-global-state.json", """{"thread-titles":{"titles":{"fake-session":"private sentinel"}}}""");
        var snapshot = Read("codex");
        var session = Assert.Single(snapshot.Sessions);
        Assert.Equal("Index title", session.Title);
        Assert.False(session.IsArchived);
        Assert.Equal(DateTimeOffset.Parse("2026-10-01T12:00:00Z"), session.LastActivityAtUtc);
        Assert.DoesNotContain("private sentinel", AgentWorkJson.Serialize(snapshot));
        Write("copy/sessions/rollout-fake.jsonl", CodexMeta);
        Write("copy/session_index.jsonl", """{"id":"fake-session","thread_name":"Older copy title"}""");
        var duplicateRoots = LocalAgentWorkStateService.ReadLocal(new[] {
            Root("codex"), new SourceRootRecord("copy", "codex", UsageSourceKind.LocalLogs, Path.Combine(_root, "copy"))
        }, Now, options: new AgentWorkOptions { ActiveWithin = null });
        Assert.Equal("Index title", Assert.Single(duplicateRoots.Sessions).Title);
    }

    [Fact]
    public void RecoveredRoots_NeverPretendAnUnfinishedTurnIsRunningLocally() {
        Write("sessions/rollout-fake.jsonl", CodexMeta + "\n" + """
            {"timestamp":"2026-11-01T11:59:00Z","type":"event_msg","payload":{"type":"task_started"}}
            """);
        var snapshot = LocalAgentWorkStateService.ReadLocal(new[] { Root("codex", UsageSourceKind.RecoveredFolder) }, Now);
        var session = Assert.Single(snapshot.Sessions);
        Assert.Equal(AgentWorkState.Idle, session.State);
        Assert.Null(session.Machine);
        Assert.Equal(AgentTurnOutcome.Incomplete, session.LastTurnOutcome);
    }

    [Theory]
    [InlineData("end_turn", AgentWorkState.Idle, AgentTurnOutcome.Completed)]
    [InlineData("stop_sequence", AgentWorkState.Idle, AgentTurnOutcome.Completed)]
    [InlineData("tool_use", AgentWorkState.Running, AgentTurnOutcome.Unknown)]
    [InlineData("max_tokens", AgentWorkState.Idle, AgentTurnOutcome.Incomplete)]
    public void Claude_UsesStopReasonsWithoutReadingText(string stop, AgentWorkState state, AgentTurnOutcome outcome) {
        Write("projects/sample/fake.jsonl", $$$"""
            {"type":"custom-title","sessionId":"claude-fake","customTitle":"Sample title"}
            {"type":"user","uuid":"synthetic-first-record","sessionId":"claude-fake","cwd":"/unavailable/sample","gitBranch":"feature/work","timestamp":"2026-11-01T11:58:00Z","message":{"content":"private sentinel"}}
            {"type":"assistant","sessionId":"claude-fake","timestamp":"2026-11-01T11:59:00Z","message":{"stop_reason":"{{{stop}}}","content":[{"type":"text","text":"private sentinel"}]}}
            """);
        var snapshot = Read("claude");
        var session = Assert.Single(snapshot.Sessions);
        Assert.Equal("claude-fake", session.Id);
        Assert.Equal("synthetic-first-record", session.TurnId);
        Assert.Equal("Sample title", session.Title);
        Assert.Equal("feature/work", session.Branch);
        Assert.Equal(state, session.State);
        Assert.Equal(outcome, session.LastTurnOutcome);
        Assert.Equal(stop is not ("end_turn" or "stop_sequence" or "max_tokens"), session.TurnOpen);
        if (session.TurnOpen == false) Assert.Equal(DateTimeOffset.Parse("2026-11-01T11:59:00Z"), session.TurnEndedAtUtc);
        Assert.DoesNotContain(snapshot.NeedsAttention, static item => item.Kind == AgentAttentionKind.WaitingOnPerson);
        Assert.DoesNotContain("private sentinel", AgentWorkJson.Serialize(snapshot));
    }

    [Fact]
    public void Claude_InputToolWaitsAndSubagentsCannotReplaceParent() {
        Write("projects/sample/fake.jsonl", """
            {"type":"assistant","sessionId":"claude-fake","timestamp":"2026-11-01T11:59:00Z","message":{"stop_reason":"tool_use","content":[{"type":"tool_use","name":"AskUserQuestion","input":{"question":"private sentinel"}}]}}
            """);
        Write("projects/sample/fake/subagents/agent-child.jsonl", """
            {"type":"assistant","sessionId":"claude-fake","isSidechain":true,"timestamp":"2026-11-01T12:00:00Z","message":{"stop_reason":"end_turn"}}
            """);
        Assert.Equal(AgentWorkState.WaitingOnPerson, Assert.Single(Read("claude").Sessions).State);
    }

    [Fact]
    public void Readers_SkipPartialAndOversizedRecordsAndHonorCancellationAndEmptyRoots() {
        Write("sessions/rollout-fake.jsonl", "{broken\n" + new string('x', 1024 * 1024 + 10) + "\n" + CodexMeta + "\n{\"partial\":");
        var snapshot = Read("codex");
        Assert.Equal("fake-session", Assert.Single(snapshot.Sessions).Id);
        Assert.Contains(snapshot.Diagnostics, static diagnostic => diagnostic.Code == "malformed-record");
        Assert.Empty(LocalAgentWorkStateService.ReadLocal(Array.Empty<SourceRootRecord>(), Now).Sessions);
        using var canceled = new CancellationTokenSource();
        canceled.Cancel();
        Assert.Throws<OperationCanceledException>(() => LocalAgentWorkStateService.ReadLocal(new[] { Root("codex") }, Now, cancellationToken: canceled.Token));
    }

    [Fact]
    public void Rules_LinkExactHeadRepositoryAndBranchAndKeepIndependentAttentionReasons() {
        var session = new AgentSessionState { Provider = "codex", Id = "fake", Repository = "Sample/Project", Branch = "feature/work",
            State = AgentWorkState.WaitingOnPerson, LastActivityAtUtc = Now.AddDays(-7), LastTurnOutcome = AgentTurnOutcome.Interrupted };
        var authored = new AgentPullRequestState { Url = "https://github.com/Sample/Project/pull/1", Repository = "Sample/Project",
            HeadRepository = "Sample/Project", Branch = "feature/work", IsAuthor = true, Checks = AgentCheckState.Failure,
            ChecksUrl = "https://github.com/Sample/Project/commit/fake/checks" };
        var review = authored with { Url = "https://github.com/Sample/Project/pull/2", HeadRepository = "Someone/Fork", IsAuthor = false, ReviewRequested = true };
        var caseSensitive = authored with { Url = "https://github.com/Sample/Project/pull/3", Branch = "Feature/work", IsAuthor = false };
        var snapshot = AgentWorkStateRules.Evaluate(new[] { session }, new[] { authored, review, caseSensitive }, Now);
        Assert.Equal(authored.Url, Assert.Single(Assert.Single(snapshot.Sessions).PullRequests).Url);
        Assert.Equal(AgentWorkState.WaitingOnPerson, snapshot.Sessions[0].State);
        Assert.Equal(new[] { AgentAttentionKind.WaitingOnPerson, AgentAttentionKind.Stale, AgentAttentionKind.InterruptedTurn,
            AgentAttentionKind.FailedChecks, AgentAttentionKind.ReviewRequested }, snapshot.NeedsAttention.Select(static item => item.Kind));
        Assert.Equal(authored.ChecksUrl, snapshot.NeedsAttention.Single(static item => item.Kind == AgentAttentionKind.FailedChecks).Url);
    }

    [Theory]
    [InlineData(AgentWorkState.Idle, 6, true, AgentWorkState.Idle, false)]
    [InlineData(AgentWorkState.Idle, 7, true, AgentWorkState.Stale, true)]
    [InlineData(AgentWorkState.Running, 7, true, AgentWorkState.Running, false)]
    [InlineData(AgentWorkState.Finished, 7, true, AgentWorkState.Finished, false)]
    [InlineData(AgentWorkState.Idle, 8, false, AgentWorkState.Idle, false)]
    [InlineData(AgentWorkState.Stale, 8, false, AgentWorkState.Idle, false)]
    public void Rules_StaleRequiresInactivityAndAnOpenPullRequest(AgentWorkState state, int age, bool open,
        AgentWorkState expected, bool stale) {
        var session = new AgentSessionState { Provider = "codex", Id = "fake", Repository = "Sample/Project", Branch = "work",
            State = state, LastActivityAtUtc = Now.AddDays(-age) };
        var pr = new AgentPullRequestState { Url = "https://github.com/Sample/Project/pull/1", HeadRepository = "Sample/Project", Branch = "work", IsOpen = open };
        var snapshot = AgentWorkStateRules.Evaluate(new[] { session }, new[] { pr }, Now);
        Assert.Equal(expected, Assert.Single(snapshot.Sessions).State);
        Assert.Equal(stale, snapshot.NeedsAttention.Any(static item => item.Kind == AgentAttentionKind.Stale));
        Assert.Empty(AgentWorkStateRules.Evaluate(new[] { session }, new[] { pr }, Now, TimeSpan.FromDays(10)).NeedsAttention);
    }

    [Fact]
    public void RepositoryEnrichment_ResolvesWorktreeCommonDirectoryAndOriginWithoutRetainingCredential() {
        var working = Path.Combine(_root, "repo");
        Write("repo/.git", "gitdir: ../git/worktrees/test\n");
        Write("git/worktrees/test/HEAD", "ref: refs/heads/feature/work\n");
        Write("git/worktrees/test/commondir", "../..\n");
        Write("git/config", "[remote \"origin\"]\n url = https://private-sentinel@github.com/Sample/Project.git\n");
        var session = AgentRepositoryIdentity.Enrich(new AgentSessionState { WorkingDirectory = working });
        Assert.Equal("Sample/Project", session.Repository);
        Assert.Equal("feature/work", session.Branch);
        Assert.DoesNotContain("private-sentinel", AgentWorkJson.Serialize(new AgentWorkSnapshot { Sessions = new[] { session } }));
    }

    [Fact]
    public void MetadataOverlay_PreservesTurnOutcomeAndAppliesArchiveAndTitle() {
        Write("sessions/rollout-fake.jsonl", CodexMeta + "\n" + """
            {"timestamp":"2026-11-01T11:59:00Z","type":"event_msg","payload":{"type":"turn_aborted"}}
            {"timestamp":"2026-11-01T11:59:01Z","type":"event_msg","payload":{"type":"thread_name_updated","thread_id":"fake-session","thread_name":"Explicit title"}}
            """);
        var metadata = new AgentSessionState { Provider = "codex", Id = "fake-session", Machine = Environment.MachineName, SourceRootId = "fixture",
            Title = "Database title", IsArchived = true };
        var snapshot = LocalAgentWorkStateService.ReadLocal(new[] { Root("codex") }, Now, codexMetadata: new[] { metadata });
        var session = Assert.Single(snapshot.Sessions);
        Assert.Equal("Explicit title", session.Title);
        Assert.Equal(AgentWorkState.Finished, session.State);
        Assert.Equal(AgentTurnOutcome.Interrupted, session.LastTurnOutcome);
        Assert.Empty(snapshot.NeedsAttention);
    }

    [Fact]
    public void Codex_ForkKeepsCanonicalIdentityWorkspaceAndTitle() {
        Write("sessions/rollout-parent.jsonl", CodexMeta);
        Write("sessions/rollout-fork.jsonl", """
            {"timestamp":"2026-11-01T10:00:00Z","type":"session_meta","payload":{"id":"fork-session","title":"private sentinel","cwd":"/missing/fork","git":{"branch":"fork/work","repository_url":"https://github.com/Sample/Fork.git"}}}
            {"timestamp":"2026-11-01T10:01:00Z","type":"event_msg","payload":{"type":"thread_name_updated","thread_id":"fork-session","thread_name":"Explicit fork"}}
            """ + "\n" + CodexMeta + "\n" + """
            {"timestamp":"2026-10-01T10:00:01Z","type":"turn_context","payload":{"cwd":"/missing/parent"}}
            {"timestamp":"2026-10-01T10:00:02Z","type":"event_msg","payload":{"type":"thread_name_updated","thread_id":"fake-session","thread_name":"Parent title"}}
            {"timestamp":"2026-11-01T11:00:00Z","type":"event_msg","payload":{"type":"turn_aborted"}}
            """);
        var snapshot = Read("codex");
        Assert.Equal(2, snapshot.Sessions.Count);
        var fork = snapshot.Sessions.Single(static s => s.Id == "fork-session");
        Assert.Equal("Sample/Fork", fork.Repository);
        Assert.Equal("fork/work", fork.Branch);
        Assert.Equal("/missing/fork", fork.WorkingDirectory);
        Assert.Equal("Explicit fork", fork.Title);
        Assert.Equal(DateTimeOffset.Parse("2026-11-01T10:00:00Z"), fork.StartedAtUtc);
        Assert.DoesNotContain("private sentinel", AgentWorkJson.Serialize(snapshot));
    }

    [Theory]
    [InlineData("null", AgentTurnOutcome.Completed, false)]
    [InlineData("{\"message\":\"private sentinel\"}", AgentTurnOutcome.Failed, true)]
    public void Codex_TerminalErrorClassifiesFailureWithoutExportingDetails(string error, AgentTurnOutcome outcome, bool failed) {
        Write("sessions/rollout-fake.jsonl", CodexMeta + "\n" + $$$"""
            {"timestamp":"2026-11-01T11:59:00Z","type":"event_msg","payload":{"type":"task_complete","error":{{{error}}}}}
            """);
        var snapshot = Read("codex");
        Assert.Equal(outcome, Assert.Single(snapshot.Sessions).LastTurnOutcome);
        Assert.Equal(failed, snapshot.NeedsAttention.Any(static item => item.Kind == AgentAttentionKind.FailedTurn));
        Assert.DoesNotContain("private sentinel", AgentWorkJson.Serialize(snapshot));
    }

    [Theory]
    [InlineData(UsageSourceKind.LocalLogs, "Sample/Current")]
    [InlineData(UsageSourceKind.RecoveredFolder, null)]
    public void WorkspaceEnrichment_RequiresLocalProvenance(UsageSourceKind kind, string? expectedRepository) {
        Write("current/.git/HEAD", "ref: refs/heads/work\n");
        Write("current/.git/config", "[remote \"origin\"]\n url = https://github.com/Sample/Current.git\n");
        var cwd = System.Text.Json.JsonSerializer.Serialize(Path.Combine(_root, "current"));
        Write("projects/sample/session.jsonl", $$$"""
            {"type":"assistant","sessionId":"fake","cwd":{{{cwd}}},"gitBranch":"work","timestamp":"2026-11-01T11:59:00Z","message":{"stop_reason":"end_turn"}}
            """);
        var snapshot = LocalAgentWorkStateService.ReadLocal(new[] { Root("claude", kind) }, Now);
        Assert.Equal(expectedRepository, Assert.Single(snapshot.Sessions).Repository);
    }

    private const string CodexMeta = """
        {"timestamp":"2026-10-01T10:00:00Z","type":"session_meta","payload":{"id":"fake-session","cwd":"/unavailable/sample","git":{"branch":"feature/work","repository_url":"https://github.com/Sample/Project.git"}}}
        """;

    [Theory]
    [InlineData("task_started", AgentWorkState.Running, AgentTurnOutcome.Unknown)]
    [InlineData("task_complete", AgentWorkState.Idle, AgentTurnOutcome.Completed)]
    [InlineData("turn_aborted", AgentWorkState.Idle, AgentTurnOutcome.Interrupted)]
    [InlineData("request_user_input", AgentWorkState.WaitingOnPerson, AgentTurnOutcome.Unknown)]
    public void DesktopActivity_RefreshesOpenTurnWithoutOverridingTerminalEvents(string type, AgentWorkState state, AgentTurnOutcome outcome) {
        Write("sessions/rollout-fake.jsonl", CodexMeta + "\n" + $$$"""
            {"timestamp":"2026-11-01T10:00:00Z","type":"event_msg","payload":{"type":"{{{type}}}"}}
            """);
        var metadata = new AgentSessionState { Provider = "codex", Id = "fake-session", Machine = Environment.MachineName, SourceRootId = "fixture",
            LastActivityAtUtc = Now.AddSeconds(-1) };
        var snapshot = LocalAgentWorkStateService.ReadLocal(new[] { Root("codex") }, Now, codexMetadata: new[] { metadata });
        var session = Assert.Single(snapshot.Sessions);
        Assert.Equal(state, session.State);
        Assert.Equal(outcome, session.LastTurnOutcome);
        Assert.Equal(metadata.LastActivityAtUtc, session.LastActivityAtUtc);
        Assert.Equal(state == AgentWorkState.Running, session.StateIsInferred);
        Assert.DoesNotContain(snapshot.NeedsAttention, static item => item.Kind == AgentAttentionKind.IncompleteTurn);
    }

    [Fact]
    public void CaptureTime_IsTakenAfterEnumeratingSources() {
        // Enumeration is the public synchronous boundary: activity arrives after collection has begun.
        System.Collections.Generic.IEnumerable<SourceRootRecord> ArrivingActivity() {
            var boundary = DateTimeOffset.UtcNow;
            Assert.True(SpinWait.SpinUntil(() => DateTimeOffset.UtcNow > boundary, TimeSpan.FromSeconds(1)));
            var activity = DateTimeOffset.UtcNow;
            Write("sessions/rollout-fake.jsonl", CodexMeta + "\n" + $$$"""
                {"timestamp":"{{{activity.ToString("O")}}}","type":"event_msg","payload":{"type":"task_started"}}
                """);
            yield return Root("codex");
        }
        var snapshot = LocalAgentWorkStateService.ReadLocal(ArrivingActivity());
        var session = Assert.Single(snapshot.Sessions);
        Assert.Equal(AgentWorkState.Running, session.State);
        Assert.True(session.LastActivityAtUtc <= snapshot.CapturedAtUtc);
        Assert.Empty(snapshot.NeedsAttention);
    }

    [Fact]
    public void Cache_ReusesUnchangedObservationsButReevaluatesClockAndInvalidatesSizeAndMtime() {
        var path = "sessions/rollout-fake.jsonl";
        var content = CodexMeta + "\n" + """
            {"timestamp":"2026-11-01T11:59:00Z","type":"event_msg","payload":{"type":"task_started","message":"private sentinel"}}
            """;
        Write(path, content);
        var cache = new InMemoryRawArtifactStore();
        var options = new AgentWorkOptions { Cache = cache };
        AgentWorkSnapshot Scan(DateTimeOffset now) => LocalAgentWorkStateService.ReadLocal(new[] { Root("codex") }, now, options: options);
        var cold = Scan(Now);
        Assert.Equal(1, cold.Scan!.FilesRead);
        Assert.Equal(AgentWorkState.Running, Assert.Single(cold.Sessions).State);
        var warm = Scan(Now.AddMinutes(10));
        Assert.Equal(1, warm.Scan!.FilesReused);
        Assert.Equal(0, warm.Scan.FilesRead);
        Assert.Equal(AgentTurnOutcome.Incomplete, Assert.Single(warm.Sessions).LastTurnOutcome);
        Assert.Equal(AgentAttentionKind.IncompleteTurn, Assert.Single(warm.NeedsAttention).Kind);
        Assert.DoesNotContain("private sentinel", Assert.Single(cache.GetAll()).StateJson!);
        Write("session_index.jsonl", """{"id":"fake-session","thread_name":"Fresh explicit name"}""");
        var renamed = Scan(Now);
        Assert.Equal(1, renamed.Scan!.FilesReused);
        Assert.Equal("Fresh explicit name", Assert.Single(renamed.Sessions).Title);
        Assert.Equal(AgentWorkState.Running, renamed.Sessions[0].State);
        // Same length, different mtime must invalidate too.
        Write(path, content.Replace("task_started", "turn_aborted"));
        File.SetLastWriteTimeUtc(Path.Combine(_root, path), DateTime.UtcNow.AddMinutes(-1));
        var changed = Scan(Now);
        Assert.Equal(1, changed.Scan!.FilesRead);
        Assert.Equal(AgentTurnOutcome.Interrupted, Assert.Single(changed.Sessions).LastTurnOutcome);
        File.AppendAllText(Path.Combine(_root, path), "\n" + """
            {"timestamp":"2026-11-01T12:00:00Z","type":"event_msg","payload":{"type":"task_complete"}}
            """);
        var appended = Scan(Now);
        Assert.Equal(1, appended.Scan!.FilesRead);
        Assert.Equal(AgentTurnOutcome.Completed, Assert.Single(appended.Sessions).LastTurnOutcome);
        File.Delete(Path.Combine(_root, path));
        File.Delete(Path.Combine(_root, "session_index.jsonl"));
        Assert.Empty(Scan(Now).Sessions);
    }

    [Fact]
    public void ActiveWithin_SkipsOldFilesBeforeCacheOrParserAndFiltersCopiedOldActivity() {
        const string id = "01234567-0123-4567-89ab-0123456789ab";
        var recentPath = "sessions/rollout-" + id + ".jsonl";
        Write(recentPath, CodexMeta.Replace("fake-session", id) + "\n" + """
            {"timestamp":"2026-10-01T11:59:00Z","type":"event_msg","payload":{"type":"task_started"}}
            """);
        File.SetLastWriteTimeUtc(Path.Combine(_root, recentPath), Now.AddDays(-30).UtcDateTime);
        Write("sessions/rollout-old.jsonl", CodexMeta);
        File.SetLastWriteTimeUtc(Path.Combine(_root, "sessions/rollout-old.jsonl"), Now.AddDays(-30).UtcDateTime);
        Write("sessions/rollout-copied.jsonl", CodexMeta.Replace("fake-session", "copied-old"));
        var metadata = new AgentSessionState { Provider = "codex", Id = id, Machine = Environment.MachineName, SourceRootId = "fixture", LastActivityAtUtc = Now.AddSeconds(-1) };
        var cache = new InMemoryRawArtifactStore();
        LocalAgentWorkStateService.ReadLocal(new[] { Root("codex") }, Now, options: new AgentWorkOptions { ActiveWithin = null, Cache = cache });
        var snapshot = LocalAgentWorkStateService.ReadLocal(new[] { Root("codex") }, Now, codexMetadata: new[] { metadata },
            options: new AgentWorkOptions { ActiveWithin = TimeSpan.FromDays(7), Cache = cache });
        var session = Assert.Single(snapshot.Sessions);
        Assert.Equal(id, session.Id);
        Assert.Equal(AgentWorkState.Idle, session.State);
        Assert.Equal(2, snapshot.Scan!.FilesSkipped);
        Assert.Equal(1, snapshot.Scan.FilesReused);
        Assert.Equal(0, snapshot.Scan.FilesRead);
    }

    [Fact]
    public void WslActivity_UsesLocalLogsWithoutResolvingWorkspaceOnWindows() {
        Write("sessions/rollout-fake.jsonl", CodexMeta + "\n" + """
            {"timestamp":"2026-11-01T11:59:00Z","type":"event_msg","payload":{"type":"task_started"}}
            """);
        var root = Root("codex");
        root.PlatformHint = "wsl";
        root.MachineLabel = "synthetic-distribution";
        var snapshot = LocalAgentWorkStateService.ReadLocal(new[] { root }, Now);
        Assert.Equal(AgentWorkState.Running, Assert.Single(snapshot.Sessions).State);
        Assert.Equal("synthetic-distribution", snapshot.Sessions[0].Machine);
        Assert.True(snapshot.Sessions[0].StateIsInferred);
        Assert.Empty(snapshot.NeedsAttention);
    }

    [Fact]
    public void ActiveWithin_DoesNotOpenAnOldFileEvenWhenDesktopMetadataIsRecent() {
        const string path = "sessions/rollout-renamed.jsonl";
        Write(path, "private sentinel malformed JSON");
        var file = Path.Combine(_root, path);
        File.SetLastWriteTimeUtc(file, Now.AddDays(-30).UtcDateTime);
        using var locked = new FileStream(file, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
        var metadata = new AgentSessionState { Provider = "codex", Id = "fake-session", Machine = Environment.MachineName,
            SourceRootId = "fixture", LastActivityAtUtc = Now.AddSeconds(-1) };
        var snapshot = LocalAgentWorkStateService.ReadLocal(new[] { Root("codex") }, Now, codexMetadata: new[] { metadata });
        Assert.Equal(1, snapshot.Scan!.FilesSkipped);
        Assert.Equal(0, snapshot.Scan.FilesRead);
        Assert.Empty(snapshot.Diagnostics);
        Assert.Equal(AgentWorkState.Idle, Assert.Single(snapshot.Sessions).State);
    }

    [Fact]
    public void Metadata_RequiresOriginatingRootForRunningInferenceAndActivityOverlay() {
        Directory.CreateDirectory(Path.Combine(_root, "recovered"));
        var recovered = new SourceRootRecord("recovered", "codex", UsageSourceKind.RecoveredFolder, Path.Combine(_root, "recovered")) {
            MachineLabel = Environment.MachineName
        };
        Write("sessions/rollout-fake.jsonl", CodexMeta + "\n" + """
            {"timestamp":"2026-10-01T11:59:00Z","type":"event_msg","payload":{"type":"task_started"}}
            """);
        var recent = new AgentSessionState { Provider = "codex", Id = "fake-session", Machine = Environment.MachineName,
            SourceRootId = recovered.Id, LastActivityAtUtc = Now.AddSeconds(-1) };
        var snapshot = LocalAgentWorkStateService.ReadLocal(new[] { Root("codex"), recovered }, Now,
            codexMetadata: new[] { recent, recent with { Id = "metadata-only" } });
        Assert.Equal(2, snapshot.Sessions.Count);
        Assert.All(snapshot.Sessions, static session => Assert.Equal(AgentWorkState.Idle, session.State));
        var selected = snapshot.Sessions.Single(static s => s.Id == "fake-session");
        Assert.Equal(recovered.Id, selected.SourceRootId);
        Assert.Equal(AgentTurnOutcome.Unknown, selected.LastTurnOutcome);
        Assert.Null(selected.Repository); // The recovered record must not inherit local log fields.
        var localOnly = LocalAgentWorkStateService.ReadLocal(new[] { Root("codex") }, Now,
            codexMetadata: new[] { recent with { Id = "local-metadata-only", SourceRootId = "fixture" } });
        Assert.Equal(AgentWorkState.Idle, localOnly.Sessions.Single(static s => s.Id == "local-metadata-only").State);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void Metadata_MergesWithinEachRootBeforeChoosingTheNewestSession(bool logs, bool reverseRoots) {
        Write("recovered/session_index.jsonl", """{"id":"fake-session","thread_name":"Recovered explicit name"}""");
        if (logs) {
            Write("sessions/rollout-fake.jsonl", CodexMeta + "\n" + """
                {"timestamp":"2026-11-01T10:00:00Z","type":"event_msg","payload":{"type":"task_started"}}
                """);
            Write("recovered/sessions/rollout-fake.jsonl", CodexMeta + "\n" + """
                {"timestamp":"2026-11-01T11:55:00Z","type":"event_msg","payload":{"type":"task_started"}}
                """);
        }
        var recovered = new SourceRootRecord("recovered", "codex", UsageSourceKind.RecoveredFolder, Path.Combine(_root, "recovered")) {
            MachineLabel = Environment.MachineName
        };
        var roots = reverseRoots ? new[] { recovered, Root("codex") } : new[] { Root("codex"), recovered };
        var metadata = new AgentSessionState { Provider = "codex", Id = "fake-session", SourceRootId = "fixture",
            Machine = Environment.MachineName, LastActivityAtUtc = Now.AddSeconds(-1) };
        Directory.CreateDirectory(_root);
        var snapshot = LocalAgentWorkStateService.ReadLocal(roots, Now, codexMetadata: new[] { metadata },
            options: new AgentWorkOptions { ActiveWithin = TimeSpan.FromDays(7) });
        var session = Assert.Single(snapshot.Sessions);
        Assert.Equal("fixture", session.SourceRootId);
        Assert.Equal(metadata.LastActivityAtUtc, session.LastActivityAtUtc);
        Assert.Equal(logs ? AgentWorkState.Running : AgentWorkState.Idle, session.State);
        Assert.Empty(snapshot.NeedsAttention);
    }

    private AgentWorkSnapshot Read(string provider) => LocalAgentWorkStateService.ReadLocal(new[] { Root(provider) }, Now, options: new AgentWorkOptions { ActiveWithin = null });
    private SourceRootRecord Root(string provider, UsageSourceKind kind = UsageSourceKind.LocalLogs) => new("fixture", provider, kind, _root);
    private void Write(string relative, string content) {
        var path = Path.Combine(_root, relative);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content);
    }
    public void Dispose() {
        if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
    }
}
