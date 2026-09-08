using IntelligenceX.OpenAI.Auth;
using IntelligenceX.OpenAI.Native;
using Xunit;

namespace IntelligenceX.UnitTests;

public sealed class AuthStoreAccountLifecycleTests {
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ChatGptKeepsLegacyProviderKeyedCredentialsReadableAndRemovable(bool expiring) {
        string path = Path.Combine(Path.GetTempPath(), "ix-legacy-account-" + Guid.NewGuid().ToString("N") + ".json");
        string payload = Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(
            "{\"https://api.openai.com/auth\":{\"chatgpt_account_id\":\"first\"}}" )).TrimEnd('=').Replace('+', '-').Replace('/', '_');
        string jwt = "e30." + payload + ".signature";
        try {
            var store = new FileAuthBundleStore(path);
            await store.SaveAsync(new("openai-codex", jwt, "refresh", expiring ? DateTimeOffset.UtcNow.AddMinutes(-1) : null));
            int refreshes = 0;
            var manager = new OpenAINativeAuthManager(new() { AuthStore = store, LoadCodexAuthJson = false, PersistCodexAuthJson = false },
                (_, _, _) => { refreshes++; return Task.FromResult(new OAuthLoginResult(
                    new("openai-codex", jwt, "renewed", DateTimeOffset.UtcNow.AddHours(1)) { AccountId = "first" }, new())); });
            Assert.Equal("first", (await manager.TryGetValidBundleAsync(default))!.AccountId);
            await store.SaveAsync(new("openai-codex", "other", "", DateTimeOffset.UtcNow.AddHours(2)) { AccountId = "second" });
            Assert.Equal("first", (await manager.TryGetValidBundleAsync(default))!.AccountId);
            Assert.Equal(expiring ? 1 : 0, refreshes);
            await manager.LogoutAsync(default);
            Assert.Null(await manager.TryGetValidBundleAsync(default));
            Assert.Equal("second", Assert.Single(await store.ListAsync("openai-codex")).AccountId);
        } finally { if (File.Exists(path)) File.Delete(path); }
    }

    [Fact]
    public async Task ChatGptExplicitSignInCanSelectADifferentAccount() {
        string path = Path.Combine(Path.GetTempPath(), "ix-explicit-account-" + Guid.NewGuid().ToString("N") + ".json");
        try {
            var store = new FileAuthBundleStore(path);
            await store.SaveAsync(new("openai-codex", "first", "", null) { AccountId = "first" });
            var manager = new OpenAINativeAuthManager(new() { AuthStore = store,
                LoadCodexAuthJson = false, PersistCodexAuthJson = false }, null, _ => Task.FromResult(
                    new OAuthLoginResult(new("openai-codex", "approved-second", "", null) { AccountId = "second" }, new())));
            Assert.Equal("first", (await manager.TryGetValidBundleAsync(default))!.AccountId);
            await manager.LoginAsync(null, null, false, TimeSpan.FromMinutes(1), default);
            Assert.Equal("approved-second", (await manager.TryGetValidBundleAsync(default))!.AccessToken);
            await manager.LogoutAsync(default);
            Assert.Equal("first", (await store.GetAsync("openai-codex", "first"))!.AccessToken);
            Assert.Null(await store.GetAsync("openai-codex", "second"));
        } finally { if (File.Exists(path)) File.Delete(path); }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ChatGptRetainsSelectedAccountWhenItsCredentialIsRemoved(bool ambientEnabled) {
        string directory = Path.Combine(Path.GetTempPath(), "ix-account-selection-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        string path = Path.Combine(directory, "store.json"), ambient = Path.Combine(directory, "auth.json");
        try {
            var store = new FileAuthBundleStore(path);
            await store.SaveAsync(new("openai-codex", "first", "", null) { AccountId = "first" });
            var manager = new OpenAINativeAuthManager(new() { AuthStore = store, CodexHome = directory,
                LoadCodexAuthJson = ambientEnabled, PreferCurrentCodexSession = ambientEnabled, PersistCodexAuthJson = false });
            Assert.Equal("first", (await manager.TryGetValidBundleAsync(default))!.AccountId);
            await store.SaveAsync(new("openai-codex", "second", "", null) { AccountId = "second" });
            if (ambientEnabled) await File.WriteAllTextAsync(ambient,
                "{\"tokens\":{\"access_token\":\"ambient\",\"refresh_token\":\"refresh\",\"account_id\":\"second\"}}");
            await store.RemoveAsync("openai-codex", "first");
            Assert.Null(await manager.TryGetValidBundleAsync(default));
            Assert.Null(await manager.TryGetValidBundleAsync(default));
            await store.SaveAsync(new("openai-codex", "first-rotated", "", null) { AccountId = "first" });
            Assert.Equal("first-rotated", (await manager.TryGetValidBundleAsync(default))!.AccessToken);
            await manager.LogoutAsync(default);
            Assert.Equal("second", (await store.GetAsync("openai-codex", "second"))!.AccessToken);
        } finally {
            if (File.Exists(path)) File.Delete(path);
            if (File.Exists(ambient)) File.Delete(ambient);
            Directory.Delete(directory);
        }
    }

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
