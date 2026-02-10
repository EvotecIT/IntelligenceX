using System;
using System.IO;
using System.Security.Cryptography;
using IntelligenceX.OpenAI.Auth;
using Xunit;

namespace IntelligenceX.UnitTests;

public sealed class AuthStoreTests {
    [Fact]
    public void InvalidKey_Throws() {
        Assert.Throws<InvalidOperationException>(() => {
            _ = new FileAuthBundleStore(path: Path.Combine(Path.GetTempPath(), $"ix-auth-{Guid.NewGuid():N}.json"), encryptionKeyBase64: "nope");
        });
    }

    [Fact]
    public async Task EncryptedRoundtrip_Works() {
        var path = Path.Combine(Path.GetTempPath(), $"ix-auth-{Guid.NewGuid():N}.json");
        var key = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
        try {
            var store = new FileAuthBundleStore(path: path, encryptionKeyBase64: key);
            var bundle = new AuthBundle("openai", "access", "refresh", DateTimeOffset.UtcNow.AddHours(1)) {
                AccountId = "acct"
            };
            await store.SaveAsync(bundle);

            var payload = File.ReadAllText(path);
            Assert.StartsWith("{\"encrypted\":true", payload, StringComparison.OrdinalIgnoreCase);

            var loaded = await store.GetAsync("openai", accountId: "acct");
            Assert.NotNull(loaded);
            Assert.Equal("access", loaded!.AccessToken);
            Assert.Equal("refresh", loaded.RefreshToken);
            Assert.Equal("acct", loaded.AccountId);
        } finally {
            try {
                if (File.Exists(path)) {
                    File.Delete(path);
                }
            } catch {
                // best-effort cleanup
            }
        }
    }
}
