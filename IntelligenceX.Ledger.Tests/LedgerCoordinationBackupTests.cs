using System.Diagnostics;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Sockets;
using System.Text.Json;
using IntelligenceX.Ledger.Contracts;
using IntelligenceX.Ledger.Coordination;
using IntelligenceX.Ledger.Hosting;
using IntelligenceX.Ledger.Storage;
using Xunit;
using static IntelligenceX.Ledger.Tests.CoordinationLifecycleTests;

namespace IntelligenceX.Ledger.Tests;

public sealed class LedgerCoordinationBackupTests {
    [Fact]
    public async Task NormalStartupWithLegacyConfigExcludesASecondWriterWithoutEnablingRetainedCoordination() {
        var root = LedgerTestPaths.NewDirectory("backup-startup");
        Directory.CreateDirectory(root);
        try {
            var data = Path.Combine(root, "data");
            using (var coordination = new CoordinationStore(Path.Combine(data, "coordination.sqlite"), Policy))
                await coordination.InitializeAsync();
            var before = await File.ReadAllBytesAsync(Path.Combine(data, "coordination.sqlite"));
            var listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            var port = ((IPEndPoint)listener.LocalEndpoint).Port;
            listener.Stop();
            var config = Path.Combine(root, "settings.json");
            // This is the pre-coordination settings shape, including an existing data directory.
            await File.WriteAllTextAsync(config, JsonSerializer.Serialize(new {
                dataDirectory = data, readToken = new string('r', 32), writeToken = new string('w', 32), port,
                expectedReporters = new Dictionary<string, string[]> { ["source-host"] = ["synthetic-reporter"] }
            }));
            using var client = new HttpClient { BaseAddress = new Uri($"http://127.0.0.1:{port}") };
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", new('r', 32));
            for (var start = 0; start < 2; start++) {
                var command = new ProcessStartInfo("dotnet") { UseShellExecute = false, CreateNoWindow = true };
                command.ArgumentList.Add(typeof(Program).Assembly.Location);
                command.ArgumentList.Add("serve");
                command.ArgumentList.Add("--config");
                command.ArgumentList.Add(config);
                using var process = Process.Start(command)!;
                try {
                    using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(30));
                    while (true) {
                        Assert.False(process.HasExited, "Synthetic service exited before it accepted reads.");
                        try {
                            using var response = await client.GetAsync("/api/ledger/v1/status", deadline.Token);
                            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
                            break;
                        } catch (HttpRequestException) { await Task.Delay(25, deadline.Token); }
                    }
                    Assert.Throws<IOException>(() => LedgerBackup.AcquireServiceLease(data));
                    using var coordinationRead = await client.GetAsync("/api/coordination/v1/requests");
                    Assert.Equal(HttpStatusCode.Unauthorized, coordinationRead.StatusCode);
                } finally {
                    if (!process.HasExited) process.Kill(entireProcessTree: true);
                    await process.WaitForExitAsync();
                }
            }
            Assert.Equal(before, await File.ReadAllBytesAsync(Path.Combine(data, "coordination.sqlite")));
        } finally { Directory.Delete(root, recursive: true); }
    }

    [Fact]
    public async Task CommandBackupRestoresBothStoresArtifactsAndUncertainAssignmentsBehindRecoveryControls() {
        var root = LedgerTestPaths.NewDirectory("coordination-backup");
        Directory.CreateDirectory(root);
        try {
            var source = Path.Combine(root, "source");
            var sourceConfig = Path.Combine(root, "source.json");
            var targetConfig = Path.Combine(root, "target.json");
            var target = Path.Combine(root, "restored");
            var backup = Path.Combine(root, "backup");
            var settings = Settings(source) with { CoordinationConfigPath = Path.Combine(root, "enrollment.json") };
            await File.WriteAllTextAsync(sourceConfig, JsonSerializer.Serialize(settings, LedgerJson.Options));
            // Retained coordination state is backed up even when command enrollment is currently disabled.
            await File.WriteAllTextAsync(targetConfig, JsonSerializer.Serialize(Settings(target), LedgerJson.Options));
            var configBytes = await File.ReadAllBytesAsync(targetConfig);
            LedgerEvent observation = new() { EventId = Guid.NewGuid(), Epoch = Guid.NewGuid(), Sequence = 1,
                ReporterId = "synthetic-reporter", MachineId = "source-host", ItemId = "synthetic-item",
                Provider = "codex", Source = "watcher", ObservedAtUtc = Now, Session = new() { TurnOpen = true } };
            using (var ledger = new LedgerStore(Path.Combine(source, "ledger.sqlite"), new(new()))) {
                await ledger.InitializeAsync();
                await ledger.IngestAsync([observation], Now);
            }
            CoordinationWork active;
            CoordinationArtifact result;
            CoordinationChange[] journal;
            using (var store = new CoordinationStore(Path.Combine(source, "coordination.sqlite"), Policy)) {
                await store.InitializeAsync();
                (active, result) = await store.ExecuteAsync((e, s) => {
                    Initialize(e, s);
                    var finished = e.Submit(Help(), Requester);
                    Start(e, s, finished);
                    var artifact = new CoordinationArtifactStore(Path.Combine(source, "coordination-results"))
                        .Put("help", finished.Generation, finished.GrantId!, "Verified synthetic restore result.");
                    e.Receipt("help", "terminal", Execution(finished, artifact.ResultRef), HelperHost);
                    e.BeginResultDelivery("help", 1, "original-send", SourceHost);
                    e.RecordResultDelivery("help", 1, new("original-send", "uncertain", "response-lost"), SourceHost);
                    var request = new CoordinationRequest("active", "task", "build", "review", "review-host", null,
                        new(BuildSlots: 1), Candidate, [], 1, "authorization", ReadOnly: true);
                    var occupied = e.Submit(request, Requester);
                    e.Submit(request with { Id = "waiting" }, Requester);
                    e.SetGoal(new("stopped", "", 0, false, true, "stop-authorization", ["criterion"]), Requester, 0);
                    return (occupied, artifact);
                }, Now);
                journal = await store.ChangesAsync(0, 100);
            }
            Assert.Equal("admitted", active.State);
            Assert.Equal(0, await Program.Main(["backup", "--config", sourceConfig, "--out", backup]));
            Assert.True(Directory.Exists(backup), "Coordination backup must publish both databases and result files in one directory.");
            Assert.True(File.Exists(Path.Combine(backup, "coordination.sqlite")));
            Assert.Equal(2, Directory.GetFiles(Path.Combine(backup, "coordination-results")).Length);
            Assert.False(File.Exists(Path.Combine(backup, "source.json")));
            Assert.False(File.Exists(settings.CoordinationConfigPath));

            // Subsequent source changes cannot leak into the restore's recovery point.
            using (var ledger = new LedgerStore(Path.Combine(source, "ledger.sqlite"), new(new()))) {
                await ledger.InitializeAsync();
                await ledger.IngestAsync([observation with { EventId = Guid.NewGuid(), Sequence = 2 }], Now.AddSeconds(1));
            }
            Assert.Equal(0, await Program.Main(["restore", "--config", targetConfig, "--from", backup]));
            Assert.Equal(configBytes, await File.ReadAllBytesAsync(targetConfig));
            Assert.Null(LedgerSettings.Load(targetConfig).CoordinationConfigPath);
            using (var ledger = new LedgerStore(Path.Combine(target, "ledger.sqlite"), new(new()))) {
                await ledger.InitializeAsync();
                Assert.Equal([observation.EventId], await ledger.IngestAsync([observation], Now.AddMinutes(1)));
                var item = Assert.Single(await ledger.EvaluateAllAsync(Now.AddMinutes(1)));
                Assert.Single(await ledger.HistoryAsync(item.Id, 0, 100, default));
            }
            using (var restored = new CoordinationStore(Path.Combine(target, "coordination.sqlite"), Policy)) {
                await restored.InitializeAsync();
                await restored.ExecuteAsync((e, s) => {
                    Assert.False(s.Policy.Enabled);
                    Assert.True(s.Goals["task"].Paused);
                    Assert.True(s.Goals["stopped"].Stopped);
                    Assert.Empty(s.Hosts);
                    Assert.All(s.Workers.Values, worker => Assert.False(worker.Eligible));
                    var retained = s.Work["active"];
                    Assert.Equal(JsonSerializer.Serialize(active.Request), JsonSerializer.Serialize(retained.Request));
                    Assert.Equal(active.GrantId, retained.GrantId);
                    Assert.Equal(active.Generation, retained.Generation);
                    Assert.Equal(active.BootId, retained.BootId);
                    Assert.Equal("reconcile", retained.State);
                    e.Schedule();
                    Assert.Equal("queued", s.Work["waiting"].State);
                    Assert.Equal("uncertain", s.ResultOutbox["help"].State);
                    Assert.Equal("original-send", s.ResultOutbox["help"].AttemptId);
                    Assert.Null(s.ResultOutbox["help"].ConsumedAtUtc);
                    Assert.Equal("helper-session", s.Bindings["help"].SessionId);
                    Assert.Null(s.Retirements["help"].CompletedAtUtc);
                    Assert.Throws<CoordinationConflictException>(() => e.BeginResultDelivery("help", 1, "new-send", SourceHost));
                    Assert.Equal(result, new CoordinationArtifactStore(Path.Combine(target, "coordination-results"))
                        .ReadForAssignment("help", 1, s.Work["help"].GrantId!, result.ResultRef));
                    // Even an explicit policy/goal resume cannot silently reuse stale grants or queued authorization.
                    e.ReplacePolicy(s.Policy with { Enabled = true, Revision = s.Policy.Revision + 1 },
                        new("operator", "operator", "", []), s.Policy.Revision);
                    var goal = s.Goals["task"];
                    e.SetGoal(goal with { Paused = false }, new("operator", "operator", "", []), goal.Revision);
                    Assert.Equal("reconcile", s.Work["active"].State);
                    Assert.Equal("queued", s.Work["waiting"].State);
                    return true;
                }, Now.AddSeconds(1));
                Assert.Equal(journal, (await restored.ChangesAsync(0, 100)).Take(journal.Length).ToArray());
            }
            Assert.Empty(Directory.GetDirectories(root, "*.partial"));
        } finally { Directory.Delete(root, recursive: true); }
    }

    [Fact]
    public async Task StoppedBackupExcludesWritersAndRejectsIncompleteOrDamagedResultsBeforeRestorePublication() {
        var root = LedgerTestPaths.NewDirectory("backup-integrity");
        Directory.CreateDirectory(root);
        try {
            var source = Path.Combine(root, "source");
            var backup = Path.Combine(root, "backup");
            var target = Path.Combine(root, "restored");
            using (var ledger = new LedgerStore(Path.Combine(source, "ledger.sqlite"), new(new())))
                await ledger.InitializeAsync();
            using (var store = new CoordinationStore(Path.Combine(source, "coordination.sqlite"), Policy)) {
                await store.InitializeAsync();
                await store.ExecuteAsync((e, s) => {
                    Initialize(e, s);
                    var work = e.Submit(Help(), Requester);
                    Start(e, s, work);
                    var artifact = new CoordinationArtifactStore(Path.Combine(source, "coordination-results"))
                        .Put("help", 1, work.GrantId!, "Durable content.");
                    e.Receipt("help", "terminal", Execution(work, artifact.ResultRef), HelperHost);
                    return true;
                }, Now);
                await Assert.ThrowsAsync<IOException>(() => LedgerBackup.CreateStoppedAsync(source, backup));
                Assert.False(Directory.Exists(backup));
            }
            using (LedgerBackup.AcquireServiceLease(source))
                await Assert.ThrowsAsync<IOException>(() => LedgerBackup.CreateStoppedAsync(source, backup));
            await Assert.ThrowsAsync<InvalidDataException>(() => LedgerBackup.CreateAsync(
                Path.Combine(source, "ledger.sqlite"), Path.Combine(root, "incomplete.sqlite")));
            await LedgerBackup.CreateStoppedAsync(source, backup);
            var content = Assert.Single(Directory.GetFiles(Path.Combine(backup, "coordination-results"), "result-*.txt"));
            await File.AppendAllTextAsync(content, "corruption");
            await Assert.ThrowsAsync<InvalidDataException>(() => LedgerBackup.RestoreAsync(backup, target));
            Assert.False(Directory.Exists(target));
            var assignment = Assert.Single(Directory.GetFiles(Path.Combine(source, "coordination-results"), "assignment-*.txt"));
            File.Delete(assignment);
            await Assert.ThrowsAsync<InvalidDataException>(() => LedgerBackup.CreateStoppedAsync(source, Path.Combine(root, "missing-binding")));
            Assert.False(Directory.Exists(Path.Combine(root, "missing-binding")));
            Assert.Empty(Directory.GetDirectories(root, "*.partial"));
        } finally { Directory.Delete(root, recursive: true); }
    }

    [Fact]
    public async Task ObservationOnlyCommandKeepsOnlineFormatAndRestoresWithEnrollmentDisabled() {
        var root = LedgerTestPaths.NewDirectory("observation-backup");
        Directory.CreateDirectory(root);
        try {
            var source = Path.Combine(root, "source");
            var settingsFile = Path.Combine(root, "settings.json");
            var backup = Path.Combine(root, "backup.sqlite");
            await File.WriteAllTextAsync(settingsFile, JsonSerializer.Serialize(Settings(source), LedgerJson.Options));
            using (var writer = new LedgerStore(Path.Combine(source, "ledger.sqlite"), new(new()))) {
                await writer.InitializeAsync();
                Assert.Equal(0, await Program.Main(["backup", "--config", settingsFile, "--out", backup]));
            }
            Assert.True(File.Exists(backup));
            using (var manifest = JsonDocument.Parse(await File.ReadAllTextAsync(backup + ".json")))
                Assert.False(manifest.RootElement.TryGetProperty("coordination", out _));
            var target = Path.Combine(root, "restored");
            await File.WriteAllTextAsync(settingsFile, JsonSerializer.Serialize(Settings(target), LedgerJson.Options));
            Assert.Equal(0, await Program.Main(["restore", "--config", settingsFile, "--from", backup]));
            Assert.False(File.Exists(Path.Combine(target, "coordination.sqlite")));
            using var restored = new LedgerStore(Path.Combine(target, "ledger.sqlite"), new(new()));
            await restored.InitializeAsync();
            Assert.Empty(await restored.EvaluateAllAsync(Now));
        } finally { Directory.Delete(root, recursive: true); }
    }

    private static LedgerSettings Settings(string directory) => new() {
        DataDirectory = directory, ReadToken = new('r', 32), WriteToken = new('w', 32),
        ExpectedReporters = new() { ["source-host"] = ["synthetic-reporter"] }
    };
}
