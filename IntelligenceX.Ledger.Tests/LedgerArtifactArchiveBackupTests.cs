using System.Diagnostics;
using System.Text.Json;
using IntelligenceX.Ledger.Contracts;
using IntelligenceX.Ledger.Coordination;
using IntelligenceX.Ledger.Hosting;
using IntelligenceX.Ledger.Storage;
using Xunit;
using static IntelligenceX.Ledger.Tests.CoordinationLifecycleTests;

namespace IntelligenceX.Ledger.Tests;

public sealed class LedgerArtifactArchiveBackupTests {
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task StoppedBackupRestoresArchiveAndInterruptedMoveWithOriginalReplayAndRecoveryControls(bool interrupted) {
        var root = LedgerTestPaths.NewDirectory("archived-backup");
        Directory.CreateDirectory(root);
        try {
            var source = Path.Combine(root, "source");
            var completed = await CreateCompletedStore(source);
            var resultRoot = Path.Combine(source, "coordination-results");
            var archive = Path.Combine(resultRoot, "archive");
            Directory.CreateDirectory(archive);
            File.Move(Path.Combine(resultRoot, completed.Artifact.ResultRef + ".txt"), Path.Combine(archive, completed.Artifact.ResultRef + ".txt"));
            if (!interrupted) File.Move(completed.Assignment, Path.Combine(archive, Path.GetFileName(completed.Assignment)));
            var backup = Path.Combine(root, "backup");
            var manifest = await LedgerBackup.CreateStoppedAsync(source, backup);
            Assert.Equal(3, manifest.SchemaVersion);
            Assert.Contains("archive/" + completed.Artifact.ResultRef + ".txt", manifest.Coordination!.Artifacts.Keys);
            Assert.Equal(interrupted ? 1 : 2, manifest.Coordination.Artifacts.Keys.Count(key => key.StartsWith("archive/", StringComparison.Ordinal)));
            var restored = Path.Combine(root, "restored");
            await LedgerBackup.RestoreAsync(backup, restored);
            var artifacts = new CoordinationArtifactStore(Path.Combine(restored, "coordination-results"));
            Assert.Equal(completed.Artifact, artifacts.ReadForAssignment("help", completed.Work.Generation,
                completed.Work.GrantId!, completed.Artifact.ResultRef));
            Assert.Equal(completed.Artifact, artifacts.Put("help", completed.Work.Generation,
                completed.Work.GrantId!, completed.Artifact.Content));
            using (var store = new CoordinationStore(Path.Combine(restored, "coordination.sqlite"), Policy)) {
                await store.InitializeAsync();
                await store.ExecuteAsync((_, state) => {
                    Assert.False(state.Policy.Enabled);
                    Assert.True(state.Goals["task"].Paused);
                    Assert.NotNull(state.ResultOutbox["help"].ConsumedAtUtc);
                    Assert.NotNull(state.Retirements["help"].CompletedAtUtc);
                    Assert.Equal(completed.Work.GrantId, state.Work["help"].GrantId);
                    Assert.Empty(state.Hosts);
                    return true;
                }, Now);
            }
            // A valid manifest does not hide corruption inside the fixed archive tier.
            await File.AppendAllTextAsync(Path.Combine(backup, "coordination-results", "archive", completed.Artifact.ResultRef + ".txt"), "corruption");
            await Assert.ThrowsAsync<InvalidDataException>(() => LedgerBackup.RestoreAsync(backup, Path.Combine(root, "corrupt-restore")));
            Assert.False(Directory.Exists(Path.Combine(root, "corrupt-restore")));
        } finally { Directory.Delete(root, recursive: true); }
    }

    [Fact]
    public async Task FlatSchema2CoordinationBackupStillRestores() {
        var root = LedgerTestPaths.NewDirectory("legacy-coordination-backup");
        Directory.CreateDirectory(root);
        try {
            var source = Path.Combine(root, "source");
            var completed = await CreateCompletedStore(source);
            var backup = Path.Combine(root, "backup");
            var manifest = await LedgerBackup.CreateStoppedAsync(source, backup);
            await File.WriteAllTextAsync(Path.Combine(backup, "ledger.sqlite.json"),
                JsonSerializer.Serialize(manifest with { SchemaVersion = 2 }, LedgerJson.Options));
            var restored = Path.Combine(root, "restored");
            Assert.Equal(2, (await LedgerBackup.RestoreAsync(backup, restored)).SchemaVersion);
            Assert.Equal(completed.Artifact, new CoordinationArtifactStore(Path.Combine(restored, "coordination-results"))
                .Put("help", completed.Work.Generation, completed.Work.GrantId!, completed.Artifact.Content));
        } finally { Directory.Delete(root, recursive: true); }
    }

