using System.Text.Json;
using DBAClientX;
using IntelligenceX.Codex;
using IntelligenceX.Ledger.Agents;
using IntelligenceX.Ledger.Reporting;
using IntelligenceX.Telemetry.Agents;
using IntelligenceX.Telemetry.Usage;
using Xunit;

namespace IntelligenceX.Ledger.Tests;

public sealed class ArchiveCleanupTests : IDisposable {
    private readonly string _root = LedgerTestPaths.NewDirectory("archive-cleanup");
    private static readonly DateTimeOffset ReadyAt = new(2030, 1, 1, 0, 0, 0, TimeSpan.Zero);
    private readonly SessionArchiveCache _archives = new();

    [Fact]
    public void ReporterWaitsForMatchingArchiveThenObservesNativeRemovalWithoutTouchingTargets() {
        var local = NewChat();
        var scratch = CreateDirectory("scratch");
        var sentinel = Path.Combine(scratch, "keep.txt");
        File.WriteAllText(sentinel, "owned scratch");
        var store = NewStore();
        var id = Guid.Parse(local.Id);
        var native = OperatingSystem.IsWindows()
            ? local with { WorkingDirectory = @"\\?\" + local.WorkingDirectory }
            : local;
        store.Register(id, native, local.WorkingDirectory, [scratch], ReadyAt);
        Assert.Null(store.Read(id).Receipt);

        var unrelated = local with { SourceRootId = "other-profile", IsArchived = true };
        Capture(store, unrelated, ReadyAt.AddSeconds(1));
        Assert.Equal("archive-state-unavailable", store.Read(id).Receipt!.State);

        Capture(store, local, ReadyAt.AddSeconds(2));
        Assert.Equal("waiting-for-archive", store.Read(id).Receipt!.State);

        var archived = local with { IsArchived = true };
        var archiveSeen = ReadyAt.AddSeconds(3);
        Capture(store, archived, archiveSeen);
        var retained = store.Read(id).Receipt!;
        Assert.Equal("retained", retained.State);
        Assert.Equal(archiveSeen, retained.ArchiveObservedAtUtc);
        Assert.All(retained.Paths, path => Assert.Equal("directory-present", path.State));
        Assert.Equal("owned scratch", File.ReadAllText(sentinel));

        File.Delete(Path.Combine(local.WorkingDirectory!, ".git"));
        Directory.Delete(local.WorkingDirectory!);
        Capture(store, archived, ReadyAt.AddSeconds(4));
        Assert.Collection(store.Read(id).Receipt!.Paths,
            worktree => Assert.Equal(new ArchiveCleanupPath(local.WorkingDirectory!, "absent"), worktree),
            folder => Assert.Equal(new ArchiveCleanupPath(scratch, "directory-present"), folder));

        File.Delete(sentinel);
        Directory.Delete(scratch);
        Capture(store, archived, ReadyAt.AddSeconds(5));
        var verified = store.Read(id).Receipt!;
        Assert.Equal("verified-absent", verified.State);
        Assert.Equal(archiveSeen, verified.ArchiveObservedAtUtc);
    }

    [Fact]
    public void OpenTurnHoldsAnArchiveAndReopeningExpiresTheRegistration() {
        var local = NewChat();
        var scratch = CreateDirectory("scratch");
        var store = NewStore();
        var id = Guid.Parse(local.Id);
        store.Register(id, local, null, [scratch], ReadyAt);

        var archivedOpen = local with { IsArchived = true, TurnOpen = true };
        Capture(store, archivedOpen, ReadyAt.AddSeconds(1));
        var firstArchive = store.Read(id).Receipt!;
        Assert.Equal("chat-binding-unconfirmed", firstArchive.State);
        Assert.Empty(firstArchive.Paths);

        Capture(store, local, ReadyAt.AddSeconds(2));
        Capture(store, local with { IsArchived = true }, ReadyAt.AddSeconds(3));
        var reopened = store.Read(id).Receipt!;
        Assert.Equal("reopened", reopened.State);
        Assert.Equal(firstArchive.ArchiveObservedAtUtc, reopened.ArchiveObservedAtUtc);
        Assert.Empty(reopened.Paths);
        Assert.True(Directory.Exists(scratch));
    }

    [Fact]
    public void RegistrationRejectsActiveCheckoutContentAndPreservesTheFirstRegistration() {
        var local = NewChat();
        var scratch = CreateDirectory("scratch");
        var child = Directory.CreateDirectory(Path.Combine(local.WorkingDirectory!, "child")).FullName;
        var store = NewStore();
        var id = Guid.Parse(local.Id);

        Assert.Throws<InvalidDataException>(() => store.Register(id, local, null, [child], ReadyAt));
        Assert.Throws<InvalidDataException>(() => store.Register(id, local, scratch, [], ReadyAt));
        Assert.Throws<InvalidDataException>(() => store.Register(id, local with { IsArchived = true }, null, [scratch], ReadyAt));
        Assert.Null(store.Read(id).Registration);

        store.Register(id, local, null, [scratch], ReadyAt);
        Assert.Throws<IOException>(() => store.Register(id, local, local.WorkingDirectory, [], ReadyAt.AddSeconds(1)));
        var registration = store.Read(id).Registration!;
        Assert.Null(registration.Worktree);
        Assert.Equal(scratch, Assert.Single(registration.Scratch));
    }

    [Theory]
    [InlineData(false, "desktop", "waiting-for-new-archive")]
    [InlineData(true, "subagent", "chat-binding-unconfirmed")]
    public void StaleArchiveAndNonHumanSessionsCannotReconcilePaths(bool newArchive, string origin, string expected) {
        var local = NewChat();
        var store = NewStore();
        var id = Guid.Parse(local.Id);
        store.Register(id, local, local.WorkingDirectory, [], ReadyAt);
        Capture(store, local with { IsArchived = true, SessionSource = origin },
            newArchive ? ReadyAt.AddSeconds(1) : ReadyAt.AddSeconds(-1));
        var receipt = store.Read(id).Receipt!;
        Assert.Equal(expected, receipt.State);
        Assert.Empty(receipt.Paths);
    }

    [Fact]
    public void OldTurnWorkspaceCannotOverrideTheNativeArchivedWorkspace() {
        var local = NewChat();
        var store = NewStore();
        var id = Guid.Parse(local.Id);
        store.Register(id, local, local.WorkingDirectory, [], ReadyAt);
        var archivedAt = ReadyAt.AddSeconds(1);
        _archives.Observe([local with { IsArchived = true, WorkingDirectory = CreateDirectory("other-workspace") }], archivedAt);
        store.Observe([local with { IsArchived = true }], _archives, archivedAt);
        var receipt = store.Read(id).Receipt!;
        Assert.Equal("chat-binding-unconfirmed", receipt.State);
        Assert.Empty(receipt.Paths);
    }

    [Fact]
    public void MatchingArchiveDoesNotResolveAnAmbiguousChat() {
        var local = NewChat();
        var store = NewStore();
        var id = Guid.Parse(local.Id);
        store.Register(id, local, local.WorkingDirectory, [], ReadyAt);
        var archivedAt = ReadyAt.AddSeconds(1);
        var archived = local with { IsArchived = true };
        _archives.Observe([archived], archivedAt);
        store.Observe([archived, archived with { WorkingDirectory = CreateDirectory("other-workspace") }], _archives, archivedAt);
        var receipt = store.Read(id).Receipt!;
        Assert.Equal("archive-state-unavailable", receipt.State);
        Assert.Empty(receipt.Paths);
    }

    [Fact]
    public async Task RelocatedRolloutWaitsForAnExplicitDesktopArchiveFlag() {
        var local = NewChat();
        var id = Guid.Parse(local.Id);
        var source = new SourceRootRecord("desktop-fixture", "codex", UsageSourceKind.LocalLogs, CreateDirectory("codex"));
        var sessions = Directory.CreateDirectory(Path.Combine(source.Path, "sessions")).FullName;
        var archived = Directory.CreateDirectory(Path.Combine(source.Path, "archived_sessions")).FullName;
        var filename = $"rollout-2030-01-01T00-00-00-{id:D}.jsonl";
        var rollout = Path.Combine(sessions, filename);
        var events = new object[] {
            new { timestamp = ReadyAt.AddSeconds(-3), type = "session_meta", payload = new { id, cwd = local.WorkingDirectory, source = "desktop" } },
            new { timestamp = ReadyAt.AddSeconds(-2), type = "event_msg", payload = new { type = "task_started", turn_id = "turn" } },
            new { timestamp = ReadyAt.AddSeconds(-1), type = "event_msg", payload = new { type = "task_completed", turn_id = "turn" } }
        };
        await File.WriteAllLinesAsync(rollout, events.Select(entry => JsonSerializer.Serialize(entry)));
        var databasePath = Path.Combine(source.Path, "state_5.sqlite");
        using var database = new SQLite();
        await using (var writer = await database.OpenSessionAsync(databasePath)) {
            await writer.ExecuteNonQueryAsync("CREATE TABLE threads(id TEXT PRIMARY KEY, cwd TEXT, archived INTEGER)");
            await writer.ExecuteNonQueryAsync("INSERT INTO threads VALUES (@id, @cwd, 0)",
                new Dictionary<string, object?> { ["id"] = local.Id, ["cwd"] = local.WorkingDirectory });
        }

        var store = NewStore();
        var scratch = CreateDirectory("scratch");
        var initialMetadata = await CodexAgentSessionMetadataReader.ReadAsync(source);
        store.Register(id, Assert.Single(initialMetadata), null, [scratch], ReadyAt);
        File.Move(rollout, Path.Combine(archived, filename));

        async Task ObserveAsync(DateTimeOffset now) {
            var metadata = await CodexAgentSessionMetadataReader.ReadAsync(source);
            _archives.Observe(metadata, now);
            var snapshot = LocalAgentWorkStateService.ReadLocal([source], nowUtc: now, codexMetadata: metadata,
                options: new AgentWorkOptions { ActiveWithin = null });
            store.Observe(snapshot.Sessions, _archives, now);
        }

        await ObserveAsync(ReadyAt.AddSeconds(1));
        Assert.Equal("waiting-for-archive", store.Read(id).Receipt!.State);
        await using (var writer = await database.OpenSessionAsync(databasePath)) {
            await writer.ExecuteNonQueryAsync("UPDATE threads SET archived = 1 WHERE id = @id",
                new Dictionary<string, object?> { ["id"] = local.Id });
        }
        var archiveObservedAt = ReadyAt.AddSeconds(2);
        await ObserveAsync(archiveObservedAt);
        var receipt = store.Read(id).Receipt!;
        Assert.Equal("retained", receipt.State);
        Assert.Equal(archiveObservedAt, receipt.ArchiveObservedAtUtc);
        Assert.Equal(new ArchiveCleanupPath(scratch, "directory-present"), Assert.Single(receipt.Paths));
        Assert.True(Directory.Exists(scratch));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ReopenRemainsTerminalWhenReceiptWritesFail(bool failFirstArchiveWrite) {
        var local = NewChat();
        var archived = local with { IsArchived = true };
        var blockReceipt = false;
        var store = NewStore((path, receipt) => {
            if (blockReceipt) throw new IOException("Simulated receipt write failure");
            LocalFiles.WriteAtomic(path, receipt);
        });
        var id = Guid.Parse(local.Id);
        store.Register(id, local, local.WorkingDirectory, [], ReadyAt);
        blockReceipt = failFirstArchiveWrite;
        Capture(store, archived, ReadyAt.AddSeconds(1));
        var persistedArchive = failFirstArchiveWrite ? null : "retained";
        Assert.Equal(persistedArchive, store.Read(id).Receipt?.State);

        blockReceipt = true;
        var reopenedAt = ReadyAt.AddSeconds(2);
        Capture(store, local, reopenedAt);
        Assert.Equal(persistedArchive, store.Read(id).Receipt?.State);

        blockReceipt = false;
        Capture(store, archived, ReadyAt.AddSeconds(3));
        var receipt = store.Read(id).Receipt!;
        Assert.Equal("reopened", receipt.State);
        Assert.Equal(reopenedAt, receipt.ObservedAtUtc);
        Assert.Empty(receipt.Paths);

        Capture(store, archived, ReadyAt.AddSeconds(4));
        Assert.Equal(reopenedAt, store.Read(id).Receipt!.ObservedAtUtc);
        Assert.True(Directory.Exists(local.WorkingDirectory));
    }

    [Fact]
    public void FreshNativeReadConfirmsANewlyRegisteredArchiveEvenWhenTheReporterMissedReopening() {
        var local = NewChat();
        var archived = local with { IsArchived = true };
        var store = NewStore();
        var id = Guid.Parse(local.Id);
        _archives.Observe([archived], ReadyAt.AddSeconds(-2));
        store.Register(id, local, local.WorkingDirectory, [], ReadyAt);

        // A metadata read spanning registration could have saved an obsolete archive flag.
        _archives.Observe([archived], ReadyAt.AddSeconds(1), readStartedAtUtc: ReadyAt.AddSeconds(-1));
        store.Observe([archived], _archives, ReadyAt.AddSeconds(1));
        var held = store.Read(id).Receipt!;
        Assert.Equal("waiting-for-new-archive", held.State);
        Assert.Empty(held.Paths);

        var freshRead = ReadyAt.AddSeconds(2);
        _archives.Observe([archived], freshRead.AddSeconds(1), readStartedAtUtc: freshRead);
        store.Observe([archived], _archives, freshRead.AddSeconds(1));
        var receipt = store.Read(id).Receipt!;
        Assert.Equal("retained", receipt.State);
        Assert.Equal(freshRead, receipt.ArchiveObservedAtUtc);

        Capture(store, archived, ReadyAt.AddSeconds(4));
        Assert.Equal(freshRead, store.Read(id).Receipt!.ArchiveObservedAtUtc);
        Assert.Equal(ReadyAt.AddSeconds(-2), _archives.For(archived)!.ObservedAtUtc);
    }

    private AgentSessionState NewChat() {
        var worktree = CreateDirectory("worktree");
        File.WriteAllText(Path.Combine(worktree, ".git"), "gitdir: fixture");
        return new() {
            Provider = "codex", SessionSource = "desktop", SourceRootId = "local-profile",
            Id = Guid.NewGuid().ToString("D"), WorkingDirectory = worktree, ArchiveStateKnown = true, TurnOpen = false
        };
    }

    private string CreateDirectory(string name) => Directory.CreateDirectory(Path.Combine(_root, name)).FullName;

    private ArchiveCleanupStore NewStore(Action<string, string>? writeReceipt = null) => new(new() {
        Endpoint = "http://127.0.0.1:1", Token = new('t', 32), MachineId = "fixture", ReporterId = "reporter",
        SpoolDirectory = Path.Combine(_root, "spool")
    }, writeReceipt);

    private void Capture(ArchiveCleanupStore store, AgentSessionState session, DateTimeOffset now) {
        _archives.Observe([session], now);
        store.Observe([session], _archives, now);
    }

    public void Dispose() => Directory.Delete(_root, recursive: true);
}
