using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using IntelligenceX.Chat.Profiles;
using Xunit;

namespace IntelligenceX.Chat.Tests;

public sealed class SqliteServiceProfileStoreRuntimePolicyTests {
    [Fact]
    public async Task UpsertAndGet_RuntimePolicyFields_RoundTripAndNormalizePaths() {
        var dbPath = CreateTempDbPath();
        try {
            using var store = new SqliteServiceProfileStore(dbPath);
            var profile = new ServiceProfile {
                WriteGovernanceMode = "yolo",
                RequireWriteGovernanceRuntime = false,
                RequireWriteAuditSinkForWriteOperations = true,
                WriteAuditSinkMode = "sqlite",
                WriteAuditSinkPath = " C:/temp/write-audit.db ",
                AuthenticationRuntimePreset = "strict",
                RequireExplicitRoutingMetadata = true,
                RequireAuthenticationRuntime = true,
                RunAsProfilePath = " C:/profiles/run-as.json ",
                AuthenticationProfilePath = " C:/profiles/auth.json "
            };

            await store.UpsertAsync("runtime-policy", profile, CancellationToken.None);
            var loaded = await store.GetAsync("runtime-policy", CancellationToken.None);

            Assert.NotNull(loaded);
            Assert.Equal("yolo", loaded!.WriteGovernanceMode);
            Assert.False(loaded.RequireWriteGovernanceRuntime);
            Assert.True(loaded.RequireWriteAuditSinkForWriteOperations);
            Assert.Equal("sqlite", loaded.WriteAuditSinkMode);
            Assert.Equal("C:/temp/write-audit.db", loaded.WriteAuditSinkPath);
            Assert.Equal("strict", loaded.AuthenticationRuntimePreset);
            Assert.True(loaded.RequireExplicitRoutingMetadata);
            Assert.True(loaded.RequireAuthenticationRuntime);
            Assert.Equal("C:/profiles/run-as.json", loaded.RunAsProfilePath);
            Assert.Equal("C:/profiles/auth.json", loaded.AuthenticationProfilePath);
        } finally {
            TryDelete(dbPath);
        }
    }

    [Fact]
    public async Task UpsertAndGet_RuntimePolicyFields_InvalidTokens_FallBackToDefaults() {
        var dbPath = CreateTempDbPath();
        try {
            using var store = new SqliteServiceProfileStore(dbPath);
            var profile = new ServiceProfile {
                WriteGovernanceMode = "invalid",
                WriteAuditSinkMode = "invalid",
                AuthenticationRuntimePreset = "invalid",
                WriteAuditSinkPath = "   ",
                RunAsProfilePath = "   ",
                AuthenticationProfilePath = "   "
            };

            await store.UpsertAsync("runtime-policy-defaults", profile, CancellationToken.None);
            var loaded = await store.GetAsync("runtime-policy-defaults", CancellationToken.None);

            Assert.NotNull(loaded);
            Assert.Equal("enforced", loaded!.WriteGovernanceMode);
            Assert.Equal("none", loaded.WriteAuditSinkMode);
            Assert.Equal("default", loaded.AuthenticationRuntimePreset);
            Assert.False(loaded.RequireExplicitRoutingMetadata);
            Assert.Null(loaded.WriteAuditSinkPath);
            Assert.Null(loaded.RunAsProfilePath);
            Assert.Null(loaded.AuthenticationProfilePath);
        } finally {
            TryDelete(dbPath);
        }
    }

    private static string CreateTempDbPath() {
        return Path.Combine(Path.GetTempPath(), $"ix-chat-profiles-{Guid.NewGuid():N}.db");
    }

    private static void TryDelete(string path) {
        try {
            if (File.Exists(path)) {
                File.Delete(path);
            }
        } catch {
            // Best-effort cleanup.
        }
    }
}
