using IntelligenceX.OpenAI.Auth;
using IntelligenceX.OpenAI.Native;
using Xunit;

namespace IntelligenceX.UnitTests;

public sealed class AuthStoreAccountLifecycleTests {
    [Fact]
    public async Task ChatGptAmbientAccountLogoutDoesNotDeleteTheStoreDefault() {
        string directory = Path.Combine(Path.GetTempPath(), "ix-ambient-logout-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        string path = Path.Combine(directory, "store.json"), ambient = Path.Combine(directory, "auth.json");
        try {
            var store = new FileAuthBundleStore(path);
            await store.SaveAsync(new("openai-codex", "unrelated", "", null) { AccountId = "other" });
            await File.WriteAllTextAsync(ambient, "{\"tokens\":{\"access_token\":\"ambient\",\"refresh_token\":\"refresh\",\"account_id\":\"current\"}}");
            var manager = new OpenAINativeAuthManager(new() { AuthStore = store, CodexHome = directory,
                PreferCurrentCodexSession = true, LoadCodexAuthJson = true, PersistCodexAuthJson = false });
            Assert.Equal("current", (await manager.TryGetValidBundleAsync(default))!.AccountId);
            await manager.LogoutAsync(default);
            Assert.Equal("unrelated", (await store.GetAsync("openai-codex", "other"))?.AccessToken);
            Assert.Null(await manager.TryGetValidBundleAsync(default));
            Assert.True(File.Exists(ambient));
        } finally {
            if (File.Exists(path)) File.Delete(path);
            if (File.Exists(ambient)) File.Delete(ambient);
            Directory.Delete(directory);
        }
    }

    [Fact]
    public async Task UnixAuthStoreWritesRemainPrivateAcrossAtomicReplacement() {
        if (OperatingSystem.IsWindows()) return;
        string path = Path.Combine(Path.GetTempPath(), "ix-private-auth-" + Guid.NewGuid().ToString("N") + ".json");
        try {
            var store = new FileAuthBundleStore(path);
            await store.SaveAsync(new("copilot", "first", "", null));
            Assert.Equal(UnixFileMode.UserRead | UnixFileMode.UserWrite, File.GetUnixFileMode(path));
            await store.SaveAsync(new("copilot", "second", "", null));
            Assert.Equal(UnixFileMode.UserRead | UnixFileMode.UserWrite, File.GetUnixFileMode(path));
        } finally { if (File.Exists(path)) File.Delete(path); }
    }

    [Fact]
    public async Task ConcurrentStoreInstancesPreserveAllAccountsAndCancelledWritesPreserveTheFile() {
        string path = Path.Combine(Path.GetTempPath(), "ix-auth-lifecycle-" + Guid.NewGuid().ToString("N") + ".json");
        try {
            await Task.WhenAll(Enumerable.Range(0, 24).Select(i => new FileAuthBundleStore(path).SaveAsync(
                new AuthBundle("copilot", "token-" + i, "", null) { AccountId = i.ToString() })));
            var store = new FileAuthBundleStore(path);
            Assert.Equal(24, (await store.ListAsync("copilot")).Count);
            string original = await File.ReadAllTextAsync(path);
            using var cancellation = new CancellationTokenSource();
            cancellation.Cancel();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => store.SaveAsync(new("copilot", "cancelled", "", null), cancellation.Token));
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => store.RemoveAsync("copilot", "0", cancellation.Token));
            Assert.Equal(original, await File.ReadAllTextAsync(path));
            await Task.WhenAll(Enumerable.Range(0, 12).Select(i => new FileAuthBundleStore(path).RemoveAsync("copilot", i.ToString())));
            var remaining = await store.ListAsync("copilot");
            Assert.Equal(12, remaining.Count);
            Assert.All(remaining, entry => Assert.Equal("token-" + entry.AccountId, entry.AccessToken));
        } finally { if (File.Exists(path)) File.Delete(path); }
    }

    [Fact]
    public async Task ChatGptLogoutPreservesOtherAccountsAndDoesNotReloadAmbientCodexCredentials() {
        string directory = Path.Combine(Path.GetTempPath(), "ix-chatgpt-logout-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        string path = Path.Combine(directory, "store.json"), ambient = Path.Combine(directory, "auth.json");
        try {
            var store = new FileAuthBundleStore(path);
            await store.SaveAsync(new("openai-codex", "first", "", null) { AccountId = "first" });
            await store.SaveAsync(new("openai-codex", "second", "", null) { AccountId = "second" });
            await store.SaveAsync(new("copilot", "github", "", null) { AccountId = "42" });
            await File.WriteAllTextAsync(ambient, "{\"tokens\":{\"access_token\":\"ambient\",\"refresh_token\":\"refresh\",\"account_id\":\"first\"}}");
            var manager = new OpenAINativeAuthManager(new() { AuthStore = store, AuthAccountId = "first", CodexHome = directory,
                LoadCodexAuthJson = true, PersistCodexAuthJson = false });
            Assert.NotNull(await manager.TryGetValidBundleAsync(default));
            await manager.LogoutAsync(default);
            Assert.Null(await manager.TryGetValidBundleAsync(default));
            Assert.Null(await store.GetAsync("openai-codex", "first"));
            Assert.Equal("second", (await store.GetAsync("openai-codex", "second"))!.AccessToken);
            Assert.Equal("github", (await store.GetAsync("copilot", "42"))!.AccessToken);
            Assert.True(File.Exists(ambient));
        } finally {
            if (File.Exists(path)) File.Delete(path);
            if (File.Exists(ambient)) File.Delete(ambient);
            Directory.Delete(directory);
        }
    }
}
