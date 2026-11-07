using System.Text.Json;
using DBAClientX;
using IntelligenceX.Codex;
using IntelligenceX.Telemetry.Agents;
using IntelligenceX.Telemetry.GitHub;
using IntelligenceX.Telemetry.Usage;

if (JsonSerializer.IsReflectionEnabledByDefault) throw new InvalidOperationException("JSON reflection must remain disabled.");

// Create and retire only our unique synthetic fixture, under a caller-supplied scratch directory.
if (args.Length != 1) throw new ArgumentException("Supply a scratch directory.");
var fixture = Path.Combine(Path.GetFullPath(args[0]), "ix-aot-fixture-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(Path.Combine(fixture, "codex", "sessions"));
Directory.CreateDirectory(Path.Combine(fixture, "claude", "projects", "sample"));
try {
    File.WriteAllText(Path.Combine(fixture, "codex", "sessions", "rollout-sample.jsonl"), """
        {"timestamp":"2026-11-01T10:00:00Z","type":"session_meta","payload":{"id":"fake-codex","git":{"repository_url":"https://github.com/Sample/Project.git","branch":"feature/work"}}}
        {"timestamp":"2026-11-01T10:00:01Z","type":"event_msg","payload":{"type":"thread_name_updated","thread_id":"fake-codex","thread_name":"Synthetic work"}}
        {"timestamp":"2026-11-01T10:00:02Z","type":"response_item","payload":{"type":"function_call","name":"functions.request_user_input_async","call_id":"fake-question","arguments":"private sentinel"}}
        {"timestamp":"2026-11-01T10:01:00Z","type":"event_msg","payload":{"type":"turn_aborted","message":"private sentinel"}}
        """);
    File.WriteAllText(Path.Combine(fixture, "claude", "projects", "sample", "sample.jsonl"), """
        {"timestamp":"2026-11-01T10:00:00Z","type":"assistant","sessionId":"fake-claude","message":{"stop_reason":"end_turn","content":[{"type":"text","text":"private sentinel"}]}}
        """);
    using (var sqlite = new SQLite()) {
        sqlite.ExecuteNonQuery(Path.Combine(fixture, "codex", "state_5.sqlite"), """
            CREATE TABLE threads (id TEXT, title TEXT, archived INTEGER);
            INSERT INTO threads VALUES ('fake-codex', 'private sentinel', 0);
            """);
    }
    var roots = new[] {
        new SourceRootRecord("codex-fixture", "codex", UsageSourceKind.LocalLogs, Path.Combine(fixture, "codex")),
        new SourceRootRecord("claude-fixture", "claude", UsageSourceKind.LocalLogs, Path.Combine(fixture, "claude"))
    };
    var metadata = await CodexAgentSessionMetadataReader.ReadAsync(roots[0]);
    using var cache = new SqliteRawArtifactStore(Path.Combine(fixture, "scan-cache.db"));
    var options = new AgentWorkOptions { Cache = cache };
    var local = LocalAgentWorkStateService.ReadLocal(roots, options: options, nowUtc: DateTimeOffset.Parse("2026-11-01T12:00:00Z"), codexMetadata: metadata);
    var warm = LocalAgentWorkStateService.ReadLocal(roots, options: options,
        nowUtc: local.CapturedAtUtc, codexMetadata: metadata);
    if (warm.Scan?.FilesReused != 2 || warm.Scan.FilesRead != 0 || AgentWorkJson.Serialize(local with { Scan = null }) != AgentWorkJson.Serialize(warm with { Scan = null })) {
        throw new InvalidOperationException("Native cache did not preserve parsed observations.");
    }
    // Root GitHub collection in the native binary too; pre-cancellation prevents any network access.
    using var github = new GitHubDashboardService("synthetic-token", "http://127.0.0.1:1");
    var partial = await github.EnrichAgentWorkAsync(local, "sample", cancellationToken: new CancellationToken(canceled: true));
    if (partial.Sessions.Count != 2 || partial.Diagnostics.All(static item => item.Code != "request-timeout")) {
        throw new InvalidOperationException("Optional remote cancellation lost local state.");
    }
    var snapshot = AgentWorkStateRules.Evaluate(local.Sessions, new[] {
        new AgentPullRequestState { Url = "https://github.com/Sample/Project/pull/1", Title = "Sample PR",
            HeadRepository = "Sample/Project", Branch = "feature/work", IsAuthor = true, Checks = AgentCheckState.Failure }
    }, local.CapturedAtUtc);
    var json = AgentWorkJson.Serialize(snapshot);
    var decoded = JsonSerializer.Deserialize(json, AgentWorkJsonContext.Default.AgentWorkSnapshot)
        ?? throw new InvalidOperationException("Snapshot deserialization failed.");
    if (decoded.Sessions.Count != 2 || decoded.NeedsAttention.Count != 3 || decoded.Sessions.All(static session => session.Title != "Synthetic work")
        || decoded.NeedsAttention.Any(static item => item.Provider == "claude" && item.Kind == AgentAttentionKind.WaitingOnPerson)) {
        throw new InvalidOperationException("Expected fixture sessions, database metadata and attention.");
    }
    var codexObservation = decoded.Sessions.Single(static session => session.Id == "fake-codex");
    if (codexObservation.PendingInputs.Single().Id != "fake-question" || !codexObservation.PendingInputs[0].AnswerIsUnconfirmed
        || codexObservation.FailureEvidence != AgentFailureEvidence.TerminalEvent
        || !codexObservation.ObservationSources.HasFlag(AgentObservationSources.SessionLog))
        throw new InvalidOperationException("Expected typed request, failure and observation evidence.");
    if (json.Contains("private sentinel", StringComparison.Ordinal)) throw new InvalidOperationException("Transcript content escaped the metadata boundary.");
    var observed = new TaskCompletionSource<AgentWorkSnapshot>(TaskCreationOptions.RunContinuationsAsynchronously);
    await using (var watcher = new AgentWorkWatcher(roots, new AgentWorkOptions { ActiveWithin = TimeSpan.FromDays(2) })) {
        watcher.SnapshotChanged += (_, e) => {
            if (e.Snapshot.Sessions.Any(static session => session.Id == "fake-codex" && session.State == AgentWorkState.Running))
                observed.TrySetResult(e.Snapshot);
        };
        await watcher.StartAsync();
        File.AppendAllText(Path.Combine(fixture, "codex", "sessions", "rollout-sample.jsonl"), "\n" +
            "{\"type\":\"event_msg\",\"timestamp\":\"" + DateTimeOffset.UtcNow.ToString("O") + "\",\"payload\":{\"type\":\"task_started\"}}\n");
        if (await Task.WhenAny(observed.Task, Task.Delay(TimeSpan.FromSeconds(10))) != observed.Task)
            throw new InvalidOperationException("Native watcher did not observe appended state.");
        var liveJson = AgentWorkJson.Serialize(await observed.Task);
        if (liveJson.Contains("private sentinel", StringComparison.Ordinal)) throw new InvalidOperationException("Live content projection failed.");
    }
    Console.WriteLine($"AOT snapshot passed: sessions={decoded.Sessions.Count}, attention={decoded.NeedsAttention.Count}, cached={warm.Scan.FilesReused}, watcher=True, reflection={JsonSerializer.IsReflectionEnabledByDefault}");
} finally {
    Directory.Delete(fixture, recursive: true);
}
