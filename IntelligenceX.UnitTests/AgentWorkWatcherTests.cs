using System;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using IntelligenceX.Telemetry.Agents;
using IntelligenceX.Telemetry.GitHub;
using IntelligenceX.Telemetry.Usage;
using Xunit;

namespace IntelligenceX.UnitTests;

public sealed class AgentWorkWatcherTests : IDisposable {
    private readonly string _root = Path.Combine(Path.GetTempPath(), "ix-live-fixtures", Guid.NewGuid().ToString("N"));
    private SourceRootRecord Root => new("live-fixture", "codex", UsageSourceKind.LocalLogs, _root);
    private string PathToLog => Path.Combine(_root, "sessions", "rollout-fake.jsonl");

    [Fact]
    public void Diagnostics_QueueOnlyTransitionsAndKeepRootFailuresSeparate() {
        var signals = 0;
        var diagnostics = new AgentWorkDiagnostics(() => signals++);
        diagnostics.Set("watcher", "watch-unavailable", "one");
        diagnostics.Set("watcher", "watch-unavailable", "one");
        Assert.Equal(1, signals);
        diagnostics.Set("watcher", "watch-unavailable", "two");
        diagnostics.Set("watcher", null, "one");
        Assert.Equal(3, signals);
        Assert.Equal("watch-unavailable", Assert.Single(diagnostics.Snapshot).Code);
        diagnostics.Set("watcher", null, "one");
        Assert.Equal(3, signals);
        diagnostics.Set("watcher", null, "two");
        Assert.Empty(diagnostics.Snapshot);
        Assert.Equal(4, signals);
    }

    [Fact]
    public async Task WatchFailure_ReconcilesAndClearsDiagnosticOnRecovery() {
        Write(Meta + Event("task_started"));
        var clock = new AgentWorkTestClock();
        var attempts = 0;
        var allow = 0;
        var second = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        FileSystemWatcher Create(string directory, string filter) {
            var count = Interlocked.Increment(ref attempts);
            if (Volatile.Read(ref allow) == 0) {
                if (count == 2) second.TrySetResult(true);
                throw new IOException("synthetic watch failure");
            }
            return new FileSystemWatcher(directory, filter);
        }
        await using var watcher = new AgentWorkWatcher(new[] { Root }, new AgentWorkOptions { ReconcileInterval = TimeSpan.FromSeconds(5) },
            null, null, null, () => throw new InvalidOperationException("Explicit roots must replace discovery."), Create, clock);
        var recovered = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        watcher.SnapshotChanged += (_, e) => {
            if (e.Snapshot.Diagnostics.All(static d => d.Code != "watch-unavailable")) recovered.TrySetResult(true);
        };
        await watcher.StartAsync();
        Assert.Contains(watcher.CurrentSnapshot.Diagnostics, static d => d.Code == "watch-unavailable");
        await Within(clock.Scheduled(TimeSpan.FromSeconds(5)));
        await Within(clock.Scheduled(TimeSpan.FromMilliseconds(75)));
        clock.Advance(TimeSpan.FromMilliseconds(75));
        await Within(second.Task);
        Interlocked.Exchange(ref allow, 1);
        clock.Advance(TimeSpan.FromSeconds(5));
        await Within(clock.Scheduled(TimeSpan.FromMilliseconds(75), 2));
        clock.Advance(TimeSpan.FromMilliseconds(75));
        await Within(recovered.Task);
        Assert.Equal(3, attempts);
    }

    [Fact]
    public async Task AutomaticDiscovery_IsRefreshedOnTheIndependentReconcileCadence() {
        var clock = new AgentWorkTestClock();
        var discoveries = 0;
        System.Collections.Generic.IReadOnlyList<SourceRootRecord> Discover() {
            Interlocked.Increment(ref discoveries);
            return Directory.Exists(_root) ? new[] { Root } : Array.Empty<SourceRootRecord>();
        }
        await using var watcher = new AgentWorkWatcher(null, new AgentWorkOptions { ReconcileInterval = TimeSpan.FromSeconds(5) },
            null, null, null, Discover, static (directory, filter) => new FileSystemWatcher(directory, filter), clock);
        var found = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        watcher.SnapshotChanged += (_, e) => { if (e.Snapshot.Sessions.Any()) found.TrySetResult(true); };
        await watcher.StartAsync();
        await Within(clock.Scheduled(TimeSpan.FromSeconds(5)));
        Write(Meta + Event("task_started"));
        clock.Advance(TimeSpan.FromSeconds(5));
        await Within(clock.Scheduled(TimeSpan.FromMilliseconds(75)));
        clock.Advance(TimeSpan.FromMilliseconds(75));
        await Within(found.Task);
        Assert.True(discoveries >= 2);
        Assert.Equal(AgentWorkState.Running, Assert.Single(watcher.CurrentSnapshot.Sessions).State);
    }

