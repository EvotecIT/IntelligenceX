using IntelligenceX.OpenAI.Auth;
using IntelligenceX.OpenAI.Native;
using Xunit;

namespace IntelligenceX.UnitTests;

public sealed class AuthStoreTransactionTests : IDisposable {
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "ix-auth-transactions-" + Guid.NewGuid().ToString("N"));
    private string StorePath => Path.Combine(_directory, "auth.json");
    private FileAuthBundleStore Store() => new(StorePath);
    private static AuthBundle Bundle(string account, string token = "original") =>
        new("openai-codex", token, token, DateTimeOffset.UtcNow.AddHours(1)) { AccountId = account };

    [Fact]
    public async Task ConcurrentStoresPreserveEveryAccount() {
        await Task.WhenAll(Enumerable.Range(0, 32).Select(i => Store().SaveAsync(Bundle("account-" + i))));
        var accounts = await Store().ListAsync("openai-codex");
        Assert.Equal(32, accounts.Count);
        Assert.Equal(32, accounts.Select(a => a.AccountId).Distinct().Count());
    }

    [Fact]
    public async Task RefreshSerializesOneAccountWithoutBlockingTheStore() {
        var original = Bundle("account");
        await Store().SaveAsync(original);
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var refresh = Store().RefreshAsync(original, async (_, _) => {
            entered.SetResult();
            await release.Task;
            return Bundle("account", "rotated");
        }, CancellationToken.None);
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
        try {
            await Store().SaveAsync(Bundle("other")).WaitAsync(TimeSpan.FromSeconds(5));
            Assert.Equal(2, (await Store().ListAsync("openai-codex")).Count);
            await Store().RefreshAsync(Bundle("other"), (_, _) => Task.FromResult(Bundle("other", "other-rotated")),
                CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(5));
            using var canceled = new CancellationTokenSource();
            var waitingRefresh = Store().RefreshAsync(original, (_, _) => throw new InvalidOperationException("Concurrent refresh"), canceled.Token);
            canceled.Cancel();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => waitingRefresh);
        } finally {
            release.TrySetResult();
        }
        Assert.Equal("rotated", (await refresh).RefreshToken);
        var reused = await Store().RefreshAsync(original, (_, _) => throw new InvalidOperationException("Old token replayed"), CancellationToken.None);
        Assert.Equal("rotated", reused.RefreshToken);
        await Store().SaveAsync(Bundle("other"));
        Assert.Equal(2, (await Store().ListAsync("openai-codex")).Count);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task LoginOrDeletionDuringRefreshWinsAtCommit(bool delete) {
        var original = Bundle("account");
        await Store().SaveAsync(original);
        var pending = Store().RefreshAsync(original, async (current, _) => {
            // Match the real OAuth path, which mutates its input bundle.
            current.AccessToken = "rotated";
            current.RefreshToken = "rotated";
            if (delete) Store().Delete();
            else await Store().SaveAsync(Bundle("account", "new-login"));
            return current;
        }, CancellationToken.None);
        if (delete) {
            await Assert.ThrowsAsync<InvalidOperationException>(() => pending);
            Assert.False(File.Exists(StorePath));
        } else {
            Assert.Equal("new-login", (await pending).RefreshToken);
            Assert.Equal("new-login", (await Store().GetAsync("openai-codex", "account"))!.RefreshToken);
        }
    }

    [Fact]
    public async Task NewLoginWinsOverAStaleRefreshSnapshot() {
        var old = Bundle("account");
        await Store().SaveAsync(old);
        await Store().SaveAsync(Bundle("account", "new-login"));
        var result = await Store().RefreshAsync(old, (_, _) => throw new InvalidOperationException("Old token replayed"), CancellationToken.None);
        Assert.Equal("new-login", result.AccessToken);
    }

    [Fact]
    public async Task DeletedAccountIsNotRestoredByRefresh() {
        var old = Bundle("account");
        await Store().SaveAsync(old);
        Store().Delete();
        await Assert.ThrowsAsync<InvalidOperationException>(() => Store().RefreshAsync(old,
            (_, _) => Task.FromResult(Bundle("account", "rotated")), CancellationToken.None));
        Assert.False(File.Exists(StorePath));
    }

    [Fact]
    public async Task ReturnedRotationIsPersistedEvenIfCallerCancels() {
        var original = Bundle("account");
        await Store().SaveAsync(original);
        using var cancellation = new CancellationTokenSource();
        var rotated = await Store().RefreshAsync(original, (_, _) => {
            cancellation.Cancel();
            return Task.FromResult(Bundle("account", "rotated-before-cancellation"));
        }, cancellation.Token);
        Assert.Equal("rotated-before-cancellation", rotated.RefreshToken);
        Assert.Equal(rotated.RefreshToken, (await Store().GetAsync("openai-codex", "account"))!.RefreshToken);
    }

    [Fact]
    public async Task SavePreservesExistingUnixCredentialPermissions() {
        if (OperatingSystem.IsWindows()) return;
        await Store().SaveAsync(Bundle("account"));
        File.SetUnixFileMode(StorePath, UnixFileMode.UserRead | UnixFileMode.UserWrite);
        await Store().SaveAsync(Bundle("other"));
        Assert.Equal(UnixFileMode.UserRead | UnixFileMode.UserWrite, File.GetUnixFileMode(StorePath));
    }

    [Fact]
    public async Task RefreshCanIdentifyALegacyAccountWithoutAnId() {
        var legacy = Bundle("account");
        legacy.AccountId = null;
        await Store().SaveAsync(legacy);
        var updated = await Store().RefreshAsync(legacy,
            (_, _) => Task.FromResult(Bundle("account", "identified")), CancellationToken.None);
        Assert.Equal("account", updated.AccountId);
        Assert.Single(await Store().ListAsync("openai-codex"));
        Assert.Equal("identified", (await Store().GetAsync("openai-codex", "account"))!.RefreshToken);
    }

    [Theory]
    [InlineData("openai")]
    [InlineData("chatgpt")]
    public async Task RefreshMigratesLegacyAliasToCanonicalProvider(string provider) {
        var legacy = new AuthBundle(provider, "old", "old-refresh", DateTimeOffset.UtcNow.AddMinutes(-1)) {
            AccountId = "account"
        };
        await Store().SaveAsync(legacy);

        var updated = await Store().RefreshAsync(legacy, (_, _) => Task.FromResult(
            new AuthBundle(provider, "new", "new-refresh", DateTimeOffset.UtcNow.AddHours(1)) {
                AccountId = "account"
            }), CancellationToken.None);

        Assert.Equal("openai-codex", updated.Provider);
        Assert.Null(await Store().GetAsync(provider, "account"));
        Assert.Equal("new-refresh", (await Store().GetAsync("openai-codex", "account"))!.RefreshToken);
    }

    [Fact]
    public async Task LogoutCannotLeaveCanonicalCredentialFromInflightAliasRefresh() {
        var legacy = new AuthBundle("openai", "old", "old-refresh", DateTimeOffset.UtcNow.AddHours(1)) {
            AccountId = "account"
        };
        await Store().SaveAsync(legacy);
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var refresh = Store().RefreshAsync(legacy, async (_, _) => {
            entered.SetResult();
            await release.Task;
            return new AuthBundle("openai", "new", "new-refresh", DateTimeOffset.UtcNow.AddHours(1)) {
                AccountId = "account"
            };
        }, CancellationToken.None);
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
        try {
            var manager = new OpenAINativeAuthManager(new OpenAINativeOptions {
                AuthStore = Store(), AuthAccountId = "account", LoadCodexAuthJson = false, PersistCodexAuthJson = false
            });
            Assert.NotNull(await manager.TryGetValidBundleAsync(default));
            await manager.LogoutAsync(default);
        } finally {
            release.TrySetResult();
        }

        await Assert.ThrowsAsync<InvalidOperationException>(() => refresh);
        Assert.Null(await Store().GetAsync("openai", "account"));
        Assert.Null(await Store().GetAsync("openai-codex", "account"));
    }

    [Fact]
    public async Task RefreshDoesNotNormalizeAnOpenAiAliasForAnotherProvider() {
        var original = new AuthBundle("copilot", "old", "old-refresh", null) { AccountId = "account" };
        await Store().SaveAsync(original);

        await Assert.ThrowsAsync<InvalidOperationException>(() => Store().RefreshAsync(original,
            (_, _) => Task.FromResult(new AuthBundle("openai", "new", "new-refresh", null) {
                AccountId = "account"
            }), CancellationToken.None));

        Assert.Equal("old-refresh", (await Store().GetAsync("copilot", "account"))!.RefreshToken);
        Assert.Null(await Store().GetAsync("openai", "account"));
    }

    [Fact]
    public async Task RefreshCannotReplaceAnotherAccountIdentity() {
        var original = Bundle("account");
        await Store().SaveAsync(original);
        await Assert.ThrowsAsync<InvalidOperationException>(() => Store().RefreshAsync(original,
            (_, _) => Task.FromResult(Bundle("different", "rotated")), CancellationToken.None));
        Assert.Equal("original", (await Store().GetAsync("openai-codex", "account"))!.RefreshToken);
        Assert.Null(await Store().GetAsync("openai-codex", "different"));
    }

    [Fact]
    public async Task FailedRefreshPreservesOriginalInventoryAndReleasesLock() {
        var old = Bundle("account");
        await Store().SaveAsync(old);
        var before = await File.ReadAllBytesAsync(StorePath);
        await Assert.ThrowsAsync<InvalidOperationException>(() => Store().RefreshAsync(old,
            (_, _) => throw new InvalidOperationException("Synthetic provider failure"), CancellationToken.None));
        Assert.Equal(before, await File.ReadAllBytesAsync(StorePath));
        await Store().SaveAsync(Bundle("other"));
        Assert.Equal("original", (await Store().GetAsync("openai-codex", "account"))!.RefreshToken);
        Assert.Empty(Directory.GetFiles(_directory, "*.tmp"));
    }

    public void Dispose() {
        if (Directory.Exists(_directory)) Directory.Delete(_directory, recursive: true);
    }
}
