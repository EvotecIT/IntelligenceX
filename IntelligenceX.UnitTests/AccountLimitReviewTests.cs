using System.Text;
using IntelligenceX.OpenAI.Auth;
using IntelligenceX.OpenAI.Native;
using IntelligenceX.Telemetry.Limits;
using Xunit;

namespace IntelligenceX.UnitTests;

public sealed class AccountLimitReviewTests {
    [Fact]
    public async Task OverallAccountDeadlineIncludesSavedAccountDiscovery() {
        var options = new OpenAINativeOptions { AuthStore = new SlowDiscoveryStore(), LoadCodexAuthJson = false };
        var snapshot = await ProviderLimitSnapshotService.FetchCodexAsync("codex", options,
            CancellationToken.None, TimeSpan.FromMilliseconds(100)).WaitAsync(TimeSpan.FromSeconds(2));
        Assert.False(snapshot.IsAvailable);
        Assert.Contains("discovery reached its time limit", snapshot.DetailMessage);
    }

    [Fact]
    public void ProviderResolvedAliasesUseOneBestAccountReadingAndRetainSelection() {
        var time = DateTimeOffset.UtcNow;
        var failed = new ProviderLimitAccountSnapshot("same", "old alias", null,
            Array.Empty<ProviderLimitWindow>(), null, "Unavailable", time, isSelected: true);
        var healthy = new ProviderLimitAccountSnapshot("same", "resolved", "Pro",
            new[] { new ProviderLimitWindow("weekly", "Weekly", 25, time.AddDays(1)) },
            null, null, time.AddSeconds(1));
        var other = new ProviderLimitAccountSnapshot("other", "other", null,
            Array.Empty<ProviderLimitWindow>(), null, null, time);

        var rows = ProviderLimitSnapshotService.CoalesceResolvedAccounts(new[] { failed, healthy, other }, null);

        Assert.Equal(2, rows.Count);
        Assert.Equal("resolved", rows[0].AccountLabel);
        Assert.True(rows[0].IsSelected);
        Assert.True(rows[0].IsAvailable);
        Assert.Equal("other", rows[1].AccountId);
    }

    [Theory]
    [InlineData(false, false, false)]
    [InlineData(false, true, false)]
    [InlineData(true, false, false)]
    [InlineData(true, true, false)]
    [InlineData(false, true, true)]
    public async Task AmbientRotationIsImportedBeforeAStaleIxRefresh(bool differentAccount, bool perAccountUsage,
        bool legacyAlias) {
        string directory = Path.Combine(Path.GetTempPath(), "ix-ambient-rotation-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try {
            var store = new FileAuthBundleStore(Path.Combine(directory, "store.json"));
            var old = new AuthBundle("openai-codex", "old-access", "old-refresh", DateTimeOffset.UtcNow.AddHours(-2)) {
                AccountId = "selected"
            };
            await store.SaveAsync(old);
            if (legacyAlias) {
                old = new AuthBundle("openai", "alias-access", "alias-refresh", DateTimeOffset.UtcNow.AddHours(-1)) {
                    AccountId = "selected"
                };
                await store.SaveAsync(old);
            }
            string account = differentAccount ? "other" : "selected";
            var payload = Convert.ToBase64String(Encoding.UTF8.GetBytes(
                "{\"exp\":" + DateTimeOffset.UtcNow.AddMinutes(-2).ToUnixTimeSeconds() + "}"));
            await File.WriteAllTextAsync(Path.Combine(directory, "auth.json"),
                "{\"tokens\":{\"access_token\":\"e30." + payload + ".signature\",\"refresh_token\":\"ambient-refresh\",\"account_id\":\"" + account + "\"}}");
            var usedTokens = new List<string>();
            var manager = new OpenAINativeAuthManager(new() { AuthStore = store, AuthAccountId = "selected",
                CodexHome = directory, LoadCodexAuthJson = true, PersistCodexAuthJson = false },
                (_, candidate, _) => {
                    usedTokens.Add(candidate.RefreshToken);
                    return Task.FromResult(new OAuthLoginResult(new("openai-codex", "renewed", "rotated",
                        DateTimeOffset.UtcNow.AddHours(1)) { AccountId = "selected" }, new()));
                });

            var result = perAccountUsage
                ? await manager.GetValidBundleAsync(old, default)
                : await manager.TryGetValidBundleAsync(default);

            Assert.Equal("renewed", result!.AccessToken);
            Assert.Equal(new[] { differentAccount ? "old-refresh" : "ambient-refresh" }, usedTokens);
            Assert.Equal("rotated", (await store.GetAsync("openai-codex", "selected"))!.RefreshToken);
        } finally {
            Directory.Delete(directory, recursive: true);
        }
    }

    private sealed class SlowDiscoveryStore : IAuthBundleStore {
        public Task<AuthBundle?> GetAsync(string provider, string? accountId = null, CancellationToken cancellationToken = default) =>
            Task.FromResult<AuthBundle?>(null);
        public async Task<IReadOnlyList<AuthBundle>> ListAsync(string provider, CancellationToken cancellationToken = default) {
            await Task.Delay(TimeSpan.FromSeconds(10), cancellationToken);
            return Array.Empty<AuthBundle>();
        }
        public Task SaveAsync(AuthBundle bundle, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }
}
