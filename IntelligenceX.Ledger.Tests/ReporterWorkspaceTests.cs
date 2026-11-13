using System.Diagnostics;
using IntelligenceX.Ledger.Reporting;
using IntelligenceX.Telemetry.Agents;
using Xunit;

namespace IntelligenceX.Ledger.Tests;

public sealed class ReporterWorkspaceTests {
    [Fact]
    public async Task WorkspaceObservationIncludesCommittedStagedAndUntrackedNamesAgainstTheBase() {
        var root = LedgerTestPaths.NewDirectory("workspace");
        Directory.CreateDirectory(root);
        try {
            await Git(root, "init", "--initial-branch=main");
            await File.WriteAllTextAsync(Path.Combine(root, "base.txt"), "base");
            await Git(root, "add", ".");
            await Git(root, "commit", "-m", "Synthetic baseline");
            await Git(root, "checkout", "-b", "feature/synthetic");
            await File.WriteAllTextAsync(Path.Combine(root, "committed.txt"), "change");
            await Git(root, "add", ".");
            await Git(root, "commit", "-m", "Synthetic change");
            await File.WriteAllTextAsync(Path.Combine(root, "staged.txt"), "change");
            await Git(root, "add", ".");
            await File.WriteAllTextAsync(Path.Combine(root, "untracked.txt"), "change");
            await File.WriteAllTextAsync(Path.Combine(root, "base.txt"), "modified");
            var session = new AgentSessionState { Id = "synthetic-session", Provider = "codex", Repository = "example/project",
                Branch = "feature/synthetic", WorkingDirectory = root, Title = "Synthetic task", TurnOpen = true, LastActivityAtUtc = DateTimeOffset.UtcNow };
            var cache = new ReporterWorkspaceCache();
            await cache.RefreshAsync([session], DateTimeOffset.UtcNow, default);
            var fact = cache.For(session)!;
            Assert.Equal("observed", fact.GitState);
            Assert.Equal("feature/synthetic", fact.Branch);
            Assert.Equal("main", fact.BaseRef);
            Assert.NotNull(fact.Files);
            Assert.Equal(["base.txt", "committed.txt", "staged.txt", "untracked.txt"], fact.Files!);
            Assert.False(fact.FilesCapped);
            Assert.False(fact.PullRequestsKnown);
            Assert.True(cache.For(session, pullRequestsKnown: true)!.PullRequestsKnown);
            var missing = session with { WorkingDirectory = Path.Combine(root, "absent") };
            await cache.RefreshAsync([missing], DateTimeOffset.UtcNow, default);
            Assert.Null(cache.For(missing)!.Files);
            Assert.Equal("workspace-missing", cache.For(missing)!.GitState);
        } finally {
            // Git marks its own object files read-only on Windows.
            foreach (var file in Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories))
                File.SetAttributes(file, File.GetAttributes(file) & ~FileAttributes.ReadOnly);
            Directory.Delete(root, true);
        }
    }

    private static async Task Git(string root, params string[] args) {
        using var process = new Process { StartInfo = new("git") { UseShellExecute = false, CreateNoWindow = true,
            RedirectStandardOutput = true, RedirectStandardError = true } };
        foreach (var arg in new[] { "-C", root, "-c", "commit.gpgsign=false", "-c", "user.name=Synthetic", "-c", "user.email=synthetic@example.test" }.Concat(args)) process.StartInfo.ArgumentList.Add(arg);
        process.Start();
        var output = process.StandardOutput.ReadToEndAsync();
        var error = process.StandardError.ReadToEndAsync();
        try {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
            await process.WaitForExitAsync(timeout.Token);
            Assert.True(process.ExitCode == 0, await error);
            await output;
        } finally {
            if (!process.HasExited) { process.Kill(true); await process.WaitForExitAsync(); }
        }
    }
}
