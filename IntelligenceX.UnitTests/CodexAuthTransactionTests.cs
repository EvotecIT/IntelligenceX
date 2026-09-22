using IntelligenceX.OpenAI.Auth;
using Xunit;

namespace IntelligenceX.UnitTests;

public sealed class CodexAuthTransactionTests : IDisposable {
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "ix-codex-transaction-" + Guid.NewGuid().ToString("N"));
    private static AuthBundle Bundle(string account, string token) => new("openai-codex", token, token, null) {
        AccountId = account, IdToken = "fixture-id"
    };

    [Fact]
    public async Task LoginAndMatchingRefreshBothWaitForTheSharedTransaction() {
        CodexAuthStore.WriteAuthJson(Bundle("old", "original"), _directory);
        var path = CodexAuthStore.ResolveAuthPath(_directory);
        foreach (var login in new[] { false, true }) {
            Task writer;
            using (var transaction = await AuthFileTransaction.AcquireAsync(path + ".lock", CancellationToken.None)) {
                var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                writer = Task.Run(() => {
                    started.SetResult();
                    if (login) CodexAuthStore.WriteAuthJson(Bundle("new", "login"), _directory);
                    else CodexAuthStore.UpdateMatchingAuthJson(Bundle("old", "rotated"), "old", "original", _directory);
                });
                await started.Task.WaitAsync(TimeSpan.FromSeconds(5));
                // Completion would mean the writer ignored the actual cross-process lock.
                await Assert.ThrowsAsync<TimeoutException>(() => writer.WaitAsync(TimeSpan.FromMilliseconds(200)));
            }
            await writer.WaitAsync(TimeSpan.FromSeconds(5));
        }
        CodexAuthStore.UpdateMatchingAuthJson(Bundle("old", "stale"), "old", "rotated", _directory);
        Assert.Equal("new", CodexAuthStore.TryReadProfile(path)!.AccountId);
        Assert.Contains("login", await File.ReadAllTextAsync(path));
        Assert.DoesNotContain("stale", await File.ReadAllTextAsync(path));
    }

    [Fact]
    public void ReplacingCodexCredentialsPreservesExistingFileAndPermissions() {
        var path = CodexAuthStore.ResolveAuthPath(_directory);
        CodexAuthStore.WriteAuthJson(Bundle("same", "original"), _directory);
        var mode = UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.GroupRead;
        if (!OperatingSystem.IsWindows()) File.SetUnixFileMode(path, mode);
        CodexAuthStore.UpdateMatchingAuthJson(Bundle("same", "rotated"), "same", "original", _directory);
        Assert.Equal("rotated", CodexAuthStore.TryReadBundle(path)!.RefreshToken);
        if (!OperatingSystem.IsWindows()) Assert.Equal(mode, File.GetUnixFileMode(path));
        CodexAuthStore.WriteAuthJson(Bundle("new", "login"), _directory);
        Assert.Equal("new", CodexAuthStore.TryReadProfile(path)!.AccountId);
        if (!OperatingSystem.IsWindows()) Assert.Equal(mode, File.GetUnixFileMode(path));
        Assert.Empty(Directory.GetFiles(_directory, "*.tmp"));
    }

    public void Dispose() {
        if (Directory.Exists(_directory)) Directory.Delete(_directory, true);
    }
}
