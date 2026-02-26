using System;
using System.IO;
using IntelligenceX.Chat.Service;
using Xunit;

namespace IntelligenceX.Chat.Tests;

public sealed partial class ChatServiceRoutingTrimTests {
    [Fact]
    public void ExpandContinuationUserRequest_RehydratesUserIntentAfterRestart() {
        var root = Path.Combine(Path.GetTempPath(), "ix-chat-user-intent-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var pendingActionsStorePath = Path.Combine(root, "pending-actions.json");
        const string threadId = "thread-user-intent-restart";
        const string intent = "Please run forest-wide replication and LDAP diagnostics.";

        try {
            var session1 = new ChatServiceSession(
                new ServiceOptions { PendingActionsStorePath = pendingActionsStorePath },
                Stream.Null);
            RememberUserIntentMethod.Invoke(session1, new object?[] { threadId, intent });

            var session2 = new ChatServiceSession(
                new ServiceOptions { PendingActionsStorePath = pendingActionsStorePath },
                Stream.Null);
            var expandedObj = ExpandContinuationUserRequestMethod.Invoke(session2, new object?[] { threadId, "run now" });
            var expanded = Assert.IsType<string>(expandedObj);

            Assert.Contains(intent, expanded, StringComparison.Ordinal);
            Assert.Contains("Follow-up: run now", expanded, StringComparison.OrdinalIgnoreCase);
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
