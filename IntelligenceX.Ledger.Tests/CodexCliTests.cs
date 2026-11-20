using IntelligenceX.Ledger.Agents;
using Xunit;

namespace IntelligenceX.Ledger.Tests;

public sealed class CodexCliTests {
    [Fact]
    public void TheNewestInstalledDesktopAppPackageProvidesTheCli() {
        var packages = new[] {
            (CodexCli.PackageVersion("OpenAI.Codex_26.1111.2228.0_x64__2p2nqsd0c76g0")!, @"C:\Apps\OpenAI.Codex_26.1111.2228.0_x64__2p2nqsd0c76g0"),
            (CodexCli.PackageVersion("OpenAI.Codex_26.1201.100.0_x64__2p2nqsd0c76g0")!, @"C:\Apps\OpenAI.Codex_26.1201.100.0_x64__2p2nqsd0c76g0"),
            (CodexCli.PackageVersion("OpenAI.Codex_26.1301.1.0_x64__2p2nqsd0c76g0")!, @"C:\Apps\OpenAI.Codex_26.1301.1.0_x64__2p2nqsd0c76g0")
        };
        // The newest package folder is registered but its files are gone (an update in progress), so the next one is used.
        var present = new HashSet<string>(StringComparer.OrdinalIgnoreCase) {
            Path.Combine(@"C:\Apps\OpenAI.Codex_26.1111.2228.0_x64__2p2nqsd0c76g0", "app", "resources", "codex.exe"),
            Path.Combine(@"C:\Apps\OpenAI.Codex_26.1201.100.0_x64__2p2nqsd0c76g0", "app", "resources", "codex.exe")
        };
        Assert.Equal(Path.Combine(@"C:\Apps\OpenAI.Codex_26.1201.100.0_x64__2p2nqsd0c76g0", "app", "resources", "codex.exe"),
            CodexCli.WindowsCli(packages, present.Contains));
        Assert.Null(CodexCli.WindowsCli(packages, _ => false));
    }

    [Theory]
    [InlineData("OpenAI.CodexPrimaryRuntime.v26-1117-1425-0_26.1117.1425.0_x64__3k8sg7r9htsxt")]
    [InlineData("OpenAI.Codex_notaversion_x64__2p2nqsd0c76g0")]
    [InlineData("OpenAI.Codex")]
    public void OnlyTheCodexAppPackageCounts(string fullName) => Assert.Null(CodexCli.PackageVersion(fullName));

    [Fact]
    public void AControlSocketThatDidNotAnswerIsSkippedUntilItChanges() {
        var directory = Path.Combine(Path.GetTempPath(), "ix-ledger-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try {
            var socket = Path.Combine(directory, "app-server-control.sock");
            Assert.False(CodexAppServer.TryLive(socket, out _));
            File.WriteAllText(socket, "");
            File.SetLastWriteTimeUtc(socket, new DateTime(2026, 11, 18, 21, 1, 3, DateTimeKind.Utc));
            Assert.True(CodexAppServer.TryLive(socket, out var stamp));
            CodexAppServer.MarkDead(socket, stamp);
            Assert.False(CodexAppServer.TryLive(socket, out _));
            File.SetLastWriteTimeUtc(socket, new DateTime(2026, 11, 20, 9, 0, 0, DateTimeKind.Utc));
            Assert.True(CodexAppServer.TryLive(socket, out _));
        } finally { Directory.Delete(directory, recursive: true); }
    }
}
