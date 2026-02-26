using System;
using System.IO;
using IntelligenceX.Chat.Service;
using Xunit;

namespace IntelligenceX.Chat.Tests;

public sealed partial class ChatServiceRoutingTrimTests {
    [Fact]
    public void ResolveRecoveredThreadAliasForTesting_RehydratesPersistedAliasAfterRestart() {
        var root = Path.Combine(Path.GetTempPath(), "ix-chat-thread-alias-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var pendingActionsStorePath = Path.Combine(root, "pending-actions.json");
        const string originalThreadId = "thread-original";
        const string recoveredThreadId = "thread-recovered";

        try {
            var session1 = new ChatServiceSession(
                new ServiceOptions { PendingActionsStorePath = pendingActionsStorePath },
                Stream.Null);
            session1.RememberRecoveredThreadAliasForTesting(originalThreadId, recoveredThreadId);

            var session2 = new ChatServiceSession(
                new ServiceOptions { PendingActionsStorePath = pendingActionsStorePath },
                Stream.Null);
            var resolved = session2.ResolveRecoveredThreadAliasForTesting(originalThreadId);

            Assert.Equal(recoveredThreadId, resolved);
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