    [Theory]
    [InlineData("extra-directory")]
    [InlineData("nested-directory")]
    [InlineData("archive-link")]
    [InlineData("nested-link")]
    [InlineData("manifest-traversal")]
    public async Task BackupAndRestoreRejectUnexpectedDirectoriesLinksAndManifestPaths(string invalid) {
        var root = LedgerTestPaths.NewDirectory("archive-backup-rejection");
        var links = new List<string>();
        Directory.CreateDirectory(root);
        try {
            var source = Path.Combine(root, "source");
            var completed = await CreateCompletedStore(source);
            var backup = Path.Combine(root, "valid-backup");
            var manifest = await LedgerBackup.CreateStoppedAsync(source, backup);
            var resultRoot = Path.Combine(source, "coordination-results");
            var backupResults = Path.Combine(backup, "coordination-results");
            if (invalid == "manifest-traversal") {
                var entry = manifest.Coordination!.Artifacts.First();
                manifest.Coordination.Artifacts.Remove(entry.Key);
                manifest.Coordination.Artifacts.Add("archive/../" + entry.Key, entry.Value);
                await File.WriteAllTextAsync(Path.Combine(backup, "ledger.sqlite.json"), JsonSerializer.Serialize(manifest, LedgerJson.Options));
            } else {
                foreach (var directory in new[] { resultRoot, backupResults }) {
                    var archive = Path.Combine(directory, "archive");
                    if (invalid == "archive-link") {
                        await LinkDirectory(archive, Path.Combine(root, "link-target"));
                        links.Add(archive);
                    } else {
                        var extra = invalid == "extra-directory" ? Path.Combine(directory, "unexpected") : Path.Combine(archive, "unexpected");
                        Directory.CreateDirectory(archive);
                        if (invalid == "nested-link") {
                            await LinkDirectory(extra, Path.Combine(root, "link-target"));
                            links.Add(extra);
                        } else Directory.CreateDirectory(extra);
                    }
                }
                await Assert.ThrowsAsync<InvalidDataException>(() => LedgerBackup.CreateStoppedAsync(source, Path.Combine(root, "rejected-backup")));
                Assert.False(Directory.Exists(Path.Combine(root, "rejected-backup")));
                if (invalid == "archive-link") {
                    var content = Directory.GetFiles(resultRoot, "result-*.txt").Single();
                    File.Move(content, Path.Combine(root, "held-synthetic-result.txt"));
                    Assert.Equal("result-storage-link", Assert.Throws<CoordinationConflictException>(() =>
                        new CoordinationArtifactStore(resultRoot).ReadForAssignment("help", completed.Work.Generation,
                            completed.Work.GrantId!, completed.Artifact.ResultRef)).Message);
                }
            }
            await Assert.ThrowsAsync<InvalidDataException>(() => LedgerBackup.RestoreAsync(backup, Path.Combine(root, "rejected-restore")));
            Assert.False(Directory.Exists(Path.Combine(root, "rejected-restore")));
            Assert.Empty(Directory.GetDirectories(root, "*.partial"));
        } finally {
            foreach (var link in links) Directory.Delete(link);
            Directory.Delete(root, recursive: true);
        }
    }

    private static async Task<(CoordinationWork Work, CoordinationArtifact Artifact, string Assignment)> CreateCompletedStore(string source) {
        using (var ledger = new LedgerStore(Path.Combine(source, "ledger.sqlite"), new(new())))
            await ledger.InitializeAsync();
        using var store = new CoordinationStore(Path.Combine(source, "coordination.sqlite"), Policy);
        await store.InitializeAsync();
        return await store.ExecuteAsync((engine, state) => {
            Initialize(engine, state);
            return CoordinationArtifactArchiveTests.Complete(Path.Combine(source, "coordination-results"), state);
        }, Now);
    }

    private static async Task LinkDirectory(string link, string target) {
        Directory.CreateDirectory(target);
        if (!OperatingSystem.IsWindows()) {
            Directory.CreateSymbolicLink(link, target);
            return;
        }
        // Windows directory junctions exercise the same reparse-point guard without a symlink privilege.
        var start = new ProcessStartInfo("cmd.exe") { UseShellExecute = false, CreateNoWindow = true,
            RedirectStandardOutput = true, RedirectStandardError = true };
        foreach (var argument in new[] { "/c", "mklink", "/J", link, target }) start.ArgumentList.Add(argument);
        using var process = Process.Start(start)!;
        var stdout = process.StandardOutput.ReadToEndAsync();
        var stderr = process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();
        await Task.WhenAll(stdout, stderr);
        Assert.True(process.ExitCode == 0, await stderr);
    }
}
