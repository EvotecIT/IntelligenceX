using System;
using System.IO;
using System.Linq;
using System.Text;
using IntelligenceX.Telemetry.Agents;
using IntelligenceX.Telemetry.Usage;
using Xunit;

namespace IntelligenceX.UnitTests;

public sealed class AgentWorkIncrementalTests : IDisposable {
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "ix-state-projection", Guid.NewGuid().ToString("N"));
    private readonly DateTimeOffset _now = DateTimeOffset.UtcNow;
    private string Log => Path.Combine(_directory, "sessions", "rollout-fixture.jsonl");
    private SourceRootRecord Root => new("projection-fixture", "codex", UsageSourceKind.LocalLogs, _directory);

    [Fact]
    public void DirectorySnapshot_RefreshesFrozenMembershipAtTheBoundedAge() {
        Directory.CreateDirectory(_directory);
        var first = Path.Combine(_directory, "first.jsonl");
        File.WriteAllText(first, "");
        var cache = new UsageDirectorySnapshotCache();
        cache.BeginScan(_now);
        Assert.Equal(first, Assert.Single(cache.Files(_directory, "*.jsonl", SearchOption.AllDirectories)));
        var stamp = Directory.GetLastWriteTimeUtc(_directory);
        var child = Path.Combine(_directory, "nested");
        Directory.CreateDirectory(child);
        File.WriteAllText(Path.Combine(child, "new.jsonl"), "");
        Directory.SetLastWriteTimeUtc(_directory, stamp);
        cache.BeginScan(_now.AddMinutes(1));
        Assert.Single(cache.Files(_directory, "*.jsonl", SearchOption.AllDirectories));
        cache.BeginScan(_now.AddMinutes(5));
        Assert.Equal(2, cache.Files(_directory, "*.jsonl", SearchOption.AllDirectories).Count());
    }

    [Theory]
    [InlineData("codex")]
    [InlineData("claude")]
    public void LargeToolPayload_ProjectsTrailingStateMetadataWithBoundedAllocation(string provider) {
        var root = new SourceRootRecord("projection", provider, UsageSourceKind.LocalLogs, _directory);
        Directory.CreateDirectory(Path.GetDirectoryName(Log)!);
        var path = provider == "codex" ? Log : Path.Combine(_directory, "fixture.jsonl");
        var meta = "{\"type\":\"session_meta\",\"payload\":{\"id\":\"fixture\"}}\n";
        var question = "{\"type\":\"response_item\",\"payload\":{\"type\":\"function_call\",\"name\":\"request_user_input\",\"call_id\":\"input\"}}\n";
        File.WriteAllText(path, provider == "codex" ? meta + question : "{\"type\":\"user\",\"sessionId\":\"fixture\"}\n");
        var options = new AgentWorkOptions { Cache = new InMemoryRawArtifactStore() };
        LocalAgentWorkStateService.ReadLocal(new[] { root }, _now, options: options); // Prime generated JSON and parser code outside the allocation check.
        var body = "private sentinel " + new string('x', 4 * 1024 * 1024) + "\\\" escaped \\u4e2d";
        var record = provider == "codex"
            ? "{\"payload\":{\"output\":\"" + body + "\",\"call_id\":\"input\",\"type\":\"function_call_output\"},\"type\":\"response_item\",\"timestamp\":\"" + _now.ToString("O") + "\"}\n"
            : "{\"message\":{\"content\":[{\"type\":\"text\",\"text\":\"" + body + "\"},{\"type\":\"tool_use\",\"name\":\"AskUserQuestion\",\"input\":{\"private\":\"ignored\"}}],\"stop_reason\":null},\"type\":\"assistant\",\"sessionId\":\"fixture\",\"timestamp\":\"" + _now.ToString("O") + "\"}\n";
        File.AppendAllText(path, record, new UTF8Encoding(false));
        var before = GC.GetAllocatedBytesForCurrentThread();
        var snapshot = LocalAgentWorkStateService.ReadLocal(new[] { root }, _now, options: options);
        var allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        var session = Assert.Single(snapshot.Sessions);
        Assert.Equal(_now, session.LastActivityAtUtc);
        Assert.Equal(provider == "codex" ? AgentWorkState.Running : AgentWorkState.WaitingOnPerson, session.State);
        Assert.Equal(1, snapshot.Scan!.FilesTailed);
        Assert.True(allocated < 1024 * 1024, "A multi-megabyte ignored payload must not be materialized.");
        Assert.DoesNotContain("private sentinel", AgentWorkJson.Serialize(snapshot));
        Assert.DoesNotContain("private sentinel", Assert.Single(options.Cache!.GetAll()).StateJson!);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Projection_RecoversAfterMalformedStringBoundaryAndOpeningBom(bool invalidPrefix) {
        Directory.CreateDirectory(Path.GetDirectoryName(Log)!);
        File.WriteAllBytes(Log, invalidPrefix ? new byte[] { 0xef, 0x58, 0x0a } : new byte[] { 0xef });
        var options = new AgentWorkOptions { Cache = new InMemoryRawArtifactStore() };
        AgentWorkSnapshot Read() => LocalAgentWorkStateService.ReadLocal(new[] { Root }, _now, options: options);
        Assert.Contains(Read().Diagnostics, static item => item.Code == "malformed-record");
        using (var output = new FileStream(Log, FileMode.Append)) {
            if (!invalidPrefix) output.Write(new byte[] { 0xbb, 0xbf });
            var data = Encoding.UTF8.GetBytes(Meta("fixture") + "{\"ignored\":\"" + new string('x', 8192) + "\n" + Event("task_started"));
            output.Write(data);
        }
        var repaired = Read();
        Assert.Equal("fixture", Assert.Single(repaired.Sessions).Id);
        Assert.Equal(AgentWorkState.Running, repaired.Sessions[0].State);
        Assert.Equal(invalidPrefix ? 2 : 1, Assert.Single(repaired.Diagnostics).Count); // Completed errors remain; partial BOM diagnostics do not.
    }

    [Fact]
    public void PartialLargeRecord_KeepsCompleteCheckpointAndClearsTransientDiagnostic() {
        Directory.CreateDirectory(Path.GetDirectoryName(Log)!);
        var history = Meta("fixture") + new string(' ', 2 * 1024 * 1024) + "\n";
        File.WriteAllText(Log, history);
        var options = new AgentWorkOptions { Cache = new InMemoryRawArtifactStore() };
        AgentWorkSnapshot Read() => LocalAgentWorkStateService.ReadLocal(new[] { Root }, _now, options: options);
        Read();
        var partial = "{\"type\":\"response_item\",\"payload\":{\"type\":\"function_call_output\",\"output\":\"" + new string('x', 3 * 1024 * 1024);
        File.AppendAllText(Log, partial);
        var incomplete = Read();
        Assert.Contains(incomplete.Diagnostics, static item => item.Code == "malformed-record");
        Assert.Equal(1, incomplete.Scan!.FilesTailed);
        Assert.Equal(Encoding.UTF8.GetByteCount(partial), incomplete.Scan.BytesRead);
        var completion = "\"}}\n" + Event("turn_aborted");
        File.AppendAllText(Log, completion);
        var completed = Read();
        Assert.Equal(1, completed.Scan!.FilesTailed);
        Assert.Equal(Encoding.UTF8.GetByteCount(partial + completion), completed.Scan.BytesRead);
        Assert.Equal(AgentTurnOutcome.Interrupted, Assert.Single(completed.Sessions).LastTurnOutcome);
        Assert.Empty(completed.Diagnostics);
    }

    [Fact]
    public void IncrementalSnapshot_ReusesUnchangedRowsAndReevaluatesClockAndRemoteAttention() {
        Directory.CreateDirectory(Path.GetDirectoryName(Log)!);
        File.WriteAllText(Log, Meta("fixture") + Event("task_started"));
        var other = Path.Combine(Path.GetDirectoryName(Log)!, "rollout-other.jsonl");
        File.WriteAllText(other, Meta("other") + Event("task_started"));
        var collection = new AgentLocalCollection(new AgentWorkOptions { Cache = new InMemoryRawArtifactStore() }, incremental: true);
        var metadata = Array.Empty<AgentSessionState>();
        collection.Reconcile(new[] { Root }, _now, default);
        var first = collection.Snapshot(metadata, _now);
        var unchanged = collection.Snapshot(metadata, _now.AddSeconds(1));
        Assert.Same(first.Sessions, unchanged.Sessions);
        File.AppendAllText(Log, Event("turn_aborted"));
        collection.Update(Root.Id, Log, default);
        var changed = collection.Snapshot(metadata, _now.AddSeconds(2));
        Assert.Equal(AgentTurnOutcome.Interrupted, changed.Sessions.Single(static row => row.Id == "fixture").LastTurnOutcome);
        Assert.Same(first.Sessions.Single(static row => row.Id == "other"), changed.Sessions.Single(static row => row.Id == "other"));
        var aged = collection.Snapshot(metadata, _now.AddMinutes(5).AddTicks(1));
        Assert.Equal(AgentWorkState.Idle, aged.Sessions.Single(static row => row.Id == "other").State);
        Assert.Contains(aged.NeedsAttention, static item => item.SessionId == "other" && item.Kind == AgentAttentionKind.IncompleteTurn);
        var remote = collection.Snapshot(metadata, _now.AddMinutes(5).AddSeconds(1), pullRequests: new[] {
            new AgentPullRequestState { Url = "https://github.com/example/work/pull/1", IsAuthor = true, Checks = AgentCheckState.Failure }
        });
        Assert.Contains(remote.NeedsAttention, static item => item.Kind == AgentAttentionKind.FailedChecks);
    }

    [Fact]
    public void IncrementalSnapshot_PreservesFarFutureActivityWithoutOverflowingItsDeadline() {
        Directory.CreateDirectory(Path.GetDirectoryName(Log)!);
        File.WriteAllText(Log, Meta("fixture") + Event("task_started").Replace(_now.ToString("O"), DateTimeOffset.MaxValue.ToString("O")));
        var collection = new AgentLocalCollection(new AgentWorkOptions(), incremental: true);
        collection.Reconcile(new[] { Root }, _now, default);
        var snapshot = collection.Snapshot(Array.Empty<AgentSessionState>(), _now);
        var session = Assert.Single(snapshot.Sessions);
        Assert.Equal(DateTimeOffset.MaxValue, session.LastActivityAtUtc);
        Assert.Equal(AgentWorkState.Idle, session.State);
    }

    [Fact]
    public void IncrementalSnapshot_ExpiresRunningAtTheMergedDesktopActivityBoundary() {
        Directory.CreateDirectory(Path.GetDirectoryName(Log)!);
        File.WriteAllText(Log, Meta("fixture") + Event("task_started").Replace(_now.ToString("O"), _now.AddMinutes(-10).ToString("O")));
        var collection = new AgentLocalCollection(new AgentWorkOptions(), incremental: true);
        collection.Reconcile(new[] { Root }, _now, default);
        var metadata = new[] { new AgentSessionState { Provider = "codex", Id = "fixture", SourceRootId = Root.Id,
            Machine = Environment.MachineName, LastActivityAtUtc = _now } };
        Assert.Equal(AgentWorkState.Running, Assert.Single(collection.Snapshot(metadata, _now).Sessions).State);
        Assert.Equal(AgentWorkState.Running, Assert.Single(collection.Snapshot(metadata, _now.AddMinutes(5)).Sessions).State);
        var expired = Assert.Single(collection.Snapshot(metadata, _now.AddMinutes(5).AddTicks(1)).Sessions);
        Assert.Equal(AgentWorkState.Idle, expired.State);
        Assert.Equal(AgentTurnOutcome.Incomplete, expired.LastTurnOutcome);
    }

    [Fact]
    public void DirectoryMembership_RetriesAFailedListingWithoutAnotherTimestampChange() {
        Directory.CreateDirectory(_directory);
        var first = Path.Combine(_directory, "one.jsonl");
        var second = Path.Combine(_directory, "two.jsonl");
        File.WriteAllText(first, "");
        var denied = false;
        var cache = new UsageDirectorySnapshotCache(directory => denied
            ? throw new UnauthorizedAccessException("Synthetic listing failure") : Directory.GetFiles(directory, "*.jsonl"));
        Assert.Equal(first, Assert.Single(cache.Files(_directory, "*.jsonl", SearchOption.TopDirectoryOnly)));
        var stamp = Directory.GetLastWriteTimeUtc(_directory).AddSeconds(2);
        File.WriteAllText(second, "");
        Directory.SetLastWriteTimeUtc(_directory, stamp);
        denied = true;
        Assert.Throws<UnauthorizedAccessException>(() => cache.Files(_directory, "*.jsonl", SearchOption.TopDirectoryOnly).ToArray());
        denied = false; // Permissions recover; membership timestamp remains unchanged.
        Assert.Equal(stamp, Directory.GetLastWriteTimeUtc(_directory));
        Assert.Equal(new[] { first, second }, cache.Files(_directory, "*.jsonl", SearchOption.TopDirectoryOnly).OrderBy(static path => path).ToArray());
    }

    [Fact]
    public void Reconcile_ChecksFileActivityWhenDirectoryMembershipIsUnchangedAndFindsNewNestedFiles() {
        Directory.CreateDirectory(Path.GetDirectoryName(Log)!);
        File.WriteAllText(Log, Meta("fixture") + Event("task_started"));
        var collection = new AgentLocalCollection(new AgentWorkOptions { Cache = new InMemoryRawArtifactStore() }, incremental: true);
        collection.Reconcile(new[] { Root }, _now, default);
        var directory = Path.GetDirectoryName(Log)!;
        var stamp = Directory.GetLastWriteTimeUtc(directory);
        File.AppendAllText(Log, Event("turn_aborted"));
        Directory.SetLastWriteTimeUtc(directory, stamp);
        collection.Reconcile(new[] { Root }, _now, default); // No filesystem notification is supplied.
        Assert.Equal(AgentTurnOutcome.Interrupted, Assert.Single(collection.Snapshot(Array.Empty<AgentSessionState>(), _now).Sessions).LastTurnOutcome);
        var nested = Path.Combine(directory, "nested", "child");
        Directory.CreateDirectory(nested);
        File.WriteAllText(Path.Combine(nested, "rollout-added.jsonl"), Meta("added") + Event("task_complete"));
        // Membership invalidation depends on the directory stamp, not the host clock's write resolution.
        Directory.SetLastWriteTimeUtc(directory, stamp.AddSeconds(2));
        collection.Reconcile(new[] { Root }, _now, default);
        Assert.Equal(2, collection.Snapshot(Array.Empty<AgentSessionState>(), _now).Sessions.Count);
    }

    private string Event(string type) => "{\"type\":\"event_msg\",\"timestamp\":\"" + _now.ToString("O") + "\",\"payload\":{\"type\":\"" + type + "\"}}\n";
    private static string Meta(string id) => "{\"type\":\"session_meta\",\"payload\":{\"id\":\"" + id + "\"}}\n";
    public void Dispose() { if (Directory.Exists(_directory)) Directory.Delete(_directory, true); }
}