    [Fact]
    public void IncrementalReader_TailsUtf8AndPreservesPendingInputAcrossAppendAndRewrite() {
        Write(Meta + Event("task_started") + """
            {"type":"response_item","payload":{"type":"function_call","name":"request_user_input","call_id":"synthetic-call","arguments":"private sentinel"}}
            """ + "\n");
        var options = new AgentWorkOptions { Cache = new InMemoryRawArtifactStore() };
        AgentWorkSnapshot Read() => LocalAgentWorkStateService.ReadLocal(new[] { Root }, options: options);
        Assert.Equal(AgentWorkState.WaitingOnPerson, Assert.Single(Read().Sessions).State);
        var append = """
            {"type":"response_item","payload":{"type":"function_call_output","call_id":"synthetic-call","output":"private sentinel"}}
            """ + "\n" + Event("thread_name_updated", ",\"thread_name\":\"Wątek 中文\"");
        File.AppendAllText(PathToLog, append, new UTF8Encoding(false));
        var resumed = Read();
        Assert.Equal(AgentWorkState.Running, Assert.Single(resumed.Sessions).State);
        Assert.Equal("Wątek 中文", resumed.Sessions[0].Title);
        Assert.Equal(1, resumed.Scan!.FilesTailed);
        Assert.Equal(Encoding.UTF8.GetByteCount(append), resumed.Scan.BytesRead);
        Assert.DoesNotContain("private sentinel", AgentWorkJson.Serialize(resumed));
        Write(Meta + Event("turn_aborted")); // Truncation replaces prior state rather than resuming it.
        var replaced = Read();
        Assert.Equal(0, replaced.Scan!.FilesTailed);
        Assert.Equal(AgentTurnOutcome.Interrupted, Assert.Single(replaced.Sessions).LastTurnOutcome);
        Assert.Null(replaced.Sessions[0].Title);
    }

    [Fact]
    public void IncrementalReader_RereadsPartialLinesAndDetectsGrowingRewrites() {
        Write(Meta + Event("task_started"));
        var options = new AgentWorkOptions { Cache = new InMemoryRawArtifactStore() };
        AgentWorkSnapshot Read() => LocalAgentWorkStateService.ReadLocal(new[] { Root }, options: options);
        Read();
        File.AppendAllText(PathToLog, "{\"type\":\"event_msg\",\"payload\":{");
        Assert.Contains(Read().Diagnostics, static d => d.Code == "malformed-record");
        File.AppendAllText(PathToLog, "\"type\":\"turn_aborted\"}}\n");
        var completedLine = Read();
        Assert.Equal(0, completedLine.Scan!.FilesTailed);
        Assert.Equal(AgentTurnOutcome.Interrupted, Assert.Single(completedLine.Sessions).LastTurnOutcome);
        Assert.Empty(completedLine.Diagnostics);
        Write(Meta.Replace("fake-live", "rewritten") + Event("task_complete") + new string(' ', 1000) + "\n");
        var grown = Read();
        Assert.Equal(0, grown.Scan!.FilesTailed);
        Assert.Equal("rewritten", Assert.Single(grown.Sessions).Id);
        Assert.Equal(AgentTurnOutcome.Completed, grown.Sessions[0].LastTurnOutcome);
    }

    [Fact]
    public void ClaudeAppend_UpdatesTheSameSessionWithoutRereadingHistory() {
        Directory.CreateDirectory(_root);
        var path = Path.Combine(_root, "fake.jsonl");
        var root = new SourceRootRecord("claude-fixture", "claude", UsageSourceKind.LocalLogs, path);
        File.WriteAllText(path, "{\"type\":\"user\",\"sessionId\":\"claude-live\",\"timestamp\":\"" + DateTimeOffset.UtcNow.ToString("O") + "\"}\n");
        var options = new AgentWorkOptions { Cache = new InMemoryRawArtifactStore() };
        Assert.Equal(AgentWorkState.Running, Assert.Single(LocalAgentWorkStateService.ReadLocal(new[] { root }, options: options).Sessions).State);
        var append = "{\"type\":\"assistant\",\"sessionId\":\"claude-live\",\"message\":{\"stop_reason\":\"end_turn\"}}\n";
        File.AppendAllText(path, append);
        var changed = LocalAgentWorkStateService.ReadLocal(new[] { root }, options: options);
        Assert.Equal(AgentWorkState.WaitingOnPerson, Assert.Single(changed.Sessions).State);
        Assert.Equal(1, changed.Scan!.FilesTailed);
        Assert.Equal(Encoding.UTF8.GetByteCount(append), changed.Scan.BytesRead);
    }

