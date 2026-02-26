using System;
using System.Collections.Generic;
using System.IO;
using IntelligenceX.Chat.Service;
using Xunit;

namespace IntelligenceX.Chat.Tests;

public sealed partial class ChatServiceRoutingTrimTests {
    [Fact]
    public void ReadToolRoutingAdjustmentForTesting_RehydratesPersistedStatsAfterRestart() {
        var root = Path.Combine(Path.GetTempPath(), "ix-chat-tool-routing-stats-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var pendingActionsStorePath = Path.Combine(root, "pending-actions.json");

        try {
            var session1 = new ChatServiceSession(
                new ServiceOptions { PendingActionsStorePath = pendingActionsStorePath },
                Stream.Null);
            session1.SetToolRoutingStatsForTesting(
                new Dictionary<string, (long LastUsedUtcTicks, long LastSuccessUtcTicks)>(StringComparer.OrdinalIgnoreCase) {
                    ["ad_scope_discovery"] = (DateTime.UtcNow.Ticks, DateTime.UtcNow.Ticks)
                });
            session1.PersistToolRoutingStatsForTesting();

            var session2 = new ChatServiceSession(
                new ServiceOptions { PendingActionsStorePath = pendingActionsStorePath },
                Stream.Null);
            var adjustment = session2.ReadToolRoutingAdjustmentForTesting("ad_scope_discovery");

            Assert.True(adjustment > 0d);
        } finally {
            try {
                if (Directory.Exists(root)) {
                    Directory.Delete(root, recursive: true);
                }
            } catch {
                // Best effort test cleanup only.
            }
        }
    }
}
