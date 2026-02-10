using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using IntelligenceX.Cli.Ci;
using Xunit;

namespace IntelligenceX.UnitTests;

public sealed class GitCliTests {
    private static readonly SemaphoreSlim EnvGate = new(1, 1);

    [Fact]
    public async Task RunBytesAsync_Returns126_When_OutputExceedsMaxBytes() {
        await EnvGate.WaitAsync();
        var root = CreateTempDir("ix-gitcli-");
        try {
            Directory.SetCurrentDirectory(root);
            await InitRepoAsync(root);

            // Create a file larger than the small maxBytes we'll set below.
            var bigPath = Path.Combine(root, "big.txt");
            File.WriteAllText(bigPath, new string('x', 4096));
            await RunGitAsync(root, "add", "big.txt");
            await RunGitAsync(root, "-c", "user.email=test@example.com", "-c", "user.name=Test", "commit", "-m", "big");

            using var env = new EnvScope(
                ("INTELLIGENCEX_GIT_MAX_OUTPUT_BYTES", "1024"),
                ("INTELLIGENCEX_GIT_TIMEOUT_SECONDS", "30"));

            var (exit, stdout, stderr) = await GitCli.RunBytesAsync(root, "show", "HEAD:big.txt");

            Assert.Equal(126, exit);
            Assert.Empty(stdout);
            Assert.NotEmpty(stderr);
        } finally {
            TryDeleteDir(root);
            EnvGate.Release();
        }
    }

    private static async Task InitRepoAsync(string root) {
        // Keep this minimal: only commands we need for the test.
        var (exit, _, _) = await GitCli.RunBytesAsync(root, "init");
        Assert.Equal(0, exit);
    }

    private static async Task RunGitAsync(string root, params string[] args) {
        var (exit, _, stderr) = await GitCli.RunBytesAsync(root, args);
        Assert.True(exit == 0, $"git {string.Join(" ", args)} failed: {System.Text.Encoding.UTF8.GetString(stderr)}");
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