    [Fact]
    public async Task Watcher_EmitsAppendsWhileGitHubIsBlockedAndStopsOnDisposal() {
        Write(Meta + Event("task_started"));
        var requested = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        using var http = new HttpClient(new BlockedHandler(requested)) { BaseAddress = new Uri("https://api.github.com/") };
        using var github = new GitHubDashboardService(http, disposeHttpClient: false);
        await using var watcher = new AgentWorkWatcher(new[] { Root }, new AgentWorkOptions {
            ReconcileInterval = TimeSpan.FromMinutes(1), GitHubRefreshInterval = TimeSpan.FromMinutes(5)
        }, github: github, gitHubLogin: "sample");
        var interrupted = new TaskCompletionSource<AgentWorkSnapshot>(TaskCreationOptions.RunContinuationsAsynchronously);
        var events = 0;
        watcher.SnapshotChanged += (_, e) => {
            Interlocked.Increment(ref events);
            if (e.Snapshot.Sessions.Any(static s => s.LastTurnOutcome == AgentTurnOutcome.Interrupted)) interrupted.TrySetResult(e.Snapshot);
        };
        await watcher.StartAsync();
        await Within(requested.Task);
        Assert.Equal(AgentWorkState.Running, Assert.Single(watcher.CurrentSnapshot.Sessions).State);
        File.AppendAllText(PathToLog, Event("turn_aborted"));
        var changed = await Within(interrupted.Task);
        Assert.Equal(1, changed.Scan!.FilesTailed);
        Assert.Equal(AgentAttentionKind.InterruptedTurn, Assert.Single(changed.NeedsAttention).Kind);
        await watcher.DisposeAsync();
        var stoppedEvents = events;
        File.AppendAllText(PathToLog, Event("task_started"));
        await Task.Delay(150);
        Assert.Equal(stoppedEvents, events);
    }

    [Fact]
    public async Task Reconcile_FindsRootsCreatedAfterStartupWithoutPublishingUnchangedSnapshots() {
        await using var watcher = new AgentWorkWatcher(new[] { Root }, new AgentWorkOptions {
            ReconcileInterval = TimeSpan.FromMilliseconds(50)
        });
        var running = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var events = 0;
        watcher.SnapshotChanged += (_, e) => {
            Interlocked.Increment(ref events);
            if (e.Snapshot.Sessions.Any(static s => s.State == AgentWorkState.Running)) running.TrySetResult(true);
        };
        await watcher.StartAsync();
        Write(Meta + Event("task_started"));
        await Within(running.Task);
        var count = events;
        await Task.Delay(300);
        Assert.Equal(count, events);
        Assert.True(watcher.CurrentSnapshot.Scan!.FilesReused > 0);
    }

    private static async Task<T> Within<T>(Task<T> task) {
        Assert.Same(task, await Task.WhenAny(task, Task.Delay(TimeSpan.FromSeconds(10))));
        return await task;
    }
    private static async Task Within(Task task) {
        Assert.Same(task, await Task.WhenAny(task, Task.Delay(TimeSpan.FromSeconds(10))));
        await task;
    }
    private const string Meta = "{\"type\":\"session_meta\",\"payload\":{\"id\":\"fake-live\"}}\n";
    private static string Event(string type, string fields = "") =>
        "{\"timestamp\":\"" + DateTimeOffset.UtcNow.ToString("O") + "\",\"type\":\"event_msg\",\"payload\":{\"type\":\"" + type + "\"" + fields + "}}\n";
    private void Write(string content) {
        Directory.CreateDirectory(Path.GetDirectoryName(PathToLog)!);
        File.WriteAllText(PathToLog, content, new UTF8Encoding(false));
    }
    public void Dispose() { if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true); }
    private sealed class BlockedHandler(TaskCompletionSource<bool> requested) : HttpMessageHandler {
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) {
            requested.TrySetResult(true);
            await Task.Delay(Timeout.Infinite, cancellationToken);
            throw new InvalidOperationException("Unreachable fake response.");
        }
    }
}
