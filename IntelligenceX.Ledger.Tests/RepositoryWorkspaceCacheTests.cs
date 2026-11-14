using System.Diagnostics;
using System.Text.Json;
using IntelligenceX.Ledger.Contracts;
using IntelligenceX.Ledger.Reporting;
using IntelligenceX.Telemetry.Agents;
using Xunit;

namespace IntelligenceX.Ledger.Tests;

public sealed class RepositoryWorkspaceCacheTests {
    [Fact]
    public async Task WorkspaceFactsStayOrderedAndRetainLastKnownPullRequestsDuringCoverageGap() {
        var directory = LedgerTestPaths.NewDirectory("repository-workspace");
        Directory.CreateDirectory(directory);
        try {
            await GitAsync(directory, "init", "-b", "main");
            await GitAsync(directory, "-c", "user.name=Synthetic", "-c", "user.email=synthetic@example.test",
                "-c", "commit.gpgsign=false", "commit", "--allow-empty", "-m", "Synthetic fixture");
            foreach (var name in new[] { "z.cs", "a.cs", "m.cs" }) await File.WriteAllTextAsync(Path.Combine(directory, name), "synthetic");
            var cache = new ReporterWorkspaceCache();
            var session = new AgentSessionState { Id = "synthetic-session", Provider = "codex", Repository = "synthetic/repository",
                WorkingDirectory = directory, TurnOpen = true, PullRequests = [
                    new() { Url = "https://example.test/pull/2" }, new() { Url = "https://example.test/pull/1" }, new() { Url = "https://example.test/pull/2" }
                ] };
            await cache.RefreshAsync([session], DateTimeOffset.UtcNow, CancellationToken.None);
            var known = cache.For(session, pullRequestsKnown: true)!;
            Assert.Equal(["a.cs", "m.cs", "z.cs"], known.Files!);
            Assert.Equal(3, known.FileCount);
            Assert.Equal(["https://example.test/pull/1", "https://example.test/pull/2"], known.PullRequests);
            Assert.True(known.PullRequestsKnown);
            var reordered = cache.For(session with { PullRequests = session.PullRequests.Reverse().ToArray() }, pullRequestsKnown: true);
            var unavailable = cache.For(session with { PullRequests = [] }, pullRequestsKnown: false);
            Assert.Equal(JsonSerializer.Serialize(known, LedgerJson.Options), JsonSerializer.Serialize(reordered, LedgerJson.Options));
            Assert.Equal(JsonSerializer.Serialize(known, LedgerJson.Options), JsonSerializer.Serialize(unavailable, LedgerJson.Options));
            var confirmedEmpty = cache.For(session with { PullRequests = [] }, pullRequestsKnown: true)!;
            Assert.Empty(confirmedEmpty.PullRequests);
            Assert.True(confirmedEmpty.PullRequestsKnown);
        } finally {
            foreach (var file in Directory.EnumerateFiles(directory, "*", SearchOption.AllDirectories)) File.SetAttributes(file, FileAttributes.Normal);
            Directory.Delete(directory, recursive: true);
        }
    }

    private static async Task GitAsync(string directory, params string[] arguments) {
        using var process = new Process { StartInfo = new("git") { WorkingDirectory = directory,
            UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true } };
        foreach (var argument in arguments) process.StartInfo.ArgumentList.Add(argument);
        process.Start();
        var output = process.StandardOutput.ReadToEndAsync();
        var error = process.StandardError.ReadToEndAsync();
        try {
            await process.WaitForExitAsync();
            await output;
            Assert.True(process.ExitCode == 0, await error);
        } finally {
            if (!process.HasExited) { process.Kill(entireProcessTree: true); await process.WaitForExitAsync(); }
        }
    }
}
