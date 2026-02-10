using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using IntelligenceX.Cli.Ci;
using Xunit;

namespace IntelligenceX.UnitTests;

public sealed class CiTuneReviewerBudgetsCommandTests {
    private static readonly SemaphoreSlim EnvGate = new(1, 1);

    [Fact]
    public async Task Rejects_OutEnv_OutsideWorkspace_When_GitHubEnv_Unset() {
        await EnvGate.WaitAsync();
        var workspace = CreateTempDir("ix-ws-");
        var outside = CreateTempDir("ix-outside-");
        try {
            File.WriteAllText(Path.Combine(workspace, "changed-files.txt"), "a.txt\nb.txt\n");

            var outEnv = Path.Combine(outside, "env.out");

            using var env = new EnvScope(
                ("GITHUB_WORKSPACE", workspace),
                ("GITHUB_ACTIONS", "false"),
                ("GITHUB_ENV", null));

            var rc = await CiTuneReviewerBudgetsCommand.RunAsync(new[] {
                "--changed-files", "changed-files.txt",
                "--changed-threshold", "1",
                "--out-env", outEnv
            });

            Assert.Equal(1, rc);
        } finally {
            TryDeleteDir(workspace);
            TryDeleteDir(outside);
            EnvGate.Release();
        }
    }

    [Fact]
    public async Task Allows_OutEnv_WithinWorkspace_When_GitHubEnv_Unset() {
        await EnvGate.WaitAsync();
        var workspace = CreateTempDir("ix-ws-");
        try {
            File.WriteAllText(Path.Combine(workspace, "changed-files.txt"), "a.txt\nb.txt\n");

            using var env = new EnvScope(
                ("GITHUB_WORKSPACE", workspace),
                ("GITHUB_ACTIONS", "false"),
                ("GITHUB_ENV", null));

            var rc = await CiTuneReviewerBudgetsCommand.RunAsync(new[] {
                "--changed-files", "changed-files.txt",
                "--changed-threshold", "1",
                "--out-env", "env.out",
                "--max-files", "7",
                "--max-patch-chars", "1234"
            });

            Assert.Equal(0, rc);

            var content = File.ReadAllText(Path.Combine(workspace, "env.out"));
            Assert.Contains("INPUT_MAX_FILES<<", content);
            Assert.Contains("7", content);
        } finally {
            TryDeleteDir(workspace);
            EnvGate.Release();
        }
    }

    [Fact]
    public async Task Allows_OutEnv_EqualTo_GitHubEnv_EvenOutsideWorkspace() {
        await EnvGate.WaitAsync();
        var workspace = CreateTempDir("ix-ws-");
        var outside = CreateTempDir("ix-outside-");
        try {
            File.WriteAllText(Path.Combine(workspace, "changed-files.txt"), "a.txt\nb.txt\n");

            var envPath = Path.Combine(outside, "github.env");
            File.WriteAllText(envPath, string.Empty);

            using var env = new EnvScope(
                ("GITHUB_WORKSPACE", workspace),
                ("GITHUB_ACTIONS", "true"),
                ("GITHUB_ENV", envPath));

            var rc = await CiTuneReviewerBudgetsCommand.RunAsync(new[] {
                "--changed-files", "changed-files.txt",
                "--changed-threshold", "1",
                "--out-env", envPath,
                "--max-files", "11",
                "--max-patch-chars", "2222"
            });

            Assert.Equal(0, rc);
            Assert.True(File.Exists(envPath));
            Assert.Contains("INPUT_MAX_FILES<<", File.ReadAllText(envPath));
        } finally {
            TryDeleteDir(workspace);
            TryDeleteDir(outside);
            EnvGate.Release();
        }
    }

    private static string CreateTempDir(string prefix) {
        var path = Path.Combine(Path.GetTempPath(), prefix + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }

    private static void TryDeleteDir(string path) {
        try {
            if (Directory.Exists(path)) {
                Directory.Delete(path, recursive: true);
            }
        } catch {
            // Best-effort cleanup.
        }
    }

    private sealed class EnvScope : IDisposable {
        private readonly (string Key, string? Value)[] _original;

        public EnvScope(params (string Key, string? Value)[] values) {
            _original = new (string, string?)[values.Length];
            for (var i = 0; i < values.Length; i++) {
                var (key, value) = values[i];
                _original[i] = (key, Environment.GetEnvironmentVariable(key));
                Environment.SetEnvironmentVariable(key, value);
            }
        }

        public void Dispose() {
            for (var i = 0; i < _original.Length; i++) {
                var (key, value) = _original[i];
                Environment.SetEnvironmentVariable(key, value);
            }
        }
    }
}
